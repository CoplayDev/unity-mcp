import asyncio
import inspect
import logging
import time
from hashlib import sha256
from typing import Optional

from fastmcp import Context, FastMCP
from pydantic import BaseModel, Field, ValidationError
from starlette.requests import Request
from starlette.responses import JSONResponse

from models.models import MCPResponse, ToolDefinitionModel, ToolParameterModel
from core.logging_decorator import log_execution
from core.telemetry_decorator import telemetry_tool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import (
    async_send_command_with_retry,
    get_unity_connection_pool,
)
from transport.plugin_hub import PluginHub
from services.tools import get_unity_instance_from_context
from services.registry import get_registered_tools

logger = logging.getLogger("mcp-for-unity-server")

_DEFAULT_POLL_INTERVAL = 1.0
_MAX_POLL_SECONDS = 600


class RegisterToolsPayload(BaseModel):
    project_id: str
    project_hash: str | None = None
    tools: list[ToolDefinitionModel]


class ToolRegistrationResponse(BaseModel):
    success: bool
    registered: list[str]
    replaced: list[str]
    message: str


class CustomToolService:
    _instance: "CustomToolService | None" = None

    def __init__(self, mcp: FastMCP, project_scoped_tools: bool = True):
        CustomToolService._instance = self
        self._mcp = mcp
        self._project_scoped_tools = project_scoped_tools
        self._project_tools: dict[str, dict[str, ToolDefinitionModel]] = {}
        self._hash_to_project: dict[str, str] = {}
        self._global_tools: dict[str, ToolDefinitionModel] = {}
        self._register_http_routes()

    @classmethod
    def get_instance(cls) -> "CustomToolService":
        if cls._instance is None:
            raise RuntimeError("CustomToolService has not been initialized")
        return cls._instance

    # --- HTTP Routes -----------------------------------------------------
    def _register_http_routes(self) -> None:
        @self._mcp.custom_route("/register-tools", methods=["POST"])
        async def register_tools(request: Request) -> JSONResponse:
            try:
                payload = RegisterToolsPayload.model_validate(await request.json())
            except ValidationError as exc:
                return JSONResponse({"success": False, "error": exc.errors()}, status_code=400)

            registered, replaced = self._register_project_tools(
                payload.project_id, payload.tools, project_hash=payload.project_hash)

            message = f"Registered {len(registered)} tool(s)"
            if replaced:
                message += f" (replaced: {', '.join(replaced)})"

            response = ToolRegistrationResponse(
                success=True,
                registered=registered,
                replaced=replaced,
                message=message,
            )
            return JSONResponse(response.model_dump())

    # --- Public API for MCP tools ---------------------------------------
    async def list_registered_tools(self, project_id: str) -> list[ToolDefinitionModel]:
        legacy = list(self._project_tools.get(project_id, {}).values())
        hub_tools = await PluginHub.get_tools_for_project(project_id)
        return legacy + hub_tools

    async def get_tool_definition(self, project_id: str, tool_name: str) -> ToolDefinitionModel | None:
        tool = self._project_tools.get(project_id, {}).get(tool_name)
        if tool:
            return tool
        return await PluginHub.get_tool_definition(project_id, tool_name)

    async def execute_tool(
        self,
        project_id: str,
        tool_name: str,
        unity_instance: str | None,
        params: dict[str, object] | None = None,
    ) -> MCPResponse:
        params = params or {}
        logger.info(
            f"Executing tool '{tool_name}' for project '{project_id}' (instance={unity_instance}) with params: {params}"
        )

        definition = await self.get_tool_definition(project_id, tool_name)
        if definition is None:
            return MCPResponse(
                success=False,
                message=f"Tool '{tool_name}' not found for project {project_id}",
            )

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            tool_name,
            params,
        )

        if not definition.requires_polling:
            result = self._normalize_response(response)
            logger.info(f"Tool '{tool_name}' immediate response: {result}")
            return result

        result = await self._poll_until_complete(
            tool_name,
            unity_instance,
            params,
            response,
            definition.poll_action or "status",
        )
        logger.info(f"Tool '{tool_name}' polled response: {result}")
        return result

    # --- Internal helpers ------------------------------------------------
    def _is_registered(self, project_id: str, tool_name: str) -> bool:
        return tool_name in self._project_tools.get(project_id, {})

    def _register_tool(self, project_id: str, definition: ToolDefinitionModel) -> None:
        self._project_tools.setdefault(project_id, {})[
            definition.name] = definition

    def get_project_id_for_hash(self, project_hash: str | None) -> str | None:
        if not project_hash:
            return None
        return self._hash_to_project.get(project_hash.lower())

    async def _poll_until_complete(
        self,
        tool_name: str,
        unity_instance,
        initial_params: dict[str, object],
        initial_response,
        poll_action: str,
    ) -> MCPResponse:
        poll_params = dict(initial_params)
        poll_params["action"] = poll_action or "status"

        deadline = time.time() + _MAX_POLL_SECONDS
        response = initial_response

        while True:
            status, poll_interval = self._interpret_status(response)

            if status in ("complete", "error", "final"):
                return self._normalize_response(response)

            if time.time() > deadline:
                return MCPResponse(
                    success=False,
                    message=f"Timeout waiting for {tool_name} to complete",
                    data=self._safe_response(response),
                )

            await asyncio.sleep(poll_interval)

            try:
                response = await send_with_unity_instance(
                    async_send_command_with_retry, unity_instance, tool_name, poll_params
                )
            except Exception as exc:  # pragma: no cover - network/domain reload variability
                logger.debug(f"Polling {tool_name} failed, will retry: {exc}")
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

    def _normalize_response(self, response) -> MCPResponse:
        if isinstance(response, MCPResponse):
            return response
        if isinstance(response, dict):
            return MCPResponse(
                success=response.get("success", True),
                message=response.get("message"),
                error=response.get("error"),
                data=response.get(
                    "data", response) if "data" not in response else response["data"],
            )

        success = True
        message = None
        error = None
        data = None

        if isinstance(response, dict):
            success = response.get("success", True)
            if "_mcp_status" in response and response["_mcp_status"] == "error":
                success = False
            message = str(response.get("message")) if response.get(
                "message") else None
            error = str(response.get("error")) if response.get(
                "error") else None
            data = response.get("data")
            if "success" not in response and "_mcp_status" not in response:
                data = response
        else:
            success = False
            message = str(response)

        return MCPResponse(success=success, message=message, error=error, data=data)

    def _safe_response(self, response):
        if isinstance(response, dict):
            return response
        if response is None:
            return None
        return {"message": str(response)}

    def _register_project_tools(
        self,
        project_id: str,
        tools: list[ToolDefinitionModel],
        project_hash: str | None = None,
    ) -> tuple[list[str], list[str]]:
        registered: list[str] = []
        replaced: list[str] = []
        for tool in tools:
            if self._is_registered(project_id, tool.name):
                replaced.append(tool.name)
            self._register_tool(project_id, tool)
            registered.append(tool.name)
            if not self._project_scoped_tools:
                self._register_global_tool(tool)

        if project_hash:
            self._hash_to_project[project_hash.lower()] = project_id

        return registered, replaced

    def register_global_tools(self, tools: list[ToolDefinitionModel]) -> None:
        if self._project_scoped_tools:
            return
        builtin_names = self._get_builtin_tool_names()
        for tool in tools:
            if tool.name in builtin_names:
                logger.info(
                    "Skipping global custom tool registration for built-in tool '%s'",
                    tool.name,
                )
                continue
            self._register_global_tool(tool)

    def _get_builtin_tool_names(self) -> set[str]:
        return {tool["name"] for tool in get_registered_tools()}

    def _register_global_tool(self, definition: ToolDefinitionModel) -> None:
        existing = self._global_tools.get(definition.name)
        if existing:
            if existing.model_dump() != definition.model_dump():
                logger.warning(
                    "Custom tool '%s' already registered with a different schema; keeping existing definition.",
                    definition.name,
                )
            return

        handler = self._build_global_tool_handler(definition)
        wrapped = log_execution(definition.name, "Tool")(handler)
        wrapped = telemetry_tool(definition.name)(wrapped)

        try:
            wrapped = self._mcp.tool(
                name=definition.name,
                description=definition.description,
            )(wrapped)
        except Exception as exc:  # pragma: no cover - defensive against tool conflicts
            logger.warning(
                "Failed to register custom tool '%s' globally: %s",
                definition.name,
                exc,
            )
            return

        self._global_tools[definition.name] = definition

    def _build_global_tool_handler(self, definition: ToolDefinitionModel):
        async def _handler(ctx: Context, **kwargs) -> MCPResponse:
            unity_instance = get_unity_instance_from_context(ctx)
            if not unity_instance:
                return MCPResponse(
                    success=False,
                    message="No active Unity instance. Call set_active_instance with Name@hash from mcpforunity://instances.",
                )

            project_id = resolve_project_id_for_unity_instance(unity_instance)
            if project_id is None:
                return MCPResponse(
                    success=False,
                    message=f"Could not resolve project id for {unity_instance}. Ensure Unity is running and reachable.",
                )

            params = {k: v for k, v in kwargs.items() if v is not None}
            service = CustomToolService.get_instance()
            return await service.execute_tool(project_id, definition.name, unity_instance, params)

        _handler.__name__ = f"custom_tool_{definition.name}"
        _handler.__doc__ = definition.description or ""
        _handler.__signature__ = self._build_signature(definition)
        _handler.__annotations__ = self._build_annotations(definition)
        return _handler

    def _build_signature(self, definition: ToolDefinitionModel) -> inspect.Signature:
        params: list[inspect.Parameter] = [
            inspect.Parameter(
                "ctx",
                inspect.Parameter.POSITIONAL_OR_KEYWORD,
                annotation=Context,
            )
        ]
        for param in definition.parameters:
            if not param.name.isidentifier():
                logger.warning(
                    "Custom tool '%s' has non-identifier parameter '%s'; exposing via kwargs only.",
                    definition.name,
                    param.name,
                )
                continue
            default = inspect._empty if param.required else self._coerce_default(
                param.default_value, param.type)
            params.append(
                inspect.Parameter(
                    param.name,
                    inspect.Parameter.POSITIONAL_OR_KEYWORD,
                    default=default,
                    annotation=self._map_param_type(param),
                )
            )
        return inspect.Signature(parameters=params)

    def _build_annotations(self, definition: ToolDefinitionModel) -> dict[str, object]:
        annotations: dict[str, object] = {"ctx": Context}
        for param in definition.parameters:
            if not param.name.isidentifier():
                continue
            annotations[param.name] = self._map_param_type(param)
        return annotations

    def _map_param_type(self, param: ToolParameterModel):
        ptype = (param.type or "string").lower()
        if ptype in ("integer", "int"):
            return int
        if ptype in ("number", "float", "double"):
            return float
        if ptype in ("bool", "boolean"):
            return bool
        if ptype in ("array", "list"):
            return list
        if ptype in ("object", "dict"):
            return dict
        return str

    def _coerce_default(self, value: str | None, param_type: str | None):
        if value is None:
            return None
        try:
            ptype = (param_type or "string").lower()
            if ptype in ("integer", "int"):
                return int(value)
            if ptype in ("number", "float", "double"):
                return float(value)
            if ptype in ("bool", "boolean"):
                return str(value).lower() in ("1", "true", "yes", "on")
            return value
        except Exception:
            return value


def compute_project_id(project_name: str, project_path: str) -> str:
    """
    DEPRECATED: Computes a SHA256-based project ID.
    This function is no longer used as of the multi-session fix.
    Unity instances now use their native project_hash (SHA1-based) for consistency
    across stdio and WebSocket transports.
    """
    combined = f"{project_name}:{project_path}"
    return sha256(combined.encode("utf-8")).hexdigest().upper()[:16]


def resolve_project_id_for_unity_instance(unity_instance: str | None) -> str | None:
    if unity_instance is None:
        return None

    # stdio transport: resolve via discovered instances with name+path
    try:
        pool = get_unity_connection_pool()
        instances = pool.discover_all_instances()
        target = None
        if "@" in unity_instance:
            name_part, _, hash_hint = unity_instance.partition("@")
            target = next(
                (
                    inst for inst in instances
                    if inst.name == name_part and inst.hash.startswith(hash_hint)
                ),
                None,
            )
        else:
            target = next(
                (
                    inst for inst in instances
                    if inst.id == unity_instance or inst.hash.startswith(unity_instance)
                ),
                None,
            )

        if target:
            # Return the project_hash from Unity (not a computed SHA256 hash).
            # This matches the hash Unity uses when registering tools via WebSocket.
            if target.hash:
                return target.hash
            logger.warning(
                f"Unity instance {target.id} has empty hash; cannot resolve project ID")
            return None
    except Exception:
        logger.debug(
            f"Failed to resolve project id via connection pool for {unity_instance}")

    # HTTP/WebSocket transport: resolve via PluginHub using project_hash
    try:
        hash_part: Optional[str] = None
        if "@" in unity_instance:
            _, _, suffix = unity_instance.partition("@")
            hash_part = suffix or None
        else:
            hash_part = unity_instance

        if hash_part:
            lowered = hash_part.lower()
            mapped: Optional[str] = None
            try:
                service = CustomToolService.get_instance()
                mapped = service.get_project_id_for_hash(lowered)
            except RuntimeError:
                mapped = None
            if mapped:
                return mapped
            return lowered
    except Exception:
        logger.debug(
            f"Failed to resolve project id via plugin hub for {unity_instance}")

    return None
