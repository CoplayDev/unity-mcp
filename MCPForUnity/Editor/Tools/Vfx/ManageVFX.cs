using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using UnityEngine;
using UnityEditor;

#if UNITY_VFX_GRAPH //Please enable the symbol in the project settings for VisualEffectGraph to work
using UnityEngine.VFX;
#endif

namespace MCPForUnity.Editor.Tools.Vfx
{
    /// <summary>
    /// Tool for managing Unity VFX components:
    /// - ParticleSystem (legacy particle effects)
    /// - Visual Effect Graph (modern GPU particles, currently only support HDRP, other SRPs may not work)
    /// - LineRenderer (lines, bezier curves, shapes)
    /// - TrailRenderer (motion trails)
    /// - More to come based on demand and feedback!
    /// </summary>
    [McpForUnityTool("manage_vfx", AutoRegister = false)]
    public static class ManageVFX
    {
        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString();
            if (string.IsNullOrEmpty(action))
            {
                return new { success = false, message = "Action is required" };
            }

            try
            {
                string actionLower = action.ToLowerInvariant();

                // Route to appropriate handler based on action prefix
                if (actionLower == "ping")
                {
                    return new { success = true, tool = "manage_vfx", components = new[] { "ParticleSystem", "VisualEffect", "LineRenderer", "TrailRenderer" } };
                }

                // ParticleSystem actions (particle_*)
                if (actionLower.StartsWith("particle_"))
                {
                    return HandleParticleSystemAction(@params, actionLower.Substring(9));
                }

                // VFX Graph actions (vfx_*)
                if (actionLower.StartsWith("vfx_"))
                {
                    return HandleVFXGraphAction(@params, actionLower.Substring(4));
                }

                // LineRenderer actions (line_*)
                if (actionLower.StartsWith("line_"))
                {
                    return HandleLineRendererAction(@params, actionLower.Substring(5));
                }

                // TrailRenderer actions (trail_*)
                if (actionLower.StartsWith("trail_"))
                {
                    return HandleTrailRendererAction(@params, actionLower.Substring(6));
                }

                return new { success = false, message = $"Unknown action: {action}. Actions must be prefixed with: particle_, vfx_, line_, or trail_" };
            }
            catch (Exception ex)
            {
                return new { success = false, message = ex.Message, stackTrace = ex.StackTrace };
            }
        }

        private static object HandleParticleSystemAction(JObject @params, string action)
        {
            switch (action)
            {
                case "get_info": return ParticleRead.GetInfo(@params);
                case "set_main": return ParticleWrite.SetMain(@params);
                case "set_emission": return ParticleWrite.SetEmission(@params);
                case "set_shape": return ParticleWrite.SetShape(@params);
                case "set_color_over_lifetime": return ParticleWrite.SetColorOverLifetime(@params);
                case "set_size_over_lifetime": return ParticleWrite.SetSizeOverLifetime(@params);
                case "set_velocity_over_lifetime": return ParticleWrite.SetVelocityOverLifetime(@params);
                case "set_noise": return ParticleWrite.SetNoise(@params);
                case "set_renderer": return ParticleWrite.SetRenderer(@params);
                case "enable_module": return ParticleControl.EnableModule(@params);
                case "play": return ParticleControl.Control(@params, "play");
                case "stop": return ParticleControl.Control(@params, "stop");
                case "pause": return ParticleControl.Control(@params, "pause");
                case "restart": return ParticleControl.Control(@params, "restart");
                case "clear": return ParticleControl.Control(@params, "clear");
                case "add_burst": return ParticleControl.AddBurst(@params);
                case "clear_bursts": return ParticleControl.ClearBursts(@params);
                default:
                    return new { success = false, message = $"Unknown particle action: {action}. Valid: get_info, set_main, set_emission, set_shape, set_color_over_lifetime, set_size_over_lifetime, set_velocity_over_lifetime, set_noise, set_renderer, enable_module, play, stop, pause, restart, clear, add_burst, clear_bursts" };
            }
        }

        // ==================== VFX GRAPH ====================
        #region VFX Graph

        private static object HandleVFXGraphAction(JObject @params, string action)
        {
#if !UNITY_VFX_GRAPH
            return new { success = false, message = "VFX Graph package (com.unity.visualeffectgraph) not installed" };
#else
            switch (action)
            {
                // Asset management
                case "create_asset": return VFXCreateAsset(@params);
                case "assign_asset": return VFXAssignAsset(@params);
                case "list_templates": return VFXListTemplates(@params);
                case "list_assets": return VFXListAssets(@params);
                
                // Runtime parameter control
                case "get_info": return VFXGetInfo(@params);
                case "set_float": return VFXSetParameter<float>(@params, (vfx, n, v) => vfx.SetFloat(n, v));
                case "set_int": return VFXSetParameter<int>(@params, (vfx, n, v) => vfx.SetInt(n, v));
                case "set_bool": return VFXSetParameter<bool>(@params, (vfx, n, v) => vfx.SetBool(n, v));
                case "set_vector2": return VFXSetVector(@params, 2);
                case "set_vector3": return VFXSetVector(@params, 3);
                case "set_vector4": return VFXSetVector(@params, 4);
                case "set_color": return VFXSetColor(@params);
                case "set_gradient": return VFXSetGradient(@params);
                case "set_texture": return VFXSetTexture(@params);
                case "set_mesh": return VFXSetMesh(@params);
                case "set_curve": return VFXSetCurve(@params);
                case "send_event": return VFXSendEvent(@params);
                case "play": return VFXControl(@params, "play");
                case "stop": return VFXControl(@params, "stop");
                case "pause": return VFXControl(@params, "pause");
                case "reinit": return VFXControl(@params, "reinit");
                case "set_playback_speed": return VFXSetPlaybackSpeed(@params);
                case "set_seed": return VFXSetSeed(@params);
                default:
                    return new { success = false, message = $"Unknown vfx action: {action}. Valid: create_asset, assign_asset, list_templates, list_assets, get_info, set_float, set_int, set_bool, set_vector2/3/4, set_color, set_gradient, set_texture, set_mesh, set_curve, send_event, play, stop, pause, reinit, set_playback_speed, set_seed" };
            }
#endif
        }

#if UNITY_VFX_GRAPH
        private static VisualEffect FindVisualEffect(JObject @params)
        {
            GameObject go = ManageVfxCommon.FindTargetGameObject(@params);
            return go?.GetComponent<VisualEffect>();
        }

        /// <summary>
        /// Creates a new VFX Graph asset file from a template
        /// </summary>
        private static object VFXCreateAsset(JObject @params)
        {
            string assetName = @params["assetName"]?.ToString();
            string folderPath = @params["folderPath"]?.ToString() ?? "Assets/VFX";
            string template = @params["template"]?.ToString() ?? "empty";
            
            if (string.IsNullOrEmpty(assetName))
                return new { success = false, message = "assetName is required" };
            
            // Ensure folder exists
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string[] folders = folderPath.Split('/');
                string currentPath = folders[0];
                for (int i = 1; i < folders.Length; i++)
                {
                    string newPath = currentPath + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(newPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, folders[i]);
                    }
                    currentPath = newPath;
                }
            }
            
            string assetPath = $"{folderPath}/{assetName}.vfx";
            
            // Check if asset already exists
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath) != null)
            {
                bool overwrite = @params["overwrite"]?.ToObject<bool>() ?? false;
                if (!overwrite)
                    return new { success = false, message = $"Asset already exists at {assetPath}. Set overwrite=true to replace." };
                AssetDatabase.DeleteAsset(assetPath);
            }
            
            // Find and copy template
            string templatePath = FindVFXTemplate(template);
            UnityEngine.VFX.VisualEffectAsset newAsset = null;
            
            if (!string.IsNullOrEmpty(templatePath) && System.IO.File.Exists(templatePath))
            {
                // templatePath is a full filesystem path, need to copy file directly
                // Get the full destination path
                string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                string fullDestPath = System.IO.Path.Combine(projectRoot, assetPath);
                
                // Ensure directory exists
                string destDir = System.IO.Path.GetDirectoryName(fullDestPath);
                if (!System.IO.Directory.Exists(destDir))
                    System.IO.Directory.CreateDirectory(destDir);
                
                // Copy the file
                System.IO.File.Copy(templatePath, fullDestPath, true);
                AssetDatabase.Refresh();
                newAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath);
            }
            else
            {
                // Create empty VFX asset using reflection to access internal API
                // Note: Develop in Progress, TODO:// Find authenticated way to create VFX asset
                try
                {
                    // Try to use VisualEffectAssetEditorUtility.CreateNewAsset if available
                    var utilityType = System.Type.GetType("UnityEditor.VFX.VisualEffectAssetEditorUtility, Unity.VisualEffectGraph.Editor");
                    if (utilityType != null)
                    {
                        var createMethod = utilityType.GetMethod("CreateNewAsset", BindingFlags.Public | BindingFlags.Static);
                        if (createMethod != null)
                        {
                            createMethod.Invoke(null, new object[] { assetPath });
                            AssetDatabase.Refresh();
                            newAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath);
                        }
                    }
                    
                    // Fallback: Create a ScriptableObject-based asset
                    if (newAsset == null)
                    {
                        // Try direct creation via internal constructor
                        var resourceType = System.Type.GetType("UnityEditor.VFX.VisualEffectResource, Unity.VisualEffectGraph.Editor");
                        if (resourceType != null)
                        {
                            var createMethod = resourceType.GetMethod("CreateNewAsset", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                            if (createMethod != null)
                            {
                                var resource = createMethod.Invoke(null, new object[] { assetPath });
                                AssetDatabase.Refresh();
                                newAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    return new { success = false, message = $"Failed to create VFX asset: {ex.Message}" };
                }
            }
            
            if (newAsset == null)
            {
                return new { success = false, message = "Failed to create VFX asset. Try using a template from list_templates." };
            }
            
            return new 
            { 
                success = true, 
                message = $"Created VFX asset: {assetPath}",
                data = new
                {
                    assetPath = assetPath,
                    assetName = newAsset.name,
                    template = template
                }
            };
        }
        
        /// <summary>
        /// Finds VFX template path by name
        /// </summary>
        private static string FindVFXTemplate(string templateName)
        {
            // Get the actual filesystem path for the VFX Graph package using PackageManager API
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.visualeffectgraph");
            
            var searchPaths = new List<string>();
            
            if (packageInfo != null)
            {
                // Use the resolved path from PackageManager (handles Library/PackageCache paths)
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Editor/Templates"));
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Samples"));
            }
            
            // Also search project-local paths
            searchPaths.Add("Assets/VFX/Templates");
            
            string[] templatePatterns = new[]
            {
                $"{templateName}.vfx",
                $"VFX{templateName}.vfx",
                $"Simple{templateName}.vfx",
                $"{templateName}VFX.vfx"
            };
            
            foreach (string basePath in searchPaths)
            {
                if (!System.IO.Directory.Exists(basePath)) continue;
                
                foreach (string pattern in templatePatterns)
                {
                    string[] files = System.IO.Directory.GetFiles(basePath, pattern, System.IO.SearchOption.AllDirectories);
                    if (files.Length > 0)
                        return files[0];
                }
                
                // Also search by partial match
                try
                {
                    string[] allVfxFiles = System.IO.Directory.GetFiles(basePath, "*.vfx", System.IO.SearchOption.AllDirectories);
                    foreach (string file in allVfxFiles)
                    {
                        if (System.IO.Path.GetFileNameWithoutExtension(file).ToLower().Contains(templateName.ToLower()))
                            return file;
                    }
                }
                catch { }
            }
            
            // Search in project assets
            string[] guids = AssetDatabase.FindAssets("t:VisualEffectAsset " + templateName);
            if (guids.Length > 0)
            {
                return AssetDatabase.GUIDToAssetPath(guids[0]);
            }
            
            return null;
        }
        
        /// <summary>
        /// Assigns a VFX asset to a VisualEffect component
        /// </summary>
        private static object VFXAssignAsset(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect component not found" };
            
            string assetPath = @params["assetPath"]?.ToString();
            if (string.IsNullOrEmpty(assetPath))
                return new { success = false, message = "assetPath is required" };
            
            // Normalize path
            if (!assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Packages/"))
                assetPath = "Assets/" + assetPath;
            if (!assetPath.EndsWith(".vfx"))
                assetPath += ".vfx";
            
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath);
            if (asset == null)
            {
                // Try searching by name
                string searchName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                string[] guids = AssetDatabase.FindAssets($"t:VisualEffectAsset {searchName}");
                if (guids.Length > 0)
                {
                    assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    asset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(assetPath);
                }
            }
            
            if (asset == null)
                return new { success = false, message = $"VFX asset not found: {assetPath}" };
            
            Undo.RecordObject(vfx, "Assign VFX Asset");
            vfx.visualEffectAsset = asset;
            EditorUtility.SetDirty(vfx);
            
            return new 
            { 
                success = true, 
                message = $"Assigned VFX asset '{asset.name}' to {vfx.gameObject.name}",
                data = new
                {
                    gameObject = vfx.gameObject.name,
                    assetName = asset.name,
                    assetPath = assetPath
                }
            };
        }
        
        /// <summary>
        /// Lists available VFX templates
        /// </summary>
        private static object VFXListTemplates(JObject @params)
        {
            var templates = new List<object>();
            
            // Get the actual filesystem path for the VFX Graph package using PackageManager API
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.visualeffectgraph");
            
            var searchPaths = new List<string>();
            
            if (packageInfo != null)
            {
                // Use the resolved path from PackageManager (handles Library/PackageCache paths)
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Editor/Templates"));
                searchPaths.Add(System.IO.Path.Combine(packageInfo.resolvedPath, "Samples"));
            }
            
            // Also search project-local paths
            searchPaths.Add("Assets/VFX/Templates");
            searchPaths.Add("Assets/VFX");
            
            // Precompute normalized package path for comparison
            string normalizedPackagePath = null;
            if (packageInfo != null)
            {
                normalizedPackagePath = packageInfo.resolvedPath.Replace("\\", "/");
            }
            
            // Precompute the Assets base path for converting absolute paths to project-relative
            string assetsBasePath = Application.dataPath.Replace("\\", "/");
            
            foreach (string basePath in searchPaths)
            {
                if (!System.IO.Directory.Exists(basePath)) continue;
                
                try
                {
                    string[] vfxFiles = System.IO.Directory.GetFiles(basePath, "*.vfx", System.IO.SearchOption.AllDirectories);
                    foreach (string file in vfxFiles)
                    {
                        string absolutePath = file.Replace("\\", "/");
                        string name = System.IO.Path.GetFileNameWithoutExtension(file);
                        bool isPackage = normalizedPackagePath != null && absolutePath.StartsWith(normalizedPackagePath);
                        
                        // Convert absolute path to project-relative path
                        string projectRelativePath;
                        if (isPackage)
                        {
                            // For package paths, convert to Packages/... format
                            projectRelativePath = "Packages/" + packageInfo.name + absolutePath.Substring(normalizedPackagePath.Length);
                        }
                        else if (absolutePath.StartsWith(assetsBasePath))
                        {
                            // For project assets, convert to Assets/... format
                            projectRelativePath = "Assets" + absolutePath.Substring(assetsBasePath.Length);
                        }
                        else
                        {
                            // Fallback: use the absolute path if we can't determine the relative path
                            projectRelativePath = absolutePath;
                        }
                        
                        templates.Add(new { name = name, path = projectRelativePath, source = isPackage ? "package" : "project" });
                    }
                }
                catch { }
            }
            
            // Also search project assets
            string[] guids = AssetDatabase.FindAssets("t:VisualEffectAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!templates.Any(t => ((dynamic)t).path == path))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    templates.Add(new { name = name, path = path, source = "project" });
                }
            }
            
            return new 
            { 
                success = true, 
                data = new
                {
                    count = templates.Count,
                    templates = templates
                }
            };
        }
        
        /// <summary>
        /// Lists all VFX assets in the project
        /// </summary>
        private static object VFXListAssets(JObject @params)
        {
            string searchFolder = @params["folder"]?.ToString();
            string searchPattern = @params["search"]?.ToString();
            
            string filter = "t:VisualEffectAsset";
            if (!string.IsNullOrEmpty(searchPattern))
                filter += " " + searchPattern;
            
            string[] guids;
            if (!string.IsNullOrEmpty(searchFolder))
                guids = AssetDatabase.FindAssets(filter, new[] { searchFolder });
            else
                guids = AssetDatabase.FindAssets(filter);
            
            var assets = new List<object>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(path);
                if (asset != null)
                {
                    assets.Add(new 
                    { 
                        name = asset.name, 
                        path = path,
                        guid = guid
                    });
                }
            }
            
            return new 
            { 
                success = true, 
                data = new
                {
                    count = assets.Count,
                    assets = assets
                }
            };
        }

        private static object VFXGetInfo(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            return new
            {
                success = true,
                data = new
                {
                    gameObject = vfx.gameObject.name,
                    assetName = vfx.visualEffectAsset?.name ?? "None",
                    aliveParticleCount = vfx.aliveParticleCount,
                    culled = vfx.culled,
                    pause = vfx.pause,
                    playRate = vfx.playRate,
                    startSeed = vfx.startSeed
                }
            };
        }

        private static object VFXSetParameter<T>(JObject @params, Action<VisualEffect, string, T> setter)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param)) return new { success = false, message = "Parameter name required" };

            JToken valueToken = @params["value"];
            if (valueToken == null) return new { success = false, message = "Value required" };

            Undo.RecordObject(vfx, $"Set VFX {param}");
            T value = valueToken.ToObject<T>();
            setter(vfx, param, value);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set {param} = {value}" };
        }

        private static object VFXSetVector(JObject @params, int dims)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param)) return new { success = false, message = "Parameter name required" };

            Vector4 vec = ManageVfxCommon.ParseVector4(@params["value"]);
            Undo.RecordObject(vfx, $"Set VFX {param}");

            switch (dims)
            {
                case 2: vfx.SetVector2(param, new Vector2(vec.x, vec.y)); break;
                case 3: vfx.SetVector3(param, new Vector3(vec.x, vec.y, vec.z)); break;
                case 4: vfx.SetVector4(param, vec); break;
            }

            EditorUtility.SetDirty(vfx);
            return new { success = true, message = $"Set {param}" };
        }

        private static object VFXSetColor(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param)) return new { success = false, message = "Parameter name required" };

            Color color = ManageVfxCommon.ParseColor(@params["value"]);
            Undo.RecordObject(vfx, $"Set VFX Color {param}");
            vfx.SetVector4(param, new Vector4(color.r, color.g, color.b, color.a));
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set color {param}" };
        }

        private static object VFXSetGradient(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param)) return new { success = false, message = "Parameter name required" };

            Gradient gradient = ManageVfxCommon.ParseGradient(@params["gradient"]);
            Undo.RecordObject(vfx, $"Set VFX Gradient {param}");
            vfx.SetGradient(param, gradient);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set gradient {param}" };
        }

        private static object VFXSetTexture(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            string path = @params["texturePath"]?.ToString();
            if (string.IsNullOrEmpty(param) || string.IsNullOrEmpty(path)) return new { success = false, message = "Parameter and texturePath required" };

            var findInst = new JObject { ["find"] = path };
            Texture tex = ObjectResolver.Resolve(findInst, typeof(Texture)) as Texture;
            if (tex == null) return new { success = false, message = $"Texture not found: {path}" };

            Undo.RecordObject(vfx, $"Set VFX Texture {param}");
            vfx.SetTexture(param, tex);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set texture {param} = {tex.name}" };
        }

        private static object VFXSetMesh(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            string path = @params["meshPath"]?.ToString();
            if (string.IsNullOrEmpty(param) || string.IsNullOrEmpty(path)) return new { success = false, message = "Parameter and meshPath required" };

            var findInst = new JObject { ["find"] = path };
            Mesh mesh = ObjectResolver.Resolve(findInst, typeof(Mesh)) as Mesh;
            if (mesh == null) return new { success = false, message = $"Mesh not found: {path}" };

            Undo.RecordObject(vfx, $"Set VFX Mesh {param}");
            vfx.SetMesh(param, mesh);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set mesh {param} = {mesh.name}" };
        }

        private static object VFXSetCurve(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string param = @params["parameter"]?.ToString();
            if (string.IsNullOrEmpty(param)) return new { success = false, message = "Parameter name required" };

            AnimationCurve curve = ManageVfxCommon.ParseAnimationCurve(@params["curve"], 1f);
            Undo.RecordObject(vfx, $"Set VFX Curve {param}");
            vfx.SetAnimationCurve(param, curve);
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set curve {param}" };
        }

        private static object VFXSendEvent(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            string eventName = @params["eventName"]?.ToString();
            if (string.IsNullOrEmpty(eventName)) return new { success = false, message = "Event name required" };

            VFXEventAttribute attr = vfx.CreateVFXEventAttribute();
            if (@params["position"] != null) attr.SetVector3("position", ManageVfxCommon.ParseVector3(@params["position"]));
            if (@params["velocity"] != null) attr.SetVector3("velocity", ManageVfxCommon.ParseVector3(@params["velocity"]));
            if (@params["color"] != null) { var c = ManageVfxCommon.ParseColor(@params["color"]); attr.SetVector3("color", new Vector3(c.r, c.g, c.b)); }
            if (@params["size"] != null) attr.SetFloat("size", @params["size"].ToObject<float>());
            if (@params["lifetime"] != null) attr.SetFloat("lifetime", @params["lifetime"].ToObject<float>());

            vfx.SendEvent(eventName, attr);
            return new { success = true, message = $"Sent event '{eventName}'" };
        }

        private static object VFXControl(JObject @params, string action)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            switch (action)
            {
                case "play": vfx.Play(); break;
                case "stop": vfx.Stop(); break;
                case "pause": vfx.pause = !vfx.pause; break;
                case "reinit": vfx.Reinit(); break;
            }

            return new { success = true, message = $"VFX {action}", isPaused = vfx.pause };
        }

        private static object VFXSetPlaybackSpeed(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            float rate = @params["playRate"]?.ToObject<float>() ?? 1f;
            Undo.RecordObject(vfx, "Set VFX Play Rate");
            vfx.playRate = rate;
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set play rate = {rate}" };
        }

        private static object VFXSetSeed(JObject @params)
        {
            VisualEffect vfx = FindVisualEffect(@params);
            if (vfx == null) return new { success = false, message = "VisualEffect not found" };

            uint seed = @params["seed"]?.ToObject<uint>() ?? 0;
            bool resetOnPlay = @params["resetSeedOnPlay"]?.ToObject<bool>() ?? true;

            Undo.RecordObject(vfx, "Set VFX Seed");
            vfx.startSeed = seed;
            vfx.resetSeedOnPlay = resetOnPlay;
            EditorUtility.SetDirty(vfx);

            return new { success = true, message = $"Set seed = {seed}" };
        }
#endif

        #endregion

        private static object HandleLineRendererAction(JObject @params, string action)
        {
            switch (action)
            {
                case "get_info": return LineRead.GetInfo(@params);
                case "set_positions": return LineWrite.SetPositions(@params);
                case "add_position": return LineWrite.AddPosition(@params);
                case "set_position": return LineWrite.SetPosition(@params);
                case "set_width": return LineWrite.SetWidth(@params);
                case "set_color": return LineWrite.SetColor(@params);
                case "set_material": return LineWrite.SetMaterial(@params);
                case "set_properties": return LineWrite.SetProperties(@params);
                case "clear": return LineWrite.Clear(@params);
                case "create_line": return LineCreate.CreateLine(@params);
                case "create_circle": return LineCreate.CreateCircle(@params);
                case "create_arc": return LineCreate.CreateArc(@params);
                case "create_bezier": return LineCreate.CreateBezier(@params);
                default:
                    return new { success = false, message = $"Unknown line action: {action}. Valid: get_info, set_positions, add_position, set_position, set_width, set_color, set_material, set_properties, clear, create_line, create_circle, create_arc, create_bezier" };
            }
        }

        private static object HandleTrailRendererAction(JObject @params, string action)
        {
            switch (action)
            {
                case "get_info": return TrailRead.GetInfo(@params);
                case "set_time": return TrailWrite.SetTime(@params);
                case "set_width": return TrailWrite.SetWidth(@params);
                case "set_color": return TrailWrite.SetColor(@params);
                case "set_material": return TrailWrite.SetMaterial(@params);
                case "set_properties": return TrailWrite.SetProperties(@params);
                case "clear": return TrailControl.Clear(@params);
                case "emit": return TrailControl.Emit(@params);
                default:
                    return new { success = false, message = $"Unknown trail action: {action}. Valid: get_info, set_time, set_width, set_color, set_material, set_properties, clear, emit" };
            }
        }
    }
}
