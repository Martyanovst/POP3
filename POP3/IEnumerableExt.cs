using System.Collections.Generic;
using System.Text;

namespace POP3
{
    static class IEnumerableExt
    {
        public static string Concat(this IEnumerable<string> lines)
        {
            var builder = new StringBuilder();
            foreach (var line in lines)
                builder.Append(line);
            return builder.ToString();
        }
    }
}
