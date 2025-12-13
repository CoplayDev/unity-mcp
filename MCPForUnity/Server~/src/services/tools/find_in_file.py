import base64
import os
import re
from typing import Annotated, Any
from urllib.parse import unquote, urlparse

from fastmcp import Context

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


def _split_uri(uri: str) -> tuple[str, str]:
    """Split an incoming URI or path into (name, directory) suitable for Unity.

    Rules:
    - unity://path/Assets/... → keep as Assets-relative (after decode/normalize)
    - file://... → percent-decode, normalize, strip host and leading slashes,
        then, if any 'Assets' segment exists, return path relative to that 'Assets' root.
        Otherwise, fall back to original name/dir behavior.
    - plain paths → decode/normalize separators; if they contain an 'Assets' segment,
        return relative to 'Assets'.
    """
    raw_path: str
    if uri.startswith("unity://path/"):
        raw_path = uri[len("unity://path/"):]
    elif uri.startswith("file://"):
        parsed = urlparse(uri)
        host = (parsed.netloc or "").strip()
        p = parsed.path or ""
        # UNC: file://server/share/... -> //server/share/...
        if host and host.lower() != "localhost":
            p = f"//{host}{p}"
        # Use percent-decoded path, preserving leading slashes
        raw_path = unquote(p)
    else:
        raw_path = uri

    # Percent-decode any residual encodings and normalize separators
    raw_path = unquote(raw_path).replace("\\", "/")
    # Strip leading slash only for Windows drive-letter forms like "/C:/..."
    if os.name == "nt" and len(raw_path) >= 3 and raw_path[0] == "/" and raw_path[2] == ":":
        raw_path = raw_path[1:]

    # Normalize path (collapse ../, ./)
    norm = os.path.normpath(raw_path).replace("\\", "/")

    # If an 'Assets' segment exists, compute path relative to it (case-insensitive)
    parts = [p for p in norm.split("/") if p not in ("", ".")]
    idx = next((i for i, seg in enumerate(parts)
                if seg.lower() == "assets"), None)
    assets_rel = "/".join(parts[idx:]) if idx is not None else None

    effective_path = assets_rel if assets_rel else norm
    # For POSIX absolute paths outside Assets, drop the leading '/'
    # to return a clean relative-like directory (e.g., '/tmp' -> 'tmp').
    if effective_path.startswith("/"):
        effective_path = effective_path[1:]

    name = os.path.splitext(os.path.basename(effective_path))[0]
    directory = os.path.dirname(effective_path)
    return name, directory


@mcp_for_unity_tool(description="Searches a file with a regex pattern and returns line numbers and excerpts.")
async def find_in_file(
    ctx: Context,
    uri: Annotated[str, "The resource URI to search under Assets/ or file path form supported by read_resource"],
    pattern: Annotated[str, "The regex pattern to search for"],
    project_root: Annotated[str | None, "Optional project root path"] = None,
    max_results: Annotated[int, "Cap results to avoid huge payloads"] = 200,
    ignore_case: Annotated[bool | str | None, "Case insensitive search"] = True,
) -> dict[str, Any]:
    # project_root is currently unused but kept for interface consistency
    unity_instance = get_unity_instance_from_context(ctx)
    await ctx.info(
        f"Processing find_in_file: {uri} (unity_instance={unity_instance or 'default'})")

    name, directory = _split_uri(uri)
    
    # 1. Read file content via Unity
    read_resp = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_script",
        {
            "action": "read",
            "name": name,
            "path": directory,
        },
    )

    if not isinstance(read_resp, dict) or not read_resp.get("success"):
        return read_resp if isinstance(read_resp, dict) else {"success": False, "message": str(read_resp)}

    data = read_resp.get("data", {})
    contents = data.get("contents")
    if not contents and data.get("contentsEncoded") and data.get("encodedContents"):
        try:
            contents = base64.b64decode(data.get("encodedContents", "").encode(
                "utf-8")).decode("utf-8", "replace")
        except (ValueError, TypeError, base64.binascii.Error):
            contents = contents or ""
    
    if contents is None:
        return {"success": False, "message": "Could not read file content."}

    # 2. Perform regex search
    flags = re.MULTILINE
    # Handle ignore_case which can be boolean or string from some clients
    ic = ignore_case
    if isinstance(ic, str):
        ic = ic.lower() in ("true", "1", "yes")
    if ic:
        flags |= re.IGNORECASE

    try:
        regex = re.compile(pattern, flags)
    except re.error as e:
        return {"success": False, "message": f"Invalid regex pattern: {e}"}

    # If the regex is not multiline specific (doesn't contain \n literal match logic), 
    # we could iterate lines. But users might use multiline regexes.
    # Let's search the whole content and map back to lines.
    
    found = list(regex.finditer(contents))
    
    results = []
    count = 0
    
    for m in found:
        if count >= max_results:
            break
        
        start_idx = m.start()
        end_idx = m.end()
        
        # Calculate line number
        # Count newlines up to start_idx
        line_num = contents.count('\n', 0, start_idx) + 1
        
        # Get line content for excerpt
        # Find start of line
        line_start = contents.rfind('\n', 0, start_idx) + 1
        # Find end of line
        line_end = contents.find('\n', start_idx)
        if line_end == -1:
            line_end = len(contents)
            
        line_content = contents[line_start:line_end]
        
        # Create excerpt
        # We can just return the line content as excerpt
        
        results.append({
            "line": line_num,
            "content": line_content.strip(), # detailed match info?
            "match": m.group(0),
            "start": start_idx,
            "end": end_idx
        })
        count += 1

    return {
        "success": True,
        "data": {
            "matches": results,
            "count": len(results),
            "total_matches": len(found)
        }
    }

