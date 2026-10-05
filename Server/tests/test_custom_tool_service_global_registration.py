import typing

from models.models import ToolDefinitionModel, ToolParameterModel
from services.custom_tool_service import CustomToolService


class _RecordingMcp:
    def __init__(self):
        self.tools = {}

    def custom_route(self, _path, methods=None):  # noqa: ARG002
        def _decorator(fn):
            return fn

        return _decorator

    def tool(self, name, description=None):  # noqa: ARG002
        def _decorator(fn):
            self.tools[name] = fn
            return fn

        return _decorator


def test_global_tool_keeps_parameter_annotations_through_wrappers():
    """FastMCP builds the tool schema from the registered function's type hints. If the
    logging/telemetry wrappers lose them (Python 3.14), pydantic raises KeyError and
    the tool is dropped."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp)
    definition = ToolDefinitionModel(
        name="my_tool",
        description="My tool",
        parameters=[
            ToolParameterModel(name="action", type="string"),
            ToolParameterModel(name="count", type="integer", required=False, default_value="3"),
        ],
    )

    service.register_global_tools([definition])

    hints = typing.get_type_hints(mcp.tools["my_tool"])
    assert hints["action"] is str
    assert hints["count"] is int
