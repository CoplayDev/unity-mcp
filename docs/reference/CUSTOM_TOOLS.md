# Adding Custom Tools to MCP for Unity

MCP for Unity makes it easy to extend your AI assistant with custom capabilities. Using C# attributes and reflection, the system automatically discovers and registers your tools—no manual configuration needed.

This guide will walk you through creating your own tools, from simple synchronous operations to complex long-running tasks that survive Unity's domain reloads.

---

# Quick Start Guide

Let's get you up and running with your first custom tool.

## Step 1: Create Your Tool Handler

First, create a C# file in any `Editor/` folder within your Unity project. **The Editor folder is crucial**—we scan Editor assemblies for tools, so placing your code elsewhere means it won't be discovered.

Each tool is a static class with two key ingredients:
1. The `[McpForUnityTool]` attribute that tells the system "Hey, I'm a tool!"
2. A `HandleCommand(JObject)` method that does the actual work

You can also define a nested `Parameters` class with `[ToolParameter]` attributes. This gives your AI assistant helpful descriptions and lets it know which parameters are required.

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
                return new ErrorResponse("param1 is required");
            }

            DoSomethingAmazing(parameters.param1, parameters.param2);

            return new SuccessResponse("Custom tool executed successfully!", new
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

## Step 2: Refresh Your MCP Client

Once you've created your tool, you'll need to let your AI assistant know about it. While the MCP server can dynamically register new tools, not all clients pick up these changes automatically.

**The easiest approach:** Disconnect and reconnect to the MCP server in your client. This forces a fresh tool discovery.

**If that doesn't work:** Some clients (like Windsurf) may need you to remove and reconfigure the MCP for Unity server entirely. It's a bit more work, but it guarantees your new tools will appear.

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
                return new ErrorResponse("filename is required");
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
                    return new ErrorResponse("No camera found in the scene");
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

                return new SuccessResponse($"Screenshot saved to {absolutePath}", new
                {
                    path = absolutePath,
                    width = width,
                    height = height
                });
            }
            catch (System.Exception ex)
            {
                return new ErrorResponse($"Failed to capture screenshot: {ex.Message}");
            }
        }
    }
}

```

## Long-Running (Polled) Tools

Some operations—like running tests, baking lightmaps, or building players—take time and might even trigger Unity domain reloads. For these cases, you'll want a **polled tool**.

Here's how it works: Your tool starts the work and returns a "pending" signal. Behind the scenes, the Python middleware automatically polls Unity for updates until the job completes (or times out after 10 minutes).

### Setting Up Polling

Mark your tool with `RequiresPolling = true` and specify a `PollAction` (typically `"status"`):

```csharp
[McpForUnityTool(RequiresPolling = true, PollAction = "status")]
```

### The Three Key Ingredients

1. **Start the work:** Return `new PendingResponse("message", pollIntervalSeconds)` to acknowledge the job has started. The poll interval tells the server how long to wait between checks.

2. **Implement the poll action:** Create a method (like `Status`) that checks progress and returns `_mcp_status` of `pending`, `complete`, or `error`. **Important:** The middleware calls your `PollAction` string exactly as written—no automatic case conversion—so make sure your `HandleCommand` recognizes it.

3. **Persist your state:** Use `McpJobStateStore` to save progress to the `Library/` folder. This ensures your tool remembers what it was doing even after a domain reload wipes memory.

### Complete Example

```csharp
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;

[McpForUnityTool(
    "bake_lightmaps",
    Description = "Simulated async lightmap bake with polling",
    RequiresPolling = true,
    PollAction = "status"
)]
public static class BakeLightmaps
{
    private const string ToolName = "bake_lightmaps";
    private const float SimulatedDurationSeconds = 5f;

    private static bool s_isRunning;
    private static double s_lastUpdateTime;

    private class State
    {
        public string lastStatus { get; set; }
        public float progress { get; set; }
    }

    public static object HandleCommand(JObject @params)
    {
        if (s_isRunning)
        {
            var existing = McpJobStateStore.LoadState<State>(ToolName) ?? new State { lastStatus = "in_progress", progress = 0f };
            return new PendingResponse("Bake already running", 0.5, existing);
        }

        var state = new State { lastStatus = "in_progress", progress = 0f };
        McpJobStateStore.SaveState(ToolName, state);

        s_isRunning = true;
        s_lastUpdateTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += UpdateBake;

        return new PendingResponse("Starting lightmap bake", 0.5, new { state.lastStatus, state.progress });
    }

    public static object Status(JObject _)
    {
        var state = McpJobStateStore.LoadState<State>(ToolName) ?? new State { lastStatus = "unknown", progress = 0f };

        if (state.lastStatus == "completed")
        {
            return new { _mcp_status = "complete", message = "Bake finished", data = state };
        }

        if (state.lastStatus == "error")
        {
            return new { _mcp_status = "error", error = "Bake failed", data = state };
        }

        return new PendingResponse($"Baking... {state.progress:P0}", 0.5, state);
    }

    private static void UpdateBake()
    {
        if (!s_isRunning)
        {
            EditorApplication.update -= UpdateBake;
            return;
        }

        var now = EditorApplication.timeSinceStartup;
        var delta = now - s_lastUpdateTime;
        s_lastUpdateTime = now;

        var state = McpJobStateStore.LoadState<State>(ToolName) ?? new State { lastStatus = "in_progress", progress = 0f };
        state.progress = Mathf.Clamp01(state.progress + (float)(delta / SimulatedDurationSeconds));

        if (state.progress >= 1f)
        {
            state.lastStatus = "completed";
            s_isRunning = false;
            EditorApplication.update -= UpdateBake;
        }
        else
        {
            state.lastStatus = "in_progress";
        }

        McpJobStateStore.SaveState(ToolName, state);
    }
}
```

### How the Polling Protocol Works

- **`_mcp_status: "pending"`** tells the middleware to keep checking back.
- **`_mcp_poll_interval`** (in seconds) controls how long to wait between polls. The server clamps this between 0.1 and 5 seconds to balance responsiveness with performance.
- **Null or empty responses** are treated as "still working" and trigger another poll.
- **Timeout protection:** After 10 minutes, the server gives up and returns a timeout error along with the last response it received.
- **Action routing:** The initial call uses whatever action your tool expects (often implicit). Subsequent polls use your exact `PollAction` string—no automatic snake_case or camelCase conversion—so make sure your `HandleCommand` switch statement handles it correctly.
