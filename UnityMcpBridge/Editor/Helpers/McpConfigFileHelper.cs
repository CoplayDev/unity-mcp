using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Shared helpers for reading and writing MCP client configuration files.
    /// Consolidates file atomics and server directory resolution so the editor
    /// window can focus on UI concerns only.
    /// </summary>
    public static class McpConfigFileHelper
    {
        public static string ExtractDirectoryArg(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--directory", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        public static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                string na = Path.GetFullPath(a.Trim());
                string nb = Path.GetFullPath(b.Trim());
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
                }
                return string.Equals(na, nb, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Resolves the server directory to use for MCP tools, preferring
        /// existing config values and falling back to installed/embedded copies.
        /// </summary>
        public static string ResolveServerDirectory(string pythonDir, string[] existingArgs)
        {
            string serverSrc = ExtractDirectoryArg(existingArgs);
            bool serverValid = !string.IsNullOrEmpty(serverSrc)
                && File.Exists(Path.Combine(serverSrc, "server.py"));
            if (!serverValid)
            {
                if (!string.IsNullOrEmpty(pythonDir)
                    && File.Exists(Path.Combine(pythonDir, "server.py")))
                {
                    serverSrc = pythonDir;
                }
                else
                {
                    serverSrc = ResolveServerSource();
                }
            }

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && !string.IsNullOrEmpty(serverSrc))
                {
                    string norm = serverSrc.Replace('\\', '/');
                    int idx = norm.IndexOf("/.local/share/UnityMCP/", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal) ?? string.Empty;
                        string suffix = norm.Substring(idx + "/.local/share/".Length);
                        serverSrc = Path.Combine(home, "Library", "Application Support", suffix);
                    }
                }
            }
            catch
            {
                // Ignore failures and fall back to the original path.
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && !string.IsNullOrEmpty(serverSrc)
                && serverSrc.IndexOf(@"\Library\PackageCache\", StringComparison.OrdinalIgnoreCase) >= 0
                && !EditorPrefs.GetBool("MCPForUnity.UseEmbeddedServer", false))
            {
                serverSrc = ServerInstaller.GetServerPath();
            }

            return serverSrc;
        }

        public static void WriteAtomicFile(string path, string contents)
        {
            string tmp = path + ".tmp";
            string backup = path + ".backup";
            bool writeDone = false;
            try
            {
                File.WriteAllText(tmp, contents, new UTF8Encoding(false));
                try
                {
                    File.Replace(tmp, path, backup);
                    writeDone = true;
                }
                catch (FileNotFoundException)
                {
                    File.Move(tmp, path);
                    writeDone = true;
                }
                catch (PlatformNotSupportedException)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            if (File.Exists(backup)) File.Delete(backup);
                        }
                        catch { }
                        File.Move(path, backup);
                    }
                    File.Move(tmp, path);
                    writeDone = true;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (!writeDone && File.Exists(backup))
                    {
                        try { File.Copy(backup, path, true); } catch { }
                    }
                }
                catch { }
                throw new Exception($"Failed to write config file '{path}': {ex.Message}", ex);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                try { if (writeDone && File.Exists(backup)) File.Delete(backup); } catch { }
            }
        }

        public static string ResolveServerSource()
        {
            try
            {
                string remembered = EditorPrefs.GetString("MCPForUnity.ServerSrc", string.Empty);
                if (!string.IsNullOrEmpty(remembered)
                    && File.Exists(Path.Combine(remembered, "server.py")))
                {
                    return remembered;
                }

                ServerInstaller.EnsureServerInstalled();
                string installed = ServerInstaller.GetServerPath();
                if (File.Exists(Path.Combine(installed, "server.py")))
                {
                    return installed;
                }

                bool useEmbedded = EditorPrefs.GetBool("MCPForUnity.UseEmbeddedServer", false);
                if (useEmbedded
                    && ServerPathResolver.TryFindEmbeddedServerSource(out string embedded)
                    && File.Exists(Path.Combine(embedded, "server.py")))
                {
                    return embedded;
                }

                return installed;
            }
            catch
            {
                return ServerInstaller.GetServerPath();
            }
        }
    }
}

