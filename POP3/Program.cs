using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Remoting;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace POP3
{
    class Program
    {
        private const string Server = "pop.mail.ru";
        private const int port = 995;
        private static readonly Encoding charset = Encoding.UTF8;

        private const string CommandsHelp = "Watch headings --- 'HEAD'\n" +
                                            "Watch first n lines of the message --- 'TOP [n]'\n" +
                                            "Download message to directory --- 'DOWNLOAD [path]'\n";

        private static readonly string[] ImportantHeadings = { "From", "Date", "Subject" };
        private static Dictionary<int, int> MsgSizes = new Dictionary<int, int>();
        private static readonly Regex boundaryRegex = new Regex("boundary=\"(.+?)\"");
        static void Main(string[] args)
        {
            Console.WriteLine("Enter your account mail.ru");
            var account = Console.ReadLine();
            //var account = "test.martyanovst@mail.ru";

            Console.WriteLine($"Enter password from {account}");
            var password = Console.ReadLine();
            //var password = "afhfjyt,fyysqgcb[";


            var buffer = new byte[1024];
            using (var sslStream = new SslStream(new TcpClient(Server, port).GetStream(), false,
                ValidateServerCertificate, null))
            {
                sslStream.AuthenticateAsClient(Server);
                sslStream.Read(buffer, 0, buffer.Length);//Read server greeting
                Console.WriteLine(charset.GetString(buffer));
                Authenticate(account, password, sslStream);
                var countOfMessages = ReadStat(sslStream);
                Console.WriteLine($"You have {countOfMessages} messages");
                Console.WriteLine("Select message number, which you want to see");
                var messageNumber = int.Parse(Console.ReadLine());
                //var messageNumber = 7;
                if (messageNumber < 1 || messageNumber > countOfMessages)
                    throw new ArgumentException($"Number must be more than zero and less than {countOfMessages + 1}");
                Console.WriteLine($"Select what you want to do with message:\n{CommandsHelp}");
                var command = ReadCommand();
                //var command = (command: "DOWNLOAD", args: new[] { @"C:\Users\Comp\Desktop\Игры\С#\SMTP" });
                Console.WriteLine(CommandExecute(command.command, command.args, messageNumber, sslStream));
                sslStream.Write(charset.GetBytes("QUIT"));
            }
        }

        static void Authenticate(string account, string password, SslStream stream)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
                throw new ArgumentException("INCORRECT PARAMETERS");
            var authentication = new[]
            {
                $"USER {account}\r\n",
                $"PASS {password}\r\n",
            };
            var buffer = new byte[1024];
            foreach (var input in authentication)
            {
                stream.Write(charset.GetBytes(input));
                stream.Read(buffer, 0, buffer.Length);
                Console.WriteLine(charset.GetString(buffer));
            }
            if (!IsSuccess(buffer))
                throw new ArgumentException("incorrect login or password");
        }

        static string CommandExecute(string command, string[] args, int messageNumber, SslStream stream)
        {
            var data = new byte[MsgSizes[messageNumber]];
            switch (command)
            {
                case "HEAD":
                    stream.Write(charset.GetBytes($"RETR {messageNumber}\r\n"));
                    return ParseHeadings(ReadBuffer(stream, data.Length));

                case "TOP":
                    stream.Write(charset.GetBytes($"RETR {messageNumber}\r\n"));
                    var MessageData = ReadBuffer(stream, data.Length);
                    return ParseHeadings(MessageData) + ReadLines(MessageData, int.Parse(args[0]));
                case "DOWNLOAD":
                    stream.Write(charset.GetBytes($"RETR {messageNumber}\r\n"));
                    MessageData = ReadBuffer(stream,data.Length);
                    return SaveMessageToDirectory(MessageData, args[0]);
            }
            return charset.GetString(data);
        }

        static string SaveMessageToDirectory(string message, string path)
        {
            var boundary = GetBoundary(message);
            var text = ReadAllTextFromMessage(message, boundary, out var encoding);
            try
            {
            System.IO.File.WriteAllText(Path.Combine(path, "text.txt"), text, encoding);
            foreach (var file in GetContent(message, boundary))
            {
                using (var ms = new MemoryStream(file.Bytes))
                {
                    var image = Image.FromStream(ms);
                    image.Save(Path.Combine(path, file.Name));
                    Console.WriteLine($"Save image {file.Name} to directory: {path}");
                }
            }

            }
            catch (Exception)
           {
                return "FAILURE!";
            }

            return "Success!";
        }

        static string ParseHeadings(string message)
        {
            var builder = new StringBuilder();
            var lines = message.Split('\n').Select(x => x.Split(':')).ToList();
            foreach (var line in lines.Where(x => ImportantHeadings.Contains(x[0])))
            {
                switch (line[0])
                {
                    case "From":
                        builder.Append("Message from:   ");
                        builder.Append(ReadBase64(line[1]));
                        builder.Append('\n');
                        break;
                    case "Date":
                        builder.Append("Date: ");
                        builder.Append(line[1]);
                        builder.Append('\n');
                        break;
                    case "Subject":
                        builder.Append("Subject: ");
                        builder.Append(ReadBase64(line[1]));
                        builder.Append('\n');
                        break;
                }
            }

            return builder.ToString();
        }

        static IEnumerable<File> GetContent(string message, string boundary)
        {
            var splitter = new Regex(boundary, RegexOptions.Compiled);
            var extensionPattern = new Regex("Content-Type: image/(.+?);", RegexOptions.Compiled);
            var namePattern = new Regex("name=\"(.+?)\"", RegexOptions.Compiled);
            var splitedByNewLineSymbol = new Regex("\r\n\r\n", RegexOptions.Compiled);
            var splittedByBoundaryLines = splitter.Split(message).ToArray();
            foreach (var dataBlock in splittedByBoundaryLines)
            {
                var splited = splitedByNewLineSymbol.Split(dataBlock).Where(x => !string.IsNullOrEmpty(x)).ToArray();
                if (!extensionPattern.IsMatch(splited[0])) continue;
                var extension = extensionPattern.Match(splited[0]).Groups[1].Value;
                var name = ReadBase64(namePattern.Match(splited[0]).Groups[1].Value);
                if (splited.Length < 2) continue;
                var index = splited[1].IndexOf("--", StringComparison.Ordinal);
                var attachment = index >= 0 ? splited[1].Substring(0, index) : splited[1];
                if (string.IsNullOrEmpty(attachment)) continue;
                var bytes = Convert.FromBase64String(attachment);
                yield return new File(name, extension, bytes);
            }
        }

        static string ReadAllTextFromMessage(string message, string boundary, out Encoding charset)
        {
            var splitter = new Regex(boundary, RegexOptions.Compiled);
            var charsetPattern = new Regex("charset=\"(.+?)\"");
            var splittedByBoundaryLines = splitter.Split(message).Where(x => x.Contains("Content-Type: text/plain")).ToArray();
            charset = Program.charset;
            if (splittedByBoundaryLines.Length == 0) return "";
            var builder = new StringBuilder();
            foreach (var line in splittedByBoundaryLines)
            {
                var splitedByNewLineSymbol = new Regex("\r\n\r\n").Split(line);
                for (var i = 0; i < splitedByNewLineSymbol.Length; i++)
                {
                    if (charsetPattern.IsMatch(splitedByNewLineSymbol[i]))
                    {
                        var encodingName = charsetPattern.Match(line).Groups[1].Value;
                        if (!string.IsNullOrEmpty(encodingName))
                            charset = Encoding.GetEncoding(encodingName);
                        builder.Append(splitedByNewLineSymbol[i + 1]);
                        break;
                    }
                }
            }
            return builder.ToString();
        }

        static string ReadLines(string message, int count)
        {
            var boundary = GetBoundary(message);
            var text = ReadAllTextFromMessage(message, boundary, out var encoding);
            try
            {
                text = encoding.GetString(Convert.FromBase64String(text));
            }
            catch
            {
                // ignored
            }
            var lines = text.Split('\n').Take(count);
            var builder = new StringBuilder();
            foreach (var line in lines)
            {
                builder.Append(line);
                builder.Append('\n');
            }
            return builder.ToString();
        }

        static string GetBoundary(string message) => "--" + boundaryRegex.Match(message).Groups[1].Value;

        static string ReadBuffer(Stream stream)
        {
            var builder = new StringBuilder();
            var data = new byte[1024];
            string str;
            do
            {
                stream.Read(data, 0, 1024);
                str = charset.GetString(data);
                builder.Append(str);
            } while (str.IndexOf("\n.\r", StringComparison.Ordinal) == -1);
            return builder.ToString();
        }
        static string ReadBuffer(Stream stream, int count)
        {
            var buffer = new List<byte>();
            for (var i = 0; i < count; i++)
                buffer.Add((byte)stream.ReadByte());

            return charset.GetString(buffer.ToArray());
        }
        static int ReadStat(SslStream stream)
        {
            stream.Write(charset.GetBytes("STAT\r\n"));
            var buffer = new byte[1024];
            stream.Read(buffer, 0, buffer.Length);
            stream.Write(charset.GetBytes("LIST\r\n"));
            var answer = ReadBuffer(stream);
            Console.WriteLine(answer);
            var result = int.Parse(answer.Split(' ')[1]);

            MsgSizes = answer
                .Split('\n')
                .Select(x => x.Split(' '))
                .Where(x => int.TryParse(x[0], out _))
            .ToDictionary(x => int.Parse(x[0]), y => int.Parse(y[1]));
            return result;
        }

        static (string command, string[] args) ReadCommand()
        {
            var raw = Console.ReadLine()?.Split(' ');
            switch (raw?[0])
            {
                case "HEAD": return (raw[0], null);
                case "TOP": return (raw[0], new[] { raw[1] });
                case "DOWNLOAD":
                    if (!Directory.Exists(raw[1]))
                        throw new DirectoryNotFoundException("This directory doens't exists");
                    return (raw[0], new[] { raw[1] });
                default: throw new ArgumentException($"Incorrect command: {raw[0]}");
            }
        }

        static bool IsSuccess(byte[] ServerResponse) => charset.GetString(ServerResponse).Split(' ')[0] == "+OK";

        static string ReadBase64(string line)
        {
            if (!line.Contains("=?")) return line;
            var splited = line.Split('?');
            return Encoding.GetEncoding(splited[1]).GetString(Convert.FromBase64String(splited[3]));
        }

        private static bool ValidateServerCertificate(object sender, X509Certificate certificate,
            X509Chain chain, SslPolicyErrors sslPolicyErrors) => true;
    }
}