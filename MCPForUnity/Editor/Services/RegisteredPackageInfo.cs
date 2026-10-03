using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Cross-version snapshot of an installed package.
    ///
    /// Unity 2021.2+ has PackageInfo.GetAllRegisteredPackages() (synchronous).
    /// Unity 2020.3 has no synchronous "list all packages" API: PackageManager.Client.List
    /// only completes on the main thread, so blocking on it from a tool handler would
    /// deadlock the editor. Instead we read the authoritative Packages/packages-lock.json
    /// (pure synchronous file I/O, no editor pumping required) and map entries here.
    /// </summary>
    public sealed class RegisteredPackageInfo
    {
        public string name;
        public string version;
        public string displayName;
        public PackageSource source;
        public string packageId;
        public string description;
        public string resolvedPath;
        public AuthorInfo author;
        public List<DependencyInfo> dependencies = new List<DependencyInfo>();

        public struct AuthorInfo
        {
            public string name;
        }

        public struct DependencyInfo
        {
            public string name;
            public string version;
        }

        /// <summary>Read the project's resolved package list without editor pumping.</summary>
        public static RegisteredPackageInfo[] GetRegisteredPackages()
        {
#if UNITY_2021_2_OR_NEWER
            return PackageInfo.GetAllRegisteredPackages()
                .Select(p => new RegisteredPackageInfo
                {
                    name = p.name,
                    version = p.version,
                    displayName = p.displayName,
                    source = p.source,
                    packageId = p.packageId,
                    description = p.description,
                    resolvedPath = p.resolvedPath,
                    author = p.author != null
                        ? new AuthorInfo { name = p.author.name }
                        : default,
                    dependencies = p.dependencies
                        .Select(d => new DependencyInfo { name = d.name, version = d.version })
                        .ToList()
                })
                .ToArray();
#else
            try
            {
                // Packages/packages-lock.json is maintained by Package Manager itself and
                // contains every resolved package (direct + transitive) with version, source
                // and direct dependencies. Reading it is synchronous and side-effect free.
                string lockPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "packages-lock.json");
                if (!File.Exists(lockPath))
                {
                    return new RegisteredPackageInfo[0];
                }

                JObject root = JObject.Parse(File.ReadAllText(lockPath));
                JObject deps = root["dependencies"] as JObject;
                if (deps == null)
                {
                    return new RegisteredPackageInfo[0];
                }

                var result = new List<RegisteredPackageInfo>();
                foreach (var kv in deps)
                {
                    JObject entry = kv.Value as JObject;
                    if (entry == null) continue;

                    var info = new RegisteredPackageInfo { name = kv.Key };
                    info.version = entry["version"]?.ToString() ?? string.Empty;
                    info.displayName = kv.Key;
                    PopulateMetadata(info);

                    string source = entry["source"]?.ToString() ?? "registry";
                    switch (source)
                    {
                        case "builtin": info.source = PackageSource.BuiltIn; break;
                        case "embedded": info.source = PackageSource.Embedded; break;
                        case "git": info.source = PackageSource.Git; break;
                        case "local": info.source = PackageSource.Local; break;
                        case "localTarball": info.source = PackageSource.LocalTarball; break;
                        case "tarball": info.source = PackageSource.LocalTarball; break;
                        default: info.source = PackageSource.Registry; break;
                    }

                    // Best-effort packageId: registry/git entries carry a url; local/embedded
                    // entries use "file:" + path when resolvable, otherwise name@version.
                    string url = entry["url"]?.ToString();
                    if (!string.IsNullOrEmpty(url))
                    {
                        info.packageId = info.source == PackageSource.Git
                            ? url + "#" + info.version
                            : url;
                    }
                    else
                    {
                        info.packageId = string.IsNullOrEmpty(info.version)
                            ? info.name
                            : info.name + "@" + info.version;
                    }

                    JObject depObj = entry["dependencies"] as JObject;
                    if (depObj != null)
                    {
                        foreach (var d in depObj)
                        {
                            info.dependencies.Add(new DependencyInfo
                            {
                                name = d.Key,
                                version = d.Value?.ToString() ?? string.Empty
                            });
                        }
                    }

                    result.Add(info);
                }
                return result.ToArray();
            }
            catch
            {
                return new RegisteredPackageInfo[0];
            }
#endif
        }

        /// <summary>
        /// Best-effort metadata (description/author/resolvedPath) for 2020.3:
        /// packages-lock.json does not carry them, so read the package's own
        /// package.json when it exists on disk. Failures are non-fatal.
        /// </summary>
        private static void PopulateMetadata(RegisteredPackageInfo info)
        {
            try
            {
                string pkgJsonPath = null;
                string localPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", info.name, "package.json");
                if (File.Exists(localPath))
                {
                    pkgJsonPath = localPath;
                    info.resolvedPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", info.name);
                }
                else
                {
                    string cacheDir = Path.Combine(Directory.GetCurrentDirectory(), "Library", "PackageCache");
                    if (Directory.Exists(cacheDir))
                    {
                        string versioned = info.name + "@" + info.version;
                        string cachePath = Path.Combine(cacheDir, versioned, "package.json");
                        if (File.Exists(cachePath))
                        {
                            pkgJsonPath = cachePath;
                            info.resolvedPath = Path.Combine(cacheDir, versioned);
                        }
                        else
                        {
                            // PackageCache uses "-<hash>" suffixes for some sources; scan prefix match.
                            string prefix = info.name + "@";
                            foreach (string dir in Directory.GetDirectories(cacheDir))
                            {
                                if (dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                {
                                    string cand = Path.Combine(dir, "package.json");
                                    if (File.Exists(cand))
                                    {
                                        pkgJsonPath = cand;
                                        info.resolvedPath = dir;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (pkgJsonPath != null)
                {
                    JObject pkg = JObject.Parse(File.ReadAllText(pkgJsonPath));
                    info.description = pkg["description"]?.ToString() ?? string.Empty;
                    JObject authorObj = pkg["author"] as JObject;
                    if (authorObj != null)
                    {
                        info.author = new AuthorInfo { name = authorObj["name"]?.ToString() ?? string.Empty };
                    }
                    else
                    {
                        string authorStr = pkg["author"]?.ToString();
                        if (!string.IsNullOrEmpty(authorStr) && !authorStr.StartsWith("{"))
                        {
                            info.author = new AuthorInfo { name = authorStr };
                        }
                    }
                }
            }
            catch
            {
                // Non-fatal: metadata stays empty.
            }
        }

    }
}
