#!/usr/bin/env python3
"""
Generate Docusaurus reference pages for MCP for Unity tools and resources.

Single source of truth: the Python `@mcp_for_unity_tool` and
`@mcp_for_unity_resource` registries under Server/src/services/. The C#
attributes carry only Name/Group/Description; the Python decorator owns the
richest typing (Annotated[...] parameter docs) and is what the MCP client
actually sees over the wire.

Outputs:
  website/docs/reference/tools/<group>/<tool-name>.md   — one per tool
  website/docs/reference/tools/<group>/index.md         — group landing
  website/docs/reference/tools/index.md                 — catalog landing
  website/docs/reference/resources/index.md             — resources catalog

Modes:
  --write (default)  regenerate files in place
  --check            re-emit to a temp dir and diff against committed files;
                     exits non-zero on drift (used by CI / pre-commit hook)

Hand-authored example blocks between <!-- examples:start --> and
<!-- examples:end --> are preserved across regeneration.

Run requirements: the Server/ Python dependencies must be importable, since
we load every tool module to trigger decorator registration. In CI:
  cd Server && uv sync && cd .. && uv --project Server run python tools/generate_docs_reference.py --check
"""

from __future__ import annotations

import argparse
import filecmp
import importlib
import inspect
import json
import re
import shutil
import sys
import tempfile
import textwrap
import typing
from dataclasses import dataclass
from pathlib import Path
from types import GenericAlias
from typing import Annotated, Any, Literal, Union, get_args, get_origin

REPO_ROOT = Path(__file__).resolve().parent.parent
SERVER_SRC = REPO_ROOT / "Server" / "src"
WEBSITE_DOCS = REPO_ROOT / "website" / "docs"
TOOLS_OUT = WEBSITE_DOCS / "reference" / "tools"
RESOURCES_OUT = WEBSITE_DOCS / "reference" / "resources"

GENERATED_BANNER = (
    "> **Auto-generated** from the Python tool registry. Do not hand-edit "
    "outside `<!-- examples:start --><!-- examples:end -->` blocks — the "
    "generator (`tools/generate_docs_reference.py`) will overwrite them."
)

EXAMPLES_OPEN = "<!-- examples:start -->"
EXAMPLES_CLOSE = "<!-- examples:end -->"
EXAMPLES_PLACEHOLDER = (
    f"{EXAMPLES_OPEN}\n"
    "*No examples yet. Add usage examples here — they will be preserved across regenerations.*\n"
    f"{EXAMPLES_CLOSE}\n"
)


# ---------------------------------------------------------------------------
# Registry loading
# ---------------------------------------------------------------------------


def _ensure_server_on_path() -> None:
    if str(SERVER_SRC) not in sys.path:
        sys.path.insert(0, str(SERVER_SRC))


def load_registries() -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    """Import every tool/resource module so the decorators fire, then return
    the populated registries."""
    _ensure_server_on_path()

    from services.registry import (  # noqa: WPS433  (deferred import by design)
        get_registered_tools,
        get_registered_resources,
        clear_tool_registry,
        clear_resource_registry,
    )
    from utils.module_discovery import discover_modules

    clear_tool_registry()
    clear_resource_registry()

    tools_pkg = importlib.import_module("services.tools")
    resources_pkg = importlib.import_module("services.resources")

    # Walk both directories and import every module — the @decorator
    # side-effects populate the registries.
    list(discover_modules(Path(tools_pkg.__file__).parent, tools_pkg.__name__))
    list(discover_modules(Path(resources_pkg.__file__).parent, resources_pkg.__name__))

    return get_registered_tools(), get_registered_resources()


# ---------------------------------------------------------------------------
# Parameter introspection
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class ParamDoc:
    name: str
    type_str: str
    required: bool
    description: str | None
    default: str | None


def _render_type(annotation: Any) -> str:
    """Render a typing annotation as a short Markdown-safe string."""
    if annotation is inspect.Parameter.empty or annotation is None:
        return "any"

    origin = get_origin(annotation)

    if origin is Annotated:
        return _render_type(get_args(annotation)[0])

    if origin in (Union, getattr(typing, "UnionType", Union)):
        parts = [_render_type(a) for a in get_args(annotation) if a is not type(None)]
        has_none = type(None) in get_args(annotation) or any(
            part.endswith(" | None") or part == "None" for part in parts
        )
        # Strip any inner "| None" — we'll add a single one at the end if needed.
        parts = [p[: -len(" | None")] if p.endswith(" | None") else p for p in parts]
        rendered = " | ".join(p for p in parts if p and p != "None")
        return rendered + (" | None" if has_none else "")

    if origin is Literal:
        literals = ", ".join(repr(a) for a in get_args(annotation))
        return f"Literal[{literals}]"

    if origin is list or annotation is list:
        args = get_args(annotation)
        inner = ", ".join(_render_type(a) for a in args) if args else "Any"
        return f"list[{inner}]"

    if origin is dict or annotation is dict:
        args = get_args(annotation)
        inner = ", ".join(_render_type(a) for a in args) if args else "Any"
        return f"dict[{inner}]"

    if origin is tuple or annotation is tuple:
        args = get_args(annotation)
        inner = ", ".join(_render_type(a) for a in args) if args else "Any"
        return f"tuple[{inner}]"

    if isinstance(annotation, type):
        return annotation.__name__

    if isinstance(annotation, GenericAlias):  # e.g. list[str] without origin
        return str(annotation)

    return str(annotation).replace("typing.", "")


def _annotation_description(annotation: Any) -> str | None:
    """Pull the human-readable string from an Annotated[...] parameter."""

    def _walk(a: Any) -> str | None:
        origin = get_origin(a)
        if origin is Annotated:
            for meta in get_args(a)[1:]:
                if isinstance(meta, str):
                    return meta
            # Recurse into the underlying type — e.g. Annotated[str, "..."] | None
            return _walk(get_args(a)[0])
        if origin in (Union, getattr(typing, "UnionType", Union)):
            for arg in get_args(a):
                desc = _walk(arg)
                if desc:
                    return desc
        return None

    return _walk(annotation)


def _is_required(param: inspect.Parameter) -> bool:
    return param.default is inspect.Parameter.empty


def _render_default(default: Any) -> str | None:
    if default is inspect.Parameter.empty:
        return None
    if default is None:
        return "None"
    return repr(default)


def introspect_params(func: Any) -> list[ParamDoc]:
    sig = inspect.signature(func)
    try:
        hints = typing.get_type_hints(func, include_extras=True)
    except Exception:
        hints = {}

    out: list[ParamDoc] = []
    for name, param in sig.parameters.items():
        if name in {"self", "cls", "ctx"}:
            continue
        annotation = hints.get(name, param.annotation)
        out.append(
            ParamDoc(
                name=name,
                type_str=_render_type(annotation),
                required=_is_required(param),
                description=_annotation_description(annotation),
                default=_render_default(param.default),
            )
        )
    return out


# ---------------------------------------------------------------------------
# Markdown rendering
# ---------------------------------------------------------------------------


_SENTENCE_BOUNDARY = re.compile(r"(?<=[.!?])\s+(?=[A-Z])|\n\s*\n")


def _first_sentence(description: str) -> str:
    """Return the first sentence of a tool description, suitable for
    frontmatter / catalog blurbs.

    The earlier implementation used `description.split(".")[0]` which
    cut the string at the first period — including periods inside
    abbreviations and parenthesized lists (e.g. `etc.) in Unity`),
    producing truncated frontmatter like `"...modify, delete, etc"`.

    Split on a real sentence boundary instead: a `.`, `!`, or `?`
    followed by whitespace + a capital letter (or a paragraph break).
    Fall back to the entire string if no boundary is found, then
    smart-truncate to keep frontmatter compact.
    """
    text = (description or "").strip().replace('"', "'")
    if not text:
        return ""
    first = _SENTENCE_BOUNDARY.split(text, maxsplit=1)[0].strip()
    # Cap absurdly long single-sentence descriptions
    if len(first) > 240:
        first = first[:237].rstrip() + "…"
    return first


def _escape_table_cell(s: str) -> str:
    return s.replace("|", "\\|").replace("\n", " ")


def _read_existing_examples(path: Path) -> str:
    """Return the existing examples block from a generated file, if any.

    Requires the start/end markers to sit on their own line so we don't
    match the markers that appear inside the generator's own warning
    banner (which references the literal marker strings)."""
    if not path.exists():
        return EXAMPLES_PLACEHOLDER
    text = path.read_text(encoding="utf-8")
    match = re.search(
        rf"^{re.escape(EXAMPLES_OPEN)}\s*\n(.*?)^{re.escape(EXAMPLES_CLOSE)}\s*$",
        text,
        re.DOTALL | re.MULTILINE,
    )
    if not match:
        return EXAMPLES_PLACEHOLDER
    captured = match.group(1)
    if not captured.strip():
        return EXAMPLES_PLACEHOLDER
    return f"{EXAMPLES_OPEN}\n{captured.strip()}\n{EXAMPLES_CLOSE}\n"


def render_tool_page(tool: dict[str, Any], existing_examples: str) -> str:
    name = tool["name"]
    description = (tool.get("description") or "").strip()
    group = tool.get("group") or "core"
    func = tool["func"]
    module = getattr(func, "__module__", "")
    params = introspect_params(func)

    # Sidebar/title metadata
    desc_for_meta = _first_sentence(description) or name
    front_matter = textwrap.dedent(
        f"""\
        ---
        title: {name}
        sidebar_label: {name}
        description: "{desc_for_meta}"
        ---
        """
    )

    if params:
        rows = ["| Name | Type | Required | Description |", "|------|------|----------|-------------|"]
        for p in params:
            req = "yes" if p.required else "—"
            desc = _escape_table_cell(p.description or "")
            type_cell = _escape_table_cell(f"`{p.type_str}`")
            rows.append(f"| `{p.name}` | {type_cell} | {req} | {desc} |")
        params_section = "\n".join(rows)
    else:
        params_section = "_No parameters._"

    return (
        f"{front_matter}\n"
        f"# `{name}`\n\n"
        f"{GENERATED_BANNER}\n\n"
        f"**Group:** `{group}` &nbsp;·&nbsp; "
        f"**Module:** `{module}`\n\n"
        f"## Description\n\n"
        f"{description or '_No description provided._'}\n\n"
        f"## Parameters\n\n"
        f"{params_section}\n\n"
        f"## Returns\n\n"
        f"A `dict` containing the Unity response. The exact shape depends on the action.\n\n"
        f"## Examples\n\n"
        f"{existing_examples}\n"
    )


def render_group_index(group: str, tools: list[dict[str, Any]], group_blurb: str) -> str:
    front_matter = textwrap.dedent(
        f"""\
        ---
        title: "{group} tools"
        sidebar_label: "{group}"
        description: "MCP for Unity tools in the {group} group."
        ---
        """
    )

    bullets = []
    for tool in sorted(tools, key=lambda t: t["name"]):
        n = tool["name"]
        d = _first_sentence(tool.get("description") or "")
        bullets.append(f"- **[`{n}`](./{n}.md)** — {d}")
    body = "\n".join(bullets) if bullets else "_No tools in this group._"

    return (
        f"{front_matter}\n"
        f"# `{group}` tools\n\n"
        f"{group_blurb}\n\n"
        f"{body}\n"
    )


def render_catalog_index(tools_by_group: dict[str, list[dict[str, Any]]],
                         group_blurbs: dict[str, str]) -> str:
    front_matter = textwrap.dedent(
        """\
        ---
        title: Tool reference
        sidebar_label: Tools
        sidebar_class_name: sidebar-hidden
        slug: /reference/tools
        description: Auto-generated catalog of every MCP for Unity tool, grouped by domain.
        ---
        """
    )

    sections = [
        "# Tool reference\n",
        GENERATED_BANNER + "\n",
        "Every tool MCP for Unity exposes, generated directly from the Python "
        "`@mcp_for_unity_tool` registry under `Server/src/services/tools/`.\n",
    ]

    for group in sorted(tools_by_group):
        tools = tools_by_group[group]
        sections.append(f"## `{group}` &nbsp; ({len(tools)} tool{'s' if len(tools) != 1 else ''})")
        sections.append(group_blurbs.get(group, ""))
        for tool in sorted(tools, key=lambda t: t["name"]):
            n = tool["name"]
            d = _first_sentence(tool.get("description") or "")
            sections.append(f"- **[`{n}`](./{group}/{n}.md)** — {d}")
        sections.append("")

    return front_matter + "\n" + "\n".join(sections) + "\n"


def render_resources_catalog(resources: list[dict[str, Any]]) -> str:
    front_matter = textwrap.dedent(
        """\
        ---
        title: Resource reference
        sidebar_label: Resources
        slug: /reference/resources
        description: Auto-generated catalog of every MCP for Unity resource.
        ---
        """
    )

    head = (
        "# Resource reference\n\n"
        f"{GENERATED_BANNER}\n\n"
        "Resources are read-only state surfaces exposed to MCP clients. "
        "Tools mutate; resources observe.\n\n"
    )

    items = []
    for res in sorted(resources, key=lambda r: r["name"]):
        name = res["name"]
        uri = res.get("uri", "")
        desc = (res.get("description") or "").strip() or "_No description._"
        func = res["func"]
        params = introspect_params(func)
        if params:
            param_lines = ["", "**Parameters:**", ""]
            for p in params:
                req = "required" if p.required else "optional"
                d = p.description or ""
                param_lines.append(f"- `{p.name}` (`{p.type_str}`, {req}) — {d}")
            param_block = "\n".join(param_lines)
        else:
            param_block = ""

        items.append(
            f"## `{name}`\n\n"
            f"**URI:** `{uri}`\n\n"
            f"{desc}\n"
            f"{param_block}\n"
        )

    return front_matter + "\n" + head + "\n".join(items) + "\n"


# ---------------------------------------------------------------------------
# File-writing
# ---------------------------------------------------------------------------


GROUP_BLURBS_FALLBACK = {
    "core": "Essential scene, script, asset, and editor tools — always on by default.",
    "docs": "Unity API reflection and documentation lookup.",
    "vfx": "Visual effects — VFX Graph, shaders, procedural textures.",
    "animation": "Animator control and AnimationClip creation.",
    "ui": "UI Toolkit — UXML, USS, UIDocument.",
    "scripting_ext": "ScriptableObject management.",
    "testing": "Test runner and async test jobs.",
    "probuilder": "ProBuilder 3D modeling — requires `com.unity.probuilder`.",
    "profiling": "Unity Profiler session control, counters, memory snapshots, Frame Debugger.",
}


def _resolve_group_blurbs() -> dict[str, str]:
    """Pull live blurbs from the registry, falling back to the local copy."""
    try:
        from services.registry import TOOL_GROUPS  # type: ignore
        return {g: blurb for g, blurb in TOOL_GROUPS.items()}
    except Exception:
        return GROUP_BLURBS_FALLBACK


def _write(path: Path, content: str) -> bool:
    """Write only if content differs. Return True if a write happened."""
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.read_text(encoding="utf-8") == content:
        return False
    path.write_text(content, encoding="utf-8")
    return True


def generate(
    tools_root: Path = TOOLS_OUT,
    resources_root: Path = RESOURCES_OUT,
    examples_source: Path | None = None,
) -> dict[str, int]:
    """Generate reference pages.

    When `examples_source` is provided, hand-authored examples blocks are
    read from there instead of from `tools_root`. `--check` uses this to
    write into a tempdir while still preserving examples from the committed
    canonical location.
    """
    tools, resources = load_registries()
    group_blurbs = _resolve_group_blurbs()

    # Group tools.
    tools_by_group: dict[str, list[dict[str, Any]]] = {}
    for t in tools:
        g = t.get("group") or "core"
        tools_by_group.setdefault(g, []).append(t)

    stats = {"tools": 0, "groups": 0, "resources": 0, "writes": 0}
    examples_root = examples_source if examples_source is not None else tools_root

    # Per-tool pages + per-group landing + Docusaurus category metadata.
    for group, group_tools in sorted(tools_by_group.items()):
        group_dir = tools_root / group
        examples_dir = examples_root / group
        for tool in sorted(group_tools, key=lambda t: t["name"]):
            page_path = group_dir / f"{tool['name']}.md"
            examples_path = examples_dir / f"{tool['name']}.md"
            existing_examples = _read_existing_examples(examples_path)
            page_md = render_tool_page(tool, existing_examples)
            if _write(page_path, page_md):
                stats["writes"] += 1
            stats["tools"] += 1
        index_md = render_group_index(group, group_tools, group_blurbs.get(group, ""))
        if _write(group_dir / "index.md", index_md):
            stats["writes"] += 1
        # _category_.json tells the autogenerated sidebar to wrap this
        # directory in a collapsible category. Without it, the group's
        # tool pages render flat as siblings of the group index.
        category_json = json.dumps(
            {
                "label": group,
                "link": {"type": "doc", "id": f"reference/tools/{group}/index"},
                "collapsed": True,
            },
            indent=2,
        ) + "\n"
        if _write(group_dir / "_category_.json", category_json):
            stats["writes"] += 1
        stats["groups"] += 1

    # Top-level catalog index. The sidebar's "Tools" parent category in
    # sidebars.js is what links to this doc — no root `_category_.json`
    # here, because that would compete with the parent's explicit link
    # and end up listing the catalog as a duplicate "Tools" child.
    catalog_md = render_catalog_index(tools_by_group, group_blurbs)
    if _write(tools_root / "index.md", catalog_md):
        stats["writes"] += 1
    root_cat = tools_root / "_category_.json"
    if root_cat.exists():
        root_cat.unlink()

    # Resources catalog (single page).
    resources_md = render_resources_catalog(resources)
    if _write(resources_root / "index.md", resources_md):
        stats["writes"] += 1
    stats["resources"] = len(resources)

    return stats


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def _copytree_into(src: Path, dst: Path) -> None:
    if src.exists():
        shutil.copytree(src, dst, dirs_exist_ok=True)


def _diff_trees(a: Path, b: Path) -> list[str]:
    diffs: list[str] = []

    def _walk(rel: Path) -> None:
        cmp = filecmp.dircmp(a / rel, b / rel)
        for name in cmp.left_only:
            diffs.append(f"committed-only: {rel / name}")
        for name in cmp.right_only:
            diffs.append(f"generated-only: {rel / name}")
        for name in cmp.diff_files:
            diffs.append(f"differs: {rel / name}")
        for name in cmp.common_dirs:
            _walk(rel / name)

    if a.exists() and b.exists():
        _walk(Path("."))
    elif b.exists():
        diffs.append(f"committed missing entirely: {a}")
    return diffs


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true",
                        help="Generate into a temp dir and diff against the committed reference. Non-zero exit on drift.")
    args = parser.parse_args(argv)

    if args.check:
        # Use a persistent dir under /tmp if MCP4U_KEEP_CHECK is set, so the
        # user can diff committed vs generated by hand.
        import os
        keep = bool(os.environ.get("MCP4U_KEEP_CHECK"))
        ctx = tempfile.TemporaryDirectory(prefix="mcp4u-docs-check-") if not keep else None
        tmp = ctx.__enter__() if ctx else tempfile.mkdtemp(prefix="mcp4u-docs-check-keep-")
        try:
            tmp_root = Path(tmp)
            tmp_tools = tmp_root / "tools"
            tmp_resources = tmp_root / "resources"
            # Read existing examples from the committed location so
            # preservation is honored in --check too.
            generate(tmp_tools, tmp_resources, examples_source=TOOLS_OUT)
            if keep:
                print(f"[--check] generated tree retained at {tmp_root}")

            diffs = []
            diffs.extend(_diff_trees(TOOLS_OUT, tmp_tools))
            diffs.extend(_diff_trees(RESOURCES_OUT, tmp_resources))

            if diffs:
                print("Generated reference is stale. Run:")
                print("  python tools/generate_docs_reference.py")
                print("then commit the changes. Details:")
                for d in diffs:
                    print(f"  - {d}")
                return 1

            print("Generated docs reference is up-to-date.")
            return 0
        finally:
            if ctx:
                ctx.__exit__(None, None, None)

    stats = generate()
    print(
        f"Generated {stats['tools']} tool pages across {stats['groups']} groups "
        f"({stats['writes']} file(s) written) + {stats['resources']} resource entries."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
