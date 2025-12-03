"""
Runtime compilation tool for MCP Unity.
Compiles and loads C# code at runtime without domain reload.
"""

from typing import Annotated, Any
from fastmcp import Context
from registry import mcp_for_unity_tool
from unity_connection import send_command_with_retry


async def safe_info(ctx: Context, message: str) -> None:
    """Safely send info messages when a request context is available."""
    try:
        if ctx and hasattr(ctx, "info"):
            await ctx.info(message)
    except RuntimeError as ex:
        # FastMCP raises this when called outside of an active request
        if "outside of a request" not in str(ex):
            raise


def handle_unity_command(command_name: str, params: dict) -> dict[str, Any]:
    """
    Wrapper for Unity commands with better error handling.
    """
    try:
        response = send_command_with_retry(command_name, params)
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}
    except Exception as e:
        error_msg = str(e)
        if "Context is not available" in error_msg or "not available outside of a request" in error_msg:
            return {
                "success": False,
                "message": "Unity is not connected. Please ensure Unity Editor is running and MCP bridge is active.",
                "error": "connection_error",
                "details": "This tool requires an active connection to Unity. Make sure the Unity project is open and the MCP bridge is initialized."
            }
        return {
            "success": False,
            "message": f"Command failed: {error_msg}",
            "error": "tool_error"
        }


@mcp_for_unity_tool(
    description="Compile and load C# code at runtime without domain reload. Creates dynamic assemblies that can be attached to GameObjects during Play Mode. Requires Roslyn (Microsoft.CodeAnalysis.CSharp) to be installed in Unity."
)
async def compile_runtime_code(
    ctx: Context,
    code: Annotated[str, "Complete C# code including using statements, namespace, and class definition"],
    assembly_name: Annotated[str, "Unique name for the dynamic assembly. If not provided, a timestamp-based name will be generated."] | None = None,
    attach_to_gameobject: Annotated[str, "Name or hierarchy path of GameObject to attach the compiled script to (e.g., 'Player' or 'Canvas/Panel')"] | None = None,
    load_immediately: Annotated[bool, "Whether to load the assembly immediately after compilation. Default is true."] = True
) -> dict[str, Any]:
    """
    Compile C# code at runtime and optionally attach it to a GameObject. Only enable it with Roslyn installed in Unity.
    
    REQUIREMENTS:
    - Unity must be running and connected
    - Roslyn (Microsoft.CodeAnalysis.CSharp) must be installed via NuGet
    - USE_ROSLYN scripting define symbol must be set
    
    This tool allows you to:
    - Compile new C# scripts without restarting Unity
    - Load compiled assemblies into the running Unity instance
    - Attach MonoBehaviour scripts to GameObjects dynamically
    - Preserve game state during script additions
    
    Example code:
    ```csharp
    using UnityEngine;
    
    namespace DynamicScripts
    {
        public class MyDynamicBehavior : MonoBehaviour
        {
            void Start()
            {
                Debug.Log("Dynamic script loaded!");
            }
        }
    }
    ```
    """
    await safe_info(ctx, f"Compiling runtime code for assembly: {assembly_name or 'auto-generated'}")
    
    params = {
        "action": "compile_and_load",
        "code": code,
        "assembly_name": assembly_name,
        "attach_to": attach_to_gameobject,
        "load_immediately": load_immediately,
    }
    params = {k: v for k, v in params.items() if v is not None}
    
    return handle_unity_command("runtime_compilation", params)


@mcp_for_unity_tool(
    description="List all dynamically loaded assemblies in the current Unity session"
)
async def list_loaded_assemblies(
    ctx: Context,
) -> dict[str, Any]:
    """
    Get a list of all dynamically loaded assemblies created during this session.
    
    Returns information about:
    - Assembly names
    - Number of types in each assembly
    - Load timestamps
    - DLL file paths
    """
    await safe_info(ctx, "Retrieving loaded dynamic assemblies...")
    
    params = {"action": "list_loaded"}
    return handle_unity_command("runtime_compilation", params)


@mcp_for_unity_tool(
    description="Get all types (classes) from a dynamically loaded assembly"
)
async def get_assembly_types(
    ctx: Context,
    assembly_name: Annotated[str, "Name of the assembly to query"],
) -> dict[str, Any]:
    """
    Retrieve all types defined in a specific dynamic assembly.
    
    This is useful for:
    - Inspecting what was compiled
    - Finding MonoBehaviour classes to attach
    - Debugging compilation results
    """
    await safe_info(ctx, f"Getting types from assembly: {assembly_name}")
    
    params = {"action": "get_types", "assembly_name": assembly_name}
    return handle_unity_command("runtime_compilation", params)


@mcp_for_unity_tool(
    description="Execute C# code using the RoslynRuntimeCompiler with full GUI tool features including history tracking, MonoBehaviour support, and coroutines"
)
async def execute_with_roslyn(
    ctx: Context,
    code: Annotated[str, "Complete C# source code to compile and execute"],
    class_name: Annotated[str, "Name of the class to instantiate/invoke (default: AIGenerated)"] = "AIGenerated",
    method_name: Annotated[str, "Name of the static method to call (default: Run)"] = "Run",
    target_object: Annotated[str, "Name or path of target GameObject (optional)"] | None = None,
    attach_as_component: Annotated[bool, "If true and type is MonoBehaviour, attach as component (default: false)"] = False,
) -> dict[str, Any]:
    """
    Execute C# code using Unity's RoslynRuntimeCompiler tool with advanced features:
    
    - MonoBehaviour attachment: Set attach_as_component=true for classes inheriting MonoBehaviour
    - Static method execution: Call public static methods (e.g., public static void Run(GameObject host))
    - Coroutine support: Methods returning IEnumerator will be started as coroutines
    - History tracking: All compilations are tracked in history for later review
    
    Supported method signatures:
    - public static void Run()
    - public static void Run(GameObject host)
    - public static void Run(MonoBehaviour host)
    - public static IEnumerator RunCoroutine(MonoBehaviour host)
    
    Example MonoBehaviour:
    ```csharp
    using UnityEngine;
    public class Rotator : MonoBehaviour {
        void Update() {
            transform.Rotate(Vector3.up * 30f * Time.deltaTime);
        }
    }
    ```
    
    Example Static Method:
    ```csharp
    using UnityEngine;
    public class AIGenerated {
        public static void Run(GameObject host) {
            Debug.Log($"Hello from {host.name}!");
        }
    }
    ```
    """
    await safe_info(ctx, f"Executing code with RoslynRuntimeCompiler: {class_name}.{method_name}")
    
    params = {
        "action": "execute_with_roslyn",
        "code": code,
        "class_name": class_name,
        "method_name": method_name,
        "target_object": target_object,
        "attach_as_component": attach_as_component,
    }
    params = {k: v for k, v in params.items() if v is not None}
    
    return handle_unity_command("runtime_compilation", params)


@mcp_for_unity_tool(
    description="Get the compilation history from RoslynRuntimeCompiler showing all previous compilations and executions"
)
async def get_compilation_history(
    ctx: Context,
) -> dict[str, Any]:
    """
    Retrieve the compilation history from the RoslynRuntimeCompiler.
    
    History includes:
    - Timestamp of each compilation
    - Class and method names
    - Success/failure status
    - Compilation diagnostics
    - Target GameObject names
    - Source code previews
    
    This is useful for:
    - Reviewing what code has been compiled
    - Debugging failed compilations
    - Tracking execution flow
    - Auditing dynamic code changes
    """
    await safe_info(ctx, "Retrieving compilation history...")
    
    params = {"action": "get_history"}
    return handle_unity_command("runtime_compilation", params)


@mcp_for_unity_tool(
    description="Save the compilation history to a JSON file outside the Assets folder"
)
async def save_compilation_history(
    ctx: Context,
) -> dict[str, Any]:
    """
    Save all compilation history to a timestamped JSON file.
    
    The file is saved to: ProjectRoot/RoslynHistory/RoslynHistory_TIMESTAMP.json
    
    This allows you to:
    - Keep a permanent record of dynamic compilations
    - Review history after Unity restarts
    - Share compilation sessions with team members
    - Archive successful code patterns
    """
    await safe_info(ctx, "Saving compilation history to file...")
    
    params = {"action": "save_history"}
    return handle_unity_command("runtime_compilation", params)


@mcp_for_unity_tool(
    description="Clear all compilation history from RoslynRuntimeCompiler"
)
async def clear_compilation_history(
    ctx: Context,
) -> dict[str, Any]:
    """
    Clear all compilation history entries.
    
    This removes all tracked compilations from memory but does not delete
    saved history files. Use this to start fresh or reduce memory usage.
    """
    await safe_info(ctx, "Clearing compilation history...")

    params = {"action": "clear_history"}
    return handle_unity_command("runtime_compilation", params)
