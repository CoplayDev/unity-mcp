# Adding Custom Tools to MCP for Unity

MCP for Unity supports auto-discovery of custom tools using C# attributes and reflection. This allows you to easily extend the MCP server with your own tools.

---

# How to Use (Quick Start Guide)

This section shows you how to add custom tools to your Unity project.

## Step 1: Create C# Handler

Create a C# file anywhere in your Unity project in an `Editor/` folder (**this is important!** We load Editor assemblies, so your tool should be in an Editor folder). Each tool is a static class decorated with `[McpForUnityTool]` and exposes a `HandleCommand(JObject)` entry point. You can optionally define a nested parameter class with `[ToolParameter]` attributes so the MCP client receives descriptions and optional/required metadata.

```csharp
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;

namespace MyProject.Editor.CustomTools
{
    [McpForUnityTool("my_custom_tool")]
    public static class MyCustomTool
    {
        public class Parameters
        {
            [ToolParameter("Value to process")]
            public string param1 { get; set; }

            [ToolParameter("Optional integer payload", Required = false)]
            public int? param2 { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var parameters = @params.ToObject<Parameters>();

            if (string.IsNullOrEmpty(parameters.param1))
            {
                return Response.Error("param1 is required");
            }

            DoSomethingAmazing(parameters.param1, parameters.param2);

            return Response.Success("Custom tool executed successfully!", new
            {
                parameters.param1,
                parameters.param2
            });
        }

        private static void DoSomethingAmazing(string param1, int? param2)
        {
            // Your implementation
        }
    }
}
```

## Step 2: Refresh MCP Client

The MCP server can dynamically register tools, however, not all MCP clients support being updated of the new tools. We recommend in your MCP client that you disconnect and reconnect to the MCP server so the new tools are available. Sometimes you may have to remove the configuration and then reconfigure the MCP for Unity server for the new tools to appear (e.g., in Windsurf).

## Complete Example: Screenshot Tool

### C# Handler (`Assets/Editor/ScreenShots/CaptureScreenshotTool.cs`)

```csharp
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Helpers;

namespace MyProject.Editor.CustomTools
{
    [McpForUnityTool(
        name: "capture_screenshot",
        Description = "Capture screenshots in Unity, saving them as PNGs"
    )]
    public static class CaptureScreenshotTool
    {
        // Define parameters as a nested class for clarity
        public class Parameters
        {
            [ToolParameter("Screenshot filename without extension, e.g., screenshot_01")]
            public string filename { get; set; }

            [ToolParameter("Width of the screenshot in pixels", Required = false)]
            public int? width { get; set; }

            [ToolParameter("Height of the screenshot in pixels", Required = false)]
            public int? height { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            // Parse parameters
            var parameters = @params.ToObject<Parameters>();

            if (string.IsNullOrEmpty(parameters.filename))
            {
                return Response.Error("filename is required");
            }

            try
            {
                int width = parameters.width ?? Screen.width;
                int height = parameters.height ?? Screen.height;

                string absolutePath = Path.Combine(Application.dataPath, "Screenshots",
                    parameters.filename + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));

                // Find camera
                Camera camera = Camera.main ?? Object.FindFirstObjectByType<Camera>();
                if (camera == null)
                {
                    return Response.Error("No camera found in the scene");
                }

                // Capture screenshot
                RenderTexture rt = new RenderTexture(width, height, 24);
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                screenshot.Apply();

                // Cleanup
                camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);

                // Save
                byte[] bytes = screenshot.EncodeToPNG();
                File.WriteAllBytes(absolutePath, bytes);
                Object.DestroyImmediate(screenshot);

                return Response.Success($"Screenshot saved to {absolutePath}", new
                {
                    path = absolutePath,
                    width = width,
                    height = height
                });
            }
            catch (System.Exception ex)
            {
                return Response.Error($"Failed to capture screenshot: {ex.Message}");
            }
        }
    }
}

```
