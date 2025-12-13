import base64
import hashlib
import re
from typing import Annotated, Any, Union

from fastmcp import Context

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import parse_json_payload
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


async def _apply_edits_locally(original_text: str, edits: list[dict[str, Any]]) -> str:
    text = original_text
    for edit in edits or []:
        op = (
            (edit.get("op")
             or edit.get("operation")
             or edit.get("type")
             or edit.get("mode")
             or "")
            .strip()
            .lower()
        )

        if not op:
            allowed = "anchor_insert, prepend, append, replace_range, regex_replace"
            raise RuntimeError(
                f"op is required; allowed: {allowed}. Use 'op' (aliases accepted: type/mode/operation)."
            )

        if op == "prepend":
            prepend_text = edit.get("text", "")
            text = (prepend_text if prepend_text.endswith(
                "\n") else prepend_text + "\n") + text
        elif op == "append":
            append_text = edit.get("text", "")
            if not text.endswith("\n"):
                text += "\n"
            text += append_text
            if not text.endswith("\n"):
                text += "\n"
        elif op == "anchor_insert":
            anchor = edit.get("anchor", "")
            position = (edit.get("position") or "before").lower()
            insert_text = edit.get("text", "")
            flags = re.MULTILINE | (
                re.IGNORECASE if edit.get("ignore_case") else 0)

            # Find the best match using improved heuristics
            match = _find_best_anchor_match(
                anchor, text, flags, bool(edit.get("prefer_last", True)))
            if not match:
                if edit.get("allow_noop", True):
                    continue
                raise RuntimeError(f"anchor not found: {anchor}")
            idx = match.start() if position == "before" else match.end()
            text = text[:idx] + insert_text + text[idx:]
        elif op == "replace_range":
            start_line = int(edit.get("startLine", 1))
            start_col = int(edit.get("startCol", 1))
            end_line = int(edit.get("endLine", start_line))
            end_col = int(edit.get("endCol", 1))
            replacement = edit.get("text", "")
            lines = text.splitlines(keepends=True)
            max_line = len(lines) + 1  # 1-based, exclusive end
            if (start_line < 1 or end_line < start_line or end_line > max_line
                    or start_col < 1 or end_col < 1):
                raise RuntimeError("replace_range out of bounds")

            def index_of(line: int, col: int) -> int:
                if line <= len(lines):
                    return sum(len(l) for l in lines[: line - 1]) + (col - 1)
                return sum(len(l) for l in lines)
            a = index_of(start_line, start_col)
            b = index_of(end_line, end_col)
            text = text[:a] + replacement + text[b:]
        elif op == "regex_replace":
            pattern = edit.get("pattern", "")
            repl = edit.get("replacement", "")
            # Translate $n backrefs (our input) to Python \g<n>
            repl_py = re.sub(r"\$(\d+)", r"\\g<\1>", repl)
            count = int(edit.get("count", 0))  # 0 = replace all
            flags = re.MULTILINE
            if edit.get("ignore_case"):
                flags |= re.IGNORECASE
            text = re.sub(pattern, repl_py, text, count=count, flags=flags)
        else:
            allowed = "anchor_insert, prepend, append, replace_range, regex_replace"
            raise RuntimeError(
                f"unknown edit op: {op}; allowed: {allowed}. Use 'op' (aliases accepted: type/mode/operation).")
    return text


def _find_best_anchor_match(pattern: str, text: str, flags: int, prefer_last: bool = True):
    """
    Find the best anchor match using improved heuristics.

    For patterns like \\s*}\\s*$ that are meant to find class-ending braces,
    this function uses heuristics to choose the most semantically appropriate match:

    1. If prefer_last=True, prefer the last match (common for class-end insertions)
    2. Use indentation levels to distinguish class vs method braces
    3. Consider context to avoid matches inside strings/comments

    Args:
        pattern: Regex pattern to search for
        text: Text to search in  
        flags: Regex flags
        prefer_last: If True, prefer the last match over the first

    Returns:
        Match object of the best match, or None if no match found
    """

    # Find all matches
    matches = list(re.finditer(pattern, text, flags))
    if not matches:
        return None

    # If only one match, return it
    if len(matches) == 1:
        return matches[0]

    # For patterns that look like they're trying to match closing braces at end of lines
    is_closing_brace_pattern = '}' in pattern and (
        '$' in pattern or pattern.endswith(r'\s*'))

    if is_closing_brace_pattern and prefer_last:
        # Use heuristics to find the best closing brace match
        return _find_best_closing_brace_match(matches, text)

    # Default behavior: use last match if prefer_last, otherwise first match
    return matches[-1] if prefer_last else matches[0]


def _find_best_closing_brace_match(matches, text: str):
    """
    Find the best closing brace match using C# structure heuristics.

    Enhanced heuristics for scope-aware matching:
    1. Prefer matches with lower indentation (likely class-level)
    2. Prefer matches closer to end of file  
    3. Avoid matches that seem to be inside method bodies
    4. For #endregion patterns, ensure class-level context
    5. Validate insertion point is at appropriate scope

    Args:
        matches: List of regex match objects
        text: The full text being searched

    Returns:
        The best match object
    """
    if not matches:
        return None

    scored_matches = []
    lines = text.splitlines()

    for match in matches:
        score = 0
        start_pos = match.start()

        # Find which line this match is on
        lines_before = text[:start_pos].count('\n')
        line_num = lines_before

        if line_num < len(lines):
            line_content = lines[line_num]

            # Calculate indentation level (lower is better for class braces)
            indentation = len(line_content) - len(line_content.lstrip())

            # Prefer lower indentation (class braces are typically less indented than method braces)
            # Max 20 points for indentation=0
            score += max(0, 20 - indentation)

            # Prefer matches closer to end of file (class closing braces are typically at the end)
            distance_from_end = len(lines) - line_num
            # More points for being closer to end
            score += max(0, 10 - distance_from_end)

            # Look at surrounding context to avoid method braces
            context_start = max(0, line_num - 3)
            context_end = min(len(lines), line_num + 2)
            context_lines = lines[context_start:context_end]

            # Penalize if this looks like it's inside a method (has method-like patterns above)
            for context_line in context_lines:
                if re.search(r'\b(void|public|private|protected)\s+\w+\s*\(', context_line):
                    score -= 5  # Penalty for being near method signatures

            # Bonus if this looks like a class-ending brace (very minimal indentation and near EOF)
            if indentation <= 4 and distance_from_end <= 3:
                score += 15  # Bonus for likely class-ending brace

        scored_matches.append((score, match))

    # Return the match with the highest score
    scored_matches.sort(key=lambda x: x[0], reverse=True)
    best_match = scored_matches[0][1]

    return best_match


def _infer_class_name(script_name: str) -> str:
    # Default to script name as class name (common Unity pattern)
    return (script_name or "").strip()


def _extract_code_after(keyword: str, request: str) -> str:
    # Deprecated with NL removal; retained as no-op for compatibility
    idx = request.lower().find(keyword)
    if idx >= 0:
        return request[idx + len(keyword):].strip()
    return ""
# Removed _is_structurally_balanced - validation now handled by C# side using Unity's compiler services


def _normalize_script_locator(name: str, path: str) -> tuple[str, str]:
    """Best-effort normalization of script "name" and "path".

    Accepts any of:
    - name = "SmartReach", path = "Assets/Scripts/Interaction"
    - name = "SmartReach.cs", path = "Assets/Scripts/Interaction"
    - name = "Assets/Scripts/Interaction/SmartReach.cs", path = ""
    - path = "Assets/Scripts/Interaction/SmartReach.cs" (name empty)
    - name or path using uri prefixes: unity://path/..., file://...
    - accidental duplicates like "Assets/.../SmartReach.cs/SmartReach.cs"

    Returns (name_without_extension, directory_path_under_Assets).
    """
    n = (name or "").strip()
    p = (path or "").strip()

    def strip_prefix(s: str) -> str:
        if s.startswith("unity://path/"):
            return s[len("unity://path/"):]
        if s.startswith("file://"):
            return s[len("file://"):]
        return s

    def collapse_duplicate_tail(s: str) -> str:
        # Collapse trailing "/X.cs/X.cs" to "/X.cs"
        parts = s.split("/")
        if len(parts) >= 2 and parts[-1] == parts[-2]:
            parts = parts[:-1]
        return "/".join(parts)

    # Prefer a full path if provided in either field
    candidate = ""
    for v in (n, p):
        v2 = strip_prefix(v)
        if v2.endswith(".cs") or v2.startswith("Assets/"):
            candidate = v2
            break

    if candidate:
        candidate = collapse_duplicate_tail(candidate)
        # If a directory was passed in path and file in name, join them
        if not candidate.endswith(".cs") and n.endswith(".cs"):
            v2 = strip_prefix(n)
            candidate = (candidate.rstrip("/") + "/" + v2.split("/")[-1])
        if candidate.endswith(".cs"):
            parts = candidate.split("/")
            file_name = parts[-1]
            dir_path = "/".join(parts[:-1]) if len(parts) > 1 else "Assets"
            base = file_name[:-
                             3] if file_name.lower().endswith(".cs") else file_name
            return base, dir_path

    # Fall back: remove extension from name if present and return given path
    base_name = n[:-3] if n.lower().endswith(".cs") else n
    return base_name, (p or "Assets")


def _with_norm(resp: dict[str, Any] | Any, edits: list[dict[str, Any]], routing: str | None = None) -> dict[str, Any] | Any:
    if not isinstance(resp, dict):
        return resp
    data = resp.setdefault("data", {})
    data.setdefault("normalizedEdits", edits)
    if routing:
        data["routing"] = routing
    return resp


def _err(code: str, message: str, *, expected: dict[str, Any] | None = None, rewrite: dict[str, Any] | None = None,
         normalized: list[dict[str, Any]] | None = None, routing: str | None = None, extra: dict[str, Any] | None = None) -> dict[str, Any]:
    payload: dict[str, Any] = {"success": False,
                               "code": code, "message": message}
    data: dict[str, Any] = {}
    if expected:
        data["expected"] = expected
    if rewrite:
        data["rewrite_suggestion"] = rewrite
    if normalized is not None:
        data["normalizedEdits"] = normalized
    if routing:
        data["routing"] = routing
    if extra:
        data.update(extra)
    if data:
        payload["data"] = data
    return payload

# Natural-language parsing removed; clients should send structured edits.


@mcp_for_unity_tool(name="script_apply_edits", description=(
    """Structured C# edits (methods/classes) with safer boundaries - prefer this over raw text.
    Best practices:
    - Prefer anchor_* ops for pattern-based insert/replace near stable markers
    - Use replace_method/delete_method for whole-method changes (keeps signatures balanced)
    - Avoid whole-file regex deletes; validators will guard unbalanced braces
    - For tail insertions, prefer anchor/regex_replace on final brace (class closing)
    - Pass options.validate='standard' for structural checks; 'relaxed' for interior-only edits
    Canonical fields (use these exact keys):
    - op: replace_method | insert_method | delete_method | anchor_insert | anchor_delete | anchor_replace
    - className: string (defaults to 'name' if omitted on method/class ops)
    - methodName: string (required for replace_method, delete_method)
    - replacement: string (required for replace_method, insert_method)
    - position: start | end | after | before (insert_method only)
    - afterMethodName / beforeMethodName: string (required when position='after'/'before')
    - anchor: regex string (for anchor_* ops)
    - text: string (for anchor_insert/anchor_replace)
    Examples:
    1) Replace a method:
    {
        "name": "SmartReach",
        "path": "Assets/Scripts/Interaction",
        "edits": [
        {
        "op": "replace_method",
        "className": "SmartReach",
        "methodName": "HasTarget",
        "replacement": "public bool HasTarget(){ return currentTarget!=null; }"
        }
    ],
    "options": {"validate": "standard", "refresh": "immediate"}
    }
    "2) Insert a method after another:
    {
        "name": "SmartReach",
        "path": "Assets/Scripts/Interaction",
        "edits": [
        {
        "op": "insert_method",
        "className": "SmartReach",
        "replacement": "public void PrintSeries(){ Debug.Log(seriesName); }",
        "position": "after",
        "afterMethodName": "GetCurrentTarget"
        }
    ],
    }
    ]"""
))
async def script_apply_edits(
    ctx: Context,
    name: Annotated[str, "Name of the script to edit"],
    path: Annotated[str, "Path to the script to edit under Assets/ directory"],
    edits: Annotated[Union[list[dict[str, Any]], str], "List of edits to apply to the script (JSON list or stringified JSON)"],
    options: Annotated[dict[str, Any],
                       "Options for the script edit"] | None = None,
    script_type: Annotated[str,
                           "Type of the script to edit"] = "MonoBehaviour",
    namespace: Annotated[str,
                         "Namespace of the script to edit"] | None = None,
) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)
    await ctx.info(
        f"Processing script_apply_edits: {name} (unity_instance={unity_instance or 'default'})")
        
    # Parse edits if they came as a stringified JSON
    edits = parse_json_payload(edits)
    if not isinstance(edits, list):
        return {"success": False, "message": f"Edits must be a list or JSON string of a list, got {type(edits)}"}

    # Normalize locator first so downstream calls target the correct script file.
    name, path = _normalize_script_locator(name, path)
    # Normalize unsupported or aliased ops to known structured/text paths

    def _unwrap_and_alias(edit: dict[str, Any]) -> dict[str, Any]:
        # Unwrap single-key wrappers like {"replace_method": {...}}
        for wrapper_key in (
            "replace_method", "insert_method", "delete_method",
            "replace_class", "delete_class",
            "anchor_insert", "anchor_replace", "anchor_delete",
        ):
            if wrapper_key in edit and isinstance(edit[wrapper_key], dict):
                inner = dict(edit[wrapper_key])
                inner["op"] = wrapper_key
                edit = inner
                break

        e = dict(edit)
        op = (e.get("op") or e.get("operation") or e.get(
            "type") or e.get("mode") or "").strip().lower()
        if op:
            e["op"] = op

        # Common field aliases
        if "class_name" in e and "className" not in e:
            e["className"] = e.pop("class_name")
        if "class" in e and "className" not in e:
            e["className"] = e.pop("class")
        if "method_name" in e and "methodName" not in e:
            e["methodName"] = e.pop("method_name")
        # Some clients use a generic 'target' for method name
        if "target" in e and "methodName" not in e:
            e["methodName"] = e.pop("target")
        if "method" in e and "methodName" not in e:
            e["methodName"] = e.pop("method")
        if "new_content" in e and "replacement" not in e:
            e["replacement"] = e.pop("new_content")
        if "newMethod" in e and "replacement" not in e:
            e["replacement"] = e.pop("newMethod")
        if "new_method" in e and "replacement" not in e:
            e["replacement"] = e.pop("new_method")
        if "content" in e and "replacement" not in e:
            e["replacement"] = e.pop("content")
        if "after" in e and "afterMethodName" not in e:
            e["afterMethodName"] = e.pop("after")
        if "after_method" in e and "afterMethodName" not in e:
            e["afterMethodName"] = e.pop("after_method")
        if "before" in e and "beforeMethodName" not in e:
            e["beforeMethodName"] = e.pop("before")
        if "before_method" in e and "beforeMethodName" not in e:
            e["beforeMethodName"] = e.pop("before_method")
        # anchor_method → before/after based on position (default after)
        if "anchor_method" in e:
            anchor = e.pop("anchor_method")
            pos = (e.get("position") or "after").strip().lower()
            if pos == "before" and "beforeMethodName" not in e:
                e["beforeMethodName"] = anchor
            elif "afterMethodName" not in e:
                e["afterMethodName"] = anchor
        if "anchorText" in e and "anchor" not in e:
            e["anchor"] = e.pop("anchorText")
        if "pattern" in e and "anchor" not in e and e.get("op") and e["op"].startswith("anchor_"):
            e["anchor"] = e.pop("pattern")
        if "newText" in e and "text" not in e:
            e["text"] = e.pop("newText")

        # CI compatibility (T‑A/T‑E):
        # Accept method-anchored anchor_insert and upgrade to insert_method
        # Example incoming shape:
        #   {"op":"anchor_insert","afterMethodName":"GetCurrentTarget","text":"..."}
        if (
            e.get("op") == "anchor_insert"
            and not e.get("anchor")
            and (e.get("afterMethodName") or e.get("beforeMethodName"))
        ):
            e["op"] = "insert_method"
            if "replacement" not in e:
                e["replacement"] = e.get("text", "")

        # LSP-like range edit -> replace_range
        if "range" in e and isinstance(e["range"], dict):
            rng = e.pop("range")
            start = rng.get("start", {})
            end = rng.get("end", {})
            # Convert 0-based to 1-based line/col
            e["op"] = "replace_range"
            e["startLine"] = int(start.get("line", 0)) + 1
            e["startCol"] = int(start.get("character", 0)) + 1
            e["endLine"] = int(end.get("line", 0)) + 1
            e["endCol"] = int(end.get("character", 0)) + 1
            if "newText" in edit and "text" not in e:
                e["text"] = edit.get("newText", "")
        return e

    normalized_edits: list[dict[str, Any]] = []
    for raw in edits or []:
        e = _unwrap_and_alias(raw)
        op = (e.get("op") or e.get("operation") or e.get(
            "type") or e.get("mode") or "").strip().lower()

        # Default className to script name if missing on structured method/class ops
        if op in ("replace_class", "delete_class", "replace_method", "delete_method", "insert_method") and not e.get("className"):
            e["className"] = name

        # Map common aliases for text ops
        if op in ("text_replace",):
            e["op"] = "replace_range"
            normalized_edits.append(e)
            continue
        if op in ("regex_delete",):
            e["op"] = "regex_replace"
            e.setdefault("text", "")
            normalized_edits.append(e)
            continue
        if op == "regex_replace" and ("replacement" not in e):
            if "text" in e:
                e["replacement"] = e.get("text", "")
            elif "insert" in e or "content" in e:
                e["replacement"] = e.get(
                    "insert") or e.get("content") or ""
        if op == "anchor_insert" and not (e.get("text") or e.get("insert") or e.get("content") or e.get("replacement")):
            e["op"] = "anchor_delete"
            normalized_edits.append(e)
            continue
        normalized_edits.append(e)

    edits = normalized_edits
    normalized_for_echo = edits

    # Validate required fields and produce machine-parsable hints
    def error_with_hint(message: str, expected: dict[str, Any], suggestion: dict[str, Any]) -> dict[str, Any]:
        return _err("missing_field", message, expected=expected, rewrite=suggestion, normalized=normalized_for_echo)

    for e in edits or []:
        op = e.get("op", "")
        if op == "replace_method":
            if not e.get("methodName"):
                return error_with_hint(
                    "replace_method requires 'methodName'.",
                    {"op": "replace_method", "required": [
                        "className", "methodName", "replacement"]},
                    {"edits[0].methodName": "HasTarget"}
                )
            if not (e.get("replacement") or e.get("text")):
                return error_with_hint(
                    "replace_method requires 'replacement' (inline or base64).",
                    {"op": "replace_method", "required": [
                        "className", "methodName", "replacement"]},
                    {"edits[0].replacement": "public bool X(){ return true; }"}
                )
        elif op == "insert_method":
            if not (e.get("replacement") or e.get("text")):
                return error_with_hint(
                    "insert_method requires a non-empty 'replacement'.",
                    {"op": "insert_method", "required": ["className", "replacement"], "position": {
                        "after_requires": "afterMethodName", "before_requires": "beforeMethodName"}},
                    {"edits[0].replacement": "public void PrintSeries(){ Debug.Log(\"1,2,3\"); }"}
                )
            pos = (e.get("position") or "").lower()
            if pos == "after" and not e.get("afterMethodName"):
                return error_with_hint(
                    "insert_method with position='after' requires 'afterMethodName'.",
                    {"op": "insert_method", "position": {
                        "after_requires": "afterMethodName"}},
                    {"edits[0].afterMethodName": "GetCurrentTarget"}
                )
            if pos == "before" and not e.get("beforeMethodName"):
                return error_with_hint(
                    "insert_method with position='before' requires 'beforeMethodName'.",
                    {"op": "insert_method", "position": {
                        "before_requires": "beforeMethodName"}},
                    {"edits[0].beforeMethodName": "GetCurrentTarget"}
                )
        elif op == "delete_method":
            if not e.get("methodName"):
                return error_with_hint(
                    "delete_method requires 'methodName'.",
                    {"op": "delete_method", "required": [
                        "className", "methodName"]},
                    {"edits[0].methodName": "PrintSeries"}
                )
        elif op in ("anchor_insert", "anchor_replace", "anchor_delete"):
            if not e.get("anchor"):
                return error_with_hint(
                    f"{op} requires 'anchor' (regex).",
                    {"op": op, "required": ["anchor"]},
                    {"edits[0].anchor": "(?m)^\\s*public\\s+bool\\s+HasTarget\\s*\\("}
                )
            if op in ("anchor_insert", "anchor_replace") and not (e.get("text") or e.get("replacement")):
                return error_with_hint(
                    f"{op} requires 'text'.",
                    {"op": op, "required": ["anchor", "text"]},
                    {"edits[0].text": "/* comment */\n"}
                )

    # Decide routing: structured vs text vs mixed
    STRUCT = {"replace_class", "delete_class", "replace_method", "delete_method",
              "insert_method", "anchor_delete", "anchor_replace", "anchor_insert"}
    TEXT = {"prepend", "append", "replace_range", "regex_replace"}
    ops_set = {(e.get("op") or "").lower() for e in edits or []}
    all_struct = ops_set.issubset(STRUCT)
    all_text = ops_set.issubset(TEXT)
    mixed = not (all_struct or all_text)

    # If everything is structured (method/class/anchor ops), forward directly to Unity's structured editor.
    if all_struct:
        opts2 = dict(options or {})
        # For structured edits, prefer immediate refresh to avoid missed reloads when Editor is unfocused
        opts2.setdefault("refresh", "immediate")
        params_struct: dict[str, Any] = {
            "action": "edit",
            "name": name,
            "path": path,
            "namespace": namespace,
            "scriptType": script_type,
            "edits": edits,
            "options": opts2,
        }
        resp_struct = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "manage_script",
            params_struct,
        )
        if isinstance(resp_struct, dict) and resp_struct.get("success"):
            pass  # Optional sentinel reload removed (deprecated)
        return _with_norm(resp_struct if isinstance(resp_struct, dict) else {"success": False, "message": str(resp_struct)}, normalized_for_echo, routing="structured")

    # 1) read from Unity
    read_resp = await async_send_command_with_retry("manage_script", {
        "action": "read",
        "name": name,
        "path": path,
        "namespace": namespace,
        "scriptType": script_type,
    }, instance_id=unity_instance)
    if not isinstance(read_resp, dict) or not read_resp.get("success"):
        return read_resp if isinstance(read_resp, dict) else {"success": False, "message": str(read_resp)}

    data = read_resp.get("data") or read_resp.get(
        "result", {}).get("data") or {}
    contents = data.get("contents")
    if contents is None and data.get("contentsEncoded") and data.get("encodedContents"):
        contents = base64.b64decode(
            data["encodedContents"]).decode("utf-8")
    if contents is None:
        return {"success": False, "message": "No contents returned from Unity read."}

    # Optional preview/dry-run: apply locally and return diff without writing
    preview = bool((options or {}).get("preview"))

    # If we have a mixed batch (TEXT + STRUCT), apply text first with precondition, then structured
    if mixed:
        text_edits = [e for e in edits or [] if (
            e.get("op") or "").lower() in TEXT]
        struct_edits = [e for e in edits or [] if (
            e.get("op") or "").lower() in STRUCT]
        try:
            base_text = contents

            def line_col_from_index(idx: int) -> tuple[int, int]:
                line = base_text.count("\n", 0, idx) + 1
                last_nl = base_text.rfind("\n", 0, idx)
                col = (idx - (last_nl + 1)) + \
                    1 if last_nl >= 0 else idx + 1
                return line, col

            at_edits: list[dict[str, Any]] = []
            for e in text_edits:
                opx = (e.get("op") or e.get("operation") or e.get(
                    "type") or e.get("mode") or "").strip().lower()
                text_field = e.get("text") or e.get("insert") or e.get(
                    "content") or e.get("replacement") or ""
                if opx == "anchor_insert":
                    anchor = e.get("anchor") or ""
                    position = (e.get("position") or "after").lower()
                    flags = re.MULTILINE | (
                        re.IGNORECASE if e.get("ignore_case") else 0)
                    try:
                        # Use improved anchor matching logic
                        m = _find_best_anchor_match(
                            anchor, base_text, flags, prefer_last=True)
                    except Exception as ex:
                        return _with_norm(_err("bad_regex", f"Invalid anchor regex: {ex}", normalized=normalized_for_echo, routing="mixed/text-first", extra={"hint": "Escape parentheses/braces or use a simpler anchor."}), normalized_for_echo, routing="mixed/text-first")
                    if not m:
                        return _with_norm({"success": False, "code": "anchor_not_found", "message": f"anchor not found: {anchor}"}, normalized_for_echo, routing="mixed/text-first")
                    idx = m.start() if position == "before" else m.end()
                    # Normalize insertion to avoid jammed methods
                    text_field_norm = text_field
                    if not text_field_norm.startswith("\n"):
                        text_field_norm = "\n" + text_field_norm
                    if not text_field_norm.endswith("\n"):
                        text_field_norm = text_field_norm + "\n"
                    sl, sc = line_col_from_index(idx)
                    at_edits.append(
                        {"startLine": sl, "startCol": sc, "endLine": sl, "endCol": sc, "newText": text_field_norm})
                    # do not mutate base_text when building atomic spans
                elif opx == "replace_range":
                    if all(k in e for k in ("startLine", "startCol", "endLine", "endCol")):
                        at_edits.append({
                            "startLine": int(e.get("startLine", 1)),
                            "startCol": int(e.get("startCol", 1)),
                            "endLine": int(e.get("endLine", 1)),
                            "endCol": int(e.get("endCol", 1)),
                            "newText": text_field
                        })
                    else:
                        return _with_norm(_err("missing_field", "replace_range requires startLine/startCol/endLine/endCol", normalized=normalized_for_echo, routing="mixed/text-first"), normalized_for_echo, routing="mixed/text-first")
                elif opx == "regex_replace":
                    pattern = e.get("pattern") or ""
                    try:
                        regex_obj = re.compile(pattern, re.MULTILINE | (
                            re.IGNORECASE if e.get("ignore_case") else 0))
                    except Exception as ex:
                        return _with_norm(_err("bad_regex", f"Invalid regex pattern: {ex}", normalized=normalized_for_echo, routing="mixed/text-first", extra={"hint": "Escape special chars or prefer structured delete for methods."}), normalized_for_echo, routing="mixed/text-first")
                    m = regex_obj.search(base_text)
                    if not m:
                        continue
                    # Expand $1, $2... in replacement using this match

                    def _expand_dollars(rep: str, _m=m) -> str:
                        return re.sub(r"\$(\d+)", lambda g: _m.group(int(g.group(1))) or "", rep)
                    repl = _expand_dollars(text_field)
                    sl, sc = line_col_from_index(m.start())
                    el, ec = line_col_from_index(m.end())
                    at_edits.append(
                        {"startLine": sl, "startCol": sc, "endLine": el, "endCol": ec, "newText": repl})
                    # do not mutate base_text when building atomic spans
                elif opx in ("prepend", "append"):
                    if opx == "prepend":
                        sl, sc = 1, 1
                        at_edits.append(
                            {"startLine": sl, "startCol": sc, "endLine": sl, "endCol": sc, "newText": text_field})
                        # prepend can be applied atomically without local mutation
                    else:
                        # Insert at true EOF position (handles both \n and \r\n correctly)
                        eof_idx = len(base_text)
                        sl, sc = line_col_from_index(eof_idx)
                        new_text = ("\n" if not base_text.endswith(
                            "\n") else "") + text_field
                        at_edits.append(
                            {"startLine": sl, "startCol": sc, "endLine": sl, "endCol": sc, "newText": new_text})
                        # do not mutate base_text when building atomic spans
                else:
                    return _with_norm(_err("unknown_op", f"Unsupported text edit op: {opx}", normalized=normalized_for_echo, routing="mixed/text-first"), normalized_for_echo, routing="mixed/text-first")

            sha = hashlib.sha256(base_text.encode("utf-8")).hexdigest()
            if at_edits:
                params_text: dict[str, Any] = {
                    "action": "apply_text_edits",
                    "name": name,
                    "path": path,
                    "namespace": namespace,
                    "scriptType": script_type,
                    "edits": at_edits,
                    "precondition_sha256": sha,
                    "options": {"refresh": (options or {}).get("refresh", "debounced"), "validate": (options or {}).get("validate", "standard"), "applyMode": ("atomic" if len(at_edits) > 1 else (options or {}).get("applyMode", "sequential"))}
                }
                resp_text = await send_with_unity_instance(
                    async_send_command_with_retry,
                    unity_instance,
                    "manage_script",
                    params_text,
                )
                if not (isinstance(resp_text, dict) and resp_text.get("success")):
                    return _with_norm(resp_text if isinstance(resp_text, dict) else {"success": False, "message": str(resp_text)}, normalized_for_echo, routing="mixed/text-first")
                # Optional sentinel reload removed (deprecated)
        except Exception as e:
            return _with_norm({"success": False, "message": f"Text edit conversion failed: {e}"}, normalized_for_echo, routing="mixed/text-first")

        if struct_edits:
            opts2 = dict(options or {})
            # Prefer debounced background refresh unless explicitly overridden
            opts2.setdefault("refresh", "debounced")
            params_struct: dict[str, Any] = {
                "action": "edit",
                "name": name,
                "path": path,
                "namespace": namespace,
                "scriptType": script_type,
                "edits": struct_edits,
                "options": opts2
            }
            resp_struct = await send_with_unity_instance(
                async_send_command_with_retry,
                unity_instance,
                "manage_script",
                params_struct,
            )
            if isinstance(resp_struct, dict) and resp_struct.get("success"):
                pass  # Optional sentinel reload removed (deprecated)
            return _with_norm(resp_struct if isinstance(resp_struct, dict) else {"success": False, "message": str(resp_struct)}, normalized_for_echo, routing="mixed/text-first")

        return _with_norm({"success": True, "message": "Applied text edits (no structured ops)"}, normalized_for_echo, routing="mixed/text-first")

    # If the edits are text-ops, prefer sending them to Unity's apply_text_edits with precondition
    # so header guards and validation run on the C# side.
    # Supported conversions: anchor_insert, replace_range, regex_replace (first match only).
    text_ops = {(e.get("op") or e.get("operation") or e.get("type") or e.get(
        "mode") or "").strip().lower() for e in (edits or [])}
    structured_kinds = {"replace_class", "delete_class",
                        "replace_method", "delete_method", "insert_method", "anchor_insert"}
    if not text_ops.issubset(structured_kinds):
        # Convert to apply_text_edits payload
        try:
            base_text = contents

            def line_col_from_index(idx: int) -> tuple[int, int]:
                # 1-based line/col against base buffer
                line = base_text.count("\n", 0, idx) + 1
                last_nl = base_text.rfind("\n", 0, idx)
                col = (idx - (last_nl + 1)) + \
                    1 if last_nl >= 0 else idx + 1
                return line, col

            at_edits: list[dict[str, Any]] = []
            for e in edits or []:
                op = (e.get("op") or e.get("operation") or e.get(
                    "type") or e.get("mode") or "").strip().lower()
                # aliasing for text field
                text_field = e.get("text") or e.get(
                    "insert") or e.get("content") or ""
                if op == "anchor_insert":
                    anchor = e.get("anchor") or ""
                    position = (e.get("position") or "after").lower()
                    # Use improved anchor matching logic with helpful errors, honoring ignore_case
                    try:
                        flags = re.MULTILINE | (
                            re.IGNORECASE if e.get("ignore_case") else 0)
                        m = _find_best_anchor_match(
                            anchor, base_text, flags, prefer_last=True)
                    except Exception as ex:
                        return _with_norm(_err("bad_regex", f"Invalid anchor regex: {ex}", normalized=normalized_for_echo, routing="text", extra={"hint": "Escape parentheses/braces or use a simpler anchor."}), normalized_for_echo, routing="text")
                    if not m:
                        return _with_norm({"success": False, "code": "anchor_not_found", "message": f"anchor not found: {anchor}"}, normalized_for_echo, routing="text")
                    idx = m.start() if position == "before" else m.end()
                    # Normalize insertion newlines
                    if text_field and not text_field.startswith("\n"):
                        text_field = "\n" + text_field
                    if text_field and not text_field.endswith("\n"):
                        text_field = text_field + "\n"
                    sl, sc = line_col_from_index(idx)
                    at_edits.append({
                        "startLine": sl,
                        "startCol": sc,
                        "endLine": sl,
                        "endCol": sc,
                        "newText": text_field or ""
                    })
                    # Do not mutate base buffer when building an atomic batch
                elif op == "replace_range":
                    # Directly forward if already in line/col form
                    if "startLine" in e:
                        at_edits.append({
                            "startLine": int(e.get("startLine", 1)),
                            "startCol": int(e.get("startCol", 1)),
                            "endLine": int(e.get("endLine", 1)),
                            "endCol": int(e.get("endCol", 1)),
                            "newText": text_field
                        })
                    else:
                        # If only indices provided, skip (we don't support index-based here)
                        return _with_norm({"success": False, "code": "missing_field", "message": "replace_range requires startLine/startCol/endLine/endCol"}, normalized_for_echo, routing="text")
                elif op == "regex_replace":
                    pattern = e.get("pattern") or ""
                    repl = text_field
                    flags = re.MULTILINE | (
                        re.IGNORECASE if e.get("ignore_case") else 0)
                    # Early compile for clearer error messages
                    try:
                        regex_obj = re.compile(pattern, flags)
                    except Exception as ex:
                        return _with_norm(_err("bad_regex", f"Invalid regex pattern: {ex}", normalized=normalized_for_echo, routing="text", extra={"hint": "Escape special chars or prefer structured delete for methods."}), normalized_for_echo, routing="text")
                    # Use smart anchor matching for consistent behavior with anchor_insert
                    m = _find_best_anchor_match(
                        pattern, base_text, flags, prefer_last=True)
                    if not m:
                        continue
                    # Expand $1, $2... backrefs in replacement using the first match (consistent with mixed-path behavior)

                    def _expand_dollars(rep: str, _m=m) -> str:
                        return re.sub(r"\$(\d+)", lambda g: _m.group(int(g.group(1))) or "", rep)
                    repl_expanded = _expand_dollars(repl)
                    # Let C# side handle validation using Unity's built-in compiler services
                    sl, sc = line_col_from_index(m.start())
                    el, ec = line_col_from_index(m.end())
                    at_edits.append({
                        "startLine": sl,
                        "startCol": sc,
                        "endLine": el,
                        "endCol": ec,
                        "newText": repl_expanded
                    })
                    # Do not mutate base buffer when building an atomic batch
                else:
                    return _with_norm({"success": False, "code": "unsupported_op", "message": f"Unsupported text edit op for server-side apply_text_edits: {op}"}, normalized_for_echo, routing="text")

            if not at_edits:
                return _with_norm({"success": False, "code": "no_spans", "message": "No applicable text edit spans computed (anchor not found or zero-length)."}, normalized_for_echo, routing="text")

            sha = hashlib.sha256(base_text.encode("utf-8")).hexdigest()
            params: dict[str, Any] = {
                "action": "apply_text_edits",
                "name": name,
                "path": path,
                "namespace": namespace,
                "scriptType": script_type,
                "edits": at_edits,
                "precondition_sha256": sha,
                "options": {
                    "refresh": (options or {}).get("refresh", "debounced"),
                    "validate": (options or {}).get("validate", "standard"),
                    "applyMode": ("atomic" if len(at_edits) > 1 else (options or {}).get("applyMode", "sequential"))
                }
            }
            resp = await send_with_unity_instance(
                async_send_command_with_retry,
                unity_instance,
                "manage_script",
                params,
            )
            if isinstance(resp, dict) and resp.get("success"):
                pass  # Optional sentinel reload removed (deprecated)
            return _with_norm(
                resp if isinstance(resp, dict)
                else {"success": False, "message": str(resp)},
                normalized_for_echo,
                routing="text",
            )
        except Exception as e:
            return _with_norm({"success": False, "code": "conversion_failed", "message": f"Edit conversion failed: {e}"}, normalized_for_echo, routing="text")

    # For regex_replace, honor preview consistently: if preview=true, always return diff without writing.
    # If confirm=false (default) and preview not requested, return diff and instruct confirm=true to apply.
    if "regex_replace" in text_ops and (preview or not (options or {}).get("confirm")):
        try:
            preview_text = _apply_edits_locally(contents, edits)
            import difflib
            diff = list(difflib.unified_diff(contents.splitlines(
            ), preview_text.splitlines(), fromfile="before", tofile="after", n=2))
            if len(diff) > 800:
                diff = diff[:800] + ["... (diff truncated) ..."]
            if preview:
                return {"success": True, "message": "Preview only (no write)", "data": {"diff": "\n".join(diff), "normalizedEdits": normalized_for_echo}}
            return _with_norm({"success": False, "message": "Preview diff; set options.confirm=true to apply.", "data": {"diff": "\n".join(diff)}}, normalized_for_echo, routing="text")
        except Exception as e:
            return _with_norm({"success": False, "code": "preview_failed", "message": f"Preview failed: {e}"}, normalized_for_echo, routing="text")
    # 2) apply edits locally (only if not text-ops)
    try:
        new_contents = _apply_edits_locally(contents, edits)
    except Exception as e:
        return {"success": False, "message": f"Edit application failed: {e}"}

    # Short-circuit no-op edits to avoid false "applied" reports downstream
    if new_contents == contents:
        return _with_norm({
            "success": True,
            "message": "No-op: contents unchanged",
            "data": {"no_op": True, "evidence": {"reason": "identical_content"}}
        }, normalized_for_echo, routing="text")

    if preview:
        # Produce a compact unified diff limited to small context
        import difflib
        a = contents.splitlines()
        b = new_contents.splitlines()
        diff = list(difflib.unified_diff(
            a, b, fromfile="before", tofile="after", n=3))
        # Limit diff size to keep responses small
        if len(diff) > 2000:
            diff = diff[:2000] + ["... (diff truncated) ..."]
        return {"success": True, "message": "Preview only (no write)", "data": {"diff": "\n".join(diff), "normalizedEdits": normalized_for_echo}}

    # 3) update to Unity
    # Default refresh/validate for natural usage on text path as well
    options = dict(options or {})
    options.setdefault("validate", "standard")
    options.setdefault("refresh", "debounced")

    # Compute the SHA of the current file contents for the precondition
    old_lines = contents.splitlines(keepends=True)
    end_line = len(old_lines) + 1  # 1-based exclusive end
    sha = hashlib.sha256(contents.encode("utf-8")).hexdigest()

    # Apply a whole-file text edit rather than the deprecated 'update' action
    params = {
        "action": "apply_text_edits",
        "name": name,
        "path": path,
        "namespace": namespace,
        "scriptType": script_type,
        "edits": [
            {
                "startLine": 1,
                "startCol": 1,
                "endLine": end_line,
                "endCol": 1,
                "newText": new_contents,
            }
        ],
        "precondition_sha256": sha,
        "options": options or {"validate": "standard", "refresh": "debounced"},
    }

    write_resp = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_script",
        params,
    )
    if isinstance(write_resp, dict) and write_resp.get("success"):
        pass  # Optional sentinel reload removed (deprecated)
    return _with_norm(
        write_resp if isinstance(write_resp, dict)
        else {"success": False, "message": str(write_resp)},
        normalized_for_echo,
        routing="text",
    )
