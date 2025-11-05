"""
Tests for JSON string parameter parsing in manage_asset tool.
"""
import pytest
import json

from .test_helpers import DummyContext
from tools.manage_asset import manage_asset


class TestManageAssetJsonParsing:
    """Test JSON string parameter parsing functionality."""
    
    @pytest.mark.asyncio
    async def test_properties_json_string_parsing(self, monkeypatch):
        """Test that JSON string properties are correctly parsed to dict."""
        # Mock context
        ctx = DummyContext()
        
        # Patch Unity transport
        async def fake_async(cmd, params, **kwargs):
            return {"success": True, "message": "Asset created successfully", "data": {"path": "Assets/Test.mat"}}
        monkeypatch.setattr("tools.manage_asset.async_send_command_with_retry", fake_async)
        
        # Test with JSON string properties
        result = await manage_asset(
            ctx=ctx,
            action="create",
            path="Assets/Test.mat",
            asset_type="Material",
            properties='{"shader": "Universal Render Pipeline/Lit", "color": [0, 0, 1, 1]}'
        )
        
        # Verify JSON parsing was logged
        assert "manage_asset: coerced properties from JSON string to dict" in ctx.log_info
        
        # Verify the result
        assert result["success"] is True
        assert "Asset created successfully" in result["message"]
    
    @pytest.mark.asyncio
    async def test_properties_invalid_json_string(self, monkeypatch):
        """Test handling of invalid JSON string properties."""
        ctx = DummyContext()
        
        async def fake_async(cmd, params, **kwargs):
            return {"success": True, "message": "Asset created successfully"}
        monkeypatch.setattr("tools.manage_asset.async_send_command_with_retry", fake_async)
        
        # Test with invalid JSON string
        result = await manage_asset(
            ctx=ctx,
            action="create",
            path="Assets/Test.mat",
            asset_type="Material",
            properties='{"invalid": json, "missing": quotes}'
        )
        
        # Verify behavior: no coercion log for invalid JSON; warning may be emitted by some runtimes
        assert not any("coerced properties" in msg for msg in ctx.log_info)
        assert result.get("success") is True
    
    @pytest.mark.asyncio
    async def test_properties_dict_unchanged(self, monkeypatch):
        """Test that dict properties are passed through unchanged."""
        ctx = DummyContext()
        
        async def fake_async(cmd, params, **kwargs):
            return {"success": True, "message": "Asset created successfully"}
        monkeypatch.setattr("tools.manage_asset.async_send_command_with_retry", fake_async)
        
        # Test with dict properties
        properties_dict = {"shader": "Universal Render Pipeline/Lit", "color": [0, 0, 1, 1]}
        
        result = await manage_asset(
            ctx=ctx,
            action="create",
            path="Assets/Test.mat",
            asset_type="Material",
            properties=properties_dict
        )
        
        # Verify no JSON parsing was attempted (allow initial Processing log)
        assert not any("coerced properties" in msg for msg in ctx.log_info)
        assert result["success"] is True
    
    @pytest.mark.asyncio
    async def test_properties_none_handling(self, monkeypatch):
        """Test that None properties are handled correctly."""
        ctx = DummyContext()

        async def fake_async(cmd, params, **kwargs):
            return {"success": True, "message": "Asset created successfully"}
        monkeypatch.setattr("tools.manage_asset.async_send_command_with_retry", fake_async)
        
        # Test with None properties
        result = await manage_asset(
            ctx=ctx,
            action="create",
            path="Assets/Test.mat",
            asset_type="Material",
            properties=None
        )
        
        # Verify no JSON parsing was attempted (allow initial Processing log)
        assert not any("coerced properties" in msg for msg in ctx.log_info)
        assert result["success"] is True


class TestManageGameObjectJsonParsing:
    """Test JSON string parameter parsing for manage_gameobject tool."""
    
    @pytest.mark.asyncio
    async def test_component_properties_json_string_parsing(self, monkeypatch):
        """Test that JSON string component_properties are correctly parsed."""
        from tools.manage_gameobject import manage_gameobject
        
        ctx = DummyContext()

        def fake_send(cmd, params, **kwargs):
            return {"success": True, "message": "GameObject created successfully"}
        monkeypatch.setattr("tools.manage_gameobject.send_command_with_retry", fake_send)
        
        # Test with JSON string component_properties
        result = manage_gameobject(
            ctx=ctx,
            action="create",
            name="TestObject",
            component_properties='{"MeshRenderer": {"material": "Assets/Materials/BlueMaterial.mat"}}'
        )
        
        # Verify JSON parsing was logged
        assert "manage_gameobject: coerced component_properties from JSON string to dict" in ctx.log_info
        
        # Verify the result
        assert result["success"] is True
