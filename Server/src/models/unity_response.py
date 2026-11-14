"""Utilities for normalizing Unity transport responses."""
from __future__ import annotations

from typing import Any, Type

from models.models import MCPResponse


# Structured fields Unity puts on a top-level {"status": "error"} envelope.
ERROR_DETAIL_KEYS = ("code", "command", "exception_type", "file", "line", "replayed")


def error_details(envelope: Any) -> dict[str, Any] | None:
    """Return the structured fields of a Unity error envelope (code, file/line, ...), if any."""
    if not isinstance(envelope, dict):
        return None
    details = {k: envelope[k] for k in ERROR_DETAIL_KEYS if envelope.get(k) is not None}
    return details or None


def normalize_unity_response(response: Any) -> Any:
    """Normalize Unity's {status,result} payloads into MCPResponse shape."""
    if not isinstance(response, dict):
        return response

    status = response.get("status")
    result = response.get("result") if isinstance(
        response.get("result"), dict) else response.get("result")

    # Already MCPResponse-shaped
    if "success" in response:
        return response
    if isinstance(result, dict) and "success" in result:
        return result

    if status is None:
        return response

    payload = result if isinstance(result, dict) else {}
    success = status == "success"
    message = payload.get("message") or response.get("message")
    error = payload.get("error") or response.get("error")

    data = payload.get("data")
    if data is None and isinstance(payload, dict) and payload:
        data = {k: v for k, v in payload.items() if k not in {
            "message", "error", "status", "code"}}
        if not data:
            data = None
    if data is None and not success and not payload:
        data = error_details(response)

    normalized: dict[str, Any] = {
        "success": success,
        "message": message,
        "error": error if not success else None,
        "data": data,
    }

    if not success and not normalized["error"]:
        normalized["error"] = message or "Unity command failed"

    return normalized


def parse_resource_response(response: Any, typed_cls: Type[MCPResponse]) -> MCPResponse:
    """Parse a Unity response into a typed response class.

    Returns a base ``MCPResponse`` for error responses so that typed subclasses
    with strict ``data`` fields (e.g. ``list[str]``) don't raise Pydantic
    validation errors when ``data`` is ``None``.
    """
    if not isinstance(response, dict):
        return response

    # Detect errors from both normalized (success=False) and raw (status="error") shapes.
    if response.get("success") is False or response.get("status") == "error":
        return MCPResponse(
            success=False,
            error=response.get("error"),
            message=response.get("message"),
        )

    return typed_cls(**response)
