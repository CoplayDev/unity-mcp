"""Addressables contracts across MCP, resources and CLI."""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from click.testing import CliRunner

from cli.commands.addressables import addressables
from cli.utils.config import CLIConfig
from services.tools.manage_addressables import manage_addressables


@pytest.mark.asyncio
@pytest.mark.parametrize("action,params", [
    ("ping", {}), ("get_settings", {}),
    ("group_list", {"page_size": 2, "cursor": "snapshot:2"}),
    ("entry_list", {"group": "Characters"}),
    ("entry_find", {"guid": "a" * 32}),
    ("entry_find", {"path": "Assets/Hero.prefab"}),
    ("label_list", {}), ("profile_list", {}),
    ("profile_get", {"profile_id": "profile-id"}),
    ("group_create", {"name": "Actors"}),
    ("group_create", {"name": "Actors", "static_content": False}),
    ("group_remove", {"group": "Actors", "force": True}),
    ("entry_add", {"path": "Assets/a.asset", "group": "Actors"}),
    ("entry_remove", {"guid": "a" * 32}),
    ("entry_move", {"path": "Assets/a.asset", "group": "World"}),
    ("entry_set_address", {"guid": "a" * 32, "address": "hero"}),
    ("label_add", {"name": "actors"}),
    ("label_remove", {"name": "actors", "force": False}),
    ("label_set", {"guid": "a" * 32, "labels": []}),
    ("profile_set", {"profile_id": "profile-id", "name": "Remote.LoadPath", "value": ""}),
    ("profile_set_active", {"profile_id": "profile-id"}),
    ("set_settings", {"initialize": True, "settings": {"unique_bundle_ids": False}}),
    ("build_content", {}), ("build_clean", {}),
    ("build_update", {"content_state_path": "Assets/Builds/state.bin"}),
    ("build_status", {"job_id": "job-id"}),
])
async def test_tool_routes_parameters_to_selected_instance(action, params):
    response = {"success": True, "data": {"items": [{"name": "Hero"}]}}
    with patch("services.tools.manage_addressables.get_unity_instance_from_context", AsyncMock(return_value="sandbox")), \
         patch("services.tools.manage_addressables.send_with_unity_instance", AsyncMock(return_value=response)) as send:
        result = await manage_addressables(MagicMock(), action=action, **params)
    assert result == response
    assert send.call_args.args[1:] == ("sandbox", "manage_addressables", {"action": action, **params})


@pytest.mark.asyncio
@pytest.mark.parametrize("params", [{}, {"guid": "a" * 32, "path": "Assets/a.asset"}])
async def test_lookup_rejects_missing_or_conflicting_identity_before_dispatch(params):
    with patch("services.tools.manage_addressables.send_with_unity_instance", AsyncMock()) as send:
        result = await manage_addressables(MagicMock(), action="entry_find", **params)
    assert result["success"] is False
    assert "exactly one" in result["message"]
    send.assert_not_called()


@pytest.mark.asyncio
@pytest.mark.parametrize("page_size", [0, -1, 201, True, 2**64, 2.7])
async def test_invalid_page_size_never_reaches_unity(page_size):
    with patch("services.tools.manage_addressables.send_with_unity_instance", AsyncMock()) as send:
        result = await manage_addressables(MagicMock(), action="group_list", page_size=page_size)
    assert result["success"] is False
    assert "page_size" in result["message"]
    send.assert_not_called()


@pytest.mark.asyncio
async def test_non_dictionary_bridge_reply_is_reported_as_failure():
    with patch("services.tools.manage_addressables.get_unity_instance_from_context", AsyncMock(return_value="sandbox")), \
         patch("services.tools.manage_addressables.send_with_unity_instance", AsyncMock(return_value="unavailable")):
        result = await manage_addressables(MagicMock(), action="ping")
    assert result == {"success": False, "message": "unavailable"}


@pytest.mark.parametrize("args,expected", [
    (["ping"], {"action": "ping"}),
    (["settings"], {"action": "get_settings"}),
    (["groups", "--page-size", "2", "--cursor", "abc:2"], {"action": "group_list", "page_size": 2, "cursor": "abc:2"}),
    (["entries", "--group", "Actors"], {"action": "entry_list", "group": "Actors"}),
    (["find", "--path", "Assets/a.asset"], {"action": "entry_find", "path": "Assets/a.asset"}),
    (["find", "--guid", "a" * 32], {"action": "entry_find", "guid": "a" * 32}),
    (["labels"], {"action": "label_list"}),
    (["profiles"], {"action": "profile_list"}),
    (["profile", "--profile-id", "id"], {"action": "profile_get", "profile_id": "id"}),
    (["group-create", "Actors"], {"action": "group_create", "name": "Actors"}),
    (["group-create", "Actors", "--dynamic-content"], {"action": "group_create", "name": "Actors", "static_content": False}),
    (["group-create", "Actors", "--static-content"], {"action": "group_create", "name": "Actors", "static_content": True}),
    (["group-remove", "Actors", "--force"], {"action": "group_remove", "group": "Actors", "force": True}),
    (["entry-add", "Actors", "--path", "Assets/a.asset"], {"action": "entry_add", "group": "Actors", "path": "Assets/a.asset"}),
    (["entry-remove", "--guid", "a" * 32], {"action": "entry_remove", "guid": "a" * 32}),
    (["entry-move", "World", "--path", "Assets/a.asset"], {"action": "entry_move", "group": "World", "path": "Assets/a.asset"}),
    (["entry-address", "hero", "--path", "Assets/a.asset"], {"action": "entry_set_address", "address": "hero", "path": "Assets/a.asset"}),
    (["label-add", "actors"], {"action": "label_add", "name": "actors"}),
    (["label-remove", "actors"], {"action": "label_remove", "name": "actors", "force": False}),
    (["label-set", "--path", "Assets/a.asset", "--labels", "[]"], {"action": "label_set", "path": "Assets/a.asset", "labels": []}),
    (["profile-set", "id", "Remote.LoadPath", ""], {"action": "profile_set", "profile_id": "id", "name": "Remote.LoadPath", "value": ""}),
    (["profile-activate", "id"], {"action": "profile_set_active", "profile_id": "id"}),
    (["settings-set", "--initialize", "--values", '{"unique_bundle_ids":false}'], {"action": "set_settings", "initialize": True, "settings": {"unique_bundle_ids": False}}),
    (["build-content"], {"action": "build_content"}),
    (["build-clean"], {"action": "build_clean"}),
    (["build-status", "--job-id", "id"], {"action": "build_status", "job_id": "id"}),
])
def test_cli_dispatches_only_explicit_options(args, expected):
    config = CLIConfig(host="127.0.0.1", port=8081, timeout=30, format="json", unity_instance="sandbox")
    with patch("cli.commands.addressables.get_config", return_value=config), \
         patch("cli.commands.addressables.run_command", return_value={"success": True}) as run:
        result = CliRunner().invoke(addressables, args)
    assert result.exit_code == 0, result.output
    run.assert_called_once_with("manage_addressables", expected, config)


@pytest.mark.parametrize("args", [["groups", "--page-size", "201"], ["find"], ["find", "--path", "a", "--guid", "b"],
    ["entry-add", "Actors"], ["label-set", "--path", "Assets/a.asset", "--labels", "{}"],
    ["settings-set", "--values", "[]"], ["settings-set", "--values", "not-json"]])
def test_cli_invalid_arguments_do_not_dispatch(args):
    with patch("cli.commands.addressables.run_command") as run:
        result = CliRunner().invoke(addressables, args)
    assert result.exit_code != 0
    run.assert_not_called()


@pytest.mark.asyncio
@pytest.mark.parametrize("resource,action", [
    ("get_addressables_settings", "get_settings"), ("get_addressables_groups", "group_list"),
])
async def test_resources_share_query_implementation_and_normalize_transport(resource, action):
    from services.resources import addressables as resources
    response = {"status": "success", "result": {"success": True, "data": {"total": 3}}}
    ctx = MagicMock()
    with patch.object(resources, "manage_addressables", AsyncMock(return_value=response)) as query:
        result = await getattr(resources, resource)(ctx)
    query.assert_awaited_once_with(ctx, action=action)
    assert result.success is True
    assert result.data == {"total": 3}


def test_cli_build_update_resolves_client_path_before_dispatch(tmp_path, monkeypatch):
    state = tmp_path / "previous.bin"
    state.write_bytes(b"fixture")
    monkeypatch.chdir(tmp_path)
    config = CLIConfig(host="127.0.0.1", port=8081, timeout=30, format="json", unity_instance="sandbox")
    with patch("cli.commands.addressables.get_config", return_value=config), \
         patch("cli.commands.addressables.run_command", return_value={"success": True}) as run:
        result = CliRunner().invoke(addressables, ["build-update", "previous.bin"])
    assert result.exit_code == 0, result.output
    run.assert_called_once_with("manage_addressables", {"action": "build_update", "content_state_path": str(state)}, config)


def test_cli_root_discovers_addressables():
    from cli.main import cli
    result = CliRunner().invoke(cli, ["addressables", "--help"])
    assert result.exit_code == 0
    assert "profiles" in result.output


@pytest.mark.asyncio
@pytest.mark.parametrize("action", ["entry_move", "label_add", "set_settings", "build_status"])
async def test_unity_failure_is_returned_without_becoming_success(action):
    response = {"success": False, "error": "ADDRESSABLES_BUILD_FAILED", "data": {"state": "failed", "error": "native failure"}}
    with patch("services.tools.manage_addressables.get_unity_instance_from_context", AsyncMock(return_value="sandbox")), \
         patch("services.tools.manage_addressables.send_with_unity_instance", AsyncMock(return_value=response)) as send:
        result = await manage_addressables(MagicMock(), action=action)
    assert result is response
    assert result["success"] is False
    send.assert_awaited_once()


@pytest.mark.asyncio
async def test_pending_job_identity_and_poll_metadata_are_preserved():
    response = {"success": True, "status": "pending", "retry_after": 2, "data": {"job_id": "queued-job", "state": "queued"}}
    with patch("services.tools.manage_addressables.get_unity_instance_from_context", AsyncMock(return_value="sandbox")), \
         patch("services.tools.manage_addressables.send_with_unity_instance", AsyncMock(return_value=response)):
        result = await manage_addressables(MagicMock(), action="build_content")
    assert result is response


@pytest.mark.asyncio
async def test_transport_timeout_is_not_masked_or_reissued_by_tool():
    with patch("services.tools.manage_addressables.get_unity_instance_from_context", AsyncMock(return_value="sandbox")), \
         patch("services.tools.manage_addressables.send_with_unity_instance", AsyncMock(side_effect=TimeoutError("build may still be running"))) as send:
        with pytest.raises(TimeoutError, match="may still be running"):
            await manage_addressables(MagicMock(), action="build_content")
    send.assert_awaited_once()


@pytest.mark.asyncio
@pytest.mark.parametrize("resource", ["get_addressables_settings", "get_addressables_groups"])
async def test_resources_keep_missing_package_as_failure(resource):
    from services.resources import addressables as resources
    response = {"success": False, "error": "ADDRESSABLES_NOT_INSTALLED", "data": {"package": "com.unity.addressables"}}
    with patch.object(resources, "manage_addressables", AsyncMock(return_value=response)):
        result = await getattr(resources, resource)(MagicMock())
    assert result.success is False
    assert "ADDRESSABLES_NOT_INSTALLED" in result.error
