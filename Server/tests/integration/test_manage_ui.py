"""Integration tests for manage_ui tool."""

import asyncio
import base64

import pytest

from .test_helpers import DummyContext
import services.tools.manage_ui as manage_ui_mod


def run_async(coro):
    """Simple wrapper to run a coroutine synchronously."""
    loop = asyncio.new_event_loop()
    try:
        asyncio.set_event_loop(loop)
        return loop.run_until_complete(coro)
    finally:
        loop.close()
        asyncio.set_event_loop(None)


SAMPLE_UXML = """<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:Label text="Hello World" />
</ui:UXML>"""

SAMPLE_USS = """.label {
    font-size: 24px;
    color: white;
}"""


class TestManageUIPathValidation:
    """Tests for path validation logic."""

    def test_create_rejects_path_outside_assets(self, monkeypatch):
        captured = {}

        async def fake_send(ctx, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True}

        monkeypatch.setattr(manage_ui_mod, "send_mutation", fake_send)

        resp = run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="create",
            path="NotAssets/UI/Test.uxml",
            contents=SAMPLE_UXML,
        ))

        assert resp["success"] is False
        assert "Assets/" in resp["message"]

    def test_create_rejects_traversal(self, monkeypatch):
        async def fake_send(ctx, instance, cmd, params, **kwargs):
            return {"success": True}

        monkeypatch.setattr(manage_ui_mod, "send_mutation", fake_send)

        resp = run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="create",
            path="Assets/../etc/passwd.uxml",
            contents=SAMPLE_UXML,
        ))

        assert resp["success"] is False
        # Path normalization resolves ".." so it either fails traversal or Assets/ check
        assert "traversal" in resp["message"] or "Assets/" in resp["message"]

    def test_create_rejects_invalid_extension(self, monkeypatch):
        async def fake_send(ctx, instance, cmd, params, **kwargs):
            return {"success": True}

        monkeypatch.setattr(manage_ui_mod, "send_mutation", fake_send)

        resp = run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="create",
            path="Assets/UI/Test.cs",
            contents="some content",
        ))

        assert resp["success"] is False
        assert ".uxml or .uss" in resp["message"]

    def test_create_accepts_uxml_extension(self, monkeypatch):
        captured = {}

        async def fake_send(ctx, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Created"}

        monkeypatch.setattr(manage_ui_mod, "send_mutation", fake_send)

        resp = run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="create",
            path="Assets/UI/Menu.uxml",
            contents=SAMPLE_UXML,
        ))

        assert resp["success"] is True
        assert captured["params"]["path"] == "Assets/UI/Menu.uxml"

    def test_create_accepts_uss_extension(self, monkeypatch):
        captured = {}

        async def fake_send(ctx, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Created"}

        monkeypatch.setattr(manage_ui_mod, "send_mutation", fake_send)

        resp = run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="create",
            path="Assets/UI/Styles.uss",
            contents=SAMPLE_USS,
        ))

        assert resp["success"] is True
        assert captured["params"]["path"] == "Assets/UI/Styles.uss"


class TestManageUIContentsEncoding:
    """Tests for base64 content encoding."""

    def test_create_encodes_contents_as_base64(self, monkeypatch):
        captured = {}

        async def fake_send(ctx, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Created"}

        monkeypatch.setattr(manage_ui_mod, "send_mutation", fake_send)

        run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="create",
            path="Assets/UI/Test.uxml",
            contents=SAMPLE_UXML,
        ))

        params = captured["params"]
        assert params["contentsEncoded"] is True
        decoded = base64.b64decode(params["encodedContents"]).decode("utf-8")
        assert decoded == SAMPLE_UXML
        # Raw contents should NOT be in params (only encoded)
        assert "contents" not in params

    def test_update_encodes_contents_as_base64(self, monkeypatch):
        captured = {}

        async def fake_send(ctx, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Updated"}

        monkeypatch.setattr(manage_ui_mod, "send_mutation", fake_send)

        run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="update",
            path="Assets/UI/Test.uss",
            contents=SAMPLE_USS,
        ))

        params = captured["params"]
        assert params["contentsEncoded"] is True
        decoded = base64.b64decode(params["encodedContents"]).decode("utf-8")
        assert decoded == SAMPLE_USS


class TestManageUIActionRouting:
    """Tests for action-based parameter routing."""

    def test_read_uses_non_mutation_path(self, monkeypatch):
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["cmd"] = cmd
            captured["params"] = params
            return {"success": True, "data": {"contents": "test"}}

        monkeypatch.setattr(manage_ui_mod, "send_with_unity_instance", fake_send)

        resp = run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="read",
            path="Assets/UI/Test.uxml",
        ))

        assert captured["cmd"] == "manage_ui"
        assert captured["params"]["action"] == "read"

    def test_create_uses_mutation_path(self, monkeypatch):
        captured = {}

        async def fake_send(ctx, instance, cmd, params, **kwargs):
            captured["cmd"] = cmd
            return {"success": True, "message": "Created"}

        monkeypatch.setattr(manage_ui_mod, "send_mutation", fake_send)

        run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="create",
            path="Assets/UI/Test.uxml",
            contents=SAMPLE_UXML,
        ))

        assert captured["cmd"] == "manage_ui"

    def test_attach_ui_document_params(self, monkeypatch):
        captured = {}

        async def fake_send(ctx, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Attached"}

        monkeypatch.setattr(manage_ui_mod, "send_mutation", fake_send)

        run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="attach_ui_document",
            target="MyCanvas",
            source_asset="Assets/UI/Menu.uxml",
            panel_settings="Assets/UI/PanelSettings.asset",
            sort_order=5,
        ))

        params = captured["params"]
        assert params["action"] == "attach_ui_document"
        assert params["target"] == "MyCanvas"
        assert params["sourceAsset"] == "Assets/UI/Menu.uxml"
        assert params["panelSettings"] == "Assets/UI/PanelSettings.asset"
        assert params["sortOrder"] == 5

    def test_create_panel_settings_params(self, monkeypatch):
        captured = {}

        async def fake_send(ctx, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Created"}

        monkeypatch.setattr(manage_ui_mod, "send_mutation", fake_send)

        run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="create_panel_settings",
            path="Assets/UI/MyPanel.asset",
            scale_mode="ScaleWithScreenSize",
            reference_resolution={"width": 1920, "height": 1080},
        ))

        params = captured["params"]
        assert params["action"] == "create_panel_settings"
        assert params["scaleMode"] == "ScaleWithScreenSize"
        assert params["referenceResolution"] == {"width": 1920, "height": 1080}

    def test_get_visual_tree_params(self, monkeypatch):
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "data": {"tree": {}}}

        monkeypatch.setattr(manage_ui_mod, "send_with_unity_instance", fake_send)

        run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="get_visual_tree",
            target="UIRoot",
            max_depth=5,
        ))

        params = captured["params"]
        assert params["action"] == "get_visual_tree"
        assert params["target"] == "UIRoot"
        assert params["maxDepth"] == 5

    def test_ping_uses_non_mutation_path(self, monkeypatch):
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "pong"}

        monkeypatch.setattr(manage_ui_mod, "send_with_unity_instance", fake_send)

        resp = run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="ping",
        ))

        assert resp["success"] is True
        assert captured["params"]["action"] == "ping"


class TestManageUINoneRemoval:
    """Tests that None values are properly excluded from params."""

    def test_none_params_excluded(self, monkeypatch):
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "data": {}}

        monkeypatch.setattr(manage_ui_mod, "send_with_unity_instance", fake_send)

        run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="read",
            path="Assets/UI/Test.uxml",
            # All other params are None
        ))

        params = captured["params"]
        assert "target" not in params
        assert "sourceAsset" not in params
        assert "panelSettings" not in params
        assert "sortOrder" not in params
        assert "scaleMode" not in params
        assert "maxDepth" not in params


class TestManageUIReadResponse:
    """Tests for read response handling."""

    def test_read_decodes_base64_response(self, monkeypatch):
        encoded = base64.b64encode(SAMPLE_UXML.encode("utf-8")).decode("utf-8")

        async def fake_send(func, instance, cmd, params, **kwargs):
            return {
                "success": True,
                "data": {
                    "path": "Assets/UI/Test.uxml",
                    "contents": SAMPLE_UXML,
                    "encodedContents": encoded,
                    "contentsEncoded": True,
                }
            }

        monkeypatch.setattr(manage_ui_mod, "send_with_unity_instance", fake_send)

        resp = run_async(manage_ui_mod.manage_ui(
            ctx=DummyContext(),
            action="read",
            path="Assets/UI/Test.uxml",
        ))

        assert resp["success"] is True
        data = resp["data"]
        assert data["contents"] == SAMPLE_UXML
        assert "encodedContents" not in data
        assert "contentsEncoded" not in data
