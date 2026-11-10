"""Prevent an unnamed dirty smoke scene from silently cancelling UTF in batch mode."""
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import local_harness as lh


@pytest.mark.parametrize("argv", [[], ["--reuse"], ["--ci", "--reuse"]])
def test_scene_preparation_does_not_save_user_editors(argv):
    def unexpected(*args, **kwargs):
        pytest.fail("must not save local or reused scenes")

    assert lh.prepare_ci_scene(lh.build_arg_parser().parse_args(argv), "inst@hash", send=unexpected)


def test_ci_saves_unique_scene_through_the_selected_bridge():
    calls = []

    def send(command, params, **kwargs):
        calls.append((command, params, kwargs))
        return {"success": True}

    args = lh.build_arg_parser().parse_args(["--ci"])
    assert lh.prepare_ci_scene(args, "inst@hash", send=send)
    assert lh.prepare_ci_scene(args, "inst@hash", send=send)
    assert calls[0][1]["name"] != calls[1][1]["name"]
    for command, params, kwargs in calls:
        assert command == "manage_scene"
        assert params["action"] == "save"
        assert params["path"] == "Assets/__MCPHarness"
        assert kwargs["instance_id"] == "inst@hash"


@pytest.mark.parametrize("raises", [False, True])
def test_ci_scene_failure_cannot_be_treated_as_ready(raises, capsys):
    def send(*args, **kwargs):
        if raises:
            raise RuntimeError("sensitive transport output")
        return {"success": False, "message": "save failed"}

    assert not lh.prepare_ci_scene(lh.build_arg_parser().parse_args(["--ci"]), "inst@hash", send=send)
    output = capsys.readouterr().out
    assert "tests were not started" in output
    assert "sensitive transport output" not in output


@pytest.mark.parametrize("fail_on_relaunch", [False, True])
def test_setup_failure_preserves_results_before_editor_teardown(tmp_path, monkeypatch, fail_on_relaunch):
    from types import SimpleNamespace
    import xml.etree.ElementTree as ET

    reports = tmp_path / "reports"
    snapshots = []
    launcher = SimpleNamespace(
        resolve_editor=lambda *_: lh.EditorSpec("fake-editor", "2021"),
        launch=lambda *_: lh.Handle(container="owned-editor", log_path="Editor.log"),
        tail_log=lambda *_: "scene setup diagnostics",
        teardown=lambda *_: snapshots.append((reports / "junit-all.xml").exists()),
    )
    monkeypatch.setenv("UNITY_IMAGE", "fake-image")
    monkeypatch.setattr(lh, "make_launcher", lambda *_: launcher)
    monkeypatch.setattr(lh, "wait_for_ready", lambda *_: lh.ReadyInfo(6400, "instance", "status.json"))
    monkeypatch.setattr(lh, "compile_probe", lambda *_: True)
    monkeypatch.setattr(lh.time, "sleep", lambda *_: None)
    monkeypatch.setattr(lh.signal, "signal", lambda *_: None)
    # main sets these in the process; monkeypatch restores their original values.
    monkeypatch.setenv("UNITY_MCP_STATUS_DIR", "test-original-status")
    monkeypatch.setenv("UNITY_MCP_DEFAULT_INSTANCE", "test-original-instance")
    preparations = iter([True, False] if fail_on_relaunch else [False])
    monkeypatch.setattr(lh, "prepare_ci_scene", lambda *_: next(preparations))

    def passed_leg(name):
        return lh.LegOutcome(name, "pass", True, "passed", 0,
                             lh.JUnitSuite(name=name, cases=[lh.JUnitCase(name=f"{name}.passed")]))

    monkeypatch.setattr(lh, "run_smoke_leg", lambda *_a, **_kw: passed_leg("smoke"))
    monkeypatch.setattr(lh, "run_utf_leg", lambda *_a, **_kw: passed_leg("editmode"))
    monkeypatch.setattr(lh, "run_playmode_with_retry", lambda *_a, **kw: kw["relaunch"]())
    result = lh.main(["--ci", "--project-path", str(tmp_path / "project"),
                      "--status-dir", str(tmp_path / "status"), "--reports", str(reports),
                      "--junit", str(reports / "junit-smoke.xml")])

    assert result == 2
    root = ET.parse(reports / "junit-all.xml").getroot()
    assert root.get("failures") == "1"
    assert root.get("tests") == ("3" if fail_on_relaunch else "1")
    assert root.find(".//testcase[@name='setup.scene']/failure") is not None
    assert snapshots[-1], "reports must exist before the failing Editor is removed"
