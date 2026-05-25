using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WTL.InternalTerminal.Editor
{
    internal static class InternalTerminalProcess
    {
        public static ProcessStartInfo CreateStartInfo(
            string executable,
            string arguments,
            string workingDirectory,
            bool redirectOutput)
        {
            var resolvedExecutable = ResolveExecutable(executable);
            var isCommandScript = IsCommandScript(resolvedExecutable);
            var startInfo = new ProcessStartInfo
            {
                FileName = isCommandScript ? ResolveExecutable("cmd.exe") : resolvedExecutable,
                Arguments = isCommandScript ? $"/d /s /c \"\"{resolvedExecutable}\" {arguments}\"" : arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (redirectOutput)
            {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
            }

            return startInfo;
        }

        public static string ResolveExecutable(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable))
            {
                throw new FileNotFoundException("Executable path is empty.");
            }

            var expanded = Environment.ExpandEnvironmentVariables(executable);
            if (Path.IsPathRooted(expanded))
            {
                foreach (var candidate in GetRootedExecutableCandidates(expanded))
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                if (File.Exists(expanded))
                {
                    return expanded;
                }

                throw new FileNotFoundException($"Executable not found: {expanded}");
            }

            foreach (var candidate in GetExecutableCandidates(expanded))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException($"Executable '{executable}' was not found. Set the full path in Preferences > WTL > Internal Terminal.");
        }

        private static IEnumerable<string> GetRootedExecutableCandidates(string executable)
        {
            if (Path.HasExtension(executable))
            {
                yield break;
            }

            yield return executable + ".exe";
            yield return executable + ".cmd";
            yield return executable + ".bat";
        }

        private static IEnumerable<string> GetExecutableCandidates(string executable)
        {
            foreach (var directory in GetSearchDirectories())
            {
                if (!Path.HasExtension(executable))
                {
                    yield return Path.Combine(directory, executable + ".exe");
                    yield return Path.Combine(directory, executable + ".cmd");
                    yield return Path.Combine(directory, executable + ".bat");
                }

                yield return Path.Combine(directory, executable);
            }
        }

        private static IEnumerable<string> GetSearchDirectories()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddIfExists(seen, Environment.GetEnvironmentVariable("PATH"));
            AddDirectory(seen, @"C:\Program Files\nodejs");
            AddDirectory(seen, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
            AddDirectory(seen, Environment.SystemDirectory);

            return seen;
        }

        private static void AddIfExists(HashSet<string> directories, string pathValue)
        {
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return;
            }

            foreach (var rawPath in pathValue.Split(Path.PathSeparator))
            {
                AddDirectory(directories, rawPath);
            }
        }

        private static void AddDirectory(HashSet<string> directories, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var expanded = Environment.ExpandEnvironmentVariables(directory.Trim());
            if (Directory.Exists(expanded))
            {
                directories.Add(expanded);
            }
        }

        private static bool IsCommandScript(string executable)
        {
            var extension = Path.GetExtension(executable);
            return string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);
        }
    }
}
