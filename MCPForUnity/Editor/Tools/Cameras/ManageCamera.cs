using System;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Cameras
{
    [McpForUnityTool("manage_camera", AutoRegister = false)]
    public static class ManageCamera
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            string action = p.Get("action")?.ToLowerInvariant();

            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("'action' parameter is required.");

            try
            {
                // Tier 1: Always-available actions (basic Camera fallback)
                switch (action)
                {
                    case "ping":
                        return new
                        {
                            success = true,
                            message = CameraHelpers.HasCinemachine
                                ? "Cinemachine is available."
                                : "Cinemachine not installed. Basic Camera operations available.",
                            data = new
                            {
                                cinemachine = CameraHelpers.HasCinemachine,
                                version = CameraHelpers.GetCinemachineVersion()
                            }
                        };

                    case "create_camera":
                        return CameraHelpers.HasCinemachine
                            ? CameraCreate.CreateCinemachineCamera(@params)
                            : CameraCreate.CreateBasicCamera(@params);

                    case "set_target":
                        return CameraHelpers.HasCinemachine
                            ? CameraConfigure.SetCinemachineTarget(@params)
                            : CameraConfigure.SetBasicCameraTarget(@params);

                    case "set_lens":
                        return CameraHelpers.HasCinemachine
                            ? CameraConfigure.SetCinemachineLens(@params)
                            : CameraConfigure.SetBasicCameraLens(@params);

                    case "set_priority":
                        return CameraHelpers.HasCinemachine
                            ? CameraConfigure.SetCinemachinePriority(@params)
                            : CameraConfigure.SetBasicCameraPriority(@params);

                    case "list_cameras":
                        return CameraControl.ListCameras(@params);

                    case "screenshot":
                    case "screenshot_multiview":
                    {
                        // Delegate to ManageScene's screenshot infrastructure
                        var shotParams = new JObject(@params);
                        shotParams["action"] = "screenshot";
                        if (action == "screenshot_multiview")
                        {
                            shotParams["batch"] = "surround";
                            shotParams["includeImage"] = true;
                        }
                        return ManageScene.HandleCommand(shotParams);
                    }
                }

                // Tier 2: Cinemachine-only actions
                if (!CameraHelpers.HasCinemachine)
                {
                    return new ErrorResponse(
                        $"Action '{action}' requires the Cinemachine package (com.unity.cinemachine). "
                        + CameraHelpers.GetFallbackSuggestion(action));
                }

                switch (action)
                {
                    case "ensure_brain":
                        return CameraCreate.EnsureBrain(@params);

                    case "get_brain_status":
                        return CameraControl.GetBrainStatus(@params);

                    case "set_body":
                        return CameraConfigure.SetBody(@params);

                    case "set_aim":
                        return CameraConfigure.SetAim(@params);

                    case "set_noise":
                        return CameraConfigure.SetNoise(@params);

                    case "add_extension":
                        return CameraConfigure.AddExtension(@params);

                    case "remove_extension":
                        return CameraConfigure.RemoveExtension(@params);

                    case "set_blend":
                        return CameraControl.SetBlend(@params);

                    case "force_camera":
                        return CameraControl.ForceCamera(@params);

                    case "release_override":
                        return CameraControl.ReleaseOverride(@params);

                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Valid actions: ping, create_camera, set_target, "
                            + "set_lens, set_priority, list_cameras, screenshot, screenshot_multiview, "
                            + "ensure_brain, get_brain_status, "
                            + "set_body, set_aim, set_noise, add_extension, remove_extension, "
                            + "set_blend, force_camera, release_override.");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ManageCamera] Action '{action}' failed: {ex}");
                return new ErrorResponse($"Error in action '{action}': {ex.Message}");
            }
        }
    }
}
