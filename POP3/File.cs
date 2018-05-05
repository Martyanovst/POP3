namespace POP3
{
    class File
    {
        public readonly string Name;
        public readonly string Extension;
        public readonly byte[] Bytes;

        public File(string name, string extension, byte[] bytes)
        {
            Name = name;
            Extension = extension;
            Bytes = bytes;
        }
    }
}
