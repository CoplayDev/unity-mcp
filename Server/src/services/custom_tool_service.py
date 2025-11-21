import asyncio
import inspect
import logging
import time

from fastmcp import Context, FastMCP
from pydantic import BaseModel, Field, ValidationError
from starlette.requests import Request
from starlette.responses import JSONResponse

from services.registry import mcp_for_unity_tool
from core.telemetry_decorator import telemetry_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

logger = logging.getLogger("mcp-for-unity-server")

_TYPE_MAP = {
    "string": {"type": "string"},
    "integer": {"type": "integer"},
    "number": {"type": "number"},
    "boolean": {"type": "boolean"},
    "array": {"type": "array"},
    "object": {"type": "object"},
}
_DEFAULT_POLL_INTERVAL = 1.0
_MAX_POLL_SECONDS = 600


class ToolParameterModel(BaseModel):
    name: str
    description: str | None = None
    type: str = Field(default="string")
    required: bool = Field(default=True)
    default_value: str | None = None


class ToolDefinitionModel(BaseModel):
    name: str
    description: str | None = None
    structured_output: bool | None = True
    requires_polling: bool | None = False
    poll_action: str | None = "status"
    parameters: list[ToolParameterModel] = Field(default_factory=list)


class RegisterToolsPayload(BaseModel):
    project_id: str
    tools: list[ToolDefinitionModel]


class CustomToolService:
    def __init__(self, mcp: FastMCP):
        self._mcp = mcp
        self._project_tools: dict[str, dict[str, object]] = {}
        self._metadata: dict[str, list[ToolDefinitionModel]] = {}
        self._register_http_routes()

    # --- HTTP Routes -----------------------------------------------------
    def _register_http_routes(self) -> None:
        @self._mcp.custom_route("/register-tools", methods=["POST"])
        async def register_tools(request: Request) -> JSONResponse:
            try:
                payload = RegisterToolsPayload.model_validate(await request.json())
            except ValidationError as exc:
                return JSONResponse({"success": False, "error": exc.errors()}, status_code=400)

            duplicates = [
                tool.name for tool in payload.tools if self._is_name_taken(tool.name)]
            if duplicates:
                return JSONResponse(
                    {
                        "success": False,
                        "error": f"Tool(s) already exist: {', '.join(duplicates)}",
                        "duplicates": duplicates,
                    },
                    status_code=409,
                )

            registered = []
            for tool in payload.tools:
                self._register_tool(payload.project_id, tool)
                registered.append(tool.name)

            return JSONResponse(
                {
                    "success": True,
                    "registered": registered,
                    "message": f"Registered {len(registered)} tool(s)",
                }
            )

        @self._mcp.custom_route("/tools", methods=["GET"])
        async def list_tools(_: Request) -> JSONResponse:
            return JSONResponse(self.list_registered_tools())

        @self._mcp.custom_route("/tools/{project_id}", methods=["DELETE"])
        async def unregister_project(request: Request) -> JSONResponse:
            project_id = request.path_params.get("project_id", "")
            removed = self.unregister_project(project_id)
            return JSONResponse(
                {
                    "success": True,
                    "unregistered": removed,
                    "message": f"Unregistered {len(removed)} tool(s) from project {project_id}",
                }
            )

    # --- Public API for MCP tools ---------------------------------------
    def list_registered_tools(self) -> dict[str, object]:
        tools = []
        for entries in self._metadata.values():
            tools.extend(entry.model_dump() for entry in entries)
        return {"success": True, "tools": tools, "count": len(tools)}

    def unregister_project(self, project_id: str) -> list[str]:
        removed = []
        entries = self._project_tools.pop(project_id, {})
        for name, tool in entries.items():
            removed.append(name)
            try:
                tool.disable()
                self._mcp._tool_manager.remove_tool(name)
            except Exception as exc:  # pragma: no cover - best effort
                logger.debug("Failed to disable tool %s: %s", name, exc)
        self._metadata.pop(project_id, None)
        return removed

    # --- Internal helpers ------------------------------------------------
    def _is_name_taken(self, tool_name: str) -> bool:
        if tool_name in getattr(self._mcp._tool_manager, "_tools", {}):
            return True
        for tools in self._project_tools.values():
            if tool_name in tools:
                return True
        return False

    def _register_tool(self, project_id: str, definition: ToolDefinitionModel) -> None:
        tool = self._create_dynamic_tool(project_id, definition)
        self._project_tools.setdefault(project_id, {})[definition.name] = tool
        self._metadata.setdefault(project_id, []).append(definition)

    def _create_dynamic_tool(self, project_id: str, definition: ToolDefinitionModel):
        tool_name = definition.name

        @mcp_for_unity_tool(name=tool_name, description=definition.description)
        async def dynamic_tool(ctx: Context, **kwargs):
            unity_instance = get_unity_instance_from_context(ctx)
            params = {k: v for k, v in kwargs.items() if v is not None}
            response = await send_with_unity_instance(
                async_send_command_with_retry, unity_instance, tool_name, params
            )

            if not definition.requires_polling:
                if isinstance(response, dict):
                    return response
                return {"success": False, "message": str(response)}

            return await self._poll_until_complete(
                tool_name,
                unity_instance,
                params,
                response,
                definition.poll_action or "status",
            )

        dynamic_tool.__signature__ = self._build_signature(
            definition.parameters)

        wrapped = telemetry_tool(tool_name)(dynamic_tool)
        tool = self._mcp.tool(
            name=tool_name, description=definition.description)(wrapped)
        tool.parameters = self._build_input_schema(definition.parameters)
        return tool

    def _build_signature(self, parameters: list[ToolParameterModel]) -> inspect.Signature:
        sig_params = [
            inspect.Parameter(
                "ctx",
                inspect.Parameter.POSITIONAL_OR_KEYWORD,
                annotation=Context,
            )
        ]

        for param in parameters:
            default = inspect._empty if param.required else None
            sig_params.append(
                inspect.Parameter(
                    param.name,
                    inspect.Parameter.KEYWORD_ONLY,
                    default=default,
                )
            )

        return inspect.Signature(parameters=sig_params)

    def _build_input_schema(self, parameters: list[ToolParameterModel]) -> dict[str, object]:
        schema = {"type": "object", "properties": {},
                  "additionalProperties": False}
        required = []
        for param in parameters:
            prop = _TYPE_MAP.get(param.type, {"type": "string"}).copy()
            if param.description:
                prop["description"] = param.description
            if param.default_value is not None:
                prop["default"] = param.default_value
            schema["properties"][param.name] = prop
            if param.required:
                required.append(param.name)
        if required:
            schema["required"] = required
        return schema

    async def _poll_until_complete(
        self,
        tool_name: str,
        unity_instance,
        initial_params: dict[str, object],
        initial_response,
        poll_action: str,
    ):
        poll_params = dict(initial_params)
        poll_params["action"] = poll_action or "status"

        deadline = time.time() + _MAX_POLL_SECONDS
        response = initial_response

        while True:
            status, poll_interval = self._interpret_status(response)

            if status in ("complete", "error", "final"):
                return self._normalize_response(response)

            if time.time() > deadline:
                return {
                    "success": False,
                    "message": f"Timeout waiting for {tool_name} to complete",
                    "data": self._safe_response(response),
                }

            await asyncio.sleep(poll_interval)

            try:
                response = await send_with_unity_instance(
                    async_send_command_with_retry, unity_instance, tool_name, poll_params
                )
            except Exception as exc:  # pragma: no cover - network/domain reload variability
                logger.debug("Polling %s failed, will retry: %s",
                             tool_name, exc)
                # Back off modestly but stay responsive.
                response = {
                    "_mcp_status": "pending",
                    "_mcp_poll_interval": min(max(poll_interval * 2, _DEFAULT_POLL_INTERVAL), 5.0),
                    "message": f"Retrying after transient error: {exc}",
                }

    def _interpret_status(self, response) -> tuple[str, float]:
        if response is None:
            return "pending", _DEFAULT_POLL_INTERVAL

        if not isinstance(response, dict):
            return "final", _DEFAULT_POLL_INTERVAL

        status = response.get("_mcp_status")
        if status is None:
            if len(response.keys()) == 0:
                return "pending", _DEFAULT_POLL_INTERVAL
            return "final", _DEFAULT_POLL_INTERVAL

        if status == "pending":
            interval_raw = response.get(
                "_mcp_poll_interval", _DEFAULT_POLL_INTERVAL)
            try:
                interval = float(interval_raw)
            except (TypeError, ValueError):
                interval = _DEFAULT_POLL_INTERVAL

            interval = max(0.1, min(interval, 5.0))
            return "pending", interval

        if status == "complete":
            return "complete", _DEFAULT_POLL_INTERVAL

        if status == "error":
            return "error", _DEFAULT_POLL_INTERVAL

        return "final", _DEFAULT_POLL_INTERVAL

    def _normalize_response(self, response):
        if isinstance(response, dict):
            return response
        return {"success": False, "message": str(response)}

    def _safe_response(self, response):
        if isinstance(response, dict):
            return response
        if response is None:
            return None
        return {"message": str(response)}
