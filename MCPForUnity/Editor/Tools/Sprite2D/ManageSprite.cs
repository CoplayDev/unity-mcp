using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Sprite2D
{
    [McpForUnityTool("manage_sprite", AutoRegister = false, Group = "animation")]
    public static class ManageSprite
    {
        private static readonly string[] ValidActions =
        {
            "get_info", "slice_sheet", "setup_clips",
            "setup_controller", "full_setup"
        };

        public static object HandleCommand(JObject @params)
        {
            var diagnostics = new SpriteDiagnosticBuilder();

            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
                return diagnostics.Fail("BAD_PARAM",
                    "'action' is required. Valid: " + string.Join(", ", ValidActions));

            try
            {
                switch (action)
                {
                    case "get_info":
                        return SpriteImportSetup.GetInfo(@params, diagnostics);

                    case "slice_sheet":
                        return SpriteImportSetup.SliceSheet(@params, diagnostics);

                    case "setup_clips":
                        return SpriteClipBuilder.SetupClips(@params, diagnostics);

                    case "setup_controller":
                        return SpriteControllerBuilder.Build(@params, diagnostics);

                    case "full_setup":
                        return SpriteFullSetup.Run(@params, diagnostics);

                    default:
                        return diagnostics.Fail("BAD_PARAM",
                            $"Unknown action '{action}'. Valid: " + string.Join(", ", ValidActions));
                }
            }
            catch (System.Exception e)
            {
                McpLog.Error($"[ManageSprite] Action '{action}' failed: {e}");
                return diagnostics.Fail("INTERNAL", $"Internal error processing action '{action}': {e.Message}");
            }
        }
    }
}
