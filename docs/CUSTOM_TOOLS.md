# Adding Custom Tools to Unity MCP

Unity MCP now supports auto-discovery of custom tools using decorators (Python) and attributes (C#). This allows you to easily extend the MCP server with your own tools without modifying core files.

## Python Side (MCP Server)

### Creating a Custom Tool

1. **Create a new Python file** in `UnityMcpBridge/UnityMcpServer~/src/tools/` (or any location that gets imported)

2. **Use the `@mcp_for_unity_tool` decorator**:

```python
from typing import Annotated, Any
from mcp.server.fastmcp import Context
from registry import mcp_for_unity_tool
from unity_connection import send_command_with_retry

@mcp_for_unity_tool(
    description="My custom tool that does something amazing"
)
def my_custom_tool(
    ctx: Context,
    param1: Annotated[str, "Description of param1"],
    param2: Annotated[int, "Description of param2"] | None = None
) -> dict[str, Any]:
    ctx.info(f"Processing my_custom_tool: {param1}")

    # Prepare parameters for Unity
    params = {
        "action": "do_something",
        "param1": param1,
        "param2": param2,
    }
    params = {k: v for k, v in params.items() if v is not None}

    # Send to Unity handler
    response = send_command_with_retry("my_custom_tool", params)
    return response if isinstance(response, dict) else {"success": False, "message": str(response)}
```

3. **The tool is automatically registered!** The decorator:
   - Auto-generates the tool name from the function name (e.g., `my_custom_tool`)
   - Registers the tool with FastMCP during module import

### Decorator Options

```python
@mcp_for_unity_tool(
    name="custom_name",          # Optional: the function name is used by default
    description="Tool description",  # Required: describe what the tool does
)
```

You can use all options available in FastMCP's `mcp.tool` function decorator: <https://gofastmcp.com/servers/tools#tools>. The `description` is the only required field.

### Auto-Discovery

Tools are automatically discovered when:
- The Python file is in the `tools/` directory
- The file is imported during server startup
- The decorator `@mcp_for_unity_tool` is used

## C# Side (Unity Editor)

### Creating a Custom Tool Handler

1. **Create a new C# file** anywhere in your Unity project (typically in `Editor/`)

2. **Add the `[McpForUnityTool]` attribute** and implement `HandleCommand`:

```csharp
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MyProject.Editor.CustomTools
{
    // The name argument is optional, it uses a snake_case version of the class name by default
    [McpForUnityTool("my_custom_tool")]
    public static class MyCustomTool
    {
        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString();
            string param1 = @params["param1"]?.ToString();
            int? param2 = @params["param2"]?.ToObject<int?>();

            // Your custom logic here
            if (string.IsNullOrEmpty(param1))
            {
                return Response.Error("param1 is required");
            }

            // Do something amazing
            DoSomethingAmazing(param1, param2);

            return Response.Success("Custom tool executed successfully!");
        }

        private static void DoSomethingAmazing(string param1, int? param2)
        {
            // Your implementation
        }
    }
}
```

3. **The tool is automatically registered!** Unity will discover it via reflection on startup.

### Attribute Options

```csharp
// Explicit command name
[McpForUnityTool("my_custom_tool")]
public static class MyCustomTool { }

// Auto-generated from class name (MyCustomTool → my_custom_tool)
[McpForUnityTool]
public static class MyCustomTool { }
```

### Auto-Discovery

Tools are automatically discovered when:
- The class has the `[McpForUnityTool]` attribute
- The class has a `public static HandleCommand(JObject)` method
- Unity loads the assembly containing the class

## Complete Example: Custom Screenshot Tool

### Python (`UnityMcpServer~/src/tools/screenshot_tool.py`)

```python
from typing import Annotated, Any

from mcp.server.fastmcp import Context

from registry import mcp_for_unity_tool
from unity_connection import send_command_with_retry


@mcp_for_unity_tool(
    description="Capture screenshots in Unity, saving them as PNGs"
)
def capture_screenshot(
    ctx: Context,
    filename: Annotated[str, "Screenshot filename without extension, e.g., screenshot_01"],
) -> dict[str, Any]:
    ctx.info(f"Capturing screenshot: {filename}")

    params = {
        "action": "capture",
        "filename": filename,
    }
    params = {k: v for k, v in params.items() if v is not None}

    response = send_command_with_retry("capture_screenshot", params)
    return response if isinstance(response, dict) else {"success": False, "message": str(response)}
```

### C# (`Editor/CaptureScreenshotTool.cs`)

```csharp
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MCPForUnity.Editor.Tools;

namespace MyProject.Editor.Tools
{
    [McpForUnityTool("capture_screenshot")]
    public static class CaptureScreenshotTool
    {
        public static object HandleCommand(JObject @params)
        {
            string filename = @params["filename"]?.ToString();

            if (string.IsNullOrEmpty(filename))
            {
                return MCPForUnity.Editor.Helpers.Response.Error("filename is required");
            }

            try
            {
                string absolutePath = Path.Combine(Application.dataPath, "Screenshots", filename);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));

                // Find the main camera
                Camera camera = Camera.main;
                if (camera == null)
                {
                    camera = Object.FindFirstObjectByType<Camera>();
                }

                if (camera == null)
                {
                    return MCPForUnity.Editor.Helpers.Response.Error("No camera found in the scene");
                }

                // Create a RenderTexture
                RenderTexture rt = new RenderTexture(Screen.width, Screen.height, 24);
                camera.targetTexture = rt;

                // Render the camera's view
                camera.Render();

                // Read pixels from the RenderTexture
                RenderTexture.active = rt;
                Texture2D screenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                screenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                screenshot.Apply();

                // Clean up
                camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);

                // Save to file
                byte[] bytes = screenshot.EncodeToPNG();
                File.WriteAllBytes(absolutePath, bytes);
                Object.DestroyImmediate(screenshot);

                return MCPForUnity.Editor.Helpers.Response.Success($"Screenshot saved to {absolutePath}", new
                {
                    path = absolutePath,
                });
            }
            catch (System.Exception ex)
            {
                return MCPForUnity.Editor.Helpers.Response.Error($"Failed to capture screenshot: {ex.Message}");
            }
        }
    }
}
```

## Best Practices

### Python
- ✅ Use type hints with `Annotated` for parameter documentation
- ✅ Return `dict[str, Any]` with `{"success": bool, "message": str, "data": Any}`
- ✅ Use `ctx.info()` for logging
- ✅ Handle errors gracefully and return structured error responses
- ✅ Use `send_command_with_retry()` for Unity communication

### C#
- ✅ Use the `Response.Success()` and `Response.Error()` helper methods
- ✅ Validate input parameters before processing
- ✅ Use `@params["key"]?.ToObject<Type>()` for safe type conversion
- ✅ Return structured responses with meaningful data
- ✅ Handle exceptions and return error responses

## Debugging

### Python
- Check server logs: `~/Library/Application Support/UnityMCP/Logs/unity_mcp_server.log`
- Look for: `"Registered X MCP tools"` message on startup
- Use `ctx.info()` for debugging messages

### C#
- Check Unity Console for: `"MCP-FOR-UNITY: Auto-discovered X tools"` message
- Look for warnings about missing `HandleCommand` methods
- Use `Debug.Log()` in your handler for debugging

## Troubleshooting

**Tool not appearing:**
- Python: Ensure the file is in `tools/` directory and imports the decorator
- C#: Ensure the class has `[McpForUnityTool]` attribute and `HandleCommand` method

**Name conflicts:**
- Use explicit names in decorators/attributes to avoid conflicts
- Check registered tools: `CommandRegistry.GetAllCommandNames()` in C#

**Tool not being called:**
- Verify the command name matches between Python and C#
- Check that parameters are being passed correctly
- Look for errors in logs
