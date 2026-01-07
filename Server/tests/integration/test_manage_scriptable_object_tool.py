import asyncio

from .test_helpers import DummyContext
import services.tools.manage_scriptable_object as mod


def test_manage_scriptable_object_forwards_create_params(monkeypatch):
    captured = {}

    async def fake_async_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True, "data": {"ok": True}}

    monkeypatch.setattr(mod, "async_send_command_with_retry", fake_async_send)

    ctx = DummyContext()
    ctx.set_state("unity_instance", "UnityMCPTests@dummy")

    result = asyncio.run(
        mod.manage_scriptable_object(
            ctx=ctx,
            action="create",
            type_name="My.Namespace.TestDefinition",
            folder_path="Assets/Temp/Foo",
            asset_name="Bar",
            overwrite="true",
            patches='[{"propertyPath":"displayName","op":"set","value":"Hello"}]',
        )
    )

    assert result["success"] is True
    assert captured["cmd"] == "manage_scriptable_object"
    assert captured["params"]["action"] == "create"
    assert captured["params"]["typeName"] == "My.Namespace.TestDefinition"
    assert captured["params"]["folderPath"] == "Assets/Temp/Foo"
    assert captured["params"]["assetName"] == "Bar"
    assert captured["params"]["overwrite"] is True
    assert isinstance(captured["params"]["patches"], list)
    assert captured["params"]["patches"][0]["propertyPath"] == "displayName"


def test_manage_scriptable_object_forwards_modify_params(monkeypatch):
    captured = {}

    async def fake_async_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True, "data": {"ok": True}}

    monkeypatch.setattr(mod, "async_send_command_with_retry", fake_async_send)

    ctx = DummyContext()
    ctx.set_state("unity_instance", "UnityMCPTests@dummy")

    result = asyncio.run(
        mod.manage_scriptable_object(
            ctx=ctx,
            action="modify",
            target='{"guid":"abc"}',
            patches=[{"propertyPath": "materials.Array.size", "op": "array_resize", "value": 2}],
        )
    )

    assert result["success"] is True
    assert captured["cmd"] == "manage_scriptable_object"
    assert captured["params"]["action"] == "modify"
    assert captured["params"]["target"] == {"guid": "abc"}
    assert captured["params"]["patches"][0]["op"] == "array_resize"


def test_manage_scriptable_object_forwards_dry_run_param(monkeypatch):
    captured = {}

    async def fake_async_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True, "data": {"dryRun": True, "validationResults": []}}

    monkeypatch.setattr(mod, "async_send_command_with_retry", fake_async_send)

    ctx = DummyContext()
    ctx.set_state("unity_instance", "UnityMCPTests@dummy")

    result = asyncio.run(
        mod.manage_scriptable_object(
            ctx=ctx,
            action="modify",
            target='{"guid":"abc123"}',
            patches=[{"propertyPath": "intValue", "op": "set", "value": 42}],
            dry_run=True,
        )
    )

    assert result["success"] is True
    assert captured["cmd"] == "manage_scriptable_object"
    assert captured["params"]["action"] == "modify"
    assert captured["params"]["dryRun"] is True
    assert captured["params"]["target"] == {"guid": "abc123"}


def test_manage_scriptable_object_dry_run_string_coercion(monkeypatch):
    """Test that dry_run accepts string 'true' and coerces to boolean."""
    captured = {}

    async def fake_async_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return {"success": True, "data": {"dryRun": True}}

    monkeypatch.setattr(mod, "async_send_command_with_retry", fake_async_send)

    ctx = DummyContext()
    ctx.set_state("unity_instance", "UnityMCPTests@dummy")

    result = asyncio.run(
        mod.manage_scriptable_object(
            ctx=ctx,
            action="modify",
            target={"guid": "xyz"},
            patches=[],
            dry_run="true",  # String instead of bool
        )
    )

    assert result["success"] is True
    assert captured["params"]["dryRun"] is True



