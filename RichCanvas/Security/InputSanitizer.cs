using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace RichCanvas.Security
{
    /// <summary>
    /// Provides methods for sanitizing user input to prevent injection attacks.
    /// </summary>
    public static class InputSanitizer
    {
        private static readonly Regex HtmlTagPattern = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex ScriptPattern = new Regex(@"<script[^>]*>.*?</script>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex SqlKeywordPattern = new Regex(@"\b(DROP|DELETE|INSERT|UPDATE|SELECT|UNION|EXEC|EXECUTE|CREATE|ALTER)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PathTraversalPattern = new Regex(@"\.\.[\\/]", RegexOptions.Compiled);
        
        /// <summary>
        /// Sanitizes a string value for safe use.
        /// </summary>
        public static string SanitizeString(string input, int maxLength = 1000)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // 1. Truncate to maximum length
            if (input.Length > maxLength)
                input = input.Substring(0, maxLength);

            // 2. Remove null characters
            input = input.Replace("\0", string.Empty);

            // 3. Remove HTML tags and scripts
            input = RemoveHtmlTags(input);
            input = RemoveScripts(input);

            // 4. Escape special characters
            input = EscapeSpecialCharacters(input);

            // 5. Remove path traversal patterns
            input = PathTraversalPattern.Replace(input, string.Empty);

            // 6. Normalize whitespace
            input = NormalizeWhitespace(input);

            return input.Trim();
        }

        /// <summary>
        /// Sanitizes a file path to prevent directory traversal attacks.
        /// </summary>
        public static string SanitizeFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            // Remove path traversal sequences
            path = path.Replace("..", string.Empty);
            path = path.Replace("../", string.Empty);
            path = path.Replace("..\\", string.Empty);
            
            // Remove invalid path characters
            char[] invalidChars = System.IO.Path.GetInvalidPathChars();
            foreach (char c in invalidChars)
            {
                path = path.Replace(c.ToString(), string.Empty);
            }

            // Remove multiple consecutive slashes
            path = Regex.Replace(path, @"[\\/]{2,}", @"\");

            // Ensure path doesn't start with a drive root if not intended
            if (path.Length > 1 && path[1] == ':')
            {
                // Validate drive letter
                if (!char.IsLetter(path[0]))
                {
                    path = path.Substring(2);
                }
            }

            return path;
        }

        /// <summary>
        /// Validates and sanitizes numeric input.
        /// </summary>
        public static double SanitizeNumeric(double value, double min = double.MinValue, double max = double.MaxValue)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;

            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        /// <summary>
        /// Validates and sanitizes integer input.
        /// </summary>
        public static int SanitizeInteger(int value, int min = int.MinValue, int max = int.MaxValue)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        /// <summary>
        /// Removes HTML tags from input.
        /// </summary>
        private static string RemoveHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return HtmlTagPattern.Replace(input, string.Empty);
        }

        /// <summary>
        /// Removes script tags and content.
        /// </summary>
        private static string RemoveScripts(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return ScriptPattern.Replace(input, string.Empty);
        }

        /// <summary>
        /// Escapes special characters that could be used in injection attacks.
        /// </summary>
        private static string EscapeSpecialCharacters(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sb = new StringBuilder(input.Length);
            
            foreach (char c in input)
            {
                switch (c)
                {
                    case '<':
                        sb.Append("&lt;");
                        break;
                    case '>':
                        sb.Append("&gt;");
                        break;
                    case '"':
                        sb.Append("&quot;");
                        break;
                    case '\'':
                        sb.Append("&#39;");
                        break;
                    case '&':
                        sb.Append("&amp;");
                        break;
                    case '\\':
                        sb.Append("&#92;");
                        break;
                    default:
                        if (c >= 32 && c <= 126) // Printable ASCII
                        {
                            sb.Append(c);
                        }
                        else if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                        {
                            sb.Append(c);
                        }
                        // Skip non-printable characters
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Normalizes whitespace in the input.
        /// </summary>
        private static string NormalizeWhitespace(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Replace multiple spaces with single space
            return Regex.Replace(input, @"\s{2,}", " ");
        }

        /// <summary>
        /// Validates if a string contains SQL injection patterns.
        /// </summary>
        public static bool ContainsSqlInjectionPattern(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            // Check for SQL keywords
            if (SqlKeywordPattern.IsMatch(input))
                return true;

            // Check for common SQL injection patterns
            string[] patterns = { "--", "/*", "*/", "xp_", "sp_", "0x", "@@", "char(", "nchar(", "varchar(", "nvarchar(", "exec(", "execute(", "cast(", "convert(", "script" };
            
            var lowerInput = input.ToLowerInvariant();
            foreach (var pattern in patterns)
            {
                if (lowerInput.Contains(pattern))
                    return true;
            }

            // Check for encoded characters
            if (input.Contains("%00") || input.Contains("%27") || input.Contains("%22"))
                return true;

            return false;
        }

        /// <summary>
        /// Validates email address format.
        /// </summary>
        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates URL format and scheme.
        /// </summary>
        public static bool IsValidUrl(string url, bool allowOnlyHttps = true)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult))
                return false;

            if (allowOnlyHttps)
            {
                return uriResult.Scheme == Uri.UriSchemeHttps;
            }

            return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
        }
    }
}