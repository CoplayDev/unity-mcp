"""Annotation guard: every MCP tool must state its safety hints explicitly.

MCP clients gate a tool behind a human approval prompt when it is not read-only
and not explicitly non-destructive. ``destructiveHint`` defaults to *true* when
omitted, so forgetting it is the dangerous direction -- a harmless read gets an
approval prompt on every call.

That is exactly how #1288 happened: PR #480's own description claimed hints for
``read_console``, ``manage_editor`` and ``set_active_instance``, but the merged
diff set only ``title``. The spec default silently supplied ``destructiveHint:
true`` and nobody noticed for a year.

So this guard requires ``title`` and ``destructiveHint`` to be stated outright,
and pins the set of tools that are safe to auto-approve. ``readOnlyHint`` is not
required: omitting it means *false*, which is the safe direction and is already
correct for every tool that leaves it unset.
"""
from pathlib import Path

import pytest

import services.tools as tools_package
from services.registry import get_registered_tools
from utils.module_discovery import discover_modules


# Tools a client may run without prompting. Two kinds live here:
#   - read-only: observe Unity, change nothing.
#   - non-destructive: change only ephemeral or session-local state.
# Adding a name here asserts that an agent may call it unattended. Do not add a
# tool that writes to the project, and do not set readOnlyHint on a tool whose
# body calls preflight(refresh_if_dirty=True) -- that can trigger a domain reload.
READ_ONLY = {
    "debug_request_context",
    "find_in_file",
    "get_sha",
    "get_test_job",
    "manage_script_capabilities",
    "unity_docs",
    "unity_reflect",
    "validate_script",
}

NON_DESTRUCTIVE = {
    # preflight(refresh_if_dirty=True) at find_gameobjects.py can refresh assets,
    # so it is not read-only -- but it never destroys anything.
    "find_gameobjects",
    # 'clear' empties the ephemeral Editor console buffer; Unity still mirrors
    # every entry to the Editor log file on disk.
    "read_console",
    # Session-local routing only.
    "set_active_instance",
    # Toggles which tools are visible to this session.
    "manage_tools",
    # Reads counters / starts a profiler session; writes no project asset.
    "manage_profiler",
    # Generate into a staging area; the import step is a separate tool.
    "generate_audio",
    "generate_image",
    "generate_model",
    "import_model",
    "import_model_file",
}

AUTO_APPROVABLE = READ_ONLY | NON_DESTRUCTIVE


def _hint(annotations, field: str):
    """Read one hint, treating 'not stated' as None.

    The real ToolAnnotations is a pydantic model with every field defaulting to
    None, but tests/integration/conftest.py substitutes a stub that only sets
    the kwargs actually passed. getattr with a default reads the same answer
    from either, so this guard means the same thing whatever ran before it.
    """
    return getattr(annotations, field, None)


@pytest.fixture(scope="module")
def tools() -> dict:
    # Import every tool module so its @mcp_for_unity_tool decorator runs. Going
    # through discover_modules rather than register_all_tools keeps this off
    # FastMCP, which tests/integration/conftest.py replaces with a stub for the
    # whole session.
    list(discover_modules(Path(tools_package.__file__).parent, tools_package.__package__))
    registered = get_registered_tools()

    # Keying by name would silently drop a duplicate registration, hiding both the
    # registry bug and the annotations of whichever entry lost. Fail on it instead.
    names = [t["name"] for t in registered]
    duplicates = sorted({n for n in names if names.count(n) > 1})
    assert not duplicates, f"Duplicate tool registrations: {duplicates}"

    return {t["name"]: t for t in registered}


def test_every_tool_declares_its_hints(tools):
    missing = []
    for name, tool in sorted(tools.items()):
        annotations = tool["kwargs"].get("annotations")
        if annotations is None:
            missing.append(f"{name}: no annotations= at all")
            continue
        if not _hint(annotations, "title"):
            missing.append(f"{name}: no title")
        if _hint(annotations, "destructiveHint") is None:
            missing.append(f"{name}: destructiveHint not stated (defaults to True)")
    assert not missing, (
        "Every tool must state title and destructiveHint explicitly:\n  "
        + "\n  ".join(missing)
    )


def test_auto_approvable_tools_are_not_gated(tools):
    """A tool in AUTO_APPROVABLE must actually serialize as auto-approvable."""
    gated = []
    for name in sorted(AUTO_APPROVABLE):
        assert name in tools, f"{name} is in AUTO_APPROVABLE but is not a registered tool"
        annotations = tools[name]["kwargs"]["annotations"]
        read_only = _hint(annotations, "readOnlyHint")
        destructive = _hint(annotations, "destructiveHint")
        if not read_only and destructive is not False:
            gated.append(f"{name}: readOnlyHint={read_only} destructiveHint={destructive}")
    assert not gated, (
        "These tools are listed as safe to auto-approve but a spec-compliant "
        "client would still prompt for them:\n  " + "\n  ".join(gated)
    )


def test_read_only_set_is_exact(tools):
    """readOnlyHint=True is a promise the tool cannot mutate anything. Pin it."""
    actual = {
        name
        for name, tool in tools.items()
        if _hint(tool["kwargs"]["annotations"], "readOnlyHint") is True
    }
    assert actual == READ_ONLY, (
        "The read-only tool set changed. Newly read-only: "
        f"{sorted(actual - READ_ONLY)}; no longer read-only: {sorted(READ_ONLY - actual)}. "
        "Update READ_ONLY only after confirming the tool truly mutates nothing -- "
        "including via preflight(refresh_if_dirty=True), which can trigger a domain reload."
    )


def test_mutating_tools_stay_gated(tools):
    """Anything outside AUTO_APPROVABLE must keep prompting."""
    ungated = []
    for name, tool in sorted(tools.items()):
        if name in AUTO_APPROVABLE:
            continue
        annotations = tool["kwargs"]["annotations"]
        if _hint(annotations, "readOnlyHint") or _hint(annotations, "destructiveHint") is False:
            ungated.append(name)
    assert not ungated, (
        "These tools write to the Unity project but are marked auto-approvable: "
        f"{ungated}. Either they belong in AUTO_APPROVABLE with a comment saying why, "
        "or the annotation is wrong."
    )
