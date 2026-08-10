using System.Diagnostics;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Unity 2020.3 compatibility shim: ProcessStartInfo.ArgumentList was introduced in
    /// .NET Core 2.1 / netstandard2.1. On Unity 2020.3 (netstandard2.0) we emulate it
    /// with ProcessStartInfo.Arguments, quoting each argument.
    /// </summary>
    public static class ProcessArgumentListCompat
    {
        /// <summary>Append one argument (quoted if it contains whitespace) — replaces ArgumentList.Add.</summary>
        public static ProcessStartInfo AddArg(this ProcessStartInfo psi, string arg)
        {
            if (string.IsNullOrEmpty(psi.Arguments))
            {
                psi.Arguments = Quote(arg);
            }
            else
            {
                psi.Arguments += " " + Quote(arg);
            }
            return psi;
        }

        private static string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                return "\"\"";
            }

            if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return arg;
            }

            // Escape embedded quotes the way Windows CreateProcess expects:
            // backslashes before a quote are doubled, then the quote escaped.
            return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
