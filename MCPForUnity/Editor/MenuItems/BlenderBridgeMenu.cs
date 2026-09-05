using System.Threading.Tasks;
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
    /// Actions are awaited so the editor stays responsive while Blender exports.
    /// </summary>
    public static class BlenderBridgeMenu
    {
        private const string Root = ProductInfo.MenuRoot + "/Blender Bridge/";

        /// <summary>Exports Blender's current selection as GLB and places it in the open scene.</summary>
        [MenuItem(Root + "Import Selection From Blender (GLB)", priority = 20)]
        private static async void ImportSelection()
        {
            await RunAsync(new JObject { ["action"] = "import_model", ["selection_only"] = true, ["format"] = "glb" });
        }

        /// <summary>Exports Blender's whole scene as GLB and places it in the open scene.</summary>
        [MenuItem(Root + "Import Whole Scene From Blender (GLB)", priority = 21)]
        private static async void ImportScene()
        {
            await RunAsync(new JObject { ["action"] = "import_model", ["format"] = "glb", ["name"] = "BlenderScene" });
        }

        /// <summary>Captures Blender's viewport and reveals the PNG.</summary>
        [MenuItem(Root + "Blender Viewport Screenshot", priority = 22)]
        private static async void Screenshot()
        {
            JObject r = await RunAsync(new JObject { ["action"] = "screenshot" });
            string path = r?["data"]?["path"]?.ToString();
            if (!string.IsNullOrEmpty(path)) EditorUtility.RevealInFinder(path);
        }

        /// <summary>Opens the MCP for Unity window; the Blender Bridge panel is in its Generative tab.</summary>
        [MenuItem(Root + "Settings...", priority = 40)]
        private static void OpenSettings()
        {
            MCPForUnityEditorWindow.ShowWindow();
            McpLog.Info("Blender Bridge settings are in the Generative tab of the MCP for Unity window.");
        }

        /// <summary>Runs one bridge action and logs its full result.</summary>
        private static async Task<JObject> RunAsync(JObject parameters)
        {
            object result = await BlenderBridgeTool.HandleCommand(parameters);
            JObject json = JObject.FromObject(result);
            bool ok = json.Value<bool?>("success") ?? false;
            string text = json.ToString(Formatting.Indented);
            if (ok) McpLog.Info($"[Blender Bridge] {parameters["action"]}: {json["message"]}\n{text}");
            else McpLog.Error($"[Blender Bridge] {parameters["action"]} failed: {json["error"]}\n{text}");
            return json;
        }
    }
}
