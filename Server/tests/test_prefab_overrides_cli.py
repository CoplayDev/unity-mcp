from types import SimpleNamespace

import pytest
from click.testing import CliRunner
from cli.commands import prefab as module


@pytest.fixture
def commands(monkeypatch):
    sent = []
    monkeypatch.setattr(module, "get_config", lambda: SimpleNamespace(format="json"))
    def send(tool, params, config):
        sent.append((tool, params))
        return {"success": True, "data": {}}
    monkeypatch.setattr(module, "run_command", send)
    return CliRunner(), sent


def test_overrides_filters_forward_without_contacting_server(commands):
    runner, sent = commands
    result = runner.invoke(module.prefab, ["overrides", "HUD", "--child-path", "Health",
        "--component", "RectTransform", "--property-prefix", "m_Anchor", "--include-defaults"])
    assert result.exit_code == 0, result.output
    assert sent == [("manage_prefabs", {"action": "list_overrides", "target": "HUD",
        "childPath": "Health", "componentType": "RectTransform", "propertyPrefix": "m_Anchor",
        "includeDefaultOverrides": True})]


@pytest.mark.parametrize("command,action", [("apply-overrides", "apply_overrides"), ("revert-overrides", "revert_overrides")])
def test_property_commands_select_exact_scope(commands, command, action):
    runner, sent = commands
    result = runner.invoke(module.prefab, [command, "Player", "--component", "Rigidbody", "--property-path", "m_Mass"])
    assert result.exit_code == 0, result.output
    assert sent == [("manage_prefabs", {"action": action, "target": "Player",
        "componentType": "Rigidbody", "propertyPath": "m_Mass", "overrideType": "property"})]


def test_structural_apply_accepts_index_zero_and_source_prefab(commands):
    runner, sent = commands
    result = runner.invoke(module.prefab, ["apply-overrides", "Player", "--override-type",
        "added_component", "--override-index", "0", "--prefab-path", "Assets/Player.prefab"])
    assert result.exit_code == 0, result.output
    assert sent[0][1] == {"action": "apply_overrides", "target": "Player",
        "overrideType": "added_component", "overrideIndex": 0, "prefabPath": "Assets/Player.prefab"}


@pytest.mark.parametrize("options", [["--override-index", "-1"], ["--property-prefix", "m_Anchor"]])
def test_invalid_write_options_do_not_send(commands, options):
    runner, sent = commands
    result = runner.invoke(module.prefab, ["revert-overrides", "HUD", *options])
    assert result.exit_code == 2
    assert sent == []
