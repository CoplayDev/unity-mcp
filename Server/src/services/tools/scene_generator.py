"""MCP tool for scene generation pipeline validation, auditing, and smoke testing."""
from __future__ import annotations

import asyncio
import json
import hashlib
from pathlib import Path
from typing import Annotated, Any, Literal

from fastmcp import Context

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from scene_generator.models import BatchExecutionPlan, MCPCallPlan, SceneSpec
from scene_generator.validator import PlanValidator

_BANNED_SCRIPT_LOOKUPS = (
    "CompareTag(",
    "FindGameObjectsWithTag(",
    "GameObject.FindGameObjectsWithTag(",
)
_RETRYABLE_PATTERNS = (
    "busy",
    "compiling",
    "timeout",
    "temporarily unavailable",
    "try again",
)
_WARNING_PATTERNS = (
    "already exists",
    "already added",
    "no-op",
)


@mcp_for_unity_tool(
    description="""Scene generation helper for EmbodiedCreate educational interactive 3D scenes.

    Actions:
    - load_spec: Load and validate a SceneSpec JSON file. Returns parsed spec with structural hints.
    - validate_plan: Validate and optimize a scene generation plan. Returns batch-optimized
      execution phases ready for sequential batch_execute calls.
    - audit_batch_result: Audit one batch_execute result and decide pass/retry/fail.
    - smoke_test_scene: Run a short Play Mode smoke test and return structured diagnostics.
    - freeze_essence: Build and return frozen Essence payload and hash from a SceneSpec.
    - validate_essence_surface: Validate required Essence/Surface anchors in SceneSpec.
    - generate_surface_variant: Return a lightweight suggested surface variant profile.
    - execute_batch_plan: Execute validated phases with audit/retry/smoke/save gating.
    - plan_and_execute: Build deterministic batch plan from SceneSpec, execute it, and
      return unified planning + execution report.

    Workflow: load_spec -> (optional LLM planning) -> validate_plan -> execute_batch_plan
    or SceneSpec-first deterministic flow: plan_and_execute"""
)
async def scene_generator(
    ctx: Context,
    action: Annotated[
        Literal[
            "load_spec",
            "validate_plan",
            "audit_batch_result",
            "smoke_test_scene",
            "freeze_essence",
            "validate_essence_surface",
            "generate_surface_variant",
            "execute_batch_plan",
            "plan_and_execute",
        ],
        """Action to perform:
        - load_spec: Load and validate a SceneSpec JSON file
        - validate_plan: Validate and optimize a plan into batch execution phases
        - audit_batch_result: Evaluate one batch result for pass/retry/fail
        - smoke_test_scene: Run play-mode smoke test (clear -> play -> collect console -> stop)
        - freeze_essence: Build Essence + hash from SceneSpec
        - validate_essence_surface: Verify Essence invariants and required runtime anchors
        - generate_surface_variant: Suggest a new Surface profile
        - execute_batch_plan: Execute a BatchExecutionPlan with bounded retries and smoke gate
        - plan_and_execute: Build a deterministic BatchExecutionPlan from SceneSpec and execute it"""
    ],
    spec_path: Annotated[
        str,
        "File path to the SceneSpec JSON file (for load_spec)"
    ] | None = None,
    spec_json: Annotated[
        str,
        "SceneSpec as a JSON string (for validate_plan, or alternative to spec_path)"
    ] | None = None,
    plan_json: Annotated[
        str,
        "MCPCallPlan as a JSON string (for validate_plan)"
    ] | None = None,
    batch_result_json: Annotated[
        str,
        "Raw batch_execute result JSON (for audit_batch_result)"
    ] | None = None,
    phase_name: Annotated[
        str,
        "Optional phase name for audit context"
    ] | None = None,
    phase_number: Annotated[
        int,
        "Optional phase number for audit context"
    ] | None = None,
    phase_context_json: Annotated[
        str,
        "Optional phase context JSON; can include commands and run metadata"
    ] | None = None,
    play_seconds: Annotated[
        float,
        "Smoke test duration in Play Mode"
    ] | None = None,
    include_warnings: Annotated[
        bool,
        "Include warning logs in smoke test output"
    ] | None = None,
    fail_on_warning: Annotated[
        bool,
        "Mark smoke test as failed when warnings are present"
    ] | None = None,
    batch_plan_json: Annotated[
        str,
        "BatchExecutionPlan JSON payload (for execute_batch_plan)"
    ] | None = None,
    max_retries_per_batch: Annotated[
        int,
        "Max retries for retryable audited batch failures (execute_batch_plan)"
    ] | None = None,
    retry_backoff_seconds: Annotated[
        float,
        "Retry backoff in seconds between attempts (execute_batch_plan)"
    ] | None = None,
    stop_on_warning: Annotated[
        bool,
        "If true, warnings are treated as failures (execute_batch_plan)"
    ] | None = None,
) -> dict[str, Any]:
    """Load scene specs and validate/optimize generation plans."""

    if action == "load_spec":
        return _handle_load_spec(spec_path, spec_json)
    if action == "validate_plan":
        return _handle_validate_plan(spec_json, plan_json)
    if action == "audit_batch_result":
        return _handle_audit_batch_result(batch_result_json, phase_name, phase_number, phase_context_json)
    if action == "smoke_test_scene":
        return await _handle_smoke_test_scene(
            ctx=ctx,
            play_seconds=play_seconds,
            include_warnings=include_warnings,
            fail_on_warning=fail_on_warning,
        )
    if action == "freeze_essence":
        return _handle_freeze_essence(spec_path, spec_json)
    if action == "validate_essence_surface":
        return _handle_validate_essence_surface(spec_json)
    if action == "generate_surface_variant":
        return _handle_generate_surface_variant(spec_json)
    if action == "execute_batch_plan":
        return await _handle_execute_batch_plan(
            ctx=ctx,
            batch_plan_json=batch_plan_json,
            max_retries_per_batch=max_retries_per_batch,
            retry_backoff_seconds=retry_backoff_seconds,
            stop_on_warning=stop_on_warning,
        )
    if action == "plan_and_execute":
        return await _handle_plan_and_execute(
            ctx=ctx,
            spec_json=spec_json,
            max_retries_per_batch=max_retries_per_batch,
            retry_backoff_seconds=retry_backoff_seconds,
            stop_on_warning=stop_on_warning,
        )
    return {"success": False, "message": f"Unknown action: {action}"}


def _as_text(value: Any) -> str:
    """Return enum values or plain values consistently as strings."""
    raw = getattr(value, "value", value)
    return str(raw)


def _load_json_dict(payload: str | None, field_name: str) -> tuple[dict[str, Any] | None, str | None]:
    """Parse JSON text into a dict for tool action payloads."""
    if not payload:
        return None, f"{field_name} is required"
    try:
        parsed = json.loads(payload)
    except json.JSONDecodeError as exc:
        return None, f"Invalid JSON in {field_name}: {exc}"
    if not isinstance(parsed, dict):
        return None, f"{field_name} must decode to a JSON object"
    return parsed, None


def _contains_banned_script_lookup(text: str) -> list[str]:
    """Return banned tag lookup patterns found in script content."""
    found: list[str] = []
    for pattern in _BANNED_SCRIPT_LOOKUPS:
        if pattern in text:
            found.append(pattern)
    return found


def _is_retryable_message(message: str) -> bool:
    """Return True when a failure looks transient and retryable."""
    lowered = message.lower()
    return any(token in lowered for token in _RETRYABLE_PATTERNS)


def _is_warning_message(message: str) -> bool:
    """Return True for expected, non-fatal idempotent/no-op outcomes."""
    lowered = message.lower()
    return any(token in lowered for token in _WARNING_PATTERNS)


def _extract_batch_results(batch_result: dict[str, Any]) -> list[dict[str, Any]]:
    """Extract per-command result entries from batch_execute response payloads."""
    data = batch_result.get("data")
    if isinstance(data, dict) and isinstance(data.get("results"), list):
        return [entry for entry in data["results"] if isinstance(entry, dict)]
    if isinstance(batch_result.get("results"), list):
        return [entry for entry in batch_result["results"] if isinstance(entry, dict)]
    return []


def _extract_message(entry: dict[str, Any]) -> str:
    """Extract the most useful status/error string from a result entry."""
    if isinstance(entry.get("error"), str) and entry.get("error"):
        return str(entry["error"])

    nested = entry.get("result")
    if isinstance(nested, dict):
        for key in ("message", "error", "detail"):
            value = nested.get(key)
            if isinstance(value, str) and value.strip():
                return value.strip()
    return ""


def _extract_script_banned_lookup_failures(phase_context: dict[str, Any]) -> list[dict[str, Any]]:
    """Validate script payloads and fail when tag-based lookup APIs are used."""
    failures: list[dict[str, Any]] = []

    commands = phase_context.get("commands")
    if isinstance(commands, list):
        for index, command in enumerate(commands):
            if not isinstance(command, dict):
                continue
            if str(command.get("tool", "")).strip() != "create_script":
                continue
            params = command.get("params")
            if not isinstance(params, dict):
                continue
            content = params.get("contents") or params.get("content")
            if not isinstance(content, str):
                continue
            matches = _contains_banned_script_lookup(content)
            if matches:
                failures.append(
                    {
                        "index": index,
                        "tool": "create_script",
                        "reason": "banned_tag_lookup_pattern",
                        "details": matches,
                    }
                )

    extra_scripts = phase_context.get("script_contents")
    if isinstance(extra_scripts, list):
        for index, content in enumerate(extra_scripts):
            if not isinstance(content, str):
                continue
            matches = _contains_banned_script_lookup(content)
            if matches:
                failures.append(
                    {
                        "index": index,
                        "tool": "script_contents",
                        "reason": "banned_tag_lookup_pattern",
                        "details": matches,
                    }
                )

    return failures


def _audit_batch_result_payload(
    batch_result: dict[str, Any],
    phase_name: str | None,
    phase_number: int | None,
    phase_context: dict[str, Any] | None,
) -> dict[str, Any]:
    """Audit one batch result and classify pass/retry/fail."""
    failures: list[dict[str, Any]] = []
    retryable: list[dict[str, Any]] = []
    warnings: list[dict[str, Any]] = []

    context = phase_context if isinstance(phase_context, dict) else {}

    for item in _extract_script_banned_lookup_failures(context):
        failures.append(item)

    for index, entry in enumerate(_extract_batch_results(batch_result)):
        tool = str(entry.get("tool", "")).strip()
        call_succeeded = entry.get("callSucceeded")
        if not isinstance(call_succeeded, bool):
            nested_result = entry.get("result")
            if isinstance(nested_result, dict) and isinstance(nested_result.get("success"), bool):
                call_succeeded = nested_result.get("success")
            else:
                call_succeeded = False if entry.get("error") else True

        message = _extract_message(entry)

        if not call_succeeded:
            item = {
                "index": index,
                "tool": tool,
                "message": message or "Command failed without detailed message.",
            }
            if _is_retryable_message(item["message"]):
                retryable.append(item)
            else:
                failures.append(item)
            continue

        if message and _is_warning_message(message):
            warnings.append(
                {
                    "index": index,
                    "tool": tool,
                    "message": message,
                }
            )

    if not batch_result.get("success", False) and not failures and not retryable:
        message = str(batch_result.get("message", "Batch failed."))
        if _is_retryable_message(message):
            retryable.append({"index": -1, "tool": "batch_execute", "message": message})
        else:
            failures.append({"index": -1, "tool": "batch_execute", "message": message})

    decision = "pass"
    next_step = "Continue to the next phase."
    if failures:
        decision = "fail"
        next_step = "Stop pipeline and repair hard failures before proceeding."
    elif retryable:
        decision = "retry"
        next_step = "Retry this phase with bounded backoff, then re-audit results."

    return {
        "success": True,
        "decision": decision,
        "phase": {
            "name": phase_name,
            "number": phase_number,
        },
        "failures": failures,
        "retryable": retryable,
        "warnings": warnings,
        "next_step": next_step,
    }


def _normalize_console_entries(response: dict[str, Any]) -> list[dict[str, Any]]:
    """Normalize read_console response entries for smoke test classification."""
    entries: list[dict[str, Any]] = []
    data = response.get("data")

    if isinstance(data, dict):
        raw_list = data.get("lines")
        if not isinstance(raw_list, list):
            raw_list = data.get("items")
        if isinstance(raw_list, list):
            for item in raw_list:
                if isinstance(item, dict):
                    entries.append(item)

    if not entries and isinstance(data, list):
        for item in data:
            if isinstance(item, dict):
                entries.append(item)

    return entries


async def _handle_smoke_test_scene(
    ctx: Context,
    play_seconds: float | None,
    include_warnings: bool | None,
    fail_on_warning: bool | None,
) -> dict[str, Any]:
    """Run a short play-mode smoke test and classify pass/fail."""
    duration = float(play_seconds) if play_seconds is not None else 5.0
    duration = max(0.5, min(duration, 30.0))
    include_warn = True if include_warnings is None else bool(include_warnings)
    fail_warn = False if fail_on_warning is None else bool(fail_on_warning)

    unity_instance = get_unity_instance_from_context(ctx)

    async def _send(tool: str, payload: dict[str, Any]) -> dict[str, Any]:
        try:
            response = await send_with_unity_instance(
                async_send_command_with_retry,
                unity_instance,
                tool,
                payload,
            )
            return response if isinstance(response, dict) else {"success": False, "message": str(response)}
        except Exception as exc:  # pragma: no cover - defensive transport guard
            return {"success": False, "message": f"{tool} call failed: {exc}"}

    clear_resp = await _send("read_console", {"action": "clear"})

    play_resp = await _send("manage_editor", {"action": "play"})

    await asyncio.sleep(duration)

    types = ["error", "warning"] if include_warn else ["error"]
    get_resp = await _send(
        "read_console",
        {
            "action": "get",
            "types": types,
            "count": 200,
            "includeStacktrace": True,
            "format": "json",
        },
    )

    stop_resp = await _send("manage_editor", {"action": "stop"})

    entries = _normalize_console_entries(get_resp)
    errors: list[dict[str, Any]] = []
    warnings: list[dict[str, Any]] = []

    for entry in entries:
        entry_type = str(entry.get("type", "")).strip().lower()
        if entry_type == "error":
            errors.append(entry)
        elif entry_type == "warning":
            warnings.append(entry)

    passed = (
        bool(clear_resp.get("success"))
        and bool(play_resp.get("success"))
        and bool(get_resp.get("success"))
        and bool(stop_resp.get("success"))
        and not errors
        and (not fail_warn or not warnings)
    )

    decision = "pass" if passed else "fail"
    summary = {
        "errors": len(errors),
        "warnings": len(warnings),
        "duration_seconds": duration,
        "fail_on_warning": fail_warn,
    }

    return {
        "success": passed,
        "decision": decision,
        "message": "Smoke test passed." if passed else "Smoke test failed. See smoke_report.",
        "smoke_report": {
            "summary": summary,
            "steps": {
                "clear_console": clear_resp,
                "play": play_resp,
                "read_console": get_resp,
                "stop": stop_resp,
            },
            "errors": errors,
            "warnings": warnings,
        },
    }


def _handle_audit_batch_result(
    batch_result_json: str | None,
    phase_name: str | None,
    phase_number: int | None,
    phase_context_json: str | None,
) -> dict[str, Any]:
    """Audit one batch_execute result and classify pass/retry/fail."""
    batch_result, batch_error = _load_json_dict(batch_result_json, "batch_result_json")
    if batch_error:
        return {"success": False, "message": batch_error}

    phase_context: dict[str, Any] | None = None
    if phase_context_json:
        phase_context, phase_error = _load_json_dict(phase_context_json, "phase_context_json")
        if phase_error:
            return {"success": False, "message": phase_error}

    return _audit_batch_result_payload(
        batch_result=batch_result or {},
        phase_name=phase_name,
        phase_number=phase_number,
        phase_context=phase_context,
    )


def _handle_load_spec(
    spec_path: str | None,
    spec_json: str | None,
) -> dict[str, Any]:
    """Load a SceneSpec from file or JSON string."""
    raw: dict[str, Any] | None = None

    if spec_path:
        path = Path(spec_path)
        if not path.exists():
            return {"success": False, "message": f"File not found: {spec_path}"}
        try:
            raw = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError) as e:
            return {"success": False, "message": f"Failed to read spec file: {e}"}
    elif spec_json:
        try:
            raw = json.loads(spec_json)
        except json.JSONDecodeError as e:
            return {"success": False, "message": f"Invalid JSON in spec_json: {e}"}
    else:
        return {"success": False, "message": "Either spec_path or spec_json is required for load_spec"}

    try:
        spec = SceneSpec.model_validate(raw)
    except Exception as e:
        return {"success": False, "message": f"SceneSpec validation failed: {e}"}

    # Build planning hints per mapping
    hints = []
    for row in spec.mappings:
        hint: dict[str, Any] = {
            "structural_component": _as_text(row.structural_component),
            "analogy_name": row.analogy_name,
            "asset_strategy": _as_text(row.asset_strategy),
        }
        if _as_text(row.asset_strategy) == "trellis":
            hint["note"] = "Use manage_3d_gen(action='generate') - async, poll status"
        elif _as_text(row.asset_strategy) == "vfx":
            hint["note"] = "Create a VFX host object, add ParticleSystem via manage_components, then configure with manage_vfx particle_* actions"
        elif _as_text(row.asset_strategy) == "mechanic":
            hint["note"] = "Script/logic only - no visual asset to create"
        elif _as_text(row.asset_strategy) == "ui":
            hint["note"] = "UI element - consider Canvas + UI components"
        else:
            hint["note"] = f"Use manage_gameobject(action='create', primitive_type='{row.primitive_type or 'Cube'}')"

        if row.instance_count > 1:
            hint["instance_count"] = row.instance_count
            hint["instance_note"] = f"Create {row.instance_count} instances spread {row.instance_spread}m apart"

        # Rich interaction-aware planning hints
        if row.interaction:
            hint["planning_hint"] = _build_interaction_planning_hint(row)

        hints.append(hint)

    return {
        "success": True,
        "spec": spec.model_dump(mode="json"),
        "planning_hints": hints,
        "message": f"Loaded SceneSpec '{spec.task_label}' with {len(spec.mappings)} mappings",
    }


def _build_interaction_planning_hint(row: Any) -> dict[str, Any]:
    """Build a detailed planning hint for a mapping row with an interaction spec."""
    ix = row.interaction
    sc = _as_text(row.structural_component)
    name = row.analogy_name
    hint: dict[str, Any] = {
        "scripts_needed": [],
        "vfx_needed": [],
        "animations_needed": [],
        "components_needed": [],
    }

    # Scripts
    if sc in ("profile_update", "ranking", "feedback_loop"):
        script_name = f"{name}Controller"
        attach_to = ix.target_objects[0] if ix.target_objects else name

        suggested_fields = []
        for k, v in ix.parameters.items():
            if isinstance(v, float):
                suggested_fields.append(f"float {k} = {v}f")
            elif isinstance(v, int):
                suggested_fields.append(f"int {k} = {v}")
            elif isinstance(v, str):
                suggested_fields.append(f'string {k} = "{v}"')

        if sc == "feedback_loop":
            purpose = f"Orchestrator connecting {ix.trigger_source or 'system'} -> {ix.effect} -> {ix.target_objects}"
        elif sc == "ranking":
            purpose = f"Sort/filter {ix.target_objects} based on: {ix.effect_description}"
        else:
            purpose = ix.effect_description

        hint["scripts_needed"].append({
            "name": script_name,
            "attach_to": attach_to,
            "purpose": purpose,
            "suggested_fields": suggested_fields,
            "script_policy": "Use explicit references only; do not use tag-based lookups.",
            "tool_sequence": [
                f"create_script(path='Assets/Scripts/{script_name}.cs', contents=...)",
                "refresh_unity(compile='request')",
                "refresh_unity(wait_for_ready=true)",
                f"manage_components(action='add', target='{attach_to}', component_type='{script_name}')",
            ],
        })

    if sc == "user_interaction" and _as_text(row.asset_strategy) == "vfx":
        script_name = f"{name}Trigger"
        hint["scripts_needed"].append({
            "name": script_name,
            "attach_to": ix.trigger_source or name,
            "purpose": f"Trigger script: listens for '{ix.trigger}' and fires {ix.vfx_type or 'particle effect'} on {ix.target_objects}",
            "suggested_fields": [],
            "script_policy": "Use explicit references only; do not use tag-based lookups.",
            "tool_sequence": [
                f"create_script(path='Assets/Scripts/{script_name}.cs', contents=...)",
                "refresh_unity(compile='request')",
                "refresh_unity(wait_for_ready=true)",
                f"manage_components(action='add', target='{ix.trigger_source or name}', component_type='{script_name}')",
            ],
        })

    # VFX
    if ix.vfx_type:
        vfx_hint: dict[str, Any] = {
            "type": ix.vfx_type,
            "target": name,
            "tool_sequence": [
                f"manage_components(action='add', target='{name}', component_type='ParticleSystem')",
                f"manage_vfx(action='particle_set_main', target='{name}', properties={{...}})",
                f"manage_vfx(action='particle_set_emission', target='{name}', properties={{...}})",
            ],
        }
        if ix.parameters:
            vfx_hint["suggested_params"] = {
                k: v for k, v in ix.parameters.items()
                if k in ("startColor", "startSize", "startSpeed", "duration",
                         "startLifetime", "gravityModifier", "maxParticles",
                         "rateOverTime", "shapeType", "radius")
            }
        hint["vfx_needed"].append(vfx_hint)

    # Animations
    if ix.animation_preset:
        targets = ix.target_objects or [name]
        for target in targets:
            hint["animations_needed"].append({
                "preset": ix.animation_preset,
                "target": target,
                "tool_sequence": [
                    f"manage_animation(action='clip_create_preset', target='{target}', "
                    f"properties={{preset: '{ix.animation_preset}', clipPath: 'Assets/Animations/{target}_{ix.animation_preset}.anim'}})",
                    f"manage_animation(action='controller_create', controller_path='Assets/Animations/{target}_Controller.controller')",
                    f"manage_animation(action='controller_add_state', controller_path='...', "
                    f"properties={{stateName: '{ix.animation_preset}', clipPath: '...'}})",
                    f"manage_animation(action='controller_assign', target='{target}', controller_path='...')",
                ],
            })

    # Components (colliders for proximity/collision triggers)
    if ix.trigger in ("proximity", "collision"):
        source = ix.trigger_source or name
        radius = ix.parameters.get("radius", 5.0)
        hint["components_needed"].append({
            "type": "SphereCollider",
            "target": source,
            "is_trigger": True,
            "radius": radius,
            "tool_sequence": [
                f"manage_components(action='add', target='{source}', component_type='SphereCollider')",
                f"manage_components(action='set_property', target='{source}', component_type='SphereCollider', property='isTrigger', value=true)",
                f"manage_components(action='set_property', target='{source}', component_type='SphereCollider', property='radius', value={radius})",
            ],
        })

    return hint


def _planning_result_payload(
    *,
    success: bool,
    message: str,
    warnings: list[str] | None = None,
    batch_plan: BatchExecutionPlan | None = None,
) -> dict[str, Any]:
    """Build stable planning payload for plan_and_execute and helper consumers."""
    warning_list = [str(item) for item in (warnings or []) if str(item).strip()]
    phase_names = [str(phase.phase_name) for phase in batch_plan.phases] if batch_plan is not None else []
    return {
        "success": bool(success),
        "message": str(message),
        "warnings": warning_list,
        "total_commands": int(batch_plan.total_commands) if batch_plan is not None else 0,
        "estimated_batches": int(batch_plan.estimated_batches) if batch_plan is not None else 0,
        "trellis_count": int(batch_plan.trellis_count) if batch_plan is not None else 0,
        "phase_names": phase_names,
        "manager_count": len(batch_plan.manager_tasks) if batch_plan is not None else 0,
        "script_task_count": len(batch_plan.script_tasks) if batch_plan is not None else 0,
        "batch_plan": batch_plan.model_dump(mode="json") if batch_plan is not None else None,
    }


def _build_batch_plan_from_spec_json(spec_json: str | None) -> tuple[BatchExecutionPlan | None, dict[str, Any]]:
    """Build a deterministic batch plan directly from SceneSpec JSON."""
    parsed, parse_error = _load_json_dict(spec_json, "spec_json")
    if parse_error:
        planning = _planning_result_payload(
            success=False,
            message=f"{parse_error} for plan_and_execute",
        )
        return None, planning

    try:
        spec = SceneSpec.model_validate(parsed)
    except Exception as exc:
        planning = _planning_result_payload(
            success=False,
            message=f"SceneSpec validation failed: {exc}",
        )
        return None, planning

    validator = PlanValidator(spec)
    try:
        repaired_plan = validator.validate_and_repair(MCPCallPlan())
        batch_plan = validator.to_batch_plan(repaired_plan)
    except Exception as exc:
        planning = _planning_result_payload(
            success=False,
            message=f"Plan validation failed: {exc}",
            warnings=validator.warnings,
        )
        return None, planning

    planning = _planning_result_payload(
        success=True,
        message=(
            f"Plan validated: {batch_plan.total_commands} commands in "
            f"{len(batch_plan.phases)} phases ({batch_plan.estimated_batches} batch calls). "
            f"Trellis generations: {batch_plan.trellis_count}."
        ),
        warnings=batch_plan.warnings,
        batch_plan=batch_plan,
    )
    return batch_plan, planning


def _format_plan_execute_summary(
    planning: dict[str, Any],
    execution: dict[str, Any] | None,
    final_decision: str,
    scene_saved: bool,
) -> str:
    """Build deterministic human-readable summary for UI and logs."""
    total_commands = int(planning.get("total_commands") or 0)
    estimated_batches = int(planning.get("estimated_batches") or 0)
    phase_names_raw = planning.get("phase_names")
    phase_names = [str(item) for item in phase_names_raw if str(item).strip()] if isinstance(phase_names_raw, list) else []
    phase_count = len(phase_names)

    status_text = "pass" if str(final_decision).strip().lower() == "pass" else "fail"
    failed_phase = ""
    if status_text == "fail" and isinstance(execution, dict):
        phase_results = execution.get("phase_results")
        if isinstance(phase_results, list):
            for phase in phase_results:
                if isinstance(phase, dict) and str(phase.get("status", "")).strip().lower() == "fail":
                    failed_phase = str(phase.get("phase_name", "")).strip()
                    break
    failed_fragment = f"; failed_phase={failed_phase or 'unknown'}" if status_text == "fail" else ""
    return (
        f"plan commands={total_commands}, phases={phase_count}, estimated_batches={estimated_batches}; "
        f"execution={status_text}{failed_fragment}; scene_saved={str(bool(scene_saved)).lower()}."
    )


def _handle_validate_plan(
    spec_json: str | None,
    plan_json: str | None,
) -> dict[str, Any]:
    """Validate a plan against a spec and return batch-optimized execution phases."""
    if not spec_json:
        return {"success": False, "message": "spec_json is required for validate_plan"}
    if not plan_json:
        return {"success": False, "message": "plan_json is required for validate_plan"}

    try:
        MCPCallPlan.model_validate_json(plan_json)
    except Exception as exc:
        return {"success": False, "message": f"MCPCallPlan validation failed: {exc}"}

    batch_plan, planning = _build_batch_plan_from_spec_json(spec_json)
    if not planning.get("success") or batch_plan is None:
        return {
            "success": False,
            "message": planning.get("message", "Plan validation failed."),
        }

    return {
        "success": True,
        "batch_plan": planning.get("batch_plan"),
        "manager_tasks": [task.model_dump(mode="json") for task in batch_plan.manager_tasks],
        "script_tasks": [task.model_dump(mode="json") for task in batch_plan.script_tasks],
        "message": planning.get("message", ""),
        "warnings": planning.get("warnings", []),
    }


def _chunk_commands(commands: list[dict[str, Any]], chunk_size: int | None) -> list[list[dict[str, Any]]]:
    """Chunk phase commands into bounded batches."""
    size = int(chunk_size or 40)
    if size <= 0:
        size = 40
    return [commands[i:i + size] for i in range(0, len(commands), size)]


_PREEXISTING_SCENE_TARGETS = frozenset({
    "Main Camera",
    "Directional Light",
    "Ground",
})


def _extract_command_target_references(command: dict[str, Any]) -> list[dict[str, str]]:
    """Extract GameObject-name references used by one command for preflight validation."""
    if not isinstance(command, dict):
        return []
    tool = str(command.get("tool", "")).strip()
    params = command.get("params")
    if not isinstance(params, dict):
        return []

    refs: list[dict[str, str]] = []
    if tool == "manage_gameobject":
        action = str(params.get("action", "")).strip().lower()
        if action == "create":
            parent = params.get("parent")
            if isinstance(parent, str) and parent.strip():
                refs.append({"field": "parent", "target": parent.strip()})
        else:
            target = params.get("target")
            if isinstance(target, str) and target.strip():
                refs.append({"field": "target", "target": target.strip()})
        return refs

    if tool == "manage_3d_gen":
        action = str(params.get("action", "")).strip().lower()
        if action != "generate":
            target = params.get("target")
            if isinstance(target, str) and target.strip():
                refs.append({"field": "target", "target": target.strip()})
        return refs

    if tool in {"manage_components", "manage_material", "manage_vfx"}:
        target = params.get("target")
        if isinstance(target, str) and target.strip():
            refs.append({"field": "target", "target": target.strip()})
        return refs

    if tool == "manage_animation":
        target = params.get("target")
        if isinstance(target, str) and target.strip():
            refs.append({"field": "target", "target": target.strip()})
        return refs

    return refs


def _extract_created_gameobject_name(command: dict[str, Any]) -> str | None:
    """Return created GameObject name for commands that create a scene object."""
    if not isinstance(command, dict):
        return None
    tool = str(command.get("tool", "")).strip()
    params = command.get("params")
    if not isinstance(params, dict):
        return None
    if tool == "manage_gameobject" and str(params.get("action", "")).strip().lower() == "create":
        name = params.get("name")
        if isinstance(name, str):
            text = name.strip()
            return text or None
    if tool == "manage_3d_gen" and str(params.get("action", "")).strip().lower() == "generate":
        name = params.get("target_name")
        if isinstance(name, str):
            text = name.strip()
            return text or None
    return None


def _preflight_validate_batch_plan_targets(phases: list[Any]) -> list[dict[str, Any]]:
    """Validate that command targets are resolvable from plan-created or known scene objects."""
    known_objects = set(_PREEXISTING_SCENE_TARGETS)
    failures: list[dict[str, Any]] = []
    ordered_phases = sorted(phases, key=lambda phase: int(getattr(phase, "phase_number", 0)))

    for phase in ordered_phases:
        phase_name = str(getattr(phase, "phase_name", "")).strip()
        commands = getattr(phase, "commands", [])
        if not isinstance(commands, list):
            continue
        for index, command in enumerate(commands):
            refs = _extract_command_target_references(command)
            for ref in refs:
                target = str(ref.get("target", "")).strip()
                if not target:
                    continue
                if target in known_objects:
                    continue
                failures.append({
                    "phase_name": phase_name,
                    "phase_number": int(getattr(phase, "phase_number", 0)),
                    "command_index": index,
                    "tool": str(command.get("tool", "")).strip(),
                    "target_field": str(ref.get("field", "")).strip() or "target",
                    "target": target,
                    "reason": "target_not_planned_or_known",
                })
            created_name = _extract_created_gameobject_name(command)
            if created_name:
                known_objects.add(created_name)
    return failures


async def _execute_scene_generator_command_from_plan(
    ctx: Context,
    command: dict[str, Any],
) -> dict[str, Any]:
    """Execute scene_generator phase commands embedded in a BatchExecutionPlan."""
    params = command.get("params")
    if not isinstance(params, dict):
        return {"success": False, "message": "scene_generator command params must be an object."}

    nested_action = str(params.get("action", "")).strip()
    if nested_action == "validate_essence_surface":
        return _handle_validate_essence_surface(params.get("spec_json"))
    if nested_action == "audit_batch_result":
        return _handle_audit_batch_result(
            batch_result_json=params.get("batch_result_json"),
            phase_name=params.get("phase_name"),
            phase_number=params.get("phase_number"),
            phase_context_json=params.get("phase_context_json"),
        )
    if nested_action == "smoke_test_scene":
        return await _handle_smoke_test_scene(
            ctx=ctx,
            play_seconds=params.get("play_seconds"),
            include_warnings=params.get("include_warnings"),
            fail_on_warning=params.get("fail_on_warning"),
        )
    return {
        "success": False,
        "message": f"Unsupported nested scene_generator action in batch plan: {nested_action}",
    }


async def _handle_plan_and_execute(
    ctx: Context,
    spec_json: str | None,
    max_retries_per_batch: int | None,
    retry_backoff_seconds: float | None,
    stop_on_warning: bool | None,
) -> dict[str, Any]:
    """Build deterministic batch plan from SceneSpec and execute it end-to-end."""
    batch_plan, planning = _build_batch_plan_from_spec_json(spec_json)

    if not planning.get("success") or batch_plan is None:
        final_decision = "fail"
        scene_saved = False
        summary = _format_plan_execute_summary(
            planning=planning,
            execution=None,
            final_decision=final_decision,
            scene_saved=scene_saved,
        )
        return {
            "success": False,
            "action": "plan_and_execute",
            "summary": summary,
            "message": planning.get("message", "Planning failed."),
            "planning": planning,
            "execution": None,
            "final_decision": final_decision,
            "scene_saved": scene_saved,
            "failure_stage": "planning",
        }

    execution = await _handle_execute_batch_plan(
        ctx=ctx,
        batch_plan_json=batch_plan.model_dump_json(),
        max_retries_per_batch=max_retries_per_batch,
        retry_backoff_seconds=retry_backoff_seconds,
        stop_on_warning=stop_on_warning,
    )
    success = bool(execution.get("success"))
    final_decision = "pass" if success else "fail"
    scene_saved = bool(execution.get("scene_saved"))
    summary = _format_plan_execute_summary(
        planning=planning,
        execution=execution,
        final_decision=final_decision,
        scene_saved=scene_saved,
    )
    return {
        "success": success,
        "action": "plan_and_execute",
        "summary": summary,
        "message": str(execution.get("message", planning.get("message", ""))),
        "planning": planning,
        "execution": execution,
        "final_decision": final_decision,
        "scene_saved": scene_saved,
        "failure_stage": None if success else "execution",
    }


async def _handle_execute_batch_plan(
    ctx: Context,
    batch_plan_json: str | None,
    max_retries_per_batch: int | None,
    retry_backoff_seconds: float | None,
    stop_on_warning: bool | None,
) -> dict[str, Any]:
    """Execute phased batch plan with audit, bounded retry, smoke gate, and conditional save."""
    parsed, parse_error = _load_json_dict(batch_plan_json, "batch_plan_json")
    if parse_error:
        return {"success": False, "message": parse_error}

    try:
        plan = BatchExecutionPlan.model_validate(parsed)
    except Exception as exc:
        return {"success": False, "message": f"BatchExecutionPlan validation failed: {exc}"}

    preflight_failures = _preflight_validate_batch_plan_targets(plan.phases)
    if preflight_failures:
        preview = preflight_failures[:20]
        return {
            "success": False,
            "final_decision": "fail",
            "message": "Batch plan preflight failed: unresolved command targets detected before execution.",
            "scene_saved": False,
            "phase_results": [],
            "warnings": [],
            "failures": preview,
            "preflight_failures_total": len(preflight_failures),
            "smoke_report": None,
        }

    retries_limit = int(max_retries_per_batch if max_retries_per_batch is not None else 2)
    retries_limit = max(0, min(retries_limit, 10))
    backoff_seconds = float(retry_backoff_seconds if retry_backoff_seconds is not None else 1.5)
    backoff_seconds = max(0.0, min(backoff_seconds, 10.0))
    fail_on_warning = bool(stop_on_warning) if stop_on_warning is not None else False

    unity_instance = get_unity_instance_from_context(ctx)

    phase_reports: list[dict[str, Any]] = []
    all_warnings: list[dict[str, Any]] = []
    all_failures: list[dict[str, Any]] = []
    smoke_report: dict[str, Any] | None = None
    scene_saved = False

    ordered_phases = sorted(plan.phases, key=lambda phase: int(phase.phase_number))
    for phase in ordered_phases:
        chunks = _chunk_commands(phase.commands, phase.batch_size_limit)
        phase_status = "pass"
        phase_failures: list[dict[str, Any]] = []
        phase_warnings: list[dict[str, Any]] = []
        phase_batches: list[dict[str, Any]] = []
        total_retries_used = 0

        for batch_index, command_chunk in enumerate(chunks, start=1):
            attempts = 0
            audited: dict[str, Any] | None = None
            raw_result: dict[str, Any] | None = None

            while True:
                attempts += 1
                if len(command_chunk) == 1 and str(command_chunk[0].get("tool", "")).strip() == "scene_generator":
                    raw_result = await _execute_scene_generator_command_from_plan(ctx, command_chunk[0])
                else:
                    payload: dict[str, Any] = {
                        "commands": command_chunk,
                        "parallel": bool(phase.parallel),
                        "failFast": True if phase.fail_fast is None else bool(phase.fail_fast),
                    }
                    raw = await send_with_unity_instance(
                        async_send_command_with_retry,
                        unity_instance,
                        "batch_execute",
                        payload,
                    )
                    raw_result = raw if isinstance(raw, dict) else {"success": False, "message": str(raw)}

                phase_context = {
                    "phase_name": phase.phase_name,
                    "phase_number": phase.phase_number,
                    "commands": command_chunk,
                }
                audited = _audit_batch_result_payload(
                    batch_result=raw_result,
                    phase_name=phase.phase_name,
                    phase_number=phase.phase_number,
                    phase_context=phase_context,
                )

                if str(phase.phase_name) == "smoke_test" and isinstance(raw_result, dict):
                    smoke_report = raw_result.get("smoke_report")

                warnings_for_batch = [item for item in audited.get("warnings", []) if isinstance(item, dict)]
                failures_for_batch = [item for item in audited.get("failures", []) if isinstance(item, dict)]

                if fail_on_warning and warnings_for_batch and audited.get("decision") == "pass":
                    audited["decision"] = "fail"
                    warnings_text = "; ".join(str(item.get("message", "")) for item in warnings_for_batch if item.get("message"))
                    failures_for_batch.append({
                        "index": -1,
                        "tool": "audit",
                        "message": f"Warnings treated as failure: {warnings_text or 'warning(s) present'}",
                    })
                    audited["failures"] = failures_for_batch

                if audited.get("decision") == "retry" and attempts <= retries_limit:
                    total_retries_used += 1
                    await asyncio.sleep(backoff_seconds * attempts)
                    continue
                if audited.get("decision") == "retry" and attempts > retries_limit:
                    audited["decision"] = "fail"
                    audited["failures"] = failures_for_batch + [{
                        "index": -1,
                        "tool": "batch_execute",
                        "message": (
                            f"Exceeded retry budget ({retries_limit}) for phase "
                            f"'{phase.phase_name}', batch {batch_index}."
                        ),
                    }]
                break

            if audited is None:
                audited = {
                    "decision": "fail",
                    "failures": [{
                        "index": -1,
                        "tool": "batch_execute",
                        "message": "Audit result missing.",
                    }],
                    "warnings": [],
                    "retryable": [],
                }
            if raw_result is None:
                raw_result = {"success": False, "message": "Batch result missing."}

            batch_report = {
                "batch_index": batch_index,
                "batch_count": len(chunks),
                "attempts": attempts,
                "commands_count": len(command_chunk),
                "audit": audited,
                "result": raw_result,
            }
            phase_batches.append(batch_report)

            warnings_for_batch = [item for item in audited.get("warnings", []) if isinstance(item, dict)]
            failures_for_batch = [item for item in audited.get("failures", []) if isinstance(item, dict)]
            phase_warnings.extend(warnings_for_batch)
            phase_failures.extend(failures_for_batch)
            all_warnings.extend(warnings_for_batch)
            all_failures.extend(failures_for_batch)

            if audited.get("decision") == "fail":
                phase_status = "fail"
                break

        if str(phase.phase_name) == "scene_save" and phase_status == "pass":
            scene_saved = True

        phase_reports.append({
            "phase_name": phase.phase_name,
            "phase_number": phase.phase_number,
            "status": phase_status,
            "retries_used": total_retries_used,
            "warnings": phase_warnings,
            "failures": phase_failures,
            "batches": phase_batches,
        })

        if phase_status == "fail":
            return {
                "success": False,
                "final_decision": "fail",
                "message": f"Execution failed in phase '{phase.phase_name}'.",
                "scene_saved": scene_saved,
                "phase_results": phase_reports,
                "warnings": all_warnings,
                "failures": all_failures,
                "smoke_report": smoke_report,
            }

    return {
        "success": True,
        "final_decision": "pass",
        "message": "Batch plan executed successfully.",
        "scene_saved": scene_saved,
        "phase_results": phase_reports,
        "warnings": all_warnings,
        "failures": all_failures,
        "smoke_report": smoke_report,
    }


def _canonical_component(component: str) -> str:
    text = str(component).strip().lower()
    text = "".join(ch if ch.isalnum() else "_" for ch in text)
    return "_".join(token for token in text.split("_") if token)


def _stable_hash(payload: dict[str, Any]) -> str:
    normalized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def _derive_essence(spec: SceneSpec) -> dict[str, Any]:
    mapping_role_ids: list[str] = []
    for row in spec.mappings:
        role = _canonical_component(row.structural_component)
        source = str(row.analogy_name).strip()
        if not role:
            continue
        mapping_role_ids.append(f"{role}:{source}" if source else role)

    phase_ids = [phase.phase_name for phase in spec.experience.phases if str(phase.phase_name).strip()]
    success_criteria = [item for item in spec.experience.success_criteria if str(item).strip()]
    causal_chain_ids = [step.trigger_event for step in spec.experience.causal_chain if str(step.trigger_event).strip()]

    required_managers = ["GameManager"]
    components = {_canonical_component(row.structural_component) for row in spec.mappings}
    if "user_interaction" in components:
        required_managers.append("InteractionManager")
    if "profile_update" in components or "user_profile" in components:
        required_managers.append("ProfileManager")
    if "candidate_generation" in components:
        required_managers.append("CandidateManager")
    if "ranking" in components:
        required_managers.append("RankingManager")

    return {
        "mapping_role_ids": mapping_role_ids,
        "phase_ids": phase_ids,
        "success_criteria": success_criteria,
        "causal_chain_ids": causal_chain_ids,
        "required_managers": required_managers,
        "character_role_id": "user",
        "ui_role_id": "feedback_hud",
    }


def _handle_freeze_essence(spec_path: str | None, spec_json: str | None) -> dict[str, Any]:
    load = _handle_load_spec(spec_path=spec_path, spec_json=spec_json)
    if not load.get("success"):
        return load
    spec = SceneSpec.model_validate(load.get("spec", {}))
    essence = _derive_essence(spec)
    essence_hash = _stable_hash(essence)
    return {
        "success": True,
        "essence": essence,
        "essence_hash": essence_hash,
        "message": "Essence frozen successfully.",
    }


def _handle_validate_essence_surface(spec_json: str | None) -> dict[str, Any]:
    if not spec_json:
        return {"success": False, "message": "spec_json is required for validate_essence_surface"}
    try:
        spec = SceneSpec.model_validate_json(spec_json)
    except Exception as exc:
        return {"success": False, "message": f"SceneSpec validation failed: {exc}"}

    issues: list[str] = []
    warnings: list[str] = []

    if spec.essence is not None and spec.essence_hash:
        current_hash = _stable_hash(spec.essence.model_dump(mode="json"))
        if current_hash != spec.essence_hash:
            issues.append("Essence relation changed; suggestion rejected.")

    has_character = any(
        _canonical_component(row.structural_component) == "user" and str(row.analogy_name).strip()
        for row in spec.mappings
    )
    if not has_character:
        issues.append("Character role missing in this variant.")

    if not spec.experience.feedback_hud_enabled or not spec.experience.feedback_hud_sections:
        warnings.append("UI was removed by suggestion; restored automatically.")

    validator = PlanValidator(spec)
    repaired = validator.validate_and_repair(MCPCallPlan())
    batch = validator.to_batch_plan(repaired)
    manager_names = [task.manager_name for task in batch.manager_tasks]
    if not manager_names or "GameManager" not in manager_names:
        issues.append("Manager architecture missing GameManager.")

    return {
        "success": len(issues) == 0,
        "issues": issues,
        "warnings": warnings + batch.warnings,
        "manager_names": manager_names,
        "message": "Essence/Surface validation passed." if not issues else "Essence/Surface validation failed.",
    }


def _handle_generate_surface_variant(spec_json: str | None) -> dict[str, Any]:
    if not spec_json:
        return {"success": False, "message": "spec_json is required for generate_surface_variant"}
    try:
        spec = SceneSpec.model_validate_json(spec_json)
    except Exception as exc:
        return {"success": False, "message": f"SceneSpec validation failed: {exc}"}

    surface = spec.surface.model_dump(mode="json")
    seed = int(surface.get("style_seed", 0)) + 1
    variation = str(surface.get("variation_level", "medium"))
    mood = str(surface.get("style_mood", "natural"))

    adjective = {
        "low": "subtle",
        "medium": "balanced",
        "high": "bold",
    }.get(variation, "balanced")

    suggestion = {
        "style_seed": seed,
        "style_mood": mood,
        "variation_level": variation,
        "character_style": f"{adjective}_{mood}_character",
        "asset_style": f"{adjective}_{mood}_assets",
        "ui_skin": f"{adjective}_{mood}_ui",
        "vfx_style": f"{adjective}_{mood}_vfx",
    }
    return {
        "success": True,
        "surface_suggestions": suggestion,
        "message": "Generated a new surface variant suggestion.",
    }
