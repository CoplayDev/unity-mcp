import importlib


def _get_decorator_module():
    # Import the telemetry_decorator module from the MCP for Unity server src
    import sys
    import pathlib
    import types
    # Tests can now import directly from parent package
    # Remove any previously stubbed module to force real import
    sys.modules.pop("core.telemetry_decorator", None)
    # Preload a minimal telemetry stub to satisfy telemetry_decorator imports
    tel = types.ModuleType("core.telemetry")

    class _MilestoneType:
        FIRST_TOOL_USAGE = "first_tool_usage"
        FIRST_SCRIPT_CREATION = "first_script_creation"
        FIRST_SCENE_MODIFICATION = "first_scene_modification"
    tel.MilestoneType = _MilestoneType

    def _noop(*a, **k):
        pass
    tel.record_resource_usage = _noop
    tel.record_tool_usage = _noop
    tel.record_milestone = _noop
    tel.get_package_version = lambda: "0.0.0"
    sys.modules.setdefault("core.telemetry", tel)
    mod = importlib.import_module("core.telemetry_decorator")
    # Drop stub to avoid bleed-through into other tests
    sys.modules.pop("core.telemetry", None)
    # Ensure attributes exist for monkeypatch targets even if not exported
    if not hasattr(mod, "record_tool_usage"):
        def _noop_record_tool_usage(*a, **k):
            pass
        mod.record_tool_usage = _noop_record_tool_usage
    if not hasattr(mod, "record_milestone"):
        def _noop_record_milestone(*a, **k):
            pass
        mod.record_milestone = _noop_record_milestone
    if not hasattr(mod, "_decorator_log_count"):
        mod._decorator_log_count = 0
    return mod


def test_subaction_extracted_from_keyword(monkeypatch):
    td = _get_decorator_module()

    captured = {}

    def fake_record_tool_usage(tool_name, success, duration_ms, error, sub_action=None):
        captured["tool_name"] = tool_name
        captured["success"] = success
        captured["error"] = error
        captured["sub_action"] = sub_action

    # Silence milestones/logging in test
    monkeypatch.setattr(td, "record_tool_usage", fake_record_tool_usage)
    monkeypatch.setattr(td, "record_milestone", lambda *a, **k: None)
    monkeypatch.setattr(td, "_decorator_log_count", 999)

    def dummy_tool(ctx, action: str, name: str = ""):
        return {"success": True, "name": name}

    wrapped = td.telemetry_tool("manage_scene")(dummy_tool)

    resp = wrapped(None, action="get_hierarchy", name="Sample")
    assert resp["success"] is True
    assert captured["tool_name"] == "manage_scene"
    assert captured["success"] is True
    assert captured["error"] is None
    assert captured["sub_action"] == "get_hierarchy"


def test_subaction_extracted_from_positionals(monkeypatch):
    td = _get_decorator_module()

    captured = {}

    def fake_record_tool_usage(tool_name, success, duration_ms, error, sub_action=None):
        captured["tool_name"] = tool_name
        captured["sub_action"] = sub_action

    monkeypatch.setattr(td, "record_tool_usage", fake_record_tool_usage)
    monkeypatch.setattr(td, "record_milestone", lambda *a, **k: None)
    monkeypatch.setattr(td, "_decorator_log_count", 999)

    def dummy_tool(ctx, action: str, name: str = ""):
        return True

    wrapped = td.telemetry_tool("manage_scene")(dummy_tool)

    _ = wrapped(None, "save", "MyScene")
    assert captured["tool_name"] == "manage_scene"
    assert captured["sub_action"] == "save"


def test_subaction_none_when_not_present(monkeypatch):
    td = _get_decorator_module()

    captured = {}

    def fake_record_tool_usage(tool_name, success, duration_ms, error, sub_action=None):
        captured["tool_name"] = tool_name
        captured["sub_action"] = sub_action

    monkeypatch.setattr(td, "record_tool_usage", fake_record_tool_usage)
    monkeypatch.setattr(td, "record_milestone", lambda *a, **k: None)
    monkeypatch.setattr(td, "_decorator_log_count", 999)

    def dummy_tool_without_action(ctx, name: str):
        return 123

    wrapped = td.telemetry_tool("apply_text_edits")(dummy_tool_without_action)
    _ = wrapped(None, name="X")
    assert captured["tool_name"] == "apply_text_edits"
    assert captured["sub_action"] is None
