import base64
import os
from typing import Annotated, Any, Literal
from urllib.parse import urlparse, unquote

from fastmcp import FastMCP, Context

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
import transport.legacy.unity_connection


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


@mcp_for_unity_tool(description=(
    """Apply small text edits to a C# script identified by URI.
    IMPORTANT: This tool replaces EXACT character positions. Always verify content at target lines/columns BEFORE editing!
    RECOMMENDED WORKFLOW:
        1. First call resources/read with start_line/line_count to verify exact content
        2. Count columns carefully (or use find_in_file to locate patterns)
        3. Apply your edit with precise coordinates
        4. Consider script_apply_edits with anchors for safer pattern-based replacements
    Notes:
        - For method/class operations, use script_apply_edits (safer, structured edits)
        - For pattern-based replacements, consider anchor operations in script_apply_edits
        - Lines, columns are 1-indexed
        - Tabs count as 1 column"""
))
async def apply_text_edits(
    ctx: Context,
    uri: Annotated[str, "URI of the script to edit under Assets/ directory, unity://path/Assets/... or file://... or Assets/..."],
    edits: Annotated[list[dict[str, Any]], "List of edits to apply to the script, i.e. a list of {startLine,startCol,endLine,endCol,newText} (1-indexed!)"],
    precondition_sha256: Annotated[str,
                                   "Optional SHA256 of the script to edit, used to prevent concurrent edits"] | None = None,
    strict: Annotated[bool,
                      "Optional strict flag, used to enforce strict mode"] | None = None,
    options: Annotated[dict[str, Any],
                       "Optional options, used to pass additional options to the script editor"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)
    await ctx.info(
        f"Processing apply_text_edits: {uri} (unity_instance={unity_instance or 'default'})")
    name, directory = _split_uri(uri)

    # Normalize common aliases/misuses for resilience:
    # - Accept LSP-style range objects: {range:{start:{line,character}, end:{...}}, newText|text}
    # - Accept index ranges as a 2-int array: {range:[startIndex,endIndex], text}
    # If normalization is required, read current contents to map indices -> 1-based line/col.
    def _needs_normalization(arr: list[dict[str, Any]]) -> bool:
        for e in arr or []:
            if ("startLine" not in e) or ("startCol" not in e) or ("endLine" not in e) or ("endCol" not in e) or ("newText" not in e and "text" in e):
                return True
        return False

    normalized_edits: list[dict[str, Any]] = []
    warnings: list[str] = []
    if _needs_normalization(edits):
        # Read file to support index->line/col conversion when needed
        read_resp = await send_with_unity_instance(
            transport.legacy.unity_connection.async_send_command_with_retry,
            unity_instance,
            "manage_script",
            {
                "action": "read",
                "name": name,
                "path": directory,
            },
        )
        if not (isinstance(read_resp, dict) and read_resp.get("success")):
            return read_resp if isinstance(read_resp, dict) else {"success": False, "message": str(read_resp)}
        data = read_resp.get("data", {})
        contents = data.get("contents")
        if not contents and data.get("contentsEncoded") and data.get("encodedContents"):
            try:
                contents = base64.b64decode(data.get("encodedContents", "").encode(
                    "utf-8")).decode("utf-8", "replace")
            except Exception:
                contents = contents or ""

        # Helper to map 0-based character index to 1-based line/col
        def line_col_from_index(idx: int) -> tuple[int, int]:
            if idx <= 0:
                return 1, 1
            # Count lines up to idx and position within line
            nl_count = contents.count("\n", 0, idx)
            line = nl_count + 1
            last_nl = contents.rfind("\n", 0, idx)
            col = (idx - (last_nl + 1)) + 1 if last_nl >= 0 else idx + 1
            return line, col

        for e in edits or []:
            e2 = dict(e)
            # Map text->newText if needed
            if "newText" not in e2 and "text" in e2:
                e2["newText"] = e2.pop("text")

            if "startLine" in e2 and "startCol" in e2 and "endLine" in e2 and "endCol" in e2:
                # Guard: explicit fields must be 1-based.
                zero_based = False
                for k in ("startLine", "startCol", "endLine", "endCol"):
                    try:
                        if int(e2.get(k, 1)) < 1:
                            zero_based = True
                    except Exception:
                        pass
                if zero_based:
                    if strict:
                        return {"success": False, "code": "zero_based_explicit_fields", "message": "Explicit line/col fields are 1-based; received zero-based.", "data": {"normalizedEdits": normalized_edits}}
                    # Normalize by clamping to 1 and warn
                    for k in ("startLine", "startCol", "endLine", "endCol"):
                        try:
                            if int(e2.get(k, 1)) < 1:
                                e2[k] = 1
                        except Exception:
                            pass
                    warnings.append(
                        "zero_based_explicit_fields_normalized")
                normalized_edits.append(e2)
                continue

            rng = e2.get("range")
            if isinstance(rng, dict):
                # LSP style: 0-based
                s = rng.get("start", {})
                t = rng.get("end", {})
                e2["startLine"] = int(s.get("line", 0)) + 1
                e2["startCol"] = int(s.get("character", 0)) + 1
                e2["endLine"] = int(t.get("line", 0)) + 1
                e2["endCol"] = int(t.get("character", 0)) + 1
                e2.pop("range", None)
                normalized_edits.append(e2)
                continue
            if isinstance(rng, (list, tuple)) and len(rng) == 2:
                try:
                    a = int(rng[0])
                    b = int(rng[1])
                    if b < a:
                        a, b = b, a
                    sl, sc = line_col_from_index(a)
                    el, ec = line_col_from_index(b)
                    e2["startLine"] = sl
                    e2["startCol"] = sc
                    e2["endLine"] = el
                    e2["endCol"] = ec
                    e2.pop("range", None)
                    normalized_edits.append(e2)
                    continue
                except Exception:
                    pass
            # Could not normalize this edit
            return {
                "success": False,
                "code": "missing_field",
                "message": "apply_text_edits requires startLine/startCol/endLine/endCol/newText or a normalizable 'range'",
                "data": {"expected": ["startLine", "startCol", "endLine", "endCol", "newText"], "got": e}
            }
    else:
        # Even when edits appear already in explicit form, validate 1-based coordinates.
        normalized_edits = []
        for e in edits or []:
            e2 = dict(e)
            has_all = all(k in e2 for k in (
                "startLine", "startCol", "endLine", "endCol"))
            if has_all:
                zero_based = False
                for k in ("startLine", "startCol", "endLine", "endCol"):
                    try:
                        if int(e2.get(k, 1)) < 1:
                            zero_based = True
                    except Exception:
                        pass
                if zero_based:
                    if strict:
                        return {"success": False, "code": "zero_based_explicit_fields", "message": "Explicit line/col fields are 1-based; received zero-based.", "data": {"normalizedEdits": [e2]}}
                    for k in ("startLine", "startCol", "endLine", "endCol"):
                        try:
                            if int(e2.get(k, 1)) < 1:
                                e2[k] = 1
                        except Exception:
                            pass
                    if "zero_based_explicit_fields_normalized" not in warnings:
                        warnings.append(
                            "zero_based_explicit_fields_normalized")
            normalized_edits.append(e2)

    # Preflight: detect overlapping ranges among normalized line/col spans
    def _pos_tuple(e: dict[str, Any], key_start: bool) -> tuple[int, int]:
        return (
            int(e.get("startLine", 1)) if key_start else int(
                e.get("endLine", 1)),
            int(e.get("startCol", 1)) if key_start else int(
                e.get("endCol", 1)),
        )

    def _le(a: tuple[int, int], b: tuple[int, int]) -> bool:
        return a[0] < b[0] or (a[0] == b[0] and a[1] <= b[1])

    # Consider only true replace ranges (non-zero length). Pure insertions (zero-width) don't overlap.
    spans = []
    for e in normalized_edits or []:
        try:
            s = _pos_tuple(e, True)
            t = _pos_tuple(e, False)
            if s != t:
                spans.append((s, t))
        except Exception:
            # If coordinates missing or invalid, let the server validate later
            pass

    if spans:
        spans_sorted = sorted(spans, key=lambda p: (p[0][0], p[0][1]))
        for i in range(1, len(spans_sorted)):
            prev_end = spans_sorted[i-1][1]
            curr_start = spans_sorted[i][0]
            # Overlap if prev_end > curr_start (strict), i.e., not prev_end <= curr_start
            if not _le(prev_end, curr_start):
                conflicts = [{
                    "startA": {"line": spans_sorted[i-1][0][0], "col": spans_sorted[i-1][0][1]},
                    "endA":   {"line": spans_sorted[i-1][1][0], "col": spans_sorted[i-1][1][1]},
                    "startB": {"line": spans_sorted[i][0][0],  "col": spans_sorted[i][0][1]},
                    "endB":   {"line": spans_sorted[i][1][0],  "col": spans_sorted[i][1][1]},
                }]
                return {"success": False, "code": "overlap", "data": {"status": "overlap", "conflicts": conflicts}}

    # Note: Do not auto-compute precondition if missing; callers should supply it
    # via mcp__unity__get_sha or a prior read. This avoids hidden extra calls and
    # preserves existing call-count expectations in clients/tests.

    # Default options: for multi-span batches, prefer atomic to avoid mid-apply imbalance
    opts: dict[str, Any] = dict(options or {})
    try:
        if len(normalized_edits) > 1 and "applyMode" not in opts:
            opts["applyMode"] = "atomic"
    except Exception:
        pass
    # Support optional debug preview for span-by-span simulation without write
    if opts.get("debug_preview"):
        try:
            import difflib
            # Apply locally to preview final result
            lines = []
            # Build an indexable original from a read if we normalized from read; otherwise skip
            prev = ""
            # We cannot guarantee file contents here without a read; return normalized spans only
            return {
                "success": True,
                "message": "Preview only (no write)",
                "data": {
                    "normalizedEdits": normalized_edits,
                    "preview": True
                }
            }
        except Exception as e:
            return {"success": False, "code": "preview_failed", "message": f"debug_preview failed: {e}", "data": {"normalizedEdits": normalized_edits}}

    params = {
        "action": "apply_text_edits",
        "name": name,
        "path": directory,
        "edits": normalized_edits,
        "precondition_sha256": precondition_sha256,
        "options": opts,
    }
    params = {k: v for k, v in params.items() if v is not None}
    resp = await send_with_unity_instance(
        transport.legacy.unity_connection.async_send_command_with_retry,
        unity_instance,
        "manage_script",
        params,
    )
    if isinstance(resp, dict):
        data = resp.setdefault("data", {})
        data.setdefault("normalizedEdits", normalized_edits)
        if warnings:
            data.setdefault("warnings", warnings)
        if resp.get("success") and (options or {}).get("force_sentinel_reload"):
            # Optional: flip sentinel via menu if explicitly requested
            try:
                import threading
                import time
                import json
                import glob
                import os

                def _latest_status() -> dict | None:
                    try:
                        files = sorted(glob.glob(os.path.expanduser(
                            "~/.unity-mcp/unity-mcp-status-*.json")), key=os.path.getmtime, reverse=True)
                        if not files:
                            return None
                        with open(files[0], "r") as f:
                            return json.loads(f.read())
                    except Exception:
                        return None

                async def _flip_async():
                    try:
                        time.sleep(0.1)
                        st = _latest_status()
                        if st and st.get("reloading"):
                            return
                        await transport.legacy.unity_connection.async_send_command_with_retry(
                            "execute_menu_item",
                            {"menuPath": "MCP/Flip Reload Sentinel"},
                            max_retries=0,
                            retry_ms=0,
                            instance_id=unity_instance,
                        )
                    except Exception:
                        pass
                threading.Thread(target=_flip_async, daemon=True).start()
            except Exception:
                pass
            return resp
        return resp
    return {"success": False, "message": str(resp)}


@mcp_for_unity_tool(description=("Create a new C# script at the given project path."))
async def create_script(
    ctx: Context,
    path: Annotated[str, "Path under Assets/ to create the script at, e.g., 'Assets/Scripts/My.cs'"],
    contents: Annotated[str, "Contents of the script to create (plain text C# code). The server handles Base64 encoding."],
    script_type: Annotated[str, "Script type (e.g., 'C#')"] | None = None,
    namespace: Annotated[str, "Namespace for the script"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)
    await ctx.info(
        f"Processing create_script: {path} (unity_instance={unity_instance or 'default'})")
    name = os.path.splitext(os.path.basename(path))[0]
    directory = os.path.dirname(path)
    # Local validation to avoid round-trips on obviously bad input
    norm_path = os.path.normpath(
        (path or "").replace("\\", "/")).replace("\\", "/")
    if not directory or directory.split("/")[0].lower() != "assets":
        return {"success": False, "code": "path_outside_assets", "message": f"path must be under 'Assets/'; got '{path}'."}
    if ".." in norm_path.split("/") or norm_path.startswith("/"):
        return {"success": False, "code": "bad_path", "message": "path must not contain traversal or be absolute."}
    if not name:
        return {"success": False, "code": "bad_path", "message": "path must include a script file name."}
    if not norm_path.lower().endswith(".cs"):
        return {"success": False, "code": "bad_extension", "message": "script file must end with .cs."}
    params: dict[str, Any] = {
        "action": "create",
        "name": name,
        "path": directory,
        "namespace": namespace,
        "scriptType": script_type,
    }
    if contents:
        params["encodedContents"] = base64.b64encode(
            contents.encode("utf-8")).decode("utf-8")
        params["contentsEncoded"] = True
    params = {k: v for k, v in params.items() if v is not None}
    resp = await send_with_unity_instance(
        transport.legacy.unity_connection.async_send_command_with_retry,
        unity_instance,
        "manage_script",
        params,
    )
    return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}


@mcp_for_unity_tool(description=("Delete a C# script by URI or Assets-relative path."))
async def delete_script(
    ctx: Context,
    uri: Annotated[str, "URI of the script to delete under Assets/ directory, unity://path/Assets/... or file://... or Assets/..."],
) -> dict[str, Any]:
    """Delete a C# script by URI."""
    unity_instance = get_unity_instance_from_context(ctx)
    await ctx.info(
        f"Processing delete_script: {uri} (unity_instance={unity_instance or 'default'})")
    name, directory = _split_uri(uri)
    if not directory or directory.split("/")[0].lower() != "assets":
        return {"success": False, "code": "path_outside_assets", "message": "URI must resolve under 'Assets/'."}
    params = {"action": "delete", "name": name, "path": directory}
    resp = await send_with_unity_instance(
        transport.legacy.unity_connection.async_send_command_with_retry,
        unity_instance,
        "manage_script",
        params,
    )
    return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}


@mcp_for_unity_tool(description=("Validate a C# script and return diagnostics."))
async def validate_script(
    ctx: Context,
    uri: Annotated[str, "URI of the script to validate under Assets/ directory, unity://path/Assets/... or file://... or Assets/..."],
    level: Annotated[Literal['basic', 'standard'],
                     "Validation level"] = "basic",
    include_diagnostics: Annotated[bool,
                                   "Include full diagnostics and summary"] = False,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)
    await ctx.info(
        f"Processing validate_script: {uri} (unity_instance={unity_instance or 'default'})")
    name, directory = _split_uri(uri)
    if not directory or directory.split("/")[0].lower() != "assets":
        return {"success": False, "code": "path_outside_assets", "message": "URI must resolve under 'Assets/'."}
    if level not in ("basic", "standard"):
        return {"success": False, "code": "bad_level", "message": "level must be 'basic' or 'standard'."}
    params = {
        "action": "validate",
        "name": name,
        "path": directory,
        "level": level,
    }
    resp = await send_with_unity_instance(
        transport.legacy.unity_connection.async_send_command_with_retry,
        unity_instance,
        "manage_script",
        params,
    )
    if isinstance(resp, dict) and resp.get("success"):
        diags = resp.get("data", {}).get("diagnostics", []) or []
        warnings = sum(1 for d in diags if str(
            d.get("severity", "")).lower() == "warning")
        errors = sum(1 for d in diags if str(
            d.get("severity", "")).lower() in ("error", "fatal"))
        if include_diagnostics:
            return {"success": True, "data": {"diagnostics": diags, "summary": {"warnings": warnings, "errors": errors}}}
        return {"success": True, "data": {"warnings": warnings, "errors": errors}}
    return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}


@mcp_for_unity_tool(description=("Compatibility router for legacy script operations. Prefer apply_text_edits (ranges) or script_apply_edits (structured) for edits."))
async def manage_script(
    ctx: Context,
    action: Annotated[Literal['create', 'read', 'delete'], "Perform CRUD operations on C# scripts."],
    name: Annotated[str, "Script name (no .cs extension)", "Name of the script to create"],
    path: Annotated[str, "Asset path (default: 'Assets/')", "Path under Assets/ to create the script at, e.g., 'Assets/Scripts/My.cs'"],
    contents: Annotated[str, "Contents of the script to create",
                        "C# code for 'create'/'update'"] | None = None,
    script_type: Annotated[str, "Script type (e.g., 'C#')",
                           "Type hint (e.g., 'MonoBehaviour')"] | None = None,
    namespace: Annotated[str, "Namespace for the script"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)
    await ctx.info(
        f"Processing manage_script: {action} (unity_instance={unity_instance or 'default'})")
    try:
        # Prepare parameters for Unity
        params = {
            "action": action,
            "name": name,
            "path": path,
            "namespace": namespace,
            "scriptType": script_type,
        }

        # Base64 encode the contents if they exist to avoid JSON escaping issues
        if contents:
            if action == 'create':
                params["encodedContents"] = base64.b64encode(
                    contents.encode('utf-8')).decode('utf-8')
                params["contentsEncoded"] = True
            else:
                params["contents"] = contents

        params = {k: v for k, v in params.items() if v is not None}

        response = await send_with_unity_instance(
            transport.legacy.unity_connection.async_send_command_with_retry,
            unity_instance,
            "manage_script",
            params,
        )

        if isinstance(response, dict):
            if response.get("success"):
                if response.get("data", {}).get("contentsEncoded"):
                    decoded_contents = base64.b64decode(
                        response["data"]["encodedContents"]).decode('utf-8')
                    response["data"]["contents"] = decoded_contents
                    del response["data"]["encodedContents"]
                    del response["data"]["contentsEncoded"]

                return {
                    "success": True,
                    "message": response.get("message", "Operation successful."),
                    "data": response.get("data"),
                }
            return response

        return {"success": False, "message": str(response)}

    except Exception as e:
        return {
            "success": False,
            "message": f"Python error managing script: {str(e)}",
        }


@mcp_for_unity_tool(description=(
    """Get manage_script capabilities (supported ops, limits, and guards).
    Returns:
        - ops: list of supported structured ops
        - text_ops: list of supported text ops
        - max_edit_payload_bytes: server edit payload cap
        - guards: header/using guard enabled flag"""
))
async def manage_script_capabilities(ctx: Context) -> dict[str, Any]:
    await ctx.info("Processing manage_script_capabilities")
    try:
        # Keep in sync with server/Editor ManageScript implementation
        ops = [
            "replace_class", "delete_class", "replace_method", "delete_method",
            "insert_method", "anchor_insert", "anchor_delete", "anchor_replace"
        ]
        text_ops = ["replace_range", "regex_replace", "prepend", "append"]
        # Match ManageScript.MaxEditPayloadBytes if exposed; hardcode a sensible default fallback
        max_edit_payload_bytes = 256 * 1024
        guards = {"using_guard": True}
        extras = {"get_sha": True}
        return {"success": True, "data": {
            "ops": ops,
            "text_ops": text_ops,
            "max_edit_payload_bytes": max_edit_payload_bytes,
            "guards": guards,
            "extras": extras,
        }}
    except Exception as e:
        return {"success": False, "error": f"capabilities error: {e}"}


@mcp_for_unity_tool(description="Get SHA256 and basic metadata for a Unity C# script without returning file contents")
async def get_sha(
    ctx: Context,
    uri: Annotated[str, "URI of the script to edit under Assets/ directory, unity://path/Assets/... or file://... or Assets/..."],
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)
    await ctx.info(
        f"Processing get_sha: {uri} (unity_instance={unity_instance or 'default'})")
    try:
        name, directory = _split_uri(uri)
        params = {"action": "get_sha", "name": name, "path": directory}
        resp = await send_with_unity_instance(
            transport.legacy.unity_connection.async_send_command_with_retry,
            unity_instance,
            "manage_script",
            params,
        )
        if isinstance(resp, dict) and resp.get("success"):
            data = resp.get("data", {})
            minimal = {"sha256": data.get(
                "sha256"), "lengthBytes": data.get("lengthBytes")}
            return {"success": True, "data": minimal}
        return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}
    except Exception as e:
        return {"success": False, "message": f"get_sha error: {e}"}
