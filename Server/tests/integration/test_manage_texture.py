"""Integration tests for manage_texture tool."""

import pytest
import asyncio
from .test_helpers import DummyContext
import services.tools.manage_texture as manage_texture_mod

def run_async(coro):
    """Simple wrapper to run a coroutine synchronously."""
    loop = asyncio.new_event_loop()
    try:
        asyncio.set_event_loop(loop)
        return loop.run_until_complete(coro)
    finally:
        loop.close()
        asyncio.set_event_loop(None)

async def noop_preflight(*args, **kwargs):
    return None

class TestManageTextureIntegration:
    """Integration tests for texture management tool logic."""

    def test_create_texture_with_color_array(self, monkeypatch):
        """Test creating a texture with RGB color array (0-255)."""
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["cmd"] = cmd
            captured["params"] = params
            return {"success": True, "message": "Created texture"}

        monkeypatch.setattr(manage_texture_mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(manage_texture_mod, "preflight", noop_preflight)

        resp = run_async(manage_texture_mod.manage_texture(
            ctx=DummyContext(),
            action="create",
            path="Assets/TestTextures/Red.png",
            width=64,
            height=64,
            fill_color=[255, 0, 0, 255]
        ))

        assert resp["success"] is True
        assert captured["params"]["fillColor"] == [255, 0, 0, 255]

    def test_create_texture_with_normalized_color(self, monkeypatch):
        """Test creating a texture with normalized color (0.0-1.0)."""
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Created texture"}

        monkeypatch.setattr(manage_texture_mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(manage_texture_mod, "preflight", noop_preflight)

        resp = run_async(manage_texture_mod.manage_texture(
            ctx=DummyContext(),
            action="create",
            path="Assets/TestTextures/Blue.png",
            fill_color=[0.0, 0.0, 1.0, 1.0]
        ))

        assert resp["success"] is True
        # Should be normalized to 0-255
        assert captured["params"]["fillColor"] == [0, 0, 255, 255]

    def test_create_sprite_with_pattern(self, monkeypatch):
        """Test creating a sprite with checkerboard pattern."""
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Created sprite", "data": {"asSprite": True}}

        monkeypatch.setattr(manage_texture_mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(manage_texture_mod, "preflight", noop_preflight)

        resp = run_async(manage_texture_mod.manage_texture(
            ctx=DummyContext(),
            action="create_sprite",
            path="Assets/TestTextures/Checkerboard.png",
            pattern="checkerboard",
            as_sprite={
                "pixelsPerUnit": 100.0,
                "pivot": [0.5, 0.5]
            }
        ))

        assert resp["success"] is True
        assert captured["params"]["action"] == "create_sprite"
        assert captured["params"]["pattern"] == "checkerboard"
        assert captured["params"]["spriteSettings"]["pixelsPerUnit"] == 100.0

    def test_create_texture_with_import_settings(self, monkeypatch):
        """Test creating a texture with import settings (conversion of snake_case to camelCase)."""
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Created texture"}

        monkeypatch.setattr(manage_texture_mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(manage_texture_mod, "preflight", noop_preflight)

        resp = run_async(manage_texture_mod.manage_texture(
            ctx=DummyContext(),
            action="create",
            path="Assets/TestTextures/SpriteTexture.png",
            import_settings={
                "texture_type": "sprite",
                "sprite_pixels_per_unit": 100,
                "filter_mode": "point",
                "wrap_mode": "clamp"
            }
        ))

        assert resp["success"] is True
        settings = captured["params"]["importSettings"]
        assert settings["textureType"] == "Sprite"
        assert settings["spritePixelsPerUnit"] == 100
        assert settings["filterMode"] == "Point"
        assert settings["wrapMode"] == "Clamp"

    def test_texture_modify_params(self, monkeypatch):
        """Test texture modify parameter conversion."""
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Modified texture"}

        monkeypatch.setattr(manage_texture_mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(manage_texture_mod, "preflight", noop_preflight)

        resp = run_async(manage_texture_mod.manage_texture(
            ctx=DummyContext(),
            action="modify",
            path="Assets/Textures/Test.png",
            set_pixels={
                "x": 0,
                "y": 0,
                "width": 10,
                "height": 10,
                "color": [255, 0, 0, 255]
            }
        ))

        assert resp["success"] is True
        assert captured["params"]["setPixels"]["color"] == [255, 0, 0, 255]

    def test_texture_modify_pixels_array(self, monkeypatch):
        """Test texture modify pixel array normalization."""
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Modified texture"}

        monkeypatch.setattr(manage_texture_mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(manage_texture_mod, "preflight", noop_preflight)

        resp = run_async(manage_texture_mod.manage_texture(
            ctx=DummyContext(),
            action="modify",
            path="Assets/Textures/Test.png",
            set_pixels={
                "x": 0,
                "y": 0,
                "width": 2,
                "height": 2,
                "pixels": [
                    [1.0, 0.0, 0.0, 1.0],
                    [0.0, 1.0, 0.0, 1.0],
                    [0.0, 0.0, 1.0, 1.0],
                    [0.5, 0.5, 0.5, 1.0],
                ]
            }
        ))

        assert resp["success"] is True
        assert captured["params"]["setPixels"]["pixels"] == [
            [255, 0, 0, 255],
            [0, 255, 0, 255],
            [0, 0, 255, 255],
            [128, 128, 128, 255],
        ]

    def test_texture_modify_pixels_invalid_length(self, monkeypatch):
        """Test error handling for invalid pixel array length."""
        async def fake_send(*args, **kwargs):
            return {"success": True}

        monkeypatch.setattr(manage_texture_mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(manage_texture_mod, "preflight", noop_preflight)

        resp = run_async(manage_texture_mod.manage_texture(
            ctx=DummyContext(),
            action="modify",
            path="Assets/Textures/Test.png",
            set_pixels={
                "x": 0,
                "y": 0,
                "width": 2,
                "height": 2,
                "pixels": [
                    [255, 0, 0, 255],
                    [0, 255, 0, 255],
                    [0, 0, 255, 255],
                ]
            }
        ))

        assert resp["success"] is False
        assert "pixels array must have 4 entries" in resp["message"]

    def test_texture_modify_invalid_set_pixels_type(self, monkeypatch):
        """Test error handling for invalid set_pixels input type."""
        async def fake_send(*args, **kwargs):
            return {"success": True}

        monkeypatch.setattr(manage_texture_mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(manage_texture_mod, "preflight", noop_preflight)

        resp = run_async(manage_texture_mod.manage_texture(
            ctx=DummyContext(),
            action="modify",
            path="Assets/Textures/Test.png",
            set_pixels=[]
        ))

        assert resp["success"] is False
        assert resp["message"] == "set_pixels must be a JSON object"

    def test_texture_delete_params(self, monkeypatch):
        """Test texture delete parameter pass-through."""
        captured = {}

        async def fake_send(func, instance, cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Deleted texture"}

        monkeypatch.setattr(manage_texture_mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(manage_texture_mod, "preflight", noop_preflight)

        resp = run_async(manage_texture_mod.manage_texture(
            ctx=DummyContext(),
            action="delete",
            path="Assets/Textures/Old.png"
        ))

        assert resp["success"] is True
        assert captured["params"]["path"] == "Assets/Textures/Old.png"

    def test_invalid_dimensions(self, monkeypatch):
        """Test error handling for invalid dimensions."""
        async def fake_send(func, instance, cmd, params, **kwargs):
            w = params.get("width", 0)
            if w > 4096:
                return {"success": False, "message": "Invalid dimensions: 5000x64. Must be 1-4096."}
            return {"success": True}

        monkeypatch.setattr(manage_texture_mod, "send_with_unity_instance", fake_send)
        monkeypatch.setattr(manage_texture_mod, "preflight", noop_preflight)

        resp = run_async(manage_texture_mod.manage_texture(
            ctx=DummyContext(),
            action="create",
            path="Assets/Invalid.png",
            width=0,
            height=64  # Non-positive dimension
        ))

        assert resp["success"] is False
        assert "positive" in resp["message"].lower()
