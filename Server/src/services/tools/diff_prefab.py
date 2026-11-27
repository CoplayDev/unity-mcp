"""Read-only Prefab comparison using local Git and the selected Unity project."""

import asyncio
import logging
import os
from pathlib import Path
from subprocess import TimeoutExpired
from time import monotonic
from typing import Annotated, Any
from uuid import uuid4

from fastmcp import Context
from mcp.types import ToolAnnotations
from pydantic import Field

from core.config import config
from models import MCPResponse
from services.prefab_diff import PrefabDiffError
from services.prefab_git_diff import diff_prefab_from_git
from services.registry import mcp_for_unity_tool
from services.resources.project_info import ProjectInfoResponse, get_project_info


logger = logging.getLogger(__name__)
PROJECT_LOOKUP_TIMEOUT_SECONDS = 10


class PrefabDiffResponse(MCPResponse):
    # The common tool logger prints response reprs. Only log counts and status.
    data: dict[str, Any] | None = Field(default=None, repr=False)


@mcp_for_unity_tool(
    unity_target=None,
    description=(
        "Read-only comparison of one regular text-serialized Prefab under Assets/ against Git HEAD. "
        "Returns GameObject paths, component names and file IDs, property paths, and old/new values. "
        "Compares the saved worktree file, including unstaged edits. Requires Git and a connected Unity editor "
        "on the same machine, using stdio or loopback HTTP. Both versions must exist and be at most 2 MiB each. "
        "Results may be truncated by count or size. Nested Prefabs and Prefab Variants are unsupported."
    ),
    annotations=ToolAnnotations(
        title="Diff Prefab",
        readOnlyHint=True,
        destructiveHint=False,
        idempotentHint=True,
        openWorldHint=False,
    ),
)
async def diff_prefab(
    ctx: Context,
    prefab_path: Annotated[str, "Project-relative Prefab path, e.g. Assets/Prefabs/Player.prefab."],
    max_changes: Annotated[int, "Maximum changes to return (1–200).", Field(ge=1, le=200)] = 200,
) -> PrefabDiffResponse:
    request_id = uuid4().hex[:12]
    start = monotonic()
    phase = "request"
    logger.info("[diff_prefab] request=%s asset=%r started", request_id, prefab_path)

    def finish(outcome: str) -> None:
        logger.info(
            "[diff_prefab] request=%s asset=%r phase=%s outcome=%s elapsed_ms=%.0f",
            request_id, prefab_path, phase, outcome, (monotonic() - start) * 1000,
        )

    try:
        http_host = os.environ.get("UNITY_MCP_HTTP_HOST", "127.0.0.1").lower()
        if config.http_remote_hosted or (
            config.transport_mode.lower() == "http" and http_host not in {"localhost", "127.0.0.1", "::1"}
        ):
            finish("skipped_non_local_server")
            return PrefabDiffResponse(success=False, message="Prefab diff requires stdio or loopback HTTP.")

        phase = "project_lookup"
        logger.info("[diff_prefab] request=%s phase=%s started", request_id, phase)
        project_info = await asyncio.wait_for(get_project_info(ctx), timeout=PROJECT_LOOKUP_TIMEOUT_SECONDS)
        if not isinstance(project_info, ProjectInfoResponse) or not project_info.success or not project_info.data.projectRoot:
            finish("project_unavailable")
            return PrefabDiffResponse(success=False, message="Could not identify the local Unity project. Connect or select a Unity instance.")
        root = Path(project_info.data.projectRoot)
        if not root.is_absolute():
            finish("invalid_project_root")
            return PrefabDiffResponse(success=False, message="Unity did not report an absolute project path.")
        finish("success")

        phase = "compare"
        logger.info("[diff_prefab] request=%s phase=%s started base=HEAD target=workingTree", request_id, phase)
        report = await asyncio.to_thread(diff_prefab_from_git, root, prefab_path, max_changes=max_changes)
    except asyncio.CancelledError:
        finish("cancelled")
        raise
    except (asyncio.TimeoutError, TimeoutExpired):
        finish("timeout")
        return PrefabDiffResponse(success=False, message=f"Prefab diff timed out during {phase.replace('_', ' ')}.")
    except PrefabDiffError as exc:
        logger.info("[diff_prefab] request=%s reason=%s", request_id, exc)
        finish("unsupported_or_unavailable")
        return PrefabDiffResponse(success=False, message=str(exc))
    except Exception:
        logger.exception("[diff_prefab] request=%s unexpected error", request_id)
        finish("failed")
        return PrefabDiffResponse(success=False, message="Prefab diff failed. See the MCP server log for details.")

    logger.info(
        "[diff_prefab] request=%s changes=%d returned=%d truncated=%s",
        request_id, report["totalChanges"], len(report["changes"]), report["truncated"],
    )
    finish("success" if report["totalChanges"] else "no_changes")
    return PrefabDiffResponse(success=True, message="Compared the saved Prefab against Git HEAD.", data=report)
