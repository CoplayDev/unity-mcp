from mcp.server.fastmcp import FastMCP, Context
from typing import Dict, Any, List
from unity_connection import send_command_with_retry
import base64
import os
from urllib.parse import urlparse, unquote


def register_manage_script_tools(mcp: FastMCP):
    """Register all script management tools with the MCP server."""

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
            raw_path = uri[len("unity://path/") :]
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
        idx = next((i for i, seg in enumerate(parts) if seg.lower() == "assets"), None)
        assets_rel = "/".join(parts[idx:]) if idx is not None else None

        effective_path = assets_rel if assets_rel else norm
        # For POSIX absolute paths outside Assets, drop the leading '/'
        # to return a clean relative-like directory (e.g., '/tmp' -> 'tmp').
        if effective_path.startswith("/"):
            effective_path = effective_path[1:]

        name = os.path.splitext(os.path.basename(effective_path))[0]
        directory = os.path.dirname(effective_path)
        return name, directory

    @mcp.tool(description=(
        "Apply small text edits to a C# script identified by URI.\n\n"
        "⚠️ IMPORTANT: This tool replaces EXACT character positions. Always verify content at target lines/columns BEFORE editing!\n"
        "Common mistakes:\n"
        "- Assuming what's on a line without checking\n"
        "- Using wrong line numbers (they're 1-indexed)\n"
        "- Miscounting column positions (also 1-indexed, tabs count as 1)\n\n"
        "RECOMMENDED WORKFLOW:\n"
        "1) First call resources/read with start_line/line_count to verify exact content\n"
        "2) Count columns carefully (or use find_in_file to locate patterns)\n"
        "3) Apply your edit with precise coordinates\n"
        "4) Consider script_apply_edits with anchors for safer pattern-based replacements\n\n"
        "Args:\n"
        "- uri: unity://path/Assets/... or file://... or Assets/...\n"
        "- edits: list of {startLine,startCol,endLine,endCol,newText} (1-indexed!)\n"
        "- precondition_sha256: optional SHA of current file (prevents concurrent edit conflicts)\n\n"
        "Notes:\n"
        "- Path must resolve under Assets/\n"
        "- For method/class operations, use script_apply_edits (safer, structured edits)\n"
        "- For pattern-based replacements, consider anchor operations in script_apply_edits\n"
    ))
    def apply_text_edits(
        ctx: Context,
        uri: str,
        edits: List[Dict[str, Any]],
        precondition_sha256: str | None = None,
        strict: bool | None = None,
        options: Dict[str, Any] | None = None,
    ) -> Dict[str, Any]:
        """Apply small text edits to a C# script identified by URI."""
        name, directory = _split_uri(uri)

        # Normalize common aliases/misuses for resilience:
        # - Accept LSP-style range objects: {range:{start:{line,character}, end:{...}}, newText|text}
        # - Accept index ranges as a 2-int array: {range:[startIndex,endIndex], text}
        # If normalization is required, read current contents to map indices -> 1-based line/col.
        def _needs_normalization(arr: List[Dict[str, Any]]) -> bool:
            for e in arr or []:
                if ("startLine" not in e) or ("startCol" not in e) or ("endLine" not in e) or ("endCol" not in e) or ("newText" not in e and "text" in e):
                    return True
            return False

        normalized_edits: List[Dict[str, Any]] = []
        warnings: List[str] = []
        if _needs_normalization(edits):
            # Read file to support index->line/col conversion when needed
            read_resp = send_command_with_retry("manage_script", {
                "action": "read",
                "name": name,
                "path": directory,
            })
            if not (isinstance(read_resp, dict) and read_resp.get("success")):
                return read_resp if isinstance(read_resp, dict) else {"success": False, "message": str(read_resp)}
            data = read_resp.get("data", {})
            contents = data.get("contents")
            if not contents and data.get("contentsEncoded"):
                try:
                    contents = base64.b64decode(data.get("encodedContents", "").encode("utf-8")).decode("utf-8", "replace")
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
                    for k in ("startLine","startCol","endLine","endCol"):
                        try:
                            if int(e2.get(k, 1)) < 1:
                                zero_based = True
                        except Exception:
                            pass
                    if zero_based:
                        if strict:
                            return {"success": False, "code": "zero_based_explicit_fields", "message": "Explicit line/col fields are 1-based; received zero-based.", "data": {"normalizedEdits": normalized_edits}}
                        # Normalize by clamping to 1 and warn
                        for k in ("startLine","startCol","endLine","endCol"):
                            try:
                                if int(e2.get(k, 1)) < 1:
                                    e2[k] = 1
                            except Exception:
                                pass
                        warnings.append("zero_based_explicit_fields_normalized")
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
                    "data": {"expected": ["startLine","startCol","endLine","endCol","newText"], "got": e}
                }
        else:
            # Even when edits appear already in explicit form, validate 1-based coordinates.
            normalized_edits = []
            for e in edits or []:
                e2 = dict(e)
                has_all = all(k in e2 for k in ("startLine","startCol","endLine","endCol"))
                if has_all:
                    zero_based = False
                    for k in ("startLine","startCol","endLine","endCol"):
                        try:
                            if int(e2.get(k, 1)) < 1:
                                zero_based = True
                        except Exception:
                            pass
                    if zero_based:
                        if strict:
                            return {"success": False, "code": "zero_based_explicit_fields", "message": "Explicit line/col fields are 1-based; received zero-based.", "data": {"normalizedEdits": [e2]}}
                        for k in ("startLine","startCol","endLine","endCol"):
                            try:
                                if int(e2.get(k, 1)) < 1:
                                    e2[k] = 1
                            except Exception:
                                pass
                        if "zero_based_explicit_fields_normalized" not in warnings:
                            warnings.append("zero_based_explicit_fields_normalized")
                normalized_edits.append(e2)

        # Preflight: detect overlapping ranges among normalized line/col spans
        def _pos_tuple(e: Dict[str, Any], key_start: bool) -> tuple[int, int]:
            return (
                int(e.get("startLine", 1)) if key_start else int(e.get("endLine", 1)),
                int(e.get("startCol", 1)) if key_start else int(e.get("endCol", 1)),
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
        opts: Dict[str, Any] = dict(options or {})
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
        resp = send_command_with_retry("manage_script", params)
        if isinstance(resp, dict):
            data = resp.setdefault("data", {})
            data.setdefault("normalizedEdits", normalized_edits)
            if warnings:
                data.setdefault("warnings", warnings)
            if resp.get("success") and (options or {}).get("force_sentinel_reload"):
                # Optional: flip sentinel via menu if explicitly requested
                try:
                    import threading, time, json, glob, os
                    def _latest_status() -> dict | None:
                        try:
                            files = sorted(glob.glob(os.path.expanduser("~/.unity-mcp/unity-mcp-status-*.json")), key=os.path.getmtime, reverse=True)
                            if not files:
                                return None
                            with open(files[0], "r") as f:
                                return json.loads(f.read())
                        except Exception:
                            return None

                    def _flip_async():
                        try:
                            time.sleep(0.1)
                            st = _latest_status()
                            if st and st.get("reloading"):
                                return
                            send_command_with_retry(
                                "execute_menu_item",
                                {"menuPath": "MCP/Flip Reload Sentinel"},
                                max_retries=0,
                                retry_ms=0,
                            )
                        except Exception:
                            pass
                    threading.Thread(target=_flip_async, daemon=True).start()
                except Exception:
                    pass
                return resp
            return resp
        return {"success": False, "message": str(resp)}

    @mcp.tool(description=(
        "Create a new C# script at the given project path.\n\n"
        "Args: path (e.g., 'Assets/Scripts/My.cs'), contents (string), script_type, namespace.\n"
        "Rules: path must be under Assets/. Contents will be Base64-encoded over transport.\n"
    ))
    def create_script(
        ctx: Context,
        path: str,
        contents: str = "",
        script_type: str | None = None,
        namespace: str | None = None,
    ) -> Dict[str, Any]:
        """Create a new C# script at the given path."""
        name = os.path.splitext(os.path.basename(path))[0]
        directory = os.path.dirname(path)
        # Local validation to avoid round-trips on obviously bad input
        norm_path = os.path.normpath((path or "").replace("\\", "/")).replace("\\", "/")
        if not directory or directory.split("/")[0].lower() != "assets":
            return {"success": False, "code": "path_outside_assets", "message": f"path must be under 'Assets/'; got '{path}'."}
        if ".." in norm_path.split("/") or norm_path.startswith("/"):
            return {"success": False, "code": "bad_path", "message": "path must not contain traversal or be absolute."}
        if not name:
            return {"success": False, "code": "bad_path", "message": "path must include a script file name."}
        if not norm_path.lower().endswith(".cs"):
            return {"success": False, "code": "bad_extension", "message": "script file must end with .cs."}
        params: Dict[str, Any] = {
            "action": "create",
            "name": name,
            "path": directory,
            "namespace": namespace,
            "scriptType": script_type,
        }
        if contents:
            params["encodedContents"] = base64.b64encode(contents.encode("utf-8")).decode("utf-8")
            params["contentsEncoded"] = True
        params = {k: v for k, v in params.items() if v is not None}
        resp = send_command_with_retry("manage_script", params)
        return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}

    @mcp.tool(description=(
        "Delete a C# script by URI or Assets-relative path.\n\n"
        "Args: uri (unity://path/... or file://... or Assets/...).\n"
        "Rules: Target must resolve under Assets/.\n"
    ))
    def delete_script(ctx: Context, uri: str) -> Dict[str, Any]:
        """Delete a C# script by URI."""
        name, directory = _split_uri(uri)
        if not directory or directory.split("/")[0].lower() != "assets":
            return {"success": False, "code": "path_outside_assets", "message": "URI must resolve under 'Assets/'."}
        params = {"action": "delete", "name": name, "path": directory}
        resp = send_command_with_retry("manage_script", params)
        return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}

    @mcp.tool(description=(
        "Validate a C# script and return diagnostics.\n\n"
        "Args: uri, level=('basic'|'standard').\n"
        "- basic: quick syntax checks.\n"
        "- standard: deeper checks (performance hints, common pitfalls).\n"
    ))
    def validate_script(
        ctx: Context, uri: str, level: str = "basic"
    ) -> Dict[str, Any]:
        """Validate a C# script and return diagnostics."""
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
        resp = send_command_with_retry("manage_script", params)
        return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}

    @mcp.tool(description=(
        "Compatibility router for legacy script operations.\n\n"
        "Actions: create|read|delete (update is routed to apply_text_edits with precondition).\n"
        "Args: name (no .cs), path (Assets/...), contents (for create), script_type, namespace.\n"
        "Notes: prefer apply_text_edits (ranges) or script_apply_edits (structured) for edits.\n"
    ))
    def manage_script(
        ctx: Context,
        action: str,
        name: str,
        path: str,
        contents: str = "",
        script_type: str | None = None,
        namespace: str | None = None,
    ) -> Dict[str, Any]:
        """Compatibility router for legacy script operations.

        IMPORTANT:
        - Direct file reads should use resources/read.
        - Edits should use apply_text_edits.

        Args:
            action: Operation ('create', 'read', 'delete').
            name: Script name (no .cs extension).
            path: Asset path (default: "Assets/").
            contents: C# code for 'create'/'update'.
            script_type: Type hint (e.g., 'MonoBehaviour').
            namespace: Script namespace.

        Returns:
            Dictionary with results ('success', 'message', 'data').
        """
        try:
            # Graceful migration for legacy 'update': route to apply_text_edits (whole-file replace)
            if action == 'update':
                try:
                    # 1) Read current contents to compute end range and precondition
                    read_resp = send_command_with_retry("manage_script", {
                        "action": "read",
                        "name": name,
                        "path": path,
                    })
                    if not (isinstance(read_resp, dict) and read_resp.get("success")):
                        return {"success": False, "code": "deprecated_update", "message": "Use apply_text_edits; automatic migration failed to read current file."}
                    data = read_resp.get("data", {})
                    current = data.get("contents")
                    if not current and data.get("contentsEncoded"):
                        current = base64.b64decode(data.get("encodedContents", "").encode("utf-8")).decode("utf-8", "replace")
                    if current is None:
                        return {"success": False, "code": "deprecated_update", "message": "Use apply_text_edits; current file read returned no contents."}

                    # 2) Compute whole-file range (1-based, end exclusive) and SHA
                    import hashlib as _hashlib
                    old_lines = current.splitlines(keepends=True)
                    end_line = len(old_lines) + 1
                    sha = _hashlib.sha256(current.encode("utf-8")).hexdigest()

                    # 3) Apply single whole-file text edit with provided 'contents'
                    edits = [{
                        "startLine": 1,
                        "startCol": 1,
                        "endLine": end_line,
                        "endCol": 1,
                        "newText": contents or "",
                    }]
                    route_params = {
                        "action": "apply_text_edits",
                        "name": name,
                        "path": path,
                        "edits": edits,
                        "precondition_sha256": sha,
                        "options": {"refresh": "debounced", "validate": "standard"},
                    }
                    # Preflight size vs. default cap (256 KiB) to avoid opaque server errors
                    try:
                        import json as _json
                        payload_bytes = len(_json.dumps({"edits": edits}, ensure_ascii=False).encode("utf-8"))
                        if payload_bytes > 256 * 1024:
                            return {"success": False, "code": "payload_too_large", "message": f"Edit payload {payload_bytes} bytes exceeds 256 KiB cap; try structured ops or chunking."}
                    except Exception:
                        pass
                    routed = send_command_with_retry("manage_script", route_params)
                    if isinstance(routed, dict):
                        routed.setdefault("message", "Routed legacy update to apply_text_edits")
                        return routed
                    return {"success": False, "message": str(routed)}
                except Exception as e:
                    return {"success": False, "code": "deprecated_update", "message": f"Use apply_text_edits; migration error: {e}"}

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
                    params["encodedContents"] = base64.b64encode(contents.encode('utf-8')).decode('utf-8')
                    params["contentsEncoded"] = True
                else:
                    params["contents"] = contents

            params = {k: v for k, v in params.items() if v is not None}

            response = send_command_with_retry("manage_script", params)

            if isinstance(response, dict):
                if response.get("success"):
                    if response.get("data", {}).get("contentsEncoded"):
                        decoded_contents = base64.b64decode(response["data"]["encodedContents"]).decode('utf-8')
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

    @mcp.tool(description=(
        "Get manage_script capabilities (supported ops, limits, and guards).\n\n"
        "Returns:\n- ops: list of supported structured ops\n- text_ops: list of supported text ops\n- max_edit_payload_bytes: server edit payload cap\n- guards: header/using guard enabled flag\n"
    ))
    def manage_script_capabilities(ctx: Context) -> Dict[str, Any]:
        try:
            # Keep in sync with server/Editor ManageScript implementation
            ops = [
                "replace_class","delete_class","replace_method","delete_method",
                "insert_method","anchor_insert","anchor_delete","anchor_replace"
            ]
            text_ops = ["replace_range","regex_replace","prepend","append"]
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

    @mcp.tool(description=(
        "Get SHA256 and metadata for a Unity C# script without returning file contents.\n\n"
        "Args: uri (unity://path/Assets/... or file://... or Assets/...).\n"
        "Returns: {sha256, lengthBytes, lastModifiedUtc, uri, path}."
    ))
    def get_sha(ctx: Context, uri: str) -> Dict[str, Any]:
        """Return SHA256 and basic metadata for a script."""
        try:
            name, directory = _split_uri(uri)
            params = {"action": "get_sha", "name": name, "path": directory}
            resp = send_command_with_retry("manage_script", params)
            return resp if isinstance(resp, dict) else {"success": False, "message": str(resp)}
        except Exception as e:
            return {"success": False, "message": f"get_sha error: {e}"}
