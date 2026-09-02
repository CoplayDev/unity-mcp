using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.Blender;
using MCPForUnity.Editor.Tools.AssetGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.Blender
{
    /// <summary>
    /// Lets the Unity Editor talk straight to the BlenderMCP addon socket, so a Blender → Unity
    /// handoff is one call (export → import through the shared model pipeline → place → normalize)
    /// instead of an AI-orchestrated multi-step dance. Also drives the Blender Bridge panel in the
    /// Asset Gen tab and the Window/MCP for Unity/Blender Bridge menu. Carries no API keys.
    /// Socket and git work runs on the thread pool; Unity API calls happen after the await, back on
    /// the editor thread (the bridge awaits handlers on Unity's synchronization context).
    /// </summary>
    [McpForUnityTool("blender_bridge", AutoRegister = false, Group = "asset_gen",
        Description = "Bridge to a running Blender with the BlenderMCP addon: status, scene/object info, viewport " +
                      "screenshot, run Python in Blender, import a model (export → import → place → normalize), " +
                      "check the blender-mcp checkout for updates, and sync its addon into Blender.")]
    public static class BlenderBridgeTool
    {
        private static readonly string[] ValidActions =
            { "status", "scene_info", "object_info", "screenshot", "run_python", "import_model", "check_updates", "sync_addon" };

        private const string NotConfiguredMessage =
            "The blender-mcp checkout is not set. Open Window > MCP for Unity > Generative > Blender Bridge " +
            "and pick the folder that contains addon.py.";

        /// <summary>Entry point for the bridge: validates parameters, dispatches by action, and never throws.</summary>
        public static async Task<object> HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            string action = (p.Get("action") ?? "status").Trim().ToLowerInvariant();
            int timeout = Math.Max(5, p.GetInt("timeout_seconds", 180) ?? 180);

            try
            {
                switch (action)
                {
                    case "status": return await StatusAsync();
                    case "scene_info":
                        return new SuccessResponse("Retrieved Blender scene info.",
                            await BlenderSocketClient.SendAsync(BlenderBridgePrefs.Endpoint, "get_scene_info", null, timeout));
                    case "object_info":
                    {
                        string objectName = p.Get("object_name");
                        if (string.IsNullOrWhiteSpace(objectName))
                            return new ErrorResponse("'object_name' is required for object_info.");
                        JToken info = await BlenderSocketClient.SendAsync(BlenderBridgePrefs.Endpoint, "get_object_info",
                            new JObject { ["object_name"] = objectName }, timeout);
                        return new SuccessResponse($"Retrieved info for '{objectName}'.", info);
                    }
                    case "screenshot": return await ScreenshotAsync(p, timeout);
                    case "run_python":
                    {
                        string code = p.Get("code");
                        if (string.IsNullOrWhiteSpace(code)) return new ErrorResponse("'code' is required for run_python.");
                        string stdout = await BlenderSocketClient.RunPythonAsync(BlenderBridgePrefs.Endpoint, code, timeout);
                        return new SuccessResponse("Executed Python in Blender.", new { stdout });
                    }
                    case "import_model": return await ImportModelAsync(p, timeout);
                    case "check_updates": return await CheckUpdatesAsync();
                    case "sync_addon": return SyncAddon(p.GetBool("force", false));
                    default:
                        return new ErrorResponse($"Unknown action '{action}'. Valid: {string.Join(", ", ValidActions)}.");
                }
            }
            catch (BlenderUnavailableException e)
            {
                return new ErrorResponse(e.Message);
            }
            catch (Exception e)
            {
                return new ErrorResponse(SecretRedactor.Scrub($"blender_bridge '{action}' failed: {e.Message}"));
            }
        }

        // ------------------------------------------------------------------ status

        /// <summary>Probes the addon socket and reports checkout / installed-addon state without failing.</summary>
        private static async Task<object> StatusAsync()
        {
            BlenderEndpoint endpoint = BlenderBridgePrefs.Endpoint;
            string forkAddon = BlenderBridgePrefs.ForkAddonPath;
            string installedAddon = BlenderBridgePrefs.InstalledAddonPath;
            bool forkConfigured = BlenderBridgePrefs.IsForkConfigured;
            string forkPath = BlenderBridgePrefs.ForkPath;
            bool blenderInstalled = BlenderDetection.IsInstalled();

            var (reachable, error) = await BlenderSocketClient.ProbeAsync(endpoint);
            JToken scene = null;
            if (reachable)
            {
                try { scene = await BlenderSocketClient.SendAsync(endpoint, "get_scene_info", null, 10); }
                catch (Exception e) { error = e.Message; }
            }

            string forkMd5 = forkAddon != null && File.Exists(forkAddon) ? FileMd5(forkAddon) : null;
            string installedMd5 = installedAddon != null && File.Exists(installedAddon) ? FileMd5(installedAddon) : null;

            var data = new JObject
            {
                ["blender_reachable"] = reachable,
                ["blender_installed"] = blenderInstalled,
                ["endpoint"] = endpoint.ToString(),
                ["error"] = reachable ? null : error,
                ["scene_name"] = scene?["name"],
                ["object_count"] = scene?["object_count"],
                ["fork_configured"] = forkConfigured,
                ["fork_path"] = forkPath,
                ["fork_addon_found"] = forkMd5 != null,
                ["installed_addon_path"] = installedAddon,
                ["installed_addon_found"] = installedMd5 != null,
                ["addon_in_sync"] = forkMd5 != null && installedMd5 != null && forkMd5 == installedMd5,
            };
            string msg = reachable
                ? $"Blender reachable. Scene '{scene?["name"]}' with {scene?["object_count"]} objects."
                : "Blender not reachable.";
            return new SuccessResponse(msg, data);
        }

        // -------------------------------------------------------------- screenshot

        /// <summary>Asks Blender for an offscreen viewport render and optionally copies it under Assets/.</summary>
        private static async Task<object> ScreenshotAsync(ToolParams p, int timeout)
        {
            int maxSize = Math.Max(64, p.GetInt("max_size", 1000) ?? 1000);
            string outputFolder = p.Get("output_folder");

            string assetsRelativeFolder = null;
            if (!string.IsNullOrWhiteSpace(outputFolder))
            {
                // Canonicalizes and rejects anything that does not resolve under Assets/ (e.g. "Assets/../x").
                if (!AssetGenPaths.NormalizeOutputFolder(outputFolder, out assetsRelativeFolder, out string folderError))
                    return new ErrorResponse(folderError);
            }

            string dir = Path.Combine(ProjectRoot(), "Library", "BlenderBridge");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"blender_viewport_{UniqueSuffix()}.png").Replace('\\', '/');

            JToken result = await BlenderSocketClient.SendAsync(BlenderBridgePrefs.Endpoint, "get_viewport_screenshot",
                new JObject { ["max_size"] = maxSize, ["filepath"] = file, ["format"] = "png" }, timeout);

            if (!File.Exists(file))
                return new ErrorResponse($"Blender did not write a screenshot to {file}. Response: {result}");

            string assetPath = null;
            if (assetsRelativeFolder != null)
            {
                string absDir = AssetGenPaths.ToAbsolute(assetsRelativeFolder);
                Directory.CreateDirectory(absDir);
                assetPath = $"{assetsRelativeFolder}/{Path.GetFileName(file)}";
                File.Copy(file, Path.Combine(absDir, Path.GetFileName(file)), true);
                AssetDatabase.ImportAsset(assetPath);
            }

            return new SuccessResponse("Captured Blender viewport.", new JObject
            {
                ["path"] = file,
                ["asset_path"] = assetPath,
                ["width"] = result?["width"],
                ["height"] = result?["height"],
                ["method"] = result?["method"],
            });
        }

        // ------------------------------------------------------------ import_model

        /// <summary>Exports from Blender, imports through the shared pipeline, then places and normalizes the instance.</summary>
        private static async Task<object> ImportModelAsync(ToolParams p, int timeout)
        {
            string fmt = (p.Get("format") ?? "glb").Trim().ToLowerInvariant();
            if (fmt != "glb" && fmt != "fbx") return new ErrorResponse("'format' must be glb or fbx.");

            string[] names = p.GetStringArray("object_names")?.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            bool selectionOnly = p.GetBool("selection_only", false);
            bool applyModifiers = p.GetBool("apply_modifiers", true);
            bool place = p.GetBool("place_in_scene", true);
            float target = Mathf.Max(0f, p.GetFloat("target_size", 0f) ?? 0f);
            string outputFolder = p.Get("output_folder");
            string animationType = p.Get("animation_type");

            string name = p.Get("name");
            if (string.IsNullOrWhiteSpace(name))
                name = names != null && names.Length == 1 ? names[0] : "BlenderModel";
            name = SanitizeName(name);

            // 1. Export from Blender to a temp file (off the editor thread).
            string exportDir = Path.Combine(Path.GetTempPath(), "BlenderBridge");
            Directory.CreateDirectory(exportDir);
            string exportPath = Path.Combine(exportDir, $"{name}_{UniqueSuffix()}.{fmt}").Replace('\\', '/');

            string script = BuildExportScript(exportPath, names, selectionOnly, applyModifiers, fmt);
            string stdout = await BlenderSocketClient.RunPythonAsync(BlenderBridgePrefs.Endpoint, script, timeout);

            if (!File.Exists(exportPath))
                return new ErrorResponse($"Blender did not produce {exportPath}. Blender output: {Truncate(stdout, 800)}");

            // 2. Import through the shared pipeline (staging under Assets/, glTFast/FBX, material setup).
            // target_size 0 is passed through: the pipeline only rescales when it is > 0.
            var importParams = new JObject
            {
                ["sourcePath"] = exportPath,
                ["name"] = name,
                ["targetSize"] = target,
            };
            if (!string.IsNullOrWhiteSpace(outputFolder)) importParams["outputFolder"] = outputFolder;
            if (!string.IsNullOrWhiteSpace(animationType)) importParams["animationType"] = animationType;

            JObject importResult = JObject.FromObject(ImportModelFile.HandleCommand(importParams));
            if (!(importResult.Value<bool?>("success") ?? false))
                return new ErrorResponse($"Import failed: {importResult["error"] ?? importResult["message"]}");

            string assetPath = importResult["data"]?["asset_path"]?.ToString();
            var data = new JObject
            {
                ["asset_path"] = assetPath,
                ["asset_guid"] = importResult["data"]?["asset_guid"],
                ["export_path"] = exportPath,
                ["format"] = fmt,
                ["blender_output"] = Truncate(stdout, 400),
                ["placed"] = false,
            };

            if (!place || string.IsNullOrEmpty(assetPath))
                return new SuccessResponse($"Imported {assetPath} (not placed).", data);

            // 3. Place in the open scene and normalize size from measured bounds. Blender exports
            // commonly land far off scale, so measuring the placed instance beats trusting the importer.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                data["note"] = "Asset has no GameObject root to instantiate.";
                return new SuccessResponse($"Imported {assetPath} but could not instantiate it.", data);
            }

            GameObject go = PrefabUtility.InstantiatePrefab(prefab) as GameObject ?? UnityEngine.Object.Instantiate(prefab);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "Import from Blender");
            go.transform.position = ParsePosition(p.GetRaw("position"));

            float scaleFactor = 1f;
            if (target > 0f && TryGetWorldBounds(go, out Bounds b0))
            {
                float maxDim = Mathf.Max(b0.size.x, Mathf.Max(b0.size.y, b0.size.z));
                if (maxDim > 1e-4f)
                {
                    scaleFactor = target / maxDim;
                    go.transform.localScale *= scaleFactor;
                }
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            data["placed"] = true;
            data["game_object"] = go.name;
            data["scene"] = go.scene.name;
            data["scale_factor_applied"] = scaleFactor;
            if (TryGetWorldBounds(go, out Bounds b1))
            {
                data["bounds_size"] = new JArray(b1.size.x, b1.size.y, b1.size.z);
                data["bounds_center"] = new JArray(b1.center.x, b1.center.y, b1.center.z);
            }
            return new SuccessResponse($"Imported {assetPath} and placed '{go.name}' in the scene.", data);
        }

        /// <summary>
        /// Builds the Python that Blender runs to export. All caller-controlled values travel inside one
        /// JSON document embedded as a single Python string literal, so names with quotes or
        /// placeholder-looking text can never alter the program.
        /// </summary>
        internal static string BuildExportScript(string outPath, string[] names, bool selectionOnly, bool applyModifiers, string fmt)
        {
            var config = new JObject
            {
                ["out"] = outPath,
                ["names"] = names == null ? new JArray() : new JArray(names.Cast<object>().ToArray()),
                ["selection_only"] = selectionOnly,
                ["apply_modifiers"] = applyModifiers,
                ["format"] = fmt,
            };
            // JSON string escaping is a subset of Python string-literal escaping, so the encoded
            // document is a valid double-quoted Python literal.
            string configLiteral = JsonConvert.ToString(config.ToString(Formatting.None));

            const string template = @"
import bpy, os, json
cfg = json.loads(__CFG__)
out = cfg['out']
names = cfg['names']
selection_only = cfg['selection_only']
apply_mods = cfg['apply_modifiers']
fmt = cfg['format']
os.makedirs(os.path.dirname(out), exist_ok=True)
try:
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
except Exception:
    pass
use_sel = False
if names:
    missing = [n for n in names if bpy.data.objects.get(n) is None]
    if missing:
        raise Exception('Objects not found in Blender: ' + ', '.join(missing))
    bpy.ops.object.select_all(action='DESELECT')
    for n in names:
        o = bpy.data.objects[n]
        o.select_set(True)
        for c in o.children_recursive:
            c.select_set(True)
    bpy.context.view_layer.objects.active = bpy.data.objects[names[0]]
    use_sel = True
elif selection_only:
    if not bpy.context.selected_objects:
        raise Exception('Nothing is selected in Blender and no object_names were given.')
    use_sel = True
if fmt == 'glb':
    bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=use_sel,
                              use_active_scene=True, export_apply=apply_mods,
                              export_animations=True, export_skins=True, export_morph=True,
                              export_yup=True)
else:
    bpy.ops.export_scene.fbx(filepath=out, use_selection=use_sel, apply_unit_scale=True,
                             bake_space_transform=apply_mods, use_mesh_modifiers=apply_mods,
                             path_mode='COPY', embed_textures=True)
print(json.dumps({'path': out, 'bytes': os.path.getsize(out), 'selection_only': use_sel,
                  'exported': [o.name for o in (bpy.context.selected_objects if use_sel else bpy.context.scene.objects)]}))
";
            int at = template.IndexOf("__CFG__", StringComparison.Ordinal);
            return template.Substring(0, at) + configLiteral + template.Substring(at + "__CFG__".Length);
        }

        // ----------------------------------------------------------- check_updates

        /// <summary>Fetches the checkout's remotes on the thread pool and reports how far behind it is.</summary>
        private static async Task<object> CheckUpdatesAsync()
        {
            if (!BlenderBridgePrefs.IsForkConfigured) return new ErrorResponse(NotConfiguredMessage);
            string fork = BlenderBridgePrefs.ForkPath;
            string forkAddon = BlenderBridgePrefs.ForkAddonPath;
            string installedAddon = BlenderBridgePrefs.InstalledAddonPath;
            if (!Directory.Exists(Path.Combine(fork, ".git")))
                return new ErrorResponse($"'{fork}' is not a git checkout (no .git folder); check_updates needs one.");

            return await Task.Run(() => CheckUpdatesBlocking(fork, forkAddon, installedAddon));
        }

        /// <summary>Git and file work for check_updates; touches no Unity API so it can run on any thread.</summary>
        private static object CheckUpdatesBlocking(string fork, string forkAddon, string installedAddon)
        {
            if (!TryGit(fork, "--version", out _, out string gitErr, 10000))
                return new ErrorResponse($"git is not available: {gitErr}");

            TryGit(fork, "log -1 --format=%h%x09%ad%x09%s --date=short", out string head, out _);
            TryGit(fork, "status --porcelain", out string porcelain, out _);
            TryGit(fork, "remote", out string remotesRaw, out _);
            var remotes = remotesRaw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim()).Where(r => r.Length > 0).ToList();

            var perRemote = new JArray();
            int totalBehind = 0;
            foreach (string remote in remotes.OrderBy(r => r == "upstream" ? 0 : r == "origin" ? 1 : 2))
            {
                var entry = new JObject { ["remote"] = remote };
                TryGit(fork, $"remote get-url {remote}", out string url, out _);
                entry["url"] = RedactRemoteUrl(url.Trim());

                bool fetched = TryGit(fork, $"fetch --quiet {remote}", out _, out string fetchErr, 90000);
                entry["fetched"] = fetched;
                if (!fetched) entry["fetch_error"] = Truncate(fetchErr, 300);

                string branch = "main";
                if (TryGit(fork, $"symbolic-ref --short refs/remotes/{remote}/HEAD", out string sym, out _) && sym.Trim().Contains('/'))
                    branch = sym.Trim().Substring(sym.Trim().IndexOf('/') + 1);
                else if (!TryGit(fork, $"rev-parse --verify --quiet refs/remotes/{remote}/main", out _, out _)
                         && TryGit(fork, $"rev-parse --verify --quiet refs/remotes/{remote}/master", out _, out _))
                    branch = "master";
                entry["branch"] = branch;

                if (TryGit(fork, $"rev-list --left-right --count HEAD...{remote}/{branch}", out string counts, out string cErr))
                {
                    var parts = counts.Trim().Split('\t', ' ');
                    int ahead = parts.Length > 0 && int.TryParse(parts[0], out int a) ? a : 0;
                    int behind = parts.Length > 1 && int.TryParse(parts[1], out int bb) ? bb : 0;
                    entry["local_ahead"] = ahead;
                    entry["behind"] = behind;
                    if (remote == "upstream" || remotes.Count == 1) totalBehind += behind;

                    TryGit(fork, $"log --format=%h%x20%ad%x20%s --date=short -n 20 HEAD..{remote}/{branch}", out string log, out _);
                    entry["new_commits"] = new JArray(log.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else
                {
                    entry["error"] = Truncate(cErr, 300);
                }
                perRemote.Add(entry);
            }

            string forkMd5 = File.Exists(forkAddon) ? FileMd5(forkAddon) : null;
            string installedMd5 = installedAddon != null && File.Exists(installedAddon) ? FileMd5(installedAddon) : null;
            bool addonInSync = forkMd5 != null && forkMd5 == installedMd5;

            int dirty = porcelain.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var recommendations = new List<string>();
            if (totalBehind > 0) recommendations.Add($"Checkout is {totalBehind} commit(s) behind upstream: merge upstream into it.");
            if (!addonInSync) recommendations.Add("Installed Blender addon differs from the checkout's addon.py: run sync_addon, then restart Blender.");
            if (dirty > 0) recommendations.Add($"Checkout has {dirty} uncommitted change(s).");
            if (recommendations.Count == 0) recommendations.Add("Everything is up to date.");

            var data = new JObject
            {
                ["fork_path"] = fork,
                ["head"] = head.Trim(),
                ["uncommitted_changes"] = dirty,
                ["remotes"] = perRemote,
                ["fork_addon_md5"] = forkMd5,
                ["installed_addon_path"] = installedAddon,
                ["installed_addon_md5"] = installedMd5,
                ["addon_in_sync"] = addonInSync,
                ["recommendations"] = new JArray(recommendations),
            };
            return new SuccessResponse(string.Join(" ", recommendations), data);
        }

        // -------------------------------------------------------------- sync_addon

        /// <summary>Copies the checkout's addon.py over Blender's installed copy, keeping a .bak of the old file.</summary>
        private static object SyncAddon(bool force)
        {
            if (!BlenderBridgePrefs.IsForkConfigured) return new ErrorResponse(NotConfiguredMessage);
            string src = BlenderBridgePrefs.ForkAddonPath;
            string dst = BlenderBridgePrefs.InstalledAddonPath;
            if (!File.Exists(src)) return new ErrorResponse($"addon.py not found in the checkout at {src}.");
            if (dst == null)
                return new ErrorResponse("Could not locate Blender's user addons directory. Set it in Window > MCP for Unity > Generative > Blender Bridge.");

            string srcMd5 = FileMd5(src);
            string dstMd5 = File.Exists(dst) ? FileMd5(dst) : null;
            if (!force && srcMd5 == dstMd5)
                return new SuccessResponse("Installed addon already matches the checkout; nothing copied.",
                    new { source = src, destination = dst, md5 = srcMd5, copied = false });

            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            string backup = null;
            if (File.Exists(dst))
            {
                backup = dst + ".bak";
                File.Copy(dst, backup, true);
            }
            File.Copy(src, dst, true);

            return new SuccessResponse(
                "Copied addon.py into Blender. Restart Blender (or Reload Scripts) and press 'Connect to MCP server' again.",
                new { source = src, destination = dst, backup, previous_md5 = dstMd5, new_md5 = srcMd5, copied = true });
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Runs one git command with no prompts; false on non-zero exit, timeout, or missing git.</summary>
        private static bool TryGit(string workingDir, string args, out string stdout, out string stderr, int timeoutMs = 30000)
        {
            stdout = string.Empty;
            stderr = string.Empty;
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                using var proc = Process.Start(psi);
                if (proc == null) { stderr = "failed to start git"; return false; }
                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { /* already gone */ }
                    stderr = $"git {args} timed out after {timeoutMs} ms";
                    return false;
                }
                stdout = outTask.Result;
                stderr = errTask.Result;
                return proc.ExitCode == 0;
            }
            catch (Exception e)
            {
                stderr = e.Message;
                return false;
            }
        }

        /// <summary>
        /// Timestamp plus a random tail for temp file names, so concurrent commands (menu + MCP) started
        /// in the same second never share a screenshot or export path.
        /// </summary>
        private static string UniqueSuffix()
            => $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        /// <summary>
        /// Strips user info (tokens, passwords) from a remote URL so credentials embedded in
        /// https://user:token@host/... never reach the MCP response or the editor log.
        /// </summary>
        internal static string RedactRemoteUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url ?? string.Empty;
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && !string.IsNullOrEmpty(uri.UserInfo))
                return new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.ToString();
            // scp-style git@host:path carries no secret; anything else with "user:secret@" is masked.
            int at = url.IndexOf('@');
            int scheme = url.IndexOf("://", StringComparison.Ordinal);
            if (at > 0 && scheme >= 0 && at > scheme && url.IndexOf(':', scheme + 3) is int colon && colon > 0 && colon < at)
                return url.Substring(0, scheme + 3) + "***@" + url.Substring(at + 1);
            return url;
        }

        /// <summary>Absolute project folder (parent of Assets/), forward slashes.</summary>
        private static string ProjectRoot() => Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');

        /// <summary>Lower-case hex MD5 of a file; shared with the Asset Gen panel's addon-sync indicator.</summary>
        internal static string FileMd5(string path)
        {
            using var md5 = MD5.Create();
            using var fs = File.OpenRead(path);
            return BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Makes a caller-supplied name safe to use as a file and GameObject name.</summary>
        private static string SanitizeName(string raw)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in raw.Trim())
                sb.Append(invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c);
            string s = sb.ToString().Trim('_', '.', ' ');
            return string.IsNullOrEmpty(s) ? "BlenderModel" : s;
        }

        /// <summary>Reads a [x, y, z] position from a JSON array or a stringified array; origin when absent.</summary>
        private static Vector3 ParsePosition(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return Vector3.zero;
            JArray arr = token as JArray;
            if (arr == null && token.Type == JTokenType.String)
            {
                try { arr = JArray.Parse(token.ToString()); } catch { return Vector3.zero; }
            }
            if (arr == null || arr.Count < 3) return Vector3.zero;
            return new Vector3(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>());
        }

        /// <summary>World-space bounds over every renderer in the hierarchy; false when it has none.</summary>
        private static bool TryGetWorldBounds(GameObject go, out Bounds bounds)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        /// <summary>Trims and caps a string for inclusion in messages.</summary>
        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
