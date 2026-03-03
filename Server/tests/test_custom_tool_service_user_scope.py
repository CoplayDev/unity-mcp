from unittest.mock import AsyncMock, Mock, patch

import pytest

from core.config import config
from models.models import MCPResponse, ToolDefinitionModel
from services.custom_tool_service import CustomToolService
from services.resources.custom_tools import get_custom_tools
from services.tools.execute_custom_tool import execute_custom_tool


class _DummyMcp:
    def custom_route(self, _path, methods=None):  # noqa: ARG002
        def _decorator(fn):
            return fn

        return _decorator


@pytest.mark.asyncio
async def test_list_registered_tools_threads_user_id_to_plugin_hub():
    service = CustomToolService(_DummyMcp())

    with patch("services.custom_tool_service.PluginHub.get_tools_for_project", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = []
        await service.list_registered_tools("project-hash", user_id="user-1")

    mock_get.assert_awaited_once_with("project-hash", user_id="user-1")


@pytest.mark.asyncio
async def test_get_tool_definition_threads_user_id_to_plugin_hub():
    service = CustomToolService(_DummyMcp())

    with patch("services.custom_tool_service.PluginHub.get_tool_definition", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = None
        await service.get_tool_definition("project-hash", "my_tool", user_id="user-1")

    mock_get.assert_awaited_once_with("project-hash", "my_tool", user_id="user-1")


@pytest.mark.asyncio
async def test_execute_tool_threads_user_id_to_definition_lookup_and_transport():
    service = CustomToolService(_DummyMcp())
    definition = ToolDefinitionModel(name="my_tool", description="My tool", requires_polling=False)

    with patch.object(service, "get_tool_definition", new_callable=AsyncMock) as mock_get_definition:
        with patch("services.custom_tool_service.send_with_unity_instance", new_callable=AsyncMock) as mock_send:
            mock_get_definition.return_value = definition
            mock_send.return_value = {"success": True, "message": "ok"}

            await service.execute_tool(
                "project-hash",
                "my_tool",
                "Project@project-hash",
                {"x": 1},
                user_id="user-1",
            )

    mock_get_definition.assert_awaited_once_with("project-hash", "my_tool", user_id="user-1")
    mock_send.assert_awaited_once()
    assert mock_send.call_args.kwargs["user_id"] == "user-1"


@pytest.mark.asyncio
async def test_execute_custom_tool_threads_user_id_from_context(monkeypatch):
    monkeypatch.setattr(config, "http_remote_hosted", True)

    ctx = Mock()
    state = {"unity_instance": "Project@project-hash", "user_id": "user-1"}
    ctx.get_state = Mock(side_effect=lambda key: state.get(key))

    service = Mock()
    service.execute_tool = AsyncMock(return_value=MCPResponse(success=True, message="ok"))

    with patch("services.tools.execute_custom_tool.resolve_project_id_for_unity_instance", return_value="project-hash"):
        with patch("services.tools.execute_custom_tool.CustomToolService.get_instance", return_value=service):
            await execute_custom_tool(ctx, "my_tool", {})

    service.execute_tool.assert_awaited_once_with(
        "project-hash",
        "my_tool",
        "Project@project-hash",
        {},
        user_id="user-1",
    )


@pytest.mark.asyncio
async def test_custom_tools_resource_threads_user_id_from_context(monkeypatch):
    monkeypatch.setattr(config, "http_remote_hosted", True)

    ctx = Mock()
    state = {"unity_instance": "Project@project-hash", "user_id": "user-1"}
    ctx.get_state = Mock(side_effect=lambda key: state.get(key))

    service = Mock()
    service.list_registered_tools = AsyncMock(
        return_value=[ToolDefinitionModel(name="my_tool", description="My tool")]
    )

    with patch("services.resources.custom_tools.resolve_project_id_for_unity_instance", return_value="project-hash"):
        with patch("services.resources.custom_tools.CustomToolService.get_instance", return_value=service):
            await get_custom_tools(ctx)

    service.list_registered_tools.assert_awaited_once_with("project-hash", user_id="user-1")
