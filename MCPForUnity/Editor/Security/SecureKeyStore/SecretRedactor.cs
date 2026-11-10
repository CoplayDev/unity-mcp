using System.Text.RegularExpressions;

namespace MCPForUnity.Editor.Security
{
    /// <summary>
    /// Scrubs secrets out of text before it is logged or returned. Use on every error/log
    /// path that might contain an auth header or a key value.
    /// </summary>
    public static class SecretRedactor
    {
        private const string Mask = "***REDACTED***";

        // Authorization-scheme tokens: "Bearer xxx", "Key xxx", "Token xxx".
        private static readonly Regex SchemeToken = new Regex(
            @"\b(Bearer|Key|Token)\s+\S{6,}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Redact auth-scheme tokens from arbitrary text (cheap; no store reads).</summary>
        public static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return SchemeToken.Replace(text, m => m.Groups[1].Value + " " + Mask);
        }

        /// <summary>Redact a specific known secret value as well as auth-scheme tokens.</summary>
        public static string Scrub(string text, string secret)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!string.IsNullOrEmpty(secret) && secret.Length >= 4)
            {
                text = text.Replace(secret, Mask);
            }
            return Scrub(text);
        }
    }
}
