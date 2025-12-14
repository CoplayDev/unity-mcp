import json
import re
import os
from typing import Any
from urllib.parse import urlparse, unquote
import math


def parse_json_payload(payload: str | Any) -> Any:
    """Helper to robustly parse a potentially stringified JSON payload."""
    if not isinstance(payload, str):
        return payload
    
    # Check if it looks like JSON structure
    stripped = payload.strip()
    if not (stripped.startswith("{") or stripped.startswith("[")):
        return payload

    try:
        return json.loads(payload)
    except (json.JSONDecodeError, ValueError):
        # If parsing fails, assume it was meant to be a literal string
        return payload


def coerce_bool(value: Any, default: bool = False) -> bool:
    """Coerce various input types to boolean."""
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        return value.lower() in ("true", "1", "yes", "on", "y")
    if isinstance(value, (int, float)):
        return value != 0
    return default


def coerce_vec3(value: Any, default: list[float] | None = None) -> list[float] | None:
    """
    Attempt to coerce a value into a [x, y, z] float list.
    
    Accepts:
    - List/Tuple of 3 numbers
    - JSON string "[x, y, z]"
    - Comma/space separated string "x, y, z" or "x y z"
    """
    if value is None:
        return default
    
    # First try to parse if it's a string that looks like JSON
    val = parse_json_payload(value)
    
    def _to_vec3(parts):
        try:
            vec = [float(parts[0]), float(parts[1]), float(parts[2])]
        except (ValueError, TypeError, IndexError):
            return default
        return vec if all(math.isfinite(n) for n in vec) else default
        
    if isinstance(val, (list, tuple)) and len(val) == 3:
        return _to_vec3(val)
        
    # Handle legacy strings "1,2,3" or "1 2 3"
    if isinstance(val, str):
        s = val.strip()
        # minimal tolerant parse for "[x,y,z]" or "x,y,z"
        if s.startswith("[") and s.endswith("]"):
            s = s[1:-1]
        # support "x,y,z" and "x y z"
        parts = [p.strip() for p in (s.split(",") if "," in s else s.split())]
        if len(parts) == 3:
            return _to_vec3(parts)
            
    return default


# --- Edit Utils (Moved from edit_utils.py) ---

def split_uri(uri: str) -> tuple[str, str]:
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


def normalize_script_locator(name: str, path: str) -> tuple[str, str]:
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


def apply_edits_locally(original_text: str, edits: list[dict[str, Any]]) -> str:
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
            match = find_best_anchor_match(
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

            def index_of(line: int, col: int, lines_ref: list[str] = lines) -> int:
                if line <= len(lines_ref):
                    return sum(len(ln) for ln in lines_ref[: line - 1]) + (col - 1)
                return sum(len(ln) for ln in lines_ref)
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


def find_best_anchor_match(pattern: str, text: str, flags: int, prefer_last: bool = True):
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
