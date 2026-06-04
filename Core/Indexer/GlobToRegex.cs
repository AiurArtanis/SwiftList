using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("TestGlobCompiler")]

namespace SwiftList.Core
{
    public static class GlobToRegex
    {
        public static Regex Compile(string glob, bool ignoreCase = true)
        {
            string pattern = Convert(glob);
            var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (ignoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }
            return new Regex(pattern, options, TimeSpan.FromMilliseconds(50));
        }

        public static string Convert(string glob)
        {
            if (string.IsNullOrEmpty(glob))
                return string.Empty;

            // Normalize path separators
            glob = glob.Replace('\\', '/');

            // Handle anchoring:
            // - If the glob starts with '/', it is anchored to the root.
            // - If it doesn't start with '/' but contains '/', it is also treated as relative to the root.
            // - Otherwise (no slashes), it matches the filename or folder name at any depth.
            bool hasSlash = glob.TrimEnd('/').Contains('/');
            bool startsWithSlash = glob.StartsWith("/");

            var sb = new StringBuilder();

            if (startsWithSlash)
            {
                glob = glob.Substring(1);
                sb.Append("^(?:[a-zA-Z]:)?[\\\\/]?");
            }
            else if (hasSlash)
            {
                sb.Append("^(?:[a-zA-Z]:)?[\\\\/]?");
            }
            else
            {
                sb.Append("(^|[\\\\/])");
            }

            int consecutiveStars = 0;
            void InsertStars()
            {
                if (consecutiveStars > 0)
                {
                    if (consecutiveStars == 1)
                    {
                        // Match a single path segment (non-separator characters)
                        sb.Append("[^\\\\/]*");
                    }
                    else if (consecutiveStars == 2)
                    {
                        // Match any character across multiple path segments
                        sb.Append(".*");
                    }
                    consecutiveStars = 0;
                }
            }

            bool slashed = false;
            int inBrackets = 0;
            bool inBraces = false;

            for (int i = 0; i < glob.Length; i++)
            {
                char c = glob[i];

                if (slashed)
                {
                    sb.Append(Regex.Escape(c.ToString()));
                    slashed = false;
                    continue;
                }

                // Check for /**/
                if (c == '/' && i + 3 < glob.Length && glob.Substring(i, 4) == "/**/")
                {
                    InsertStars();
                    sb.Append("[\\\\/](?:.*[\\\\/])?");
                    i += 3; // Skip "**/", next loop iteration will move past the slash
                    continue;
                }

                // Check for **/ at start
                if (i == 0 && glob.Length >= 3 && glob.Substring(0, 3) == "**/")
                {
                    sb.Append("(?:.*[\\\\/])?");
                    i += 2;
                    continue;
                }

                // Check for /** at end
                if (c == '/' && i + 2 < glob.Length && glob.Substring(i, 3) == "/**" && i + 3 == glob.Length)
                {
                    InsertStars();
                    sb.Append("[\\\\/].*");
                    i += 2;
                    continue;
                }

                if (c != '*')
                {
                    InsertStars();
                }

                if (inBrackets > 0)
                {
                    if (c == '[') inBrackets++;
                    if (c == ']') inBrackets--;
                    sb.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '\\':
                        slashed = true;
                        break;
                    case '*':
                        consecutiveStars++;
                        break;
                    case '?':
                        sb.Append("[^\\\\/]");
                        break;
                    case '[':
                        sb.Append('[');
                        inBrackets++;
                        break;
                    case ']':
                        throw new ArgumentException("Mismatched ']' in glob: " + glob);
                    case '{':
                        if (inBraces)
                            throw new ArgumentException("Nested '{' '}' not supported in glob: " + glob);
                        sb.Append("(?:");
                        inBraces = true;
                        break;
                    case '}':
                        if (!inBraces)
                            throw new ArgumentException("Mismatched '}' in glob: " + glob);
                        sb.Append(')');
                        inBraces = false;
                        break;
                    case ',':
                        if (inBraces)
                        {
                            sb.Append('|');
                        }
                        else
                        {
                            sb.Append(',');
                        }
                        break;
                    // Escape standard regex characters that are not part of glob syntax
                    case '.':
                    case '+':
                    case '(':
                    case ')':
                    case '|':
                    case '^':
                    case '$':
                    case '@':
                    case '%':
                        sb.Append('\\').Append(c);
                        break;
                    case '/':
                        sb.Append("[\\\\/]");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            InsertStars();

            if (inBrackets > 0)
                throw new ArgumentException("Mismatched '[' and ']' in glob: " + glob);
            if (inBraces)
                throw new ArgumentException("Mismatched '{' and '}' in glob: " + glob);

            sb.Append("$");

            return sb.ToString();
        }
    }
}
