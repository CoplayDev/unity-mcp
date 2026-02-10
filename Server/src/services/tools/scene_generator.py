"""MCP tool for scene generation pipeline 鈥?load specs and validate plans."""
from __future__ import annotations

import json
from pathlib import Path
from typing import Annotated, Any, Literal

from fastmcp import Context

from services.registry import mcp_for_unity_tool
from scene_generator.models import (
    BatchExecutionPlan,
    MCPCallPlan,
    SceneSpec,
)
from scene_generator.validator import PlanValidator


@mcp_for_unity_tool(
    description="""Scene generation helper for EmbodiedCreate educational VR scenes.

    Actions:
    - load_spec: Load and validate a SceneSpec JSON file. Returns parsed spec with structural hints.
    - validate_plan: Validate and optimize a scene generation plan. Returns batch-optimized
      execution phases ready for sequential batch_execute calls.

    Workflow: load_spec -> LLM plans MCP calls -> validate_plan -> LLM executes batches"""
)
async def scene_generator(
    ctx: Context,
    action: Annotated[
        Literal["load_spec", "validate_plan"],
        """Action to perform:
        - load_spec: Load and validate a SceneSpec JSON file
        - validate_plan: Validate and optimize a plan into batch execution phases"""
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
) -> dict[str, Any]:
    """Load scene specs and validate/optimize generation plans."""

    if action == "load_spec":
        return _handle_load_spec(spec_path, spec_json)
    elif action == "validate_plan":
        return _handle_validate_plan(spec_json, plan_json)
    else:
        return {"success": False, "message": f"Unknown action: {action}"}


def _as_text(value: Any) -> str:
    """Return enum values or plain values consistently as strings."""
    raw = getattr(value, "value", value)
    return str(raw)


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
            hint["note"] = "Use manage_3d_gen(action='generate') 鈥?async, poll status"
        elif _as_text(row.asset_strategy) == "vfx":
            hint["note"] = "Use manage_vfx(action='particle_create') for particle effects"
        elif _as_text(row.asset_strategy) == "mechanic":
            hint["note"] = "Script/logic only 鈥?no visual asset to create"
        elif _as_text(row.asset_strategy) == "ui":
            hint["note"] = "UI element 鈥?consider Canvas + UI components"
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
            "tool_sequence": [
                f"create_script(path='Assets/Scripts/{script_name}.cs', contents=...)",
                "refresh_unity(compile='request')",
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
            "tool_sequence": [
                f"create_script(path='Assets/Scripts/{script_name}.cs', contents=...)",
                "refresh_unity(compile='request')",
                f"manage_components(action='add', target='{ix.trigger_source or name}', component_type='{script_name}')",
            ],
        })

    # VFX
    if ix.vfx_type:
        vfx_hint: dict[str, Any] = {
            "type": ix.vfx_type,
            "target": name,
            "tool_sequence": [
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
        spec = SceneSpec.model_validate_json(spec_json)
    except Exception as e:
        return {"success": False, "message": f"SceneSpec validation failed: {e}"}

    try:
        plan = MCPCallPlan.model_validate_json(plan_json)
    except Exception as e:
        return {"success": False, "message": f"MCPCallPlan validation failed: {e}"}

    validator = PlanValidator(spec)
    repaired_plan = validator.validate_and_repair(plan)
    batch_plan = validator.to_batch_plan(repaired_plan)

    return {
        "success": True,
        "batch_plan": batch_plan.model_dump(mode="json"),
        "manager_tasks": [task.model_dump(mode="json") for task in batch_plan.manager_tasks],
        "script_tasks": [task.model_dump(mode="json") for task in batch_plan.script_tasks],
        "message": (
            f"Plan validated: {batch_plan.total_commands} commands in "
            f"{len(batch_plan.phases)} phases ({batch_plan.estimated_batches} batch calls). "
            f"Trellis generations: {batch_plan.trellis_count}."
        ),
        "warnings": batch_plan.warnings,
    }


