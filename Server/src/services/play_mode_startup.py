"""Wait for Unity-owned play startup jobs; never resubmit the play/code request."""
from __future__ import annotations

import asyncio
import math
import time
import uuid
from typing import Any, Awaitable, Callable

from models.unity_response import normalize_unity_response

EditorSender = Callable[[dict[str, Any], str | None], Awaitable[Any]]
STARTUP_OPTIONS = ("waitUntil", "onReadyCode", "waitTimeout")


def _response(raw: Any) -> dict[str, Any]:
    if callable(getattr(raw, "model_dump", None)):
        raw = raw.model_dump()
    result = normalize_unity_response(raw)
    return result if isinstance(result, dict) else {"success": False, "error": "Invalid startup response from Unity."}


def has_startup_options(params: dict[str, Any]) -> bool:
    return any(params.get(key) is not None for key in STARTUP_OPTIONS)


async def run_play_startup(send: EditorSender, params: dict[str, Any]) -> dict[str, Any]:
    wait_until = params.get("waitUntil")
    code = params.get("onReadyCode")
    if wait_until not in (None, "scene_loaded", "first_frame"):
        return {"success": False, "error": "wait_until must be scene_loaded or first_frame."}
    if code is not None and (not isinstance(code, str) or not code.strip() or len(code) > 50_000):
        return {"success": False, "error": "on_ready_code must contain a C# body of at most 50000 characters."}
    if wait_until is None and code is None:
        return {"success": False, "error": "wait_timeout requires wait_until or on_ready_code."}
    try:
        timeout = float(params.get("waitTimeout", 30))
    except (TypeError, ValueError):
        timeout = math.nan
    if not math.isfinite(timeout) or not 1 <= timeout <= 300:
        return {"success": False, "error": "wait_timeout must be between 1 and 300 seconds."}

    request = dict(params)
    request["startupRequestId"] = uuid.uuid4().hex
    request["waitTimeout"] = timeout
    deadline = time.monotonic() + timeout
    job_id = request["startupRequestId"]
    bound_instance = None
    last_data: dict[str, Any] = {"job_id": job_id, "status": "unknown"}

    def timed_out() -> dict[str, Any]:
        return {"success": False, "error": "play_startup_timeout",
            "message": "Play startup did not finish within wait_timeout. Poll get_play_mode_startup with job_id; do not resubmit startup code.",
            "data": last_data}

    try:
        response = _response(await asyncio.wait_for(send(request, None), timeout=timeout))
    except asyncio.TimeoutError:
        return timed_out()
    except Exception:
        return {"success": False, "error": "play_startup_response_unknown",
            "message": "The startup reply was lost. Poll the known job_id instead of resubmitting code.", "data": last_data}
    if not response.get("success"):
        # Preserve a live job reported by Unity (for example, a busy rejection).
        # Use the request id only as a fallback when the reply did not identify a job.
        failure_data = response.get("data")
        return {**response, "data": {**last_data, **(failure_data if isinstance(failure_data, dict) else {})}}
    data = response.get("data")
    if not isinstance(data, dict) or not data.get("job_id"):
        return {"success": False, "error": "play_startup_unsupported",
            "message": "Unity did not return a startup job. Update the Unity package to use wait_until/on_ready_code."}
    job_id = data["job_id"]
    bound_instance = data.get("unity_instance")
    if not bound_instance:
        return {"success": False, "error": "play_startup_instance_missing", "data": data}

    while True:
        if response.get("success") and isinstance(response.get("data"), dict):
            last_data = response["data"]
            status = last_data.get("status")
            if status == "completed":
                return {"success": True, "message": "Play Mode startup completed.", "data": last_data}
            if status == "failed":
                return {"success": False, "error": "play_startup_failed",
                    "message": last_data.get("error") or "Play Mode startup failed.", "data": last_data}
            if status not in ("waiting", "executing"):
                return {"success": False, "error": "play_startup_status_invalid", "data": last_data}
        elif response.get("hint") != "retry":
            return {**response, "data": last_data}
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return timed_out()
        await asyncio.sleep(min(0.1, remaining))
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return timed_out()
        try:
            response = _response(await asyncio.wait_for(
                send({"action": "get_play_mode_startup", "jobId": job_id}, bound_instance), timeout=remaining))
        except asyncio.TimeoutError:
            return timed_out()
        except Exception:
            response = {"success": False, "hint": "retry", "error": "Startup status is temporarily unavailable."}
