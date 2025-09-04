"""
Resource wrapper tools so clients that do not expose MCP resources primitives
can still list and read files via normal tools. These call into the same
safe path logic (re-implemented here to avoid importing server.py).
"""
from __future__ import annotations

from typing import Dict, Any, List
import re
from pathlib import Path
from urllib.parse import urlparse, unquote
import fnmatch
import hashlib
import os

from mcp.server.fastmcp import FastMCP, Context
from unity_connection import send_command_with_retry


def _resolve_project_root(override: str | None) -> Path:
    # 1) Explicit override
    if override:
        pr = Path(override).expanduser().resolve()
        if (pr / "Assets").exists():
            return pr
    # 2) Environment
    env = os.environ.get("UNITY_PROJECT_ROOT")
    if env:
        env_path = Path(env).expanduser()
        # If UNITY_PROJECT_ROOT is relative, resolve against repo root (cwd's repo) instead of src dir
        pr = (Path.cwd() / env_path).resolve() if not env_path.is_absolute() else env_path.resolve()
        if (pr / "Assets").exists():
            return pr
    # 3) Ask Unity via manage_editor.get_project_root
    try:
        resp = send_command_with_retry("manage_editor", {"action": "get_project_root"})
        if isinstance(resp, dict) and resp.get("success"):
            pr = Path(resp.get("data", {}).get("projectRoot", "")).expanduser().resolve()
            if pr and (pr / "Assets").exists():
                return pr
    except Exception:
        pass

    # 4) Walk up from CWD to find a Unity project (Assets + ProjectSettings)
    cur = Path.cwd().resolve()
    for _ in range(6):
        if (cur / "Assets").exists() and (cur / "ProjectSettings").exists():
            return cur
        if cur.parent == cur:
            break
        cur = cur.parent
    # 5) Search downwards (shallow) from repo root for first folder with Assets + ProjectSettings
    try:
        import os as _os
        root = Path.cwd().resolve()
        max_depth = 3
        for dirpath, dirnames, _ in _os.walk(root):
            rel = Path(dirpath).resolve()
            try:
                depth = len(rel.relative_to(root).parts)
            except Exception:
                # Unrelated mount/permission edge; skip deeper traversal
                dirnames[:] = []
                continue
            if depth > max_depth:
                # Prune deeper traversal
                dirnames[:] = []
                continue
            if (rel / "Assets").exists() and (rel / "ProjectSettings").exists():
                return rel
    except Exception:
        pass
    # 6) Fallback: CWD
    return Path.cwd().resolve()


def _resolve_safe_path_from_uri(uri: str, project: Path) -> Path | None:
    raw: str | None = None
    if uri.startswith("unity://path/"):
        raw = uri[len("unity://path/"):]
    elif uri.startswith("file://"):
        parsed = urlparse(uri)
        raw = unquote(parsed.path or "")
        # On Windows, urlparse('file:///C:/x') -> path='/C:/x'. Strip the leading slash for drive letters.
        try:
            import os as _os
            if _os.name == "nt" and raw.startswith("/") and re.match(r"^/[A-Za-z]:/", raw):
                raw = raw[1:]
            # UNC paths: file://server/share -> netloc='server', path='/share'. Treat as \\\\server/share
            if _os.name == "nt" and parsed.netloc:
                raw = f"//{parsed.netloc}{raw}"
        except Exception:
            pass
    elif uri.startswith("Assets/"):
        raw = uri
    if raw is None:
        return None
    # Normalize separators early
    raw = raw.replace("\\", "/")
    p = (project / raw).resolve()
    try:
        p.relative_to(project)
    except ValueError:
        return None
    return p


def register_resource_tools(mcp: FastMCP) -> None:
    """Registers list_resources and read_resource wrapper tools."""

    @mcp.tool(description=(
        "List project URIs (unity://path/...) under a folder (default: Assets).\n\n"
        "Args: pattern (glob, default *.cs), under (folder under project root), limit, project_root.\n"
        "Security: restricted to Assets/ subtree; symlinks are resolved and must remain under Assets/.\n"
        "Notes: Only .cs files are returned by default; always appends unity://spec/script-edits.\n"
    ))
    async def list_resources(
        ctx: Context | None = None,
        pattern: str | None = "*.cs",
        under: str = "Assets",
        limit: int = 200,
        project_root: str | None = None,
    ) -> Dict[str, Any]:
        """
        Lists project URIs (unity://path/...) under a folder (default: Assets).
        - pattern: glob like *.cs or *.shader (None to list all files)
        - under: relative folder under project root
        - limit: max results
        """
        try:
            project = _resolve_project_root(project_root)
            base = (project / under).resolve()
            try:
                base.relative_to(project)
            except ValueError:
                return {"success": False, "error": "Base path must be under project root"}
            # Enforce listing only under Assets
            try:
                base.relative_to(project / "Assets")
            except ValueError:
                return {"success": False, "error": "Listing is restricted to Assets/"}

            matches: List[str] = []
            for p in base.rglob("*"):
                if not p.is_file():
                    continue
                # Resolve symlinks and ensure the real path stays under project/Assets
                try:
                    rp = p.resolve()
                    rp.relative_to(project / "Assets")
                except Exception:
                    continue
                # Enforce .cs extension regardless of provided pattern
                if p.suffix.lower() != ".cs":
                    continue
                if pattern and not fnmatch.fnmatch(p.name, pattern):
                    continue
                rel = p.relative_to(project).as_posix()
                matches.append(f"unity://path/{rel}")
                if len(matches) >= max(1, limit):
                    break

            # Always include the canonical spec resource so NL clients can discover it
            if "unity://spec/script-edits" not in matches:
                matches.append("unity://spec/script-edits")

            return {"success": True, "data": {"uris": matches, "count": len(matches)}}
        except Exception as e:
            return {"success": False, "error": str(e)}

    @mcp.tool(description=(
        "Read a resource by unity://path/... URI with optional slicing.\n\n"
        "Args: uri, start_line/line_count or head_bytes, tail_lines (optional), project_root, request (NL hints).\n"
        "Security: uri must resolve under Assets/.\n"
        "Examples: head_bytes=1024; start_line=100,line_count=40; tail_lines=120.\n"
    ))
    async def read_resource(
        uri: str,
        ctx: Context | None = None,
        start_line: int | None = None,
        line_count: int | None = None,
        head_bytes: int | None = None,
        tail_lines: int | None = None,
        project_root: str | None = None,
        request: str | None = None,
    ) -> Dict[str, Any]:
        """
        Reads a resource by unity://path/... URI with optional slicing.
        One of line window (start_line/line_count) or head_bytes can be used to limit size.
        """
        try:
            # Serve the canonical spec directly when requested (allow bare or with scheme)
            if uri in ("unity://spec/script-edits", "spec/script-edits", "script-edits"):
                spec_json = (
                    '{\n'
                    '  "name": "Unity MCP — Script Edits v1",\n'
                    '  "target_tool": "script_apply_edits",\n'
                    '  "canonical_rules": {\n'
                    '    "always_use": ["op","className","methodName","replacement","afterMethodName","beforeMethodName"],\n'
                    '    "never_use": ["new_method","anchor_method","content","newText"],\n'
                    '    "defaults": {\n'
                    '      "className": "\u2190 server will default to \'name\' when omitted",\n'
                    '      "position": "end"\n'
                    '    }\n'
                    '  },\n'
                    '  "ops": [\n'
                    '    {"op":"replace_method","required":["className","methodName","replacement"],"optional":["returnType","parametersSignature","attributesContains"],"examples":[{"note":"match overload by signature","parametersSignature":"(int a, string b)"},{"note":"ensure attributes retained","attributesContains":"ContextMenu"}]},\n'
                    '    {"op":"insert_method","required":["className","replacement"],"position":{"enum":["start","end","after","before"],"after_requires":"afterMethodName","before_requires":"beforeMethodName"}},\n'
                    '    {"op":"delete_method","required":["className","methodName"]},\n'
                    '    {"op":"anchor_insert","required":["anchor","text"],"notes":"regex; position=before|after"}\n'
                    '  ],\n'
                    '  "apply_text_edits_recipe": {\n'
                    '    "step1_read": { "tool": "resources/read", "args": {"uri": "unity://path/Assets/Scripts/Interaction/SmartReach.cs"} },\n'
                    '    "step2_apply": {\n'
                    '      "tool": "manage_script",\n'
                    '      "args": {\n'
                    '        "action": "apply_text_edits",\n'
                    '        "name": "SmartReach", "path": "Assets/Scripts/Interaction",\n'
                    '        "edits": [{"startLine": 42, "startCol": 1, "endLine": 42, "endCol": 1, "newText": "[MyAttr]\\n"}],\n'
                    '        "precondition_sha256": "<sha-from-step1>",\n'
                    '        "options": {"refresh": "immediate", "validate": "standard"}\n'
                    '      }\n'
                    '    },\n'
                    '    "note": "newText is for apply_text_edits ranges only; use replacement in script_apply_edits ops."\n'
                    '  },\n'
                    '  "examples": [\n'
                    '    {\n'
                    '      "title": "Replace a method",\n'
                    '      "args": {\n'
                    '        "name": "SmartReach",\n'
                    '        "path": "Assets/Scripts/Interaction",\n'
                    '        "edits": [\n'
                    '          {"op":"replace_method","className":"SmartReach","methodName":"HasTarget","replacement":"public bool HasTarget() { return currentTarget != null; }"}\n'
                    '        ],\n'
                    '        "options": { "validate": "standard", "refresh": "immediate" }\n'
                    '      }\n'
                    '    },\n'
                    '    {\n'
                    '      "title": "Insert a method after another",\n'
                    '      "args": {\n'
                    '        "name": "SmartReach",\n'
                    '        "path": "Assets/Scripts/Interaction",\n'
                    '        "edits": [\n'
                    '          {"op":"insert_method","className":"SmartReach","replacement":"public void PrintSeries() { Debug.Log(seriesName); }","position":"after","afterMethodName":"GetCurrentTarget"}\n'
                    '        ]\n'
                    '      }\n'
                    '    }\n'
                    '  ]\n'
                    '}\n'
                )
                sha = hashlib.sha256(spec_json.encode("utf-8")).hexdigest()
                return {"success": True, "data": {"text": spec_json, "metadata": {"sha256": sha}}}

            project = _resolve_project_root(project_root)
            p = _resolve_safe_path_from_uri(uri, project)
            if not p or not p.exists() or not p.is_file():
                return {"success": False, "error": f"Resource not found: {uri}"}
            try:
                p.relative_to(project / "Assets")
            except ValueError:
                return {"success": False, "error": "Read restricted to Assets/"}
            # Natural-language convenience: request like "last 120 lines", "first 200 lines",
            # "show 40 lines around MethodName", etc.
            if request:
                req = request.strip().lower()
                m = re.search(r"last\s+(\d+)\s+lines", req)
                if m:
                    tail_lines = int(m.group(1))
                m = re.search(r"first\s+(\d+)\s+lines", req)
                if m:
                    start_line = 1
                    line_count = int(m.group(1))
                m = re.search(r"first\s+(\d+)\s*bytes", req)
                if m:
                    head_bytes = int(m.group(1))
                m = re.search(r"show\s+(\d+)\s+lines\s+around\s+([A-Za-z_][A-Za-z0-9_]*)", req)
                if m:
                    window = int(m.group(1))
                    method = m.group(2)
                    # naive search for method header to get a line number
                    text_all = p.read_text(encoding="utf-8")
                    lines_all = text_all.splitlines()
                    pat = re.compile(rf"^\s*(?:\[[^\]]+\]\s*)*(?:public|private|protected|internal|static|virtual|override|sealed|async|extern|unsafe|new|partial).*?\b{re.escape(method)}\s*\(", re.MULTILINE)
                    hit_line = None
                    for i, line in enumerate(lines_all, start=1):
                        if pat.search(line):
                            hit_line = i
                            break
                    if hit_line:
                        half = max(1, window // 2)
                        start_line = max(1, hit_line - half)
                        line_count = window

            # Mutually exclusive windowing options precedence:
            # 1) head_bytes, 2) tail_lines, 3) start_line+line_count, else full text
            if head_bytes and head_bytes > 0:
                raw = p.read_bytes()[: head_bytes]
                text = raw.decode("utf-8", errors="replace")
            else:
                text = p.read_text(encoding="utf-8")
                if tail_lines is not None and tail_lines > 0:
                    lines = text.splitlines()
                    n = max(0, tail_lines)
                    text = "\n".join(lines[-n:])
                elif start_line is not None and line_count is not None and line_count >= 0:
                    lines = text.splitlines()
                    s = max(0, start_line - 1)
                    e = min(len(lines), s + line_count)
                    text = "\n".join(lines[s:e])

            sha = hashlib.sha256(text.encode("utf-8")).hexdigest()
            return {"success": True, "data": {"text": text, "metadata": {"sha256": sha}}}
        except Exception as e:
            return {"success": False, "error": str(e)}

    @mcp.tool()
    async def find_in_file(
        uri: str,
        pattern: str,
        ctx: Context | None = None,
        ignore_case: bool | None = True,
        project_root: str | None = None,
        max_results: int | None = 200,
    ) -> Dict[str, Any]:
        """
        Searches a file with a regex pattern and returns line numbers and excerpts.
        - uri: unity://path/Assets/... or file path form supported by read_resource
        - pattern: regular expression (Python re)
        - ignore_case: case-insensitive by default
        - max_results: cap results to avoid huge payloads
        """
        # re is already imported at module level
        try:
            project = _resolve_project_root(project_root)
            p = _resolve_safe_path_from_uri(uri, project)
            if not p or not p.exists() or not p.is_file():
                return {"success": False, "error": f"Resource not found: {uri}"}

            text = p.read_text(encoding="utf-8")
            flags = re.MULTILINE
            if ignore_case:
                flags |= re.IGNORECASE
            rx = re.compile(pattern, flags)

            results = []
            lines = text.splitlines()
            for i, line in enumerate(lines, start=1):
                if rx.search(line):
                    results.append({"line": i, "text": line})
                    if max_results and len(results) >= max_results:
                        break

            return {"success": True, "data": {"matches": results, "count": len(results)}}
        except Exception as e:
            return {"success": False, "error": str(e)}


