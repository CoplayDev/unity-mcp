"""Utilities for normalizing Unity transport responses."""
from __future__ import annotations

from typing import Any


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

    normalized: dict[str, Any] = {
        "success": success,
        "message": message,
        "error": error if not success else None,
        "data": data,
    }

    if not success and not normalized["error"]:
        normalized["error"] = message or "Unity command failed"

    return normalized
