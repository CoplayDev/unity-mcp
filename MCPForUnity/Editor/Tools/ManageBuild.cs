using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity build pipeline control.
    /// Actions: get_player_settings, set_player_settings, get_quality_settings, set_quality_level,
    ///          get_build_settings, set_build_scenes, build, get_scripting_defines, set_scripting_defines.
    /// </summary>
    [McpForUnityTool("manage_build", AutoRegister = true)]
    public static class ManageBuild
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "get_player_settings"   => GetPlayerSettings(p),
                    "set_player_settings"   => SetPlayerSettings(p),
                    "get_quality_settings"  => GetQualitySettings(),
                    "set_quality_level"     => SetQualityLevel(p),
                    "get_build_settings"    => GetBuildSettings(),
                    "set_build_scenes"      => SetBuildScenes(p),
                    "build"                 => Build(p),
                    "get_scripting_defines" => GetScriptingDefines(p),
                    "set_scripting_defines" => SetScriptingDefines(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: get_player_settings, set_player_settings, " +
                        "get_quality_settings, set_quality_level, get_build_settings, set_build_scenes, " +
                        "build, get_scripting_defines, set_scripting_defines")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageBuild] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Player Settings

        private static object GetPlayerSettings(ToolParams p)
        {
            return new SuccessResponse("Player settings.", new Dictionary<string, object>
            {
                ["company_name"] = PlayerSettings.companyName,
                ["product_name"] = PlayerSettings.productName,
                ["application_identifier"] = PlayerSettings.applicationIdentifier,
                ["bundle_version"] = PlayerSettings.bundleVersion,
                ["default_icon"] = PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Unknown)?.Length > 0,
                ["color_space"] = PlayerSettings.colorSpace.ToString(),
                ["scripting_backend"] = PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString(),
                ["api_compatibility_level"] = PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup).ToString(),
            });
        }

        private static object SetPlayerSettings(ToolParams p)
        {
            var props = ParseProps(p);
            if (props == null) return new ErrorResponse("'properties' parameter is required (valid JSON).");

            if (props["company_name"] != null) PlayerSettings.companyName = props["company_name"].ToString();
            if (props["product_name"] != null) PlayerSettings.productName = props["product_name"].ToString();
            if (props["application_identifier"] != null) PlayerSettings.applicationIdentifier = props["application_identifier"].ToString();
            if (props["bundle_version"] != null) PlayerSettings.bundleVersion = props["bundle_version"].ToString();

            return new SuccessResponse("Player settings updated.");
        }

        #endregion

        #region Quality Settings

        private static object GetQualitySettings()
        {
            var names = QualitySettings.names;
            var levels = new List<Dictionary<string, object>>();
            for (int i = 0; i < names.Length; i++)
            {
                levels.Add(new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["name"] = names[i],
                    ["is_current"] = i == QualitySettings.GetQualityLevel(),
                });
            }

            return new SuccessResponse("Quality settings.", new Dictionary<string, object>
            {
                ["current_level"] = QualitySettings.GetQualityLevel(),
                ["current_name"] = names[QualitySettings.GetQualityLevel()],
                ["levels"] = levels,
            });
        }

        private static object SetQualityLevel(ToolParams p)
        {
            var levelResult = p.GetRequired("level");
            if (!levelResult.IsSuccess)
                return new ErrorResponse(levelResult.ErrorMessage);

            string levelStr = levelResult.Value;
            var names = QualitySettings.names;

            // Try as index first
            if (int.TryParse(levelStr, out int idx) && idx >= 0 && idx < names.Length)
            {
                QualitySettings.SetQualityLevel(idx, true);
                return new SuccessResponse($"Quality level set to {idx} ('{names[idx]}').");
            }

            // Try as name
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], levelStr, StringComparison.OrdinalIgnoreCase))
                {
                    QualitySettings.SetQualityLevel(i, true);
                    return new SuccessResponse($"Quality level set to {i} ('{names[i]}').");
                }
            }

            return new ErrorResponse($"Quality level '{levelStr}' not found. Available: {string.Join(", ", names)}");
        }

        #endregion

        #region Build Settings

        private static object GetBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            var sceneList = new List<Dictionary<string, object>>();
            foreach (var scene in scenes)
            {
                sceneList.Add(new Dictionary<string, object>
                {
                    ["path"] = scene.path,
                    ["enabled"] = scene.enabled,
                    ["guid"] = scene.guid.ToString(),
                });
            }

            return new SuccessResponse("Build settings.", new Dictionary<string, object>
            {
                ["active_build_target"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                ["build_target_group"] = EditorUserBuildSettings.selectedBuildTargetGroup.ToString(),
                ["development_build"] = EditorUserBuildSettings.development,
                ["scenes"] = sceneList,
            });
        }

        private static object SetBuildScenes(ToolParams p)
        {
            var scenesToken = p.GetRaw("scenes");
            if (scenesToken == null)
                return new ErrorResponse("'scenes' parameter is required (JSON array of scene paths).");

            JArray scenesArray;
            if (scenesToken.Type == JTokenType.String)
            {
                try { scenesArray = JArray.Parse(scenesToken.ToString()); }
                catch { return new ErrorResponse("'scenes' must be a valid JSON array."); }
            }
            else
            {
                scenesArray = scenesToken as JArray;
                if (scenesArray == null) return new ErrorResponse("'scenes' must be a JSON array.");
            }

            var newScenes = new List<EditorBuildSettingsScene>();
            foreach (var item in scenesArray)
            {
                newScenes.Add(new EditorBuildSettingsScene(item.ToString(), true));
            }

            EditorBuildSettings.scenes = newScenes.ToArray();
            return new SuccessResponse($"Build scenes updated ({newScenes.Count} scene(s)).");
        }

        #endregion

        #region Build

        private static object Build(ToolParams p)
        {
            string target = p.Get("target", EditorUserBuildSettings.activeBuildTarget.ToString());
            var outputResult = p.GetRequired("output_path");
            if (!outputResult.IsSuccess)
                return new ErrorResponse(outputResult.ErrorMessage);

            if (!Enum.TryParse<BuildTarget>(target, true, out var buildTarget))
                return new ErrorResponse($"Unknown build target: '{target}'.");

            var enabledScenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (enabledScenes.Length == 0)
                return new ErrorResponse("No enabled scenes in build settings.");

            var buildOptions = new BuildPlayerOptions
            {
                scenes = enabledScenes,
                locationPathName = outputResult.Value,
                target = buildTarget,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(buildOptions);
            var summary = report.summary;

            return new SuccessResponse($"Build {summary.result}.", new Dictionary<string, object>
            {
                ["result"] = summary.result.ToString(),
                ["total_time"] = summary.totalTime.TotalSeconds,
                ["total_size"] = summary.totalSize,
                ["total_errors"] = summary.totalErrors,
                ["total_warnings"] = summary.totalWarnings,
                ["output_path"] = summary.outputPath,
                ["platform"] = summary.platform.ToString(),
            });
        }

        #endregion

        #region Scripting Defines

        private static object GetScriptingDefines(ToolParams p)
        {
            var namedTarget = GetNamedBuildTarget(p.Get("platform"));

            PlayerSettings.GetScriptingDefineSymbols(namedTarget, out var definesArr);
            var definesStr = string.Join(";", definesArr);

            var defines = string.IsNullOrEmpty(definesStr)
                ? Array.Empty<string>()
                : definesStr.Split(';').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();

            return new SuccessResponse("Scripting defines.", new Dictionary<string, object>
            {
                ["platform"] = namedTarget.TargetName,
                ["defines"] = defines,
                ["defines_string"] = definesStr,
            });
        }

        private static object SetScriptingDefines(ToolParams p)
        {
            var definesResult = p.GetRequired("defines");
            if (!definesResult.IsSuccess)
                return new ErrorResponse(definesResult.ErrorMessage);

            var namedTarget = GetNamedBuildTarget(p.Get("platform"));

            PlayerSettings.SetScriptingDefineSymbols(namedTarget, definesResult.Value);
            return new SuccessResponse($"Scripting defines updated for {namedTarget.TargetName}.");
        }

        private static UnityEditor.Build.NamedBuildTarget GetNamedBuildTarget(string platform)
        {
            if (string.IsNullOrEmpty(platform))
                return UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);

            if (Enum.TryParse<BuildTargetGroup>(platform, true, out var group))
                return UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);

            return UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        }

        #endregion

        #region Helpers

        private static JObject ParseProps(ToolParams p)
        {
            var propsToken = p.GetRaw("properties");
            if (propsToken == null) return null;
            if (propsToken.Type == JTokenType.String)
            {
                try { return JObject.Parse(propsToken.ToString()); }
                catch { return null; }
            }
            return propsToken as JObject;
        }

        #endregion
    }
}
