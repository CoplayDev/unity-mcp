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
            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
                return new ErrorResponse(
                    "'action' is required. Valid: " + string.Join(", ", ValidActions));

            var diagnostics = new SpriteDiagnosticBuilder();

            switch (action)
            {
                case "get_info":
                    return SpriteImportSetup.GetInfo(@params);

                case "slice_sheet":
                    return SpriteImportSetup.SliceSheet(@params, diagnostics);

                case "setup_clips":
                    return SpriteClipBuilder.SetupClips(@params, diagnostics);

                case "setup_controller":
                    return SpriteControllerBuilder.Build(@params, diagnostics);

                case "full_setup":
                    return SpriteFullSetup.Run(@params);

                default:
                    return new ErrorResponse(
                        $"Unknown action '{action}'. Valid: " + string.Join(", ", ValidActions));
            }
        }
    }
}
