using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MCPForUnity.Editor.Security
{
    /// <summary>
    /// Guardrail for destructive file operations executed through <c>execute_code</c>.
    ///
    /// This is NOT a sandbox: <c>execute_code</c> compiles arbitrary C#, so reflection,
    /// P/Invoke or a shell can bypass any source-text check. The policy only removes
    /// false positives for the common case (a literal path under a regenerable folder)
    /// while keeping <c>Assets/</c> and everything outside the project hard-denied.
    ///
    /// The allowlist is project-relative roots. A path is deletable only when it is
    /// strictly below one of those roots (never the root itself), inside the project,
    /// and not under a hard-denied root.
    /// </summary>
    internal static class DeletePathPolicy
    {
        internal const string BlockedPrefix = "Blocked pattern detected: ";

        /// <summary>Project-relative roots that may be deleted by default.</summary>
        internal static readonly string[] DefaultAllowedRoots =
        {
            "Builds",
            "Temp",
            "Logs",
            "obj",
            "Library/Bee",
            "Library/Artifacts",
        };

        /// <summary>Roots that can never be deleted, even if configured.</summary>
        internal static readonly string[] HardDeniedRoots = { "Assets" };

        // Match the member reference itself, not just a direct call. A method group
        // (`Action d = File.Delete;`) or a delegate construction has no inspectable
        // path argument, so the loop below rejects it instead of silently skipping it.
        private static readonly Regex DeleteInvocation = new Regex(
            @"\b(?:System\s*\.\s*IO\s*\.\s*)?(?:File|Directory)\s*\.\s*Delete\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex StaticIoImport = new Regex(
            @"\busing\s+static\s+(?:System\s*\.\s*IO\s*\.\s*)?(?:File|Directory)\s*;",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex InfoConstruction = new Regex(
            @"\bnew\s+(?:System\s*\.\s*IO\s*\.\s*)?(?:FileInfo|DirectoryInfo)\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static string ProjectRoot
        {
            get
            {
                try
                {
                    var parent = Directory.GetParent(Application.dataPath);
                    if (parent != null && !string.IsNullOrEmpty(parent.FullName))
                        return parent.FullName;
                }
                catch
                {
                    // Fall through to the process directory; callers will fail the
                    // project-boundary check rather than silently allowing a delete.
                }
                return Environment.CurrentDirectory;
            }
        }

        private static StringComparison PathComparison
        {
            get
            {
                // Linux filesystems are typically case-sensitive; Windows and macOS
                // default to case-insensitive. Comparing case-insensitively can only
                // over-block on Linux, which is the safe direction.
                return Application.platform == RuntimePlatform.LinuxEditor
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;
            }
        }

        /// <summary>
        /// Resolves <paramref name="rawPath"/> to a normalized absolute path.
        /// Rejects device/UNC paths, drive-relative paths, alternate data streams
        /// and anything that fails to normalize.
        /// </summary>
        internal static bool TryResolve(string rawPath, out string fullPath, out string error)
        {
            fullPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                error = "Path is empty.";
                return false;
            }

            string path = rawPath.Trim();

            if (path.IndexOf('\0') >= 0)
            {
                error = "Path contains a null character.";
                return false;
            }

            // \\server\share, \\?\C:\..., \\.\device — never valid here.
            if (path.StartsWith("\\\\", StringComparison.Ordinal) ||
                path.StartsWith("//", StringComparison.Ordinal))
            {
                error = "UNC and device paths are not allowed.";
                return false;
            }

            // A colon is only valid as a drive prefix ("C:\"). Anything else is either
            // drive-relative ("C:foo") or an alternate data stream ("file.txt:stream").
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] != ':')
                    continue;

                bool drivePrefix = i == 1 && path.Length > 2 && IsSeparator(path[2]);
                if (!drivePrefix)
                {
                    error = "Drive-relative paths and alternate data streams are not allowed.";
                    return false;
                }
            }

            try
            {
                string combined = Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(ProjectRoot, path);
                fullPath = TrimTrailingSeparators(Path.GetFullPath(combined));
            }
            catch (Exception e)
            {
                error = "Invalid path: " + e.Message;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true when <paramref name="fullPath"/> is strictly below one of
        /// <paramref name="allowedRoots"/> (project-relative), inside the project,
        /// and not under a hard-denied root.
        /// </summary>
        internal static bool IsAllowed(
            string fullPath,
            IReadOnlyList<string> allowedRoots,
            out string reason)
        {
            reason = null;

            if (string.IsNullOrEmpty(fullPath))
            {
                reason = "Path is empty.";
                return false;
            }

            string root;
            try
            {
                root = TrimTrailingSeparators(Path.GetFullPath(ProjectRoot));
            }
            catch (Exception e)
            {
                reason = "Could not resolve the project root: " + e.Message;
                return false;
            }

            var comparison = PathComparison;

            if (fullPath.Equals(root, comparison))
            {
                reason = "The project root itself cannot be deleted.";
                return false;
            }

            if (!IsStrictlyUnder(fullPath, root, comparison))
            {
                reason = "Path is outside the Unity project.";
                return false;
            }

            foreach (var denied in HardDeniedRoots)
            {
                string deniedRoot = TrimTrailingSeparators(Path.Combine(root, denied));
                if (IsSameOrUnder(fullPath, deniedRoot, comparison))
                {
                    reason = $"'{denied}/' is never deletable, even when configured.";
                    return false;
                }
            }

            if (allowedRoots == null || allowedRoots.Count == 0)
            {
                reason = "No delete roots are configured.";
                return false;
            }

            foreach (var relative in allowedRoots)
            {
                if (string.IsNullOrWhiteSpace(relative))
                    continue;

                string allowedRoot = TrimTrailingSeparators(
                    Path.Combine(root, ToPlatformPath(relative)));
                if (IsStrictlyUnder(fullPath, allowedRoot, comparison))
                    return true;
            }

            reason = "Path is not under any allowed delete root.";
            return false;
        }

        /// <summary>Convenience wrapper: resolve + allowlist check.</summary>
        internal static bool IsDeleteAllowed(
            string rawPath,
            IReadOnlyList<string> allowedRoots,
            out string reason)
        {
            if (!TryResolve(rawPath, out string fullPath, out string error))
            {
                reason = error;
                return false;
            }
            return IsAllowed(fullPath, allowedRoots, out reason);
        }

        /// <summary>
        /// Normalizes user-configured roots: project-relative, forward slashes,
        /// no <c>..</c>, no rooted paths, no hard-denied roots, de-duplicated.
        /// Pass <c>null</c> for "unset" to get the defaults; an empty list means
        /// "deny every delete" (an explicit lockdown).
        /// </summary>
        internal static IReadOnlyList<string> NormalizeAllowedRoots(IEnumerable<string> configured)
        {
            if (configured == null)
                return DefaultAllowedRoots;

            var result = new List<string>();
            var comparison = PathComparison;

            foreach (var raw in configured)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                string trimmed = raw.Trim();
                if (trimmed.StartsWith("\\\\", StringComparison.Ordinal) ||
                    trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    Path.IsPathRooted(trimmed) ||
                    trimmed.IndexOf(':') >= 0)
                    continue;

                string entry = trimmed.Replace('\\', '/').Trim('/');
                if (entry.Length == 0)
                    continue;

                var segments = entry.Split('/');
                if (segments.Any(s => s.Length == 0 || s == "." || s == ".."))
                    continue;

                if (IsHardDenied(entry))
                    continue;

                if (!result.Any(existing => string.Equals(existing, entry, comparison)))
                    result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// Scans generated code for <c>File.Delete</c> / <c>Directory.Delete</c> calls
        /// and validates each one against the allowlist. Returns a client-facing error
        /// string, or null when nothing is wrong.
        ///
        /// Only single string literals are accepted. Variables, interpolation,
        /// concatenation, <c>using static</c> and <c>FileInfo</c>/<c>DirectoryInfo</c>
        /// construction are rejected because their runtime path cannot be validated
        /// before execution.
        /// </summary>
        /// <param name="runtimeWorkingDirectory">
        /// Directory the compiled snippet will run in. Defaults to the current process
        /// directory; tests pass the Unity project root explicitly.
        /// </param>
        internal static string CheckSource(
            string code,
            IReadOnlyList<string> allowedRoots,
            string runtimeWorkingDirectory = null)
        {
            if (string.IsNullOrEmpty(code))
                return null;

            if (StaticIoImport.IsMatch(code))
            {
                return Blocked(
                    "'using static System.IO.File/Directory' cannot be checked against the delete allowlist. " +
                    "Call File.Delete(\"literal path\") or Directory.Delete(\"literal path\", true) instead.");
            }

            if (InfoConstruction.IsMatch(code))
            {
                return Blocked(
                    "FileInfo/DirectoryInfo.Delete() cannot be checked against the delete allowlist. " +
                    "Use File.Delete(\"literal path\") or Directory.Delete(\"literal path\", true) instead.");
            }

            foreach (Match match in DeleteInvocation.Matches(code))
            {
                int argumentStart = SkipWhitespace(code, match.Index + match.Length);
                if (argumentStart >= code.Length || code[argumentStart] != '(')
                {
                    return Blocked(
                        "Delete calls must be followed by a parenthesized string literal. Method groups, " +
                        "delegate references and comments between the member name and argument list cannot be " +
                        "checked against the delete allowlist; call File.Delete(\"literal path\") directly.");
                }

                if (!TryReadFirstArgument(code, argumentStart + 1, out string literal, out string error))
                    return Blocked(error);

                if (!TryResolve(literal, out string fullPath, out string resolveError))
                    return Blocked($"'{literal}' — {resolveError}");

                if (!IsAllowed(fullPath, allowedRoots, out string reason))
                    return Blocked($"'{literal}' — {reason} {DescribeRoots(allowedRoots)}");

                if (!Path.IsPathRooted(literal) &&
                    !RelativePathResolvesInsideProject(
                        literal, fullPath, runtimeWorkingDirectory, out string mismatch))
                    return Blocked($"'{literal}' — {mismatch}");
            }

            return null;
        }

        private static bool RelativePathResolvesInsideProject(
            string literal,
            string validatedPath,
            string runtimeWorkingDirectory,
            out string mismatch)
        {
            mismatch = null;
            try
            {
                // A relative literal is validated against the project root, but the
                // compiled snippet runs with the Editor's current directory. If those
                // differ, the file that gets deleted is not the file that was checked.
                string workingDirectory = string.IsNullOrEmpty(runtimeWorkingDirectory)
                    ? Environment.CurrentDirectory
                    : runtimeWorkingDirectory;
                string atRuntime = TrimTrailingSeparators(
                    Path.GetFullPath(Path.Combine(workingDirectory, literal)));
                if (!atRuntime.Equals(validatedPath, PathComparison))
                {
                    mismatch =
                        "Relative paths are validated against the project root, but this one resolves elsewhere at runtime " +
                        $"({atRuntime}). Use an absolute path literal instead.";
                    return false;
                }
            }
            catch (Exception e)
            {
                mismatch = "Could not resolve the relative path: " + e.Message;
                return false;
            }
            return true;
        }

        private static bool TryReadFirstArgument(
            string code,
            int index,
            out string value,
            out string error)
        {
            value = null;
            error = null;

            int i = SkipWhitespace(code, index);
            if (i >= code.Length)
            {
                error = "Missing path argument in the delete call.";
                return false;
            }

            if (code[i] == '$')
            {
                error = "Interpolated strings cannot be checked against the delete allowlist. Use a plain string literal.";
                return false;
            }

            bool verbatim = false;
            if (code[i] == '@')
            {
                verbatim = true;
                i++;
            }

            if (i >= code.Length || code[i] != '"')
            {
                error = "The delete path must be a single string literal so the allowlist can be checked " +
                        "(variables, Path.Combine and concatenation are not allowed).";
                return false;
            }

            i++;
            var builder = new StringBuilder();
            bool closed = false;

            while (i < code.Length)
            {
                char c = code[i];

                if (verbatim)
                {
                    if (c == '"')
                    {
                        if (i + 1 < code.Length && code[i + 1] == '"')
                        {
                            builder.Append('"');
                            i += 2;
                            continue;
                        }
                        i++;
                        closed = true;
                        break;
                    }

                    builder.Append(c);
                    i++;
                    continue;
                }

                if (c == '\\')
                {
                    if (i + 1 >= code.Length)
                        break;

                    char escaped = code[i + 1];
                    switch (escaped)
                    {
                        case '\\': builder.Append('\\'); break;
                        case '"': builder.Append('"'); break;
                        case '\'': builder.Append('\''); break;
                        case '0': builder.Append('\0'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        default:
                            error = "Unsupported escape sequence '\\" + escaped +
                                    "' in the delete path literal; use a verbatim string (@\"...\").";
                            return false;
                    }

                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    i++;
                    closed = true;
                    break;
                }

                if (c == '\n' || c == '\r')
                    break;

                builder.Append(c);
                i++;
            }

            if (!closed)
            {
                error = "Unterminated string literal in the delete path argument.";
                return false;
            }

            int after = SkipWhitespace(code, i);
            if (after < code.Length && code[after] != ',' && code[after] != ')')
            {
                error = "The delete path must be a single string literal (concatenation and expressions are not allowed).";
                return false;
            }

            value = builder.ToString();
            return true;
        }

        private static int SkipWhitespace(string code, int index)
        {
            while (index < code.Length && char.IsWhiteSpace(code[index]))
                index++;
            return index;
        }

        private static string Blocked(string message)
        {
            return BlockedPrefix + message;
        }

        private static string DescribeRoots(IReadOnlyList<string> roots)
        {
            if (roots == null || roots.Count == 0)
                return "Allowed delete roots: none (all deletes are blocked).";
            return "Allowed delete roots: " + string.Join(", ", roots) + ".";
        }

        private static bool IsHardDenied(string relativeEntry)
        {
            foreach (var denied in HardDeniedRoots)
            {
                if (relativeEntry.Equals(denied, PathComparison) ||
                    relativeEntry.StartsWith(denied + "/", PathComparison))
                    return true;
            }
            return false;
        }

        private static bool IsStrictlyUnder(string path, string root, StringComparison comparison)
        {
            return path.Length > root.Length + 1
                   && path.StartsWith(root, comparison)
                   && IsSeparator(path[root.Length]);
        }

        private static bool IsSameOrUnder(string path, string root, StringComparison comparison)
        {
            return path.Equals(root, comparison) || IsStrictlyUnder(path, root, comparison);
        }

        private static string TrimTrailingSeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 ? path : trimmed;
        }

        private static string ToPlatformPath(string relative)
        {
            return relative.Replace('/', Path.DirectorySeparatorChar)
                           .Replace('\\', Path.DirectorySeparatorChar);
        }

        private static bool IsSeparator(char c)
        {
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }
    }
}
