using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity camera management.
    /// Actions: list_cameras, get_camera, set_camera, render_to_file,
    ///          world_to_screen, screen_to_ray, get_main_camera.
    /// </summary>
    [McpForUnityTool("manage_camera", AutoRegister = true)]
    public static class ManageCamera
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
                    "list_cameras"    => ListCameras(p),
                    "get_camera"      => GetCamera(p),
                    "set_camera"      => SetCamera(p),
                    "render_to_file"  => RenderToFile(p),
                    "world_to_screen" => WorldToScreen(p),
                    "screen_to_ray"   => ScreenToRay(p),
                    "get_main_camera" => GetMainCamera(),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: list_cameras, get_camera, set_camera, " +
                        "render_to_file, world_to_screen, screen_to_ray, get_main_camera")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageCamera] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Camera Operations

        private static object ListCameras(ToolParams p)
        {
            var cameras = Camera.allCameras;
            var pagination = PaginationRequest.FromParams(
                new JObject { ["page_size"] = p.GetInt("page_size"), ["cursor"] = p.GetInt("cursor") });

            var allItems = new List<Dictionary<string, object>>();
            foreach (var cam in cameras)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = cam.gameObject.name,
                    ["instance_id"] = cam.gameObject.GetInstanceID(),
                    ["depth"] = Math.Round(cam.depth, 4),
                    ["fov"] = Math.Round(cam.fieldOfView, 4),
                    ["near_clip"] = Math.Round(cam.nearClipPlane, 4),
                    ["far_clip"] = Math.Round(cam.farClipPlane, 4),
                    ["culling_mask"] = cam.cullingMask,
                    ["is_main"] = cam == Camera.main,
                    ["enabled"] = cam.enabled,
                    ["orthographic"] = cam.orthographic,
                });
            }

            var page = PaginationResponse<Dictionary<string, object>>.Create(allItems, pagination);
            return new SuccessResponse($"Found {cameras.Length} active Camera(s).", page);
        }

        private static object GetCamera(ToolParams p)
        {
            var cam = ResolveCamera(p);
            if (cam == null)
                return new ErrorResponse("Camera not found. Provide 'target' (name or ID).");

            return new SuccessResponse($"Camera '{cam.gameObject.name}'.", new Dictionary<string, object>
            {
                ["name"] = cam.gameObject.name,
                ["instance_id"] = cam.gameObject.GetInstanceID(),
                ["depth"] = Math.Round(cam.depth, 4),
                ["fov"] = Math.Round(cam.fieldOfView, 4),
                ["near_clip"] = Math.Round(cam.nearClipPlane, 4),
                ["far_clip"] = Math.Round(cam.farClipPlane, 4),
                ["orthographic"] = cam.orthographic,
                ["orthographic_size"] = Math.Round(cam.orthographicSize, 4),
                ["clear_flags"] = cam.clearFlags.ToString(),
                ["background_color"] = FormatColor(cam.backgroundColor),
                ["culling_mask"] = cam.cullingMask,
                ["target_display"] = cam.targetDisplay,
                ["pixel_width"] = cam.pixelWidth,
                ["pixel_height"] = cam.pixelHeight,
                ["aspect"] = Math.Round(cam.aspect, 4),
                ["is_main"] = cam == Camera.main,
                ["enabled"] = cam.enabled,
                ["position"] = FormatVec3(cam.transform.position),
                ["rotation"] = FormatVec3(cam.transform.eulerAngles),
            });
        }

        private static object SetCamera(ToolParams p)
        {
            var cam = ResolveCamera(p);
            if (cam == null)
                return new ErrorResponse("Camera not found. Provide 'target' (name or ID).");

            var props = ParseProps(p);
            if (props == null) return new ErrorResponse("'properties' parameter is required (valid JSON).");

            Undo.RecordObject(cam, "MCP SetCamera");

            if (props["fov"] != null) cam.fieldOfView = props["fov"].ToObject<float>();
            if (props["near_clip"] != null) cam.nearClipPlane = props["near_clip"].ToObject<float>();
            if (props["far_clip"] != null) cam.farClipPlane = props["far_clip"].ToObject<float>();
            if (props["orthographic"] != null) cam.orthographic = props["orthographic"].ToObject<bool>();
            if (props["orthographic_size"] != null) cam.orthographicSize = props["orthographic_size"].ToObject<float>();
            if (props["depth"] != null) cam.depth = props["depth"].ToObject<float>();
            if (props["background_color"] != null)
            {
                var col = VectorParsing.ParseColor(props["background_color"]);
                if (col.HasValue) cam.backgroundColor = col.Value;
            }
            if (props["clear_flags"] != null)
            {
                if (Enum.TryParse<CameraClearFlags>(props["clear_flags"].ToString(), true, out var flags))
                    cam.clearFlags = flags;
            }
            if (props["enabled"] != null) cam.enabled = props["enabled"].ToObject<bool>();

            EditorUtility.SetDirty(cam);
            return new SuccessResponse($"Camera '{cam.gameObject.name}' updated.");
        }

        #endregion

        #region Render

        private static object RenderToFile(ToolParams p)
        {
            var cam = ResolveCamera(p);
            if (cam == null)
                return new ErrorResponse("Camera not found. Provide 'target' (name or ID).");

            var pathResult = p.GetRequired("path");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            int width = p.GetInt("width") ?? 1920;
            int height = p.GetInt("height") ?? 1080;

            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;
            cam.Render();

            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(rt);

            byte[] bytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string outputPath = pathResult.Value;
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(outputPath, bytes);

            return new SuccessResponse($"Rendered to '{outputPath}' ({width}x{height}).", new Dictionary<string, object>
            {
                ["path"] = outputPath,
                ["width"] = width,
                ["height"] = height,
                ["size_bytes"] = bytes.Length,
            });
        }

        #endregion

        #region Coordinate Conversions

        private static object WorldToScreen(ToolParams p)
        {
            var cam = ResolveCamera(p);
            if (cam == null)
                return new ErrorResponse("Camera not found. Provide 'target' (name or ID).");

            if (!TryParseVector3(p.Get("position"), out var worldPos))
                return new ErrorResponse("'position' parameter is required as 'x,y,z'.");

            var screenPos = cam.WorldToScreenPoint(worldPos);
            return new SuccessResponse("World to screen conversion.", new Dictionary<string, object>
            {
                ["world_position"] = FormatVec3(worldPos),
                ["screen_x"] = Math.Round(screenPos.x, 2),
                ["screen_y"] = Math.Round(screenPos.y, 2),
                ["depth"] = Math.Round(screenPos.z, 4),
                ["is_in_front"] = screenPos.z > 0,
            });
        }

        private static object ScreenToRay(ToolParams p)
        {
            var cam = ResolveCamera(p);
            if (cam == null)
                return new ErrorResponse("Camera not found. Provide 'target' (name or ID).");

            string posStr = p.Get("position");
            if (string.IsNullOrEmpty(posStr))
                return new ErrorResponse("'position' parameter is required as 'x,y'.");

            var parts = posStr.Split(',');
            if (parts.Length < 2)
                return new ErrorResponse("'position' must be 'x,y' screen coordinates.");

            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float sx) ||
                !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float sy))
                return new ErrorResponse("Invalid screen coordinates.");

            var ray = cam.ScreenPointToRay(new Vector3(sx, sy, 0));
            return new SuccessResponse("Screen to ray conversion.", new Dictionary<string, object>
            {
                ["screen_position"] = $"{sx:F2},{sy:F2}",
                ["ray_origin"] = FormatVec3(ray.origin),
                ["ray_direction"] = FormatVec3(ray.direction),
            });
        }

        #endregion

        #region Main Camera

        private static object GetMainCamera()
        {
            var cam = Camera.main;
            if (cam == null)
                return new ErrorResponse("No Camera.main found (no camera tagged 'MainCamera').");

            return new SuccessResponse($"Main camera: '{cam.gameObject.name}'.", new Dictionary<string, object>
            {
                ["name"] = cam.gameObject.name,
                ["instance_id"] = cam.gameObject.GetInstanceID(),
                ["fov"] = Math.Round(cam.fieldOfView, 4),
                ["position"] = FormatVec3(cam.transform.position),
                ["rotation"] = FormatVec3(cam.transform.eulerAngles),
                ["orthographic"] = cam.orthographic,
                ["near_clip"] = Math.Round(cam.nearClipPlane, 4),
                ["far_clip"] = Math.Round(cam.farClipPlane, 4),
            });
        }

        #endregion

        #region Helpers

        private static Camera ResolveCamera(ToolParams p)
        {
            string target = p.Get("target");
            if (string.IsNullOrEmpty(target))
                return Camera.main;

            var go = ObjectResolver.ResolveGameObject(new JValue(target));
            return go != null ? go.GetComponent<Camera>() : null;
        }

        private static bool TryParseVector3(string str, out Vector3 result)
        {
            result = Vector3.zero;
            if (string.IsNullOrEmpty(str)) return false;

            var parts = str.Split(',');
            if (parts.Length != 3) return false;

            if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                result = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        private static string FormatVec3(Vector3 v) => $"{v.x:F3},{v.y:F3},{v.z:F3}";
        private static string FormatColor(Color c) => $"{c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}";

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
