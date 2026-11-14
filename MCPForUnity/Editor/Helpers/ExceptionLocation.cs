using System;
using System.Text.RegularExpressions;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Extracts the source location of the first stack frame that has one, so tool errors can be
    /// reported as {exception_type, file, line} instead of a raw stack trace string.
    /// Handles both Mono ("... in /path/File.cs:42") and .NET ("... in /path/File.cs:line 42") frames.
    /// </summary>
    internal static class ExceptionLocation
    {
        private static readonly Regex FrameWithFile = new(
            @"\bin (?<file>.+?):(?:line )?(?<line>\d+)\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        internal static bool TryParse(string stackTrace, out string file, out int line)
        {
            file = null;
            line = 0;
            if (string.IsNullOrEmpty(stackTrace))
            {
                return false;
            }

            // Frames without source information ("<00000000000000000000000000000000>:0", line 0) can
            // come before the first real one; skip them instead of reporting the marker.
            for (Match match = FrameWithFile.Match(stackTrace); match.Success; match = match.NextMatch())
            {
                string candidate = match.Groups["file"].Value.Trim();
                if (!int.TryParse(match.Groups["line"].Value, out int candidateLine) || candidateLine <= 0) continue;
                if (candidate.Length == 0 || (candidate.StartsWith("<", StringComparison.Ordinal) && candidate.EndsWith(">", StringComparison.Ordinal))) continue;
                file = candidate.Replace('\\', '/');
                line = candidateLine;
                return true;
            }
            return false;
        }

        /// <summary>Error envelope fields shared by the dispatcher and async command failures.</summary>
        internal static object Describe(string commandType, Exception ex)
        {
            TryParse(ex.StackTrace, out var file, out var line);
            return new
            {
                status = "error",
                error = ex.Message,
                command = commandType ?? "Unknown",
                exception_type = ex.GetType().FullName,
                file,
                line = file != null ? line : (int?)null,
                stackTrace = ex.StackTrace,
            };
        }
    }
}
