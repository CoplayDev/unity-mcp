using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools.Blender;
using MCPForUnity.Editor.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.MenuItems
{
    /// <summary>
    /// Menu entry points that drive the same code path as the blender_bridge tool, so the
    /// Blender handoff works without any AI client attached. Settings live in the Generative tab.
    /// </summary>
    public static class BlenderBridgeMenu
    {
        private const string Root = ProductInfo.MenuRoot + "/Blender Bridge/";

        [MenuItem(Root + "Import Selection From Blender (GLB)", priority = 20)]
        private static void ImportSelection()
        {
            Run(new JObject { ["action"] = "import_model", ["selection_only"] = true, ["format"] = "glb" });
        }

        [MenuItem(Root + "Import Whole Scene From Blender (GLB)", priority = 21)]
        private static void ImportScene()
        {
            Run(new JObject { ["action"] = "import_model", ["format"] = "glb", ["name"] = "BlenderScene" });
        }

        [MenuItem(Root + "Blender Viewport Screenshot", priority = 22)]
        private static void Screenshot()
        {
            JObject r = Run(new JObject { ["action"] = "screenshot" });
            string path = r?["data"]?["path"]?.ToString();
            if (!string.IsNullOrEmpty(path)) EditorUtility.RevealInFinder(path);
        }

        [MenuItem(Root + "Settings...", priority = 40)]
        private static void OpenSettings()
        {
            MCPForUnityEditorWindow.ShowWindow();
            McpLog.Info("Blender Bridge settings are in the Generative tab of the MCP for Unity window.");
        }

        private static JObject Run(JObject parameters)
        {
            object result = BlenderBridgeTool.HandleCommand(parameters);
            JObject json = JObject.FromObject(result);
            bool ok = json.Value<bool?>("success") ?? false;
            string text = json.ToString(Formatting.Indented);
            if (ok) McpLog.Info($"[Blender Bridge] {parameters["action"]}: {json["message"]}\n{text}");
            else McpLog.Error($"[Blender Bridge] {parameters["action"]} failed: {json["error"]}\n{text}");
            return json;
        }
    }
}
