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

            // Only quote when necessary; otherwise pass through verbatim so
            // backslashes in paths (e.g. C:\Program Files\...) are preserved.
            if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return arg;
            }

            // Standard Windows command-line quoting (the algorithm used by
            // .NET's ArgumentList / CommandLineToArgvW): wrap in quotes; for every
            // run of backslashes, double them only when immediately followed by a
            // quote or at the very end of the argument (before the closing quote).
            var sb = new System.Text.StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            foreach (char ch in arg)
            {
                if (ch == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (ch == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }
                sb.Append('\\', backslashes);
                backslashes = 0;
                sb.Append(ch);
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
