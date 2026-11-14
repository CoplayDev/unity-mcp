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

            var match = FrameWithFile.Match(stackTrace);
            if (!match.Success)
            {
                return false;
            }

            file = match.Groups["file"].Value.Trim().Replace('\\', '/');
            line = int.Parse(match.Groups["line"].Value);
            return true;
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
