"""Plan validation and batch optimization for scene generation."""
from __future__ import annotations

import math
import re
from typing import Any

from .models import (
    AssetStrategy,
    BatchExecutionPlan,
    CausalChainStep,
    EnvironmentSpec,
    ExperienceSpec,
    ExecutionPhase,
    InteractionSpec,
    IntentContract,
    ManagerTask,
    MCPCallPlan,
    MCPToolCall,
    SceneSpec,
    ScriptTask,
)

# Valid MCP tool names that can appear in plans
VALID_TOOLS = frozenset({
    "manage_gameobject",
    "manage_material",
    "manage_components",
    "manage_vfx",
    "manage_3d_gen",
    "manage_scene",
    "manage_asset",
    "manage_prefabs",
    "manage_animation",
    "manage_texture",
    "manage_shader",
    "create_script",
    "refresh_unity",
})

MAX_BATCH_SIZE = 40
SCRIPT_PHASE_BATCH_SIZE = 8
SMOKE_TEST_PHASE_BATCH_SIZE = 1
REQUIRED_PHASE_FLOW = (
    "Intro",
    "Explore",
    "Trigger",
    "Observe Feedback Loop",
    "Summary",
)
MEANINGFUL_TRIGGERS = frozenset({
    "button_press",
    "proximity",
    "collision",
    "continuous",
    "on_start",
    "custom",
})

# Skybox preset -> lighting defaults
SKYBOX_LIGHTING: dict[str, dict[str, Any]] = {
    "sunny":    {"color": [1.0, 0.95, 0.9, 1.0], "intensity": 1.0, "rotation": [50, -30, 0]},
    "sunset":   {"color": [1.0, 0.6, 0.3, 1.0],  "intensity": 0.8, "rotation": [10, -45, 0]},
    "night":    {"color": [0.4, 0.5, 0.8, 1.0],  "intensity": 0.3, "rotation": [70, -20, 0]},
    "overcast": {"color": [0.7, 0.7, 0.7, 1.0],  "intensity": 0.6, "rotation": [60, -30, 0]},
}

SUPPORTED_ANIMATION_PRESETS = frozenset({
    "bounce", "rotate", "pulse", "fade", "shake", "hover", "spin", "sway",
    "bob", "wiggle", "blink", "slide_in", "elastic", "grow", "shrink",
})

ANIMATION_PRESET_ALIASES: dict[str, str] = {
    "fade_in": "fade",
    "fade_out": "fade",
}

PARTICLE_ACTION_SUFFIXES = frozenset({
    "get_info",
    "set_main",
    "set_emission",
    "set_shape",
    "set_color_over_lifetime",
    "set_size_over_lifetime",
    "set_velocity_over_lifetime",
    "set_noise",
    "set_renderer",
    "enable_module",
    "play",
    "stop",
    "pause",
    "restart",
    "clear",
    "add_burst",
    "clear_bursts",
})

VFX_ACTION_ALIASES: dict[str, str] = {
    suffix: f"particle_{suffix}" for suffix in PARTICLE_ACTION_SUFFIXES
}

VFX_ACTION_PREFIXES = ("particle_", "vfx_", "line_", "trail_")


class PlanValidator:
    """Validates and repairs scene generation plans, then groups them into batch phases."""

    def __init__(self, spec: SceneSpec):
        self.spec = spec
        self.warnings: list[str] = []
        self.script_tasks: list[ScriptTask] = []
        self.manager_tasks: list[ManagerTask] = []
        self.experience_plan: ExperienceSpec = self.spec.experience.model_copy(deep=True)
        self._inferred_interaction_mappings: set[str] = set()
        self._runtime_ui_anchor_names: set[str] = set()

    def validate_and_repair(self, plan: MCPCallPlan) -> MCPCallPlan:
        """Validate a plan against the spec and auto-repair common issues.

        Returns the repaired plan. Warnings are accumulated in self.warnings.
        """
        self._inject_environment_calls(plan)
        self._ensure_object_create_calls(plan)
        self._repair_primitive_create_calls(plan)
        self._repair_vfx_calls(plan)
        self._filter_invalid_material_calls(plan)
        self._ensure_material_calls(plan)
        self._ensure_mapping_interactions()
        self._ensure_vfx_configuration(plan)
        self._ensure_animation_calls(plan)
        self._ensure_colliders_for_interactions(plan)
        self._generate_script_tasks()
        self.experience_plan = self._synthesize_experience_plan()
        self._generate_manager_tasks()
        self._ensure_runtime_anchors()
        self._ensure_manager_anchor_calls(plan)
        self._ensure_script_scaffolds(plan)
        self._ensure_experience_ui_calls(plan)
        self._ensure_intent_completeness(plan)
        self._deduplicate_names(plan)
        self._validate_tool_names(plan)
        self._validate_trellis_calls(plan)
        self._ensure_user_component(plan)
        self._add_scene_save(plan)
        return plan

    def to_batch_plan(self, plan: MCPCallPlan) -> BatchExecutionPlan:
        """Convert a validated MCPCallPlan into a BatchExecutionPlan with sequential phases."""
        essence_commands = [{
            "tool": "scene_generator",
            "params": {
                "action": "validate_essence_surface",
                "spec_json": self.spec.model_dump_json(),
            },
        }]
        smoke_test_commands = [{
            "tool": "scene_generator",
            "params": {
                "action": "smoke_test_scene",
                "play_seconds": 5,
                "include_warnings": True,
                "fail_on_warning": False,
            },
        }]

        phase_defs = [
            ("validate_essence", 0, essence_commands, False,
             "Validate Essence invariants and required runtime anchors before scene mutation.", SMOKE_TEST_PHASE_BATCH_SIZE, True),
            ("environment", 1, plan.environment_calls, True,
             "Ground plane, directional light, camera setup", MAX_BATCH_SIZE, True),
            ("objects", 2, plan.primitive_calls + plan.trellis_calls, True,
             "Create all primitives and start Trellis generations", MAX_BATCH_SIZE, True),
            ("materials", 3, plan.material_calls, True,
             "Apply colors and materials to objects", MAX_BATCH_SIZE, True),
            ("scripts", 4, plan.script_calls, False,
             "Create interaction scripts and trigger compilation", SCRIPT_PHASE_BATCH_SIZE, True),
            ("components_vfx", 5, plan.component_calls + plan.vfx_calls, True,
             "Add Rigidbody, colliders, particle systems, script attachment", MAX_BATCH_SIZE, True),
            ("animations", 6, plan.animation_calls, True,
             "Create animation clips, controllers, and assign to objects", MAX_BATCH_SIZE, True),
            ("hierarchy", 7, plan.hierarchy_calls, False,
             "Parent objects and final position adjustments", MAX_BATCH_SIZE, True),
            ("smoke_test", 8, smoke_test_commands, False,
             "Required gate: run Play Mode smoke test and block completion on runtime errors.", SMOKE_TEST_PHASE_BATCH_SIZE, True),
            ("scene_save", 9, plan.scene_save_calls, False,
             "Save the scene only after smoke test passes", SMOKE_TEST_PHASE_BATCH_SIZE, True),
        ]

        phases: list[ExecutionPhase] = []
        for name, number, calls, parallel, note, batch_size_limit, fail_fast in phase_defs:
            if not calls:
                continue
            commands: list[dict[str, Any]] = []
            for call in calls:
                if isinstance(call, MCPToolCall):
                    commands.append({"tool": call.tool, "params": call.params})
                elif isinstance(call, dict):
                    commands.append(call)
            phases.append(ExecutionPhase(
                phase_name=name,
                phase_number=number,
                commands=commands,
                parallel=parallel,
                note=note,
                batch_size_limit=batch_size_limit,
                fail_fast=fail_fast,
            ))

        return BatchExecutionPlan(
            phases=phases,
            warnings=self.warnings,
            script_tasks=self.script_tasks,
            manager_tasks=self.manager_tasks,
            experience_plan=self.experience_plan,
            intent_contract=self._build_intent_contract(),
            audit_rules={
                "hard_fail_patterns": [
                    "unknown action",
                    "target gameobject not found",
                    "missing target",
                    "compilation failed",
                    "exception",
                ],
                "retryable_patterns": [
                    "busy",
                    "compiling",
                    "timeout",
                    "temporarily unavailable",
                ],
                "warning_patterns": [
                    "already exists",
                    "already added",
                    "no-op",
                ],
                "banned_script_lookup_patterns": [
                    "CompareTag(",
                    "FindGameObjectsWithTag(",
                ],
            },
            smoke_test_plan={
                "required": True,
                "play_seconds": 5,
                "include_warnings": True,
                "fail_on_warning": False,
            },
        )

    def _ensure_runtime_anchors(self) -> None:
        """Enforce minimum architecture anchors for every generated experience."""
        has_character = any(
            self._canonical_component(row.structural_component) == "user" and str(row.analogy_name).strip()
            for row in self.spec.mappings
        )
        if not has_character:
            self.warnings.append("Character role missing in this variant.")

        if not self.experience_plan.feedback_hud_enabled:
            self.experience_plan.feedback_hud_enabled = True
            self.warnings.append("UI was removed by suggestion; restored automatically.")

        if not self.experience_plan.feedback_hud_sections:
            self.experience_plan.feedback_hud_sections = ExperienceSpec().feedback_hud_sections
            self.warnings.append("UI was removed by suggestion; restored automatically.")

        manager_names = {task.manager_name for task in self.manager_tasks}
        if "GameManager" not in manager_names:
            self.warnings.append("Manager architecture missing GameManager; added as required anchor.")
            self.manager_tasks.insert(0, ManagerTask(
                manager_id="manager_game_manager_auto",
                manager_name="GameManager",
                script_name="GameManager.cs",
                attach_to="GameManager",
                orchestration_scope="global",
                required_reason="Global scene coordinator required for cross-mapping orchestration.",
            ))

    def _ensure_manager_anchor_calls(self, plan: MCPCallPlan) -> None:
        """Ensure every manager task has a concrete GameObject anchor before script attachment."""
        planned_names = self._planned_gameobject_names(plan)
        for manager in self.manager_tasks:
            manager_name = str(manager.manager_name).strip()
            if not manager_name or manager_name in planned_names:
                continue
            plan.environment_calls.append(MCPToolCall(
                tool="manage_gameobject",
                params={
                    "action": "create",
                    "name": manager_name,
                    "position": [0, 0, 0],
                },
                description=f"Create manager runtime anchor '{manager_name}'",
                phase="environment",
            ))
            planned_names.add(manager_name)

    # --- Private validation methods ---

    def _inject_environment_calls(self, plan: MCPCallPlan) -> None:
        """Auto-generate Phase 1 calls from EnvironmentSpec. Replaces any existing environment calls."""
        env = self.spec.environment
        calls: list[MCPToolCall] = []

        # Ground plane
        calls.append(MCPToolCall(
            tool="manage_gameobject",
            params={
                "action": "create",
                "name": "Ground",
                "primitive_type": "Plane",
                "position": [0, 0, 0],
                "scale": env.terrain_size,
            },
            description="Create ground plane",
            phase="environment",
        ))
        calls.append(MCPToolCall(
            tool="manage_material",
            params={
                "action": "set_renderer_color",
                "target": "Ground",
                "color": env.terrain_color,
            },
            description="Set ground color",
            phase="environment",
        ))

        # Directional light
        calls.append(MCPToolCall(
            tool="manage_gameobject",
            params={
                "action": "create",
                "name": "Directional Light",
                "position": [0, 10, 0],
                "rotation": env.lighting.rotation,
            },
            description="Create directional light",
            phase="environment",
        ))
        calls.append(MCPToolCall(
            tool="manage_components",
            params={
                "action": "add",
                "target": "Directional Light",
                "component_type": "Light",
            },
            description="Add Light component to directional light",
            phase="environment",
        ))
        calls.append(MCPToolCall(
            tool="manage_components",
            params={
                "action": "set_property",
                "target": "Directional Light",
                "component_type": "Light",
                "property": "intensity",
                "value": env.lighting.intensity,
            },
            description="Set light intensity",
            phase="environment",
        ))
        if env.lighting.color != [1.0, 1.0, 1.0, 1.0]:
            calls.append(MCPToolCall(
                tool="manage_components",
                params={
                    "action": "set_property",
                    "target": "Directional Light",
                    "component_type": "Light",
                    "property": "color",
                    "value": {"r": env.lighting.color[0], "g": env.lighting.color[1],
                              "b": env.lighting.color[2], "a": env.lighting.color[3]},
                },
                description="Set light color",
                phase="environment",
            ))

        # Camera (standard interactive 3D camera)
        if not env.camera.is_vr:
            calls.append(MCPToolCall(
                tool="manage_gameobject",
                params={
                    "action": "create",
                    "name": "Main Camera",
                    "position": env.camera.position,
                    "rotation": env.camera.rotation,
                },
                description="Create main camera",
                phase="environment",
            ))
            calls.append(MCPToolCall(
                tool="manage_components",
                params={
                    "action": "add",
                    "target": "Main Camera",
                    "component_type": "Camera",
                },
                description="Add Camera component",
                phase="environment",
            ))
            if env.camera.field_of_view != 60.0:
                calls.append(MCPToolCall(
                    tool="manage_components",
                    params={
                        "action": "set_property",
                        "target": "Main Camera",
                        "component_type": "Camera",
                        "property": "fieldOfView",
                        "value": env.camera.field_of_view,
                    },
                    description="Set camera FOV",
                    phase="environment",
                ))

        plan.environment_calls = calls

    @staticmethod
    def _canonical_component(component: str) -> str:
        """Normalize structural component text for robust matching."""
        text = str(component).strip().lower()
        text = re.sub(r"[^a-z0-9]+", "_", text)
        return text.strip("_")

    def _mapping_instance_names(self, row: Any) -> list[str]:
        """Return concrete scene object names that represent one mapping row."""
        component = self._canonical_component(row.structural_component)
        if component == "content_item" and row.instance_count > 1:
            return [f"{row.analogy_name}_{i + 1}" for i in range(row.instance_count)]
        return [row.analogy_name]

    def _visual_object_names(self) -> set[str]:
        """Return object names expected to have scene GameObjects with renderers/components."""
        names = {"Ground"}
        for row in self.spec.mappings:
            if row.asset_strategy == AssetStrategy.MECHANIC:
                continue
            for name in self._mapping_instance_names(row):
                names.add(name)
        return names

    def _row_by_base_name(self, name: str) -> Any | None:
        """Find mapping row by exact analogy name or its numbered instance prefix."""
        token = str(name).strip()
        if not token:
            return None
        for row in self.spec.mappings:
            base = row.analogy_name
            if token == base or token.startswith(base + "_"):
                return row
        return None

    def _resolve_targets(self, targets: list[str], context: str) -> list[str]:
        """Resolve template names (e.g., Flower) to concrete instance names (Flower_1..N)."""
        resolved: list[str] = []
        for raw in targets:
            name = str(raw).strip()
            if not name:
                continue
            row = self._row_by_base_name(name)
            if row is None:
                resolved.append(name)
                continue
            if name == row.analogy_name:
                expanded = self._mapping_instance_names(row)
                if len(expanded) > 1:
                    self.warnings.append(
                        f"Expanded '{name}' to concrete instances for {context}: {', '.join(expanded)}"
                    )
                resolved.extend(expanded)
                continue
            resolved.append(name)
        # De-duplicate preserving order.
        deduped: list[str] = []
        seen: set[str] = set()
        for name in resolved:
            if name in seen:
                continue
            seen.add(name)
            deduped.append(name)
        return deduped

    def _resolve_single_target(self, target: str, context: str) -> str:
        """Resolve a possibly-template target name to one concrete object name."""
        name = str(target).strip()
        if not name:
            return name
        row = self._row_by_base_name(name)
        if row is None:
            return name
        if name == row.analogy_name:
            expanded = self._mapping_instance_names(row)
            if len(expanded) > 1:
                chosen = expanded[0]
                self.warnings.append(
                    f"Resolved single target '{name}' to '{chosen}' for {context}."
                )
                return chosen
        return name

    def _normalize_animation_preset(self, preset: str, mapping_name: str) -> str:
        """Map aliases and reject unsupported animation presets before command generation."""
        text = str(preset).strip().lower()
        if not text:
            return ""
        mapped = ANIMATION_PRESET_ALIASES.get(text, text)
        if mapped not in SUPPORTED_ANIMATION_PRESETS:
            self.warnings.append(
                f"Unsupported animation preset '{text}' for mapping '{mapping_name}'. Skipping animation calls."
            )
            return ""
        if mapped != text:
            self.warnings.append(
                f"Normalized animation preset '{text}' to '{mapped}' for mapping '{mapping_name}'."
            )
        return mapped

    def _ensure_object_create_calls(self, plan: MCPCallPlan) -> None:
        """Ensure every mapping with a visual asset strategy has at least one create call."""
        existing_names = set()
        for call in plan.primitive_calls + plan.trellis_calls:
            name = call.params.get("name") or call.params.get("target_name")
            if name:
                existing_names.add(name)

        for row in self.spec.mappings:
            if row.asset_strategy == AssetStrategy.MECHANIC:
                continue

            component = self._canonical_component(row.structural_component)
            count = row.instance_count if component == "content_item" else 1

            for i in range(count):
                name = row.analogy_name if count == 1 else f"{row.analogy_name}_{i + 1}"
                if name in existing_names:
                    continue

                # Calculate position for multiple instances
                pos = list(row.position)
                if count > 1 and i > 0:
                    angle = (2 * math.pi * i) / count
                    pos[0] += row.instance_spread * math.cos(angle)
                    pos[2] += row.instance_spread * math.sin(angle)

                if row.asset_strategy == AssetStrategy.TRELLIS:
                    # target_name serves as both the object name and the Trellis prompt
                    # For multiple instances, use the unique name so objects don't collide
                    trellis_target = name if count > 1 else (row.trellis_prompt or row.analogy_name)
                    plan.trellis_calls.append(MCPToolCall(
                        tool="manage_3d_gen",
                        params={
                            "action": "generate",
                            "target_name": trellis_target,
                            "position": pos,
                            "rotation": row.rotation,
                            "scale": row.scale,
                        },
                        description=f"Generate Trellis model for {name}",
                        phase="objects",
                    ))
                elif row.asset_strategy == AssetStrategy.VFX:
                    plan.primitive_calls.append(MCPToolCall(
                        tool="manage_gameobject",
                        params={
                            "action": "create",
                            "name": name,
                            "position": pos,
                            "rotation": row.rotation,
                            "scale": row.scale,
                        },
                        description=f"Create VFX host GameObject for {name}",
                        phase="objects",
                    ))
                    plan.component_calls.append(MCPToolCall(
                        tool="manage_components",
                        params={
                            "action": "add",
                            "target": name,
                            "component_type": "ParticleSystem",
                        },
                        description=f"Add ParticleSystem to {name}",
                        phase="components_vfx",
                    ))
                elif row.asset_strategy == AssetStrategy.UI:
                    component = self._canonical_component(row.structural_component)
                    primitive_type = row.primitive_type or ("Plane" if component == "candidate_generation" else "Quad")
                    plan.primitive_calls.append(MCPToolCall(
                        tool="manage_gameobject",
                        params={
                            "action": "create",
                            "name": name,
                            "primitive_type": primitive_type,
                            "position": pos,
                            "rotation": row.rotation,
                            "scale": row.scale,
                        },
                        description=f"Create UI visualization surface for {name}",
                        phase="objects",
                    ))
                else:
                    # PRIMITIVE
                    plan.primitive_calls.append(MCPToolCall(
                        tool="manage_gameobject",
                        params={
                            "action": "create",
                            "name": name,
                            "primitive_type": row.primitive_type or "Cube",
                            "position": pos,
                            "rotation": row.rotation,
                            "scale": row.scale,
                        },
                        description=f"Create {row.primitive_type or 'Cube'} for {name}",
                        phase="objects",
                    ))

                existing_names.add(name)

    def _repair_primitive_create_calls(self, plan: MCPCallPlan) -> None:
        """Normalize existing primitive create calls so they always produce renderer-backed objects."""
        for call in plan.primitive_calls:
            if call.tool != "manage_gameobject":
                continue
            if str(call.params.get("action", "")).lower() != "create":
                continue
            if call.params.get("primitive_type"):
                continue
            name = str(call.params.get("name", "")).strip()
            row = self._row_by_base_name(name)
            if row is not None and row.asset_strategy == AssetStrategy.VFX:
                # VFX host objects are intentionally empty; ParticleSystem is added in components_vfx phase.
                continue
            call.params["primitive_type"] = "Cube"
            self.warnings.append(
                f"Primitive create call for '{name}' was missing primitive_type. Defaulted to 'Cube'."
            )

    def _filter_invalid_material_calls(self, plan: MCPCallPlan) -> None:
        """Drop/repair material calls that target non-visual template rows."""
        valid_targets: set[str] = {"Ground"}

        for call in plan.primitive_calls:
            if str(call.params.get("action", "")).lower() != "create":
                continue
            if not call.params.get("primitive_type"):
                continue
            name = str(call.params.get("name", "")).strip()
            if name:
                valid_targets.add(name)

        for call in plan.trellis_calls:
            if str(call.params.get("action", "")).lower() != "generate":
                continue
            name = str(call.params.get("target_name", "")).strip()
            if name:
                valid_targets.add(name)

        repaired_calls: list[MCPToolCall] = []

        for call in plan.material_calls:
            action = str(call.params.get("action", "")).lower()
            target = str(call.params.get("target", "")).strip()
            if action != "set_renderer_color" or not target:
                repaired_calls.append(call)
                continue

            expanded_targets = self._resolve_targets([target], context="material calls")
            if not expanded_targets:
                self.warnings.append(
                    f"Removed material call with empty target: {call.description or call.params}"
                )
                continue

            for resolved_target in expanded_targets:
                if resolved_target not in valid_targets:
                    self.warnings.append(
                        f"Removed material call for non-visual target '{resolved_target}'."
                    )
                    continue
                new_params = dict(call.params)
                new_params["target"] = resolved_target
                repaired_calls.append(MCPToolCall(
                    tool=call.tool,
                    params=new_params,
                    description=call.description,
                    phase=call.phase,
                ))

        plan.material_calls = repaired_calls

    def _repair_vfx_calls(self, plan: MCPCallPlan) -> None:
        """Normalize common VFX action aliases and remove obviously invalid calls."""
        repaired_calls: list[MCPToolCall] = []

        for call in plan.vfx_calls:
            action = str(call.params.get("action", "")).strip().lower()
            if not action:
                self.warnings.append(f"Removed VFX call without action: {call.description or call.params}")
                continue

            normalized = VFX_ACTION_ALIASES.get(action, action)
            if normalized != action:
                self.warnings.append(f"Normalized VFX action '{action}' to '{normalized}'.")

            if not normalized.startswith(VFX_ACTION_PREFIXES):
                self.warnings.append(
                    f"Removed VFX call with unsupported action '{normalized}'. Expected one of prefixes: {VFX_ACTION_PREFIXES}."
                )
                continue

            if normalized.startswith("particle_"):
                suffix = normalized[len("particle_"):]
                if suffix not in PARTICLE_ACTION_SUFFIXES:
                    self.warnings.append(
                        f"Removed VFX call with unknown particle action '{normalized}'."
                    )
                    continue

            params = dict(call.params)
            params["action"] = normalized
            repaired_calls.append(MCPToolCall(
                tool=call.tool,
                params=params,
                description=call.description,
                phase=call.phase,
            ))

        plan.vfx_calls = repaired_calls

    def _ensure_material_calls(self, plan: MCPCallPlan) -> None:
        """Ensure every primitive object has at least a default material/color."""
        objects_with_material = set()
        for call in plan.material_calls:
            target = call.params.get("target")
            if target:
                objects_with_material.add(target)

        # Also check environment calls (ground already has material)
        for call in plan.environment_calls:
            if call.tool == "manage_material":
                target = call.params.get("target")
                if target:
                    objects_with_material.add(target)

        for call in plan.primitive_calls:
            if str(call.params.get("action", "")).lower() != "create":
                continue
            if not call.params.get("primitive_type"):
                continue
            name = call.params.get("name")
            if name and name not in objects_with_material:
                # Check if the mapping row has a color
                color = None
                for row in self.spec.mappings:
                    if row.analogy_name == name or name.startswith(row.analogy_name + "_"):
                        color = row.color
                        break

                plan.material_calls.append(MCPToolCall(
                    tool="manage_material",
                    params={
                        "action": "set_renderer_color",
                        "target": name,
                        "color": color or [0.7, 0.7, 0.7, 1.0],
                    },
                    description=f"Set color for {name}",
                    phase="materials",
                ))

    def _deduplicate_names(self, plan: MCPCallPlan) -> None:
        """Suffix duplicate object names."""
        seen: dict[str, int] = {}
        for call in plan.primitive_calls + plan.trellis_calls:
            name_key = "name" if "name" in call.params else "target_name"
            name = call.params.get(name_key)
            if not name:
                continue
            if name in seen:
                seen[name] += 1
                new_name = f"{name}_{seen[name]}"
                call.params[name_key] = new_name
                self.warnings.append(f"Renamed duplicate '{name}' to '{new_name}'")
            else:
                seen[name] = 1

    def _validate_tool_names(self, plan: MCPCallPlan) -> None:
        """Warn on invalid tool names."""
        for call in plan.all_calls_flat():
            if call.tool not in VALID_TOOLS:
                self.warnings.append(f"Unknown tool '{call.tool}' in plan. Valid tools: {sorted(VALID_TOOLS)}")

    def _validate_trellis_calls(self, plan: MCPCallPlan) -> None:
        """Ensure Trellis calls have required target_name parameter."""
        for call in plan.trellis_calls:
            if call.tool == "manage_3d_gen" and not call.params.get("target_name"):
                self.warnings.append(
                    f"Trellis call missing 'target_name': {call.description}"
                )

    def _ensure_user_component(self, plan: MCPCallPlan) -> None:
        """Warn if no USER structural component mapping exists."""
        has_user = any(
            self._canonical_component(row.structural_component) == "user"
            for row in self.spec.mappings
        )
        if not has_user:
            self.warnings.append(
                "No USER structural component in mappings. Interactive 3D scenes require a user representation."
            )

    def _add_scene_save(self, plan: MCPCallPlan) -> None:
        """Add a scene save call at the end if not present."""
        has_save = any(
            call.tool == "manage_scene" and call.params.get("action") == "save"
            for call in plan.scene_save_calls
        )
        if not has_save:
            plan.scene_save_calls.append(MCPToolCall(
                tool="manage_scene",
                params={"action": "save"},
                description="Save the scene",
                phase="scene_save",
            ))

    def _ensure_vfx_configuration(self, plan: MCPCallPlan) -> None:
        """For VFX mappings with interaction specs, generate configured particle system calls."""
        for row in self.spec.mappings:
            if row.asset_strategy != AssetStrategy.VFX or not row.interaction:
                continue

            ix = row.interaction
            name = row.analogy_name

            # Build particle_set_main params from interaction spec
            main_props: dict[str, Any] = {"playOnAwake": False}
            params = ix.parameters
            if "startColor" in params:
                main_props["startColor"] = params["startColor"]
            if "startSize" in params:
                main_props["startSize"] = params["startSize"]
            if "startSpeed" in params:
                main_props["startSpeed"] = params["startSpeed"]
            if "duration" in params:
                main_props["duration"] = params["duration"]
            if "startLifetime" in params:
                main_props["startLifetime"] = params["startLifetime"]
            if "gravityModifier" in params:
                main_props["gravityModifier"] = params["gravityModifier"]
            if "maxParticles" in params:
                main_props["maxParticles"] = params["maxParticles"]

            # Set defaults based on vfx_type
            if ix.vfx_type == "particle_burst":
                main_props.setdefault("duration", 0.5)
                main_props.setdefault("startLifetime", 1.0)
                main_props.setdefault("startSpeed", 3.0)
                main_props.setdefault("maxParticles", 50)
                main_props["looping"] = False
            elif ix.vfx_type == "particle_continuous":
                main_props.setdefault("duration", 5.0)
                main_props.setdefault("startLifetime", 2.0)
                main_props.setdefault("startSpeed", 1.0)
                main_props["looping"] = True
            elif ix.vfx_type == "trail":
                main_props.setdefault("startLifetime", 0.5)
                main_props.setdefault("startSpeed", 0.0)
                main_props["looping"] = True
                main_props["simulationSpace"] = "World"

            plan.vfx_calls.append(MCPToolCall(
                tool="manage_vfx",
                params={"action": "particle_set_main", "target": name, "properties": main_props},
                description=f"Configure particle main module for {name}",
                phase="components_vfx",
            ))

            # Emission settings
            emission_props: dict[str, Any] = {}
            if ix.vfx_type == "particle_burst":
                emission_props["rateOverTime"] = 0
            elif ix.vfx_type == "particle_continuous":
                emission_props["rateOverTime"] = params.get("rateOverTime", 20)
            if "rateOverDistance" in params:
                emission_props["rateOverDistance"] = params["rateOverDistance"]
            if emission_props:
                plan.vfx_calls.append(MCPToolCall(
                    tool="manage_vfx",
                    params={"action": "particle_set_emission", "target": name, "properties": emission_props},
                    description=f"Configure particle emission for {name}",
                    phase="components_vfx",
                ))

            # Shape settings
            shape_props: dict[str, Any] = {}
            if "shapeType" in params:
                shape_props["shapeType"] = params["shapeType"]
            if "radius" in params:
                shape_props["radius"] = params["radius"]
            if "angle" in params:
                shape_props["angle"] = params["angle"]
            if shape_props:
                plan.vfx_calls.append(MCPToolCall(
                    tool="manage_vfx",
                    params={"action": "particle_set_shape", "target": name, "properties": shape_props},
                    description=f"Configure particle shape for {name}",
                    phase="components_vfx",
                ))

    def _ensure_animation_calls(self, plan: MCPCallPlan) -> None:
        """For mappings with animation_preset, generate clip + controller + assign calls."""
        existing_clip_keys: set[tuple[str, str]] = set()
        existing_controller_keys: set[str] = set()
        existing_state_keys: set[tuple[str, str]] = set()
        existing_assign_keys: set[tuple[str, str]] = set()

        for call in plan.animation_calls:
            action = str(call.params.get("action", "")).strip().lower()
            if action == "clip_create_preset":
                target = str(call.params.get("target", "")).strip()
                properties = call.params.get("properties", {})
                preset = ""
                if isinstance(properties, dict):
                    preset = str(properties.get("preset", "")).strip().lower()
                if target and preset:
                    existing_clip_keys.add((target, preset))
            elif action == "controller_create":
                controller_path = str(call.params.get("controller_path", "")).strip()
                if controller_path:
                    existing_controller_keys.add(controller_path)
            elif action == "controller_add_state":
                controller_path = str(call.params.get("controller_path", "")).strip()
                properties = call.params.get("properties", {})
                state_name = ""
                if isinstance(properties, dict):
                    state_name = str(properties.get("stateName", "")).strip().lower()
                if controller_path and state_name:
                    existing_state_keys.add((controller_path, state_name))
            elif action == "controller_assign":
                target = str(call.params.get("target", "")).strip()
                controller_path = str(call.params.get("controller_path", "")).strip()
                if target and controller_path:
                    existing_assign_keys.add((target, controller_path))

        scene_object_names = self._visual_object_names()
        for row in self.spec.mappings:
            if not row.interaction or not row.interaction.animation_preset:
                continue

            ix = row.interaction
            preset = self._normalize_animation_preset(ix.animation_preset, row.analogy_name)
            if not preset:
                continue

            targets = self._resolve_targets(
                ix.target_objects or [row.analogy_name],
                context=f"animation mapping '{row.analogy_name}'",
            )
            targets = [target for target in targets if target in scene_object_names]
            if not targets:
                self.warnings.append(
                    f"No valid scene targets for animation mapping '{row.analogy_name}'. Skipping animation calls."
                )
                continue

            for target in targets:
                clip_path = f"Assets/Animations/{target}_{preset}.anim"
                controller_path = f"Assets/Animations/{target}_Controller.controller"

                clip_props: dict[str, Any] = {"preset": preset, "clipPath": clip_path}
                if "duration" in ix.parameters:
                    clip_props["duration"] = ix.parameters["duration"]
                if "amplitude" in ix.parameters:
                    clip_props["amplitude"] = ix.parameters["amplitude"]
                clip_props["loop"] = preset not in {"grow", "shrink"}

                clip_key = (target, preset)
                if clip_key not in existing_clip_keys:
                    plan.animation_calls.append(MCPToolCall(
                        tool="manage_animation",
                        params={"action": "clip_create_preset", "target": target, "properties": clip_props},
                        description=f"Create {preset} animation clip for {target}",
                        phase="animations",
                    ))
                    existing_clip_keys.add(clip_key)

                if controller_path not in existing_controller_keys:
                    plan.animation_calls.append(MCPToolCall(
                        tool="manage_animation",
                        params={"action": "controller_create", "controller_path": controller_path},
                        description=f"Create animator controller for {target}",
                        phase="animations",
                    ))
                    existing_controller_keys.add(controller_path)

                state_key = (controller_path, preset)
                if state_key not in existing_state_keys:
                    plan.animation_calls.append(MCPToolCall(
                        tool="manage_animation",
                        params={
                            "action": "controller_add_state",
                            "controller_path": controller_path,
                            "properties": {"stateName": preset, "clipPath": clip_path},
                        },
                        description=f"Add {preset} state to {target} controller",
                        phase="animations",
                    ))
                    existing_state_keys.add(state_key)

                assign_key = (target, controller_path)
                if assign_key not in existing_assign_keys:
                    plan.animation_calls.append(MCPToolCall(
                        tool="manage_animation",
                        params={"action": "controller_assign", "target": target, "controller_path": controller_path},
                        description=f"Assign animator controller to {target}",
                        phase="animations",
                    ))
                    existing_assign_keys.add(assign_key)

    def _ensure_colliders_for_interactions(self, plan: MCPCallPlan) -> None:
        """Add trigger colliders for proximity/collision-based interactions."""
        scene_object_names = self._visual_object_names()
        existing_collider_targets = {
            call.params.get("target")
            for call in plan.component_calls
            if call.params.get("component_type", "").endswith("Collider")
        }

        for row in self.spec.mappings:
            if not row.interaction:
                continue
            ix = row.interaction
            if ix.trigger not in ("proximity", "collision"):
                continue

            target = self._resolve_single_target(
                ix.trigger_source or row.analogy_name,
                context=f"collider mapping '{row.analogy_name}'",
            )
            if target not in scene_object_names:
                self.warnings.append(
                    f"Skipped collider generation for '{target}' because no scene object is planned."
                )
                continue
            if target in existing_collider_targets:
                continue

            radius = ix.parameters.get("radius", 5.0)

            plan.component_calls.append(MCPToolCall(
                tool="manage_components",
                params={"action": "add", "target": target, "component_type": "SphereCollider"},
                description=f"Add SphereCollider to {target} for {ix.trigger} detection",
                phase="components_vfx",
            ))
            plan.component_calls.append(MCPToolCall(
                tool="manage_components",
                params={
                    "action": "set_property",
                    "target": target,
                    "component_type": "SphereCollider",
                    "property": "isTrigger",
                    "value": True,
                },
                description=f"Set {target} SphereCollider as trigger",
                phase="components_vfx",
            ))
            plan.component_calls.append(MCPToolCall(
                tool="manage_components",
                params={
                    "action": "set_property",
                    "target": target,
                    "component_type": "SphereCollider",
                    "property": "radius",
                    "value": radius,
                },
                description=f"Set {target} trigger radius to {radius}",
                phase="components_vfx",
            ))
            existing_collider_targets.add(target)

    # Known component patterns from the recommendation system template
    _KNOWN_COMPONENT_PATTERNS: dict[str, str] = {
        "user_interaction": "trigger_vfx",
        "profile_update": "profile_update_logic",
        "candidate_generation": "candidate_filter_logic",
        "ranking": "ranking_logic",
        "feedback_loop": "feedback_orchestrator",
    }

    def _classify_task_kind(self, component: str, asset_strategy: AssetStrategy) -> str:
        """Determine script task_kind from component name and asset strategy."""
        if component in self._KNOWN_COMPONENT_PATTERNS:
            if component == "user_interaction" and asset_strategy == AssetStrategy.VFX:
                return "trigger_vfx"
            return self._KNOWN_COMPONENT_PATTERNS[component]
        return "interaction_logic"

    def _generate_script_tasks(self) -> None:
        """Generate structured script tasks from interaction specs."""
        self.script_tasks = []
        scene_object_names = self._visual_object_names()

        for i, row in enumerate(self.spec.mappings):
            if not row.interaction:
                continue

            ix = row.interaction
            name = row.analogy_name
            source = self._resolve_single_target(
                ix.trigger_source or name,
                context=f"script source '{name}'",
            )
            targets = self._resolve_targets(
                ix.target_objects or [name],
                context=f"script targets '{name}'",
            )
            if not targets:
                targets = [name]
            sc = self._canonical_component(row.structural_component)

            task_kind = self._classify_task_kind(sc, row.asset_strategy)

            if task_kind == "trigger_vfx":
                script_name = f"{name}Trigger"
                attach_to = source
            elif sc in ("profile_update", "ranking"):
                script_name = f"{name}Controller"
                attach_to = targets[0]
            elif sc in ("candidate_generation", "feedback_loop"):
                script_name = f"{name}Controller"
                attach_to = source
            else:
                script_name = f"{name}Controller"
                attach_to = targets[0]

            if attach_to not in scene_object_names:
                self.warnings.append(
                    f"Script task '{script_name}' had non-scene attach target '{attach_to}'. Reassigned to GameManager."
                )
                attach_to = "GameManager"

            task_id_name = "".join(ch.lower() if ch.isalnum() else "_" for ch in name).strip("_") or "mapping"
            preconditions: list[str] = []
            notes: list[str] = []
            normalized_animation_preset = self._normalize_animation_preset(ix.animation_preset, name)

            if ix.trigger in ("proximity", "collision"):
                radius = ix.parameters.get("radius", 5.0)
                preconditions.append(f"{source}:SphereCollider(isTrigger=true,radius={radius})")
            if row.asset_strategy == AssetStrategy.VFX:
                preconditions.append(f"{name}:ParticleSystemConfigured")
            if normalized_animation_preset:
                preconditions.append(f"AnimationPreset:{normalized_animation_preset}")

            if sc == "candidate_generation":
                notes.append("Track in-range candidates and keep a stable, queryable candidate set.")
            elif sc == "ranking":
                notes.append("Apply deterministic ordering for repeated runs.")
            elif sc == "feedback_loop":
                notes.append("Orchestrate profile update -> candidate generation -> ranking chain.")
            elif sc == "user_interaction":
                notes.append("Capture learner action and fan out to the next state transition.")
            notes.append(
                "Do not use tag-based lookup APIs (CompareTag/FindGameObjectsWithTag). Use explicit references/lists."
            )

            self.script_tasks.append(
                ScriptTask(
                    task_id=f"script_task_{i + 1}_{task_id_name}",
                    task_kind=task_kind,
                    mapping_name=name,
                    structural_component=sc,
                    asset_strategy=row.asset_strategy.value,
                    script_name=script_name,
                    attach_to=attach_to,
                    trigger=ix.trigger,
                    trigger_source=source,
                    target_objects=targets,
                    effect=ix.effect,
                    effect_description=ix.effect_description,
                    parameters=ix.parameters,
                    animation_preset=normalized_animation_preset,
                    vfx_type=ix.vfx_type,
                    preconditions=preconditions,
                    notes=notes,
                )
            )

    @staticmethod
    def _unique_nonempty(values: list[str]) -> list[str]:
        """Return unique, non-empty values preserving input order."""
        seen: set[str] = set()
        out: list[str] = []
        for value in values:
            text = str(value).strip()
            if not text or text in seen:
                continue
            seen.add(text)
            out.append(text)
        return out

    def _generate_manager_tasks(self) -> None:
        """Generate manager architecture tasks for orchestration.

        Strategy:
        - Always include a global GameManager.
        - Add focused managers only when the analogy mappings/interactions require them.
        - Keep feedback loop ownership in GameManager.
        """
        self.manager_tasks = []

        component_rows: dict[str, list[Any]] = {}
        interaction_rows: list[Any] = []
        for row in self.spec.mappings:
            component = self._canonical_component(row.structural_component)
            component_rows.setdefault(component, []).append(row)
            if row.interaction:
                interaction_rows.append(row)

        mapping_names = self._unique_nonempty([row.analogy_name for row in self.spec.mappings])
        triggers = self._unique_nonempty(
            [row.interaction.trigger for row in interaction_rows if row.interaction]
        )

        feedback_rows = component_rows.get("feedback_loop", [])
        game_responsibilities = [
            "Bootstrap shared runtime state and register focused managers.",
            "Route interaction events between focused managers.",
            "Own and execute the end-to-end feedback loop orchestration.",
            "Act as ExperienceDirector for learner flow: Intro -> Explore -> Trigger -> Observe Feedback Loop -> Summary.",
            "Advance experience phases based on explicit completion criteria.",
            "Drive objective/progress UI and preserve causal visibility (trigger -> immediate -> delayed -> outcome).",
        ]
        if self.experience_plan.objective:
            game_responsibilities.append(f"Primary learner objective: {self.experience_plan.objective}")
        for criterion in self.experience_plan.success_criteria:
            game_responsibilities.append(f"Success criterion: {criterion}")
        if self.experience_plan.feedback_hud_enabled:
            game_responsibilities.append(
                "Maintain a toggleable feedback HUD that exposes system state updates in real time."
            )
        for row in feedback_rows:
            if row.interaction and row.interaction.effect_description:
                game_responsibilities.append(
                    f"Feedback loop '{row.analogy_name}': {row.interaction.effect_description}"
                )

        self.manager_tasks.append(
            ManagerTask(
                manager_id="manager_game_manager",
                manager_name="GameManager",
                script_name="GameManager.cs",
                attach_to="GameManager",
                orchestration_scope="global",
                required_reason="Global scene coordinator required for cross-mapping orchestration.",
                responsibilities=self._unique_nonempty(game_responsibilities),
                creates_or_updates=[
                    "GameManager GameObject",
                    "GameManager.cs script component",
                    "Shared state: profile, candidates, ranking cache",
                    "Experience phase state machine",
                    "Objective/progress tracker",
                    "Guided prompt presenter",
                    "Feedback HUD state",
                ],
                listens_to=triggers or ["on_start"],
                emits=[
                    "OnProfileUpdated",
                    "OnCandidatesUpdated",
                    "OnRankingUpdated",
                    "OnFeedbackLoopTick",
                    "OnExperiencePhaseChanged",
                    "OnObjectiveProgressChanged",
                ],
                managed_mappings=mapping_names,
            )
        )

        manager_specs: list[dict[str, Any]] = [
            {
                "id": "profile",
                "name": "ProfileManager",
                "script": "ProfileManager.cs",
                "components": {"user_profile", "profile_update"},
                "reason": "Profile state updates are required by analogy mappings.",
                "responsibilities": [
                    "Maintain learner profile state derived from interactions.",
                    "Apply profile_update mapping effects deterministically.",
                ],
                "creates": ["Profile state model", "Profile update handlers"],
                "emits": ["OnProfileUpdated"],
            },
            {
                "id": "candidate",
                "name": "CandidateManager",
                "script": "CandidateManager.cs",
                "components": {"candidate_generation"},
                "reason": "Candidate filtering/range selection behavior is required.",
                "responsibilities": [
                    "Maintain active candidate set for content selection.",
                    "Apply candidate_generation filters (range/constraints).",
                ],
                "creates": ["Candidate set cache", "Candidate filter routines"],
                "emits": ["OnCandidatesUpdated"],
            },
            {
                "id": "ranking",
                "name": "RankingManager",
                "script": "RankingManager.cs",
                "components": {"ranking"},
                "reason": "Ranking/sorting behavior is required by analogy mappings.",
                "responsibilities": [
                    "Compute ordered ranking over active candidates.",
                    "Apply ranking interaction effects and tie-break policies.",
                ],
                "creates": ["Ranking list", "Ranking update rules"],
                "emits": ["OnRankingUpdated"],
            },
            {
                "id": "interaction",
                "name": "InteractionManager",
                "script": "InteractionManager.cs",
                "components": {"user_interaction"},
                "reason": "User-triggered interactions are present and need centralized dispatch.",
                "responsibilities": [
                    "Normalize user triggers and dispatch to GameManager pipeline.",
                    "Coordinate trigger guards/cooldowns across interaction mappings.",
                ],
                "creates": ["Trigger dispatch table", "Interaction event adapters"],
                "emits": ["OnUserInteraction"],
            },
        ]

        present_components = set(component_rows.keys())
        for spec in manager_specs:
            if not (present_components & spec["components"]):
                continue

            relevant_rows = [
                row for component in spec["components"] for row in component_rows.get(component, [])
            ]
            managed_names = self._unique_nonempty([row.analogy_name for row in relevant_rows])
            listens_to = self._unique_nonempty(
                [row.interaction.trigger for row in relevant_rows if row.interaction]
            )
            self.manager_tasks.append(
                ManagerTask(
                    manager_id=f"manager_{spec['id']}",
                    manager_name=spec["name"],
                    script_name=spec["script"],
                    attach_to=spec["name"],
                    orchestration_scope="focused",
                    required_reason=spec["reason"],
                    responsibilities=spec["responsibilities"],
                    creates_or_updates=spec["creates"],
                    listens_to=listens_to or ["OnFeedbackLoopTick"],
                    emits=spec["emits"],
                    managed_mappings=managed_names,
                )
            )

    @staticmethod
    def _safe_script_class_name(raw_name: str) -> str:
        """Convert script names into valid C# class identifiers."""
        stem = str(raw_name).strip()
        if stem.lower().endswith(".cs"):
            stem = stem[:-3]
        stem = re.sub(r"[^a-zA-Z0-9_]", "_", stem)
        stem = re.sub(r"_+", "_", stem).strip("_")
        if not stem:
            return "GeneratedScript"
        if stem[0].isdigit():
            stem = f"Script_{stem}"
        return stem

    @staticmethod
    def _escape_csharp_string(value: str) -> str:
        """Escape text for safe embedding in C# string literals."""
        return str(value).replace("\\", "\\\\").replace("\"", "\\\"")

    def _build_beginner_ui_script_contents(self) -> str:
        """Build a beginner-facing HUD script with onboarding and scene-flow guidance."""
        objective = self._escape_csharp_string(self.experience_plan.objective or "Complete one full interaction loop.")

        phase_lines: list[str] = []
        for idx, phase in enumerate(self.experience_plan.phases, start=1):
            action = str(phase.player_action).strip() or str(phase.objective).strip() or "Follow the on-screen guidance."
            phase_lines.append(f"{idx}. {phase.phase_name}: {action}")
        if not phase_lines:
            phase_lines = [
                "1. Intro: Read the objective and locate key objects.",
                "2. Explore: Learn object roles.",
                "3. Trigger: Perform the main interaction.",
                "4. Observe Feedback Loop: Watch delayed updates on HUD.",
                "5. Summary: Review what changed and why.",
            ]

        guided_lines = [str(item.prompt).strip() for item in self.experience_plan.guided_prompts if str(item.prompt).strip()]
        if not guided_lines:
            guided_lines = [
                "Activate the trigger source to start the system response.",
                "Watch HUD updates: profile, candidates, ranking.",
            ]

        section_text = ", ".join(self.experience_plan.feedback_hud_sections) if self.experience_plan.feedback_hud_sections else "Objective, Progress, Profile, Candidates, Ranking"
        controls_hint = "Move around the scene, perform the trigger action, then watch the HUD for immediate and delayed effects."
        phase_text = "\\n".join(self._escape_csharp_string(line) for line in phase_lines)
        guided_text = "\\n".join(self._escape_csharp_string(f"- {line}") for line in guided_lines[:5])
        hud_text = self._escape_csharp_string(section_text)
        controls_text = self._escape_csharp_string(controls_hint)

        return (
            "using UnityEngine;\n"
            "using UnityEngine.UI;\n\n"
            "public class BeginnerGuideUI : MonoBehaviour\n"
            "{\n"
            f"    [TextArea(4, 12)] public string objective = \"{objective}\";\n"
            "    [TextArea(8, 20)] public string phaseGuide =\n"
            f"        \"{phase_text}\";\n"
            "    [TextArea(4, 12)] public string guidedPrompts =\n"
            f"        \"{guided_text}\";\n"
            f"    [TextArea(2, 6)] public string controlsHint = \"{controls_text}\";\n"
            f"    [TextArea(2, 6)] public string hudSections = \"{hud_text}\";\n"
            "    public float autoHideSeconds = 20f;\n\n"
            "    private GameObject _panel;\n"
            "    private float _startTime;\n"
            "    private bool _hidden;\n\n"
            "    private void Start()\n"
            "    {\n"
            "        EnsureCanvasRoot();\n"
            "        BuildGuidePanel();\n"
            "        _startTime = Time.time;\n"
            "        Debug.Log(\"BeginnerGuideUI initialized.\");\n"
            "    }\n\n"
            "    private void Update()\n"
            "    {\n"
            "        if (_hidden || _panel == null)\n"
            "        {\n"
            "            return;\n"
            "        }\n"
            "        if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))\n"
            "        {\n"
            "            HideGuide();\n"
            "            return;\n"
            "        }\n"
            "        if (autoHideSeconds > 0f && Time.time - _startTime >= autoHideSeconds)\n"
            "        {\n"
            "            HideGuide();\n"
            "        }\n"
            "    }\n\n"
            "    private void HideGuide()\n"
            "    {\n"
            "        _hidden = true;\n"
            "        _panel.SetActive(false);\n"
            "    }\n\n"
            "    private void EnsureCanvasRoot()\n"
            "    {\n"
            "        var canvas = GetComponent<Canvas>();\n"
            "        if (canvas == null)\n"
            "        {\n"
            "            canvas = gameObject.AddComponent<Canvas>();\n"
            "        }\n"
            "        canvas.renderMode = RenderMode.ScreenSpaceOverlay;\n\n"
            "        var scaler = GetComponent<CanvasScaler>();\n"
            "        if (scaler == null)\n"
            "        {\n"
            "            scaler = gameObject.AddComponent<CanvasScaler>();\n"
            "        }\n"
            "        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;\n"
            "        scaler.referenceResolution = new Vector2(1920f, 1080f);\n\n"
            "        if (GetComponent<GraphicRaycaster>() == null)\n"
            "        {\n"
            "            gameObject.AddComponent<GraphicRaycaster>();\n"
            "        }\n"
            "    }\n\n"
            "    private void BuildGuidePanel()\n"
            "    {\n"
            "        _panel = new GameObject(\"HUD_BeginnerGuidePanel\", typeof(RectTransform), typeof(Image));\n"
            "        _panel.transform.SetParent(transform, false);\n\n"
            "        var panelRect = _panel.GetComponent<RectTransform>();\n"
            "        panelRect.anchorMin = new Vector2(0.02f, 0.62f);\n"
            "        panelRect.anchorMax = new Vector2(0.48f, 0.98f);\n"
            "        panelRect.offsetMin = Vector2.zero;\n"
            "        panelRect.offsetMax = Vector2.zero;\n\n"
            "        var panelImage = _panel.GetComponent<Image>();\n"
            "        panelImage.color = new Color(0f, 0f, 0f, 0.72f);\n\n"
            "        var textObj = new GameObject(\"HUD_BeginnerGuideText\", typeof(RectTransform), typeof(Text));\n"
            "        textObj.transform.SetParent(_panel.transform, false);\n\n"
            "        var textRect = textObj.GetComponent<RectTransform>();\n"
            "        textRect.anchorMin = new Vector2(0.04f, 0.06f);\n"
            "        textRect.anchorMax = new Vector2(0.96f, 0.94f);\n"
            "        textRect.offsetMin = Vector2.zero;\n"
            "        textRect.offsetMax = Vector2.zero;\n\n"
            "        var text = textObj.GetComponent<Text>();\n"
            "        text.font = Resources.GetBuiltinResource<Font>(\"Arial.ttf\");\n"
            "        text.fontSize = 24;\n"
            "        text.alignment = TextAnchor.UpperLeft;\n"
            "        text.horizontalOverflow = HorizontalWrapMode.Wrap;\n"
            "        text.verticalOverflow = VerticalWrapMode.Overflow;\n"
            "        text.color = new Color(0.95f, 0.95f, 0.95f, 1f);\n"
            "        text.text =\n"
            "            \"How to interact\\n\\n\" +\n"
            "            \"Objective: \" + objective + \"\\n\\n\" +\n"
            "            \"Scene flow:\\n\" + phaseGuide + \"\\n\\n\" +\n"
            "            \"Guided prompts:\\n\" + guidedPrompts + \"\\n\\n\" +\n"
            "            \"How the scene works: Your action triggers immediate feedback, then manager updates propagate through profile/candidates/ranking.\\n\\n\" +\n"
            "            \"HUD shows: \" + hudSections + \"\\n\\n\" +\n"
            "            \"Controls: \" + controlsHint + \"\\n\\n\" +\n"
            "            \"Tip: Press any key/click or wait to dismiss this panel.\";\n"
            "    }\n"
            "}\n"
        )

    def _to_csharp_string_array(self, values: list[str]) -> str:
        """Render a list of Python strings as a C# string array literal."""
        cleaned = [str(value).strip() for value in values if str(value).strip()]
        if not cleaned:
            return "new string[0]"
        encoded = ", ".join(f"\"{self._escape_csharp_string(value)}\"" for value in cleaned)
        return f"new string[] {{ {encoded} }}"

    def _build_manager_script_contents(self, class_name: str, summary: str) -> str:
        """Build manager scaffolds with executable state/update methods."""
        escaped_summary = self._escape_csharp_string(summary)
        objective = self._escape_csharp_string(
            self.experience_plan.objective or "Complete one full interaction loop."
        )
        progress_target = max(1, int(self.experience_plan.progress_target or 1))
        if class_name == "GameManager":
            return (
                "using System;\n"
                "using UnityEngine;\n\n"
                "public class GameManager : MonoBehaviour\n"
                "{\n"
                "    public static GameManager Instance { get; private set; }\n\n"
                "    [TextArea] public string intentSummary = "
                f"\"{escaped_summary}\";\n"
                f"    [TextArea] public string currentObjective = \"{objective}\";\n"
                f"    public int progressTarget = {progress_target};\n\n"
                "    private int _progress;\n"
                "    private string _lastTrigger = \"none\";\n"
                "    private int _candidateCount;\n"
                "    private string _topRanked = \"(none)\";\n"
                "    private Vector3 _profilePosition;\n"
                "    public event Action<string> OnStatusUpdated;\n\n"
                "    private void Awake()\n"
                "    {\n"
                "        if (Instance != null && Instance != this)\n"
                "        {\n"
                "            Destroy(gameObject);\n"
                "            return;\n"
                "        }\n"
                "        Instance = this;\n"
                "    }\n\n"
                "    public void RecordTrigger(string source, string target)\n"
                "    {\n"
                "        _lastTrigger = string.IsNullOrEmpty(source) ? \"trigger\" : source;\n"
                "        _progress = Mathf.Min(progressTarget, _progress + 1);\n"
                "        if (!string.IsNullOrEmpty(target))\n"
                "        {\n"
                "            _topRanked = target;\n"
                "        }\n"
                "        PublishStatus(\"trigger\");\n"
                "    }\n\n"
                "    public void UpdateProfileState(Vector3 profilePosition)\n"
                "    {\n"
                "        _profilePosition = profilePosition;\n"
                "        PublishStatus(\"profile\");\n"
                "    }\n\n"
                "    public void UpdateCandidateCount(int count)\n"
                "    {\n"
                "        _candidateCount = Mathf.Max(0, count);\n"
                "        PublishStatus(\"candidates\");\n"
                "    }\n\n"
                "    public void UpdateTopRanked(string target)\n"
                "    {\n"
                "        if (!string.IsNullOrEmpty(target))\n"
                "        {\n"
                "            _topRanked = target;\n"
                "            PublishStatus(\"ranking\");\n"
                "        }\n"
                "    }\n\n"
                "    public string BuildStatusLine()\n"
                "    {\n"
                "        return \"Progress \" + _progress + \"/\" + progressTarget +\n"
                "            \" | Trigger: \" + _lastTrigger +\n"
                "            \" | Candidates: \" + _candidateCount +\n"
                "            \" | Top: \" + _topRanked +\n"
                "            \" | Profile: \" + _profilePosition;\n"
                "    }\n\n"
                "    private void PublishStatus(string reason)\n"
                "    {\n"
                "        var line = BuildStatusLine();\n"
                "        Debug.Log(\"[GameManager][\" + reason + \"] \" + line);\n"
                "        OnStatusUpdated?.Invoke(line);\n"
                "    }\n"
                "}\n"
            )
        if class_name == "ProfileManager":
            return (
                "using UnityEngine;\n\n"
                "public class ProfileManager : MonoBehaviour\n"
                "{\n"
                "    public static ProfileManager Instance { get; private set; }\n"
                "    [TextArea] public string intentSummary = "
                f"\"{escaped_summary}\";\n"
                "    public Vector3 LastProfilePosition { get; private set; }\n\n"
                "    private void Awake() => Instance = this;\n"
                "    public void SetProfilePosition(Vector3 position) => LastProfilePosition = position;\n"
                "}\n"
            )
        if class_name == "CandidateManager":
            return (
                "using UnityEngine;\n\n"
                "public class CandidateManager : MonoBehaviour\n"
                "{\n"
                "    public static CandidateManager Instance { get; private set; }\n"
                "    [TextArea] public string intentSummary = "
                f"\"{escaped_summary}\";\n"
                "    public int ActiveCandidateCount { get; private set; }\n\n"
                "    private void Awake() => Instance = this;\n"
                "    public void SetCandidateCount(int count) => ActiveCandidateCount = Mathf.Max(0, count);\n"
                "}\n"
            )
        if class_name == "RankingManager":
            return (
                "using UnityEngine;\n\n"
                "public class RankingManager : MonoBehaviour\n"
                "{\n"
                "    public static RankingManager Instance { get; private set; }\n"
                "    [TextArea] public string intentSummary = "
                f"\"{escaped_summary}\";\n"
                "    public string TopResultName { get; private set; } = \"(none)\";\n\n"
                "    private void Awake() => Instance = this;\n"
                "    public void SetTopResult(string resultName)\n"
                "    {\n"
                "        if (!string.IsNullOrEmpty(resultName))\n"
                "        {\n"
                "            TopResultName = resultName;\n"
                "        }\n"
                "    }\n"
                "}\n"
            )
        if class_name == "InteractionManager":
            return (
                "using UnityEngine;\n\n"
                "public class InteractionManager : MonoBehaviour\n"
                "{\n"
                "    public static InteractionManager Instance { get; private set; }\n"
                "    [TextArea] public string intentSummary = "
                f"\"{escaped_summary}\";\n"
                "    public string LastTriggerSource { get; private set; } = \"none\";\n"
                "    public string LastTriggerTarget { get; private set; } = \"none\";\n\n"
                "    private void Awake() => Instance = this;\n"
                "    public void RegisterTrigger(string source, string target)\n"
                "    {\n"
                "        LastTriggerSource = string.IsNullOrEmpty(source) ? \"unknown\" : source;\n"
                "        LastTriggerTarget = string.IsNullOrEmpty(target) ? \"unknown\" : target;\n"
                "    }\n"
                "}\n"
            )
        return ""

    def _build_interaction_script_contents(self, class_name: str, summary: str) -> str:
        """Build functional interaction script scaffolds for generated task controllers."""
        escaped_summary = self._escape_csharp_string(summary)
        if class_name.endswith("Trigger"):
            return (
                "using System.Collections;\n"
                "using System.Collections.Generic;\n"
                "using UnityEngine;\n\n"
                f"public class {class_name} : MonoBehaviour\n"
                "{\n"
                "    [TextArea] public string intentSummary = "
                f"\"{escaped_summary}\";\n"
                "    public float aimRange = 10f;\n"
                "    public string inputButton = \"Fire1\";\n"
                "    public string targetPrefix = \"Flower\";\n\n"
                "    private readonly List<Renderer> _targets = new List<Renderer>();\n"
                "    private Camera _mainCamera;\n"
                "    private float _nextPulseAllowedAt;\n\n"
                "    private void Start()\n"
                "    {\n"
                "        _mainCamera = Camera.main;\n"
                "        ResolveTargets();\n"
                "    }\n\n"
                "    private void ResolveTargets()\n"
                "    {\n"
                "        _targets.Clear();\n"
                "        var renderers = FindObjectsOfType<Renderer>();\n"
                "        foreach (var renderer in renderers)\n"
                "        {\n"
                "            if (renderer == null || !renderer.gameObject.name.StartsWith(targetPrefix))\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            _targets.Add(renderer);\n"
                "        }\n"
                "    }\n\n"
                "    private void Update()\n"
                "    {\n"
                "        if (!Input.GetButtonDown(inputButton))\n"
                "        {\n"
                "            return;\n"
                "        }\n"
                "        var target = SelectTarget();\n"
                "        if (target == null)\n"
                "        {\n"
                "            return;\n"
                "        }\n"
                "        if (Time.time < _nextPulseAllowedAt)\n"
                "        {\n"
                "            return;\n"
                "        }\n"
                "        _nextPulseAllowedAt = Time.time + 0.12f;\n"
                "        StartCoroutine(PulseTarget(target));\n"
                "        InteractionManager.Instance?.RegisterTrigger(gameObject.name, target.gameObject.name);\n"
                "        GameManager.Instance?.RecordTrigger(gameObject.name, target.gameObject.name);\n"
                "        NotifyControllers(\"ApplyPollination\", target.transform);\n"
                "        NotifyControllers(\"RefreshCandidates\");\n"
                "        NotifyControllers(\"RefreshRanking\");\n"
                "        NotifyControllers(\"ApplyFeedback\", target.transform);\n"
                "    }\n\n"
                "    private Renderer SelectTarget()\n"
                "    {\n"
                "        if (_targets.Count == 0)\n"
                "        {\n"
                "            ResolveTargets();\n"
                "        }\n"
                "        if (_targets.Count == 0)\n"
                "        {\n"
                "            return null;\n"
                "        }\n"
                "        if (_mainCamera != null)\n"
                "        {\n"
                "            var ray = _mainCamera.ScreenPointToRay(Input.mousePosition);\n"
                "            RaycastHit hit;\n"
                "            if (Physics.Raycast(ray, out hit, aimRange))\n"
                "            {\n"
                "                var renderer = hit.collider.GetComponentInChildren<Renderer>();\n"
                "                if (renderer != null && _targets.Contains(renderer))\n"
                "                {\n"
                "                    return renderer;\n"
                "                }\n"
                "            }\n"
                "        }\n"
                "        Renderer nearest = null;\n"
                "        var bestDist = float.MaxValue;\n"
                "        var origin = transform.position;\n"
                "        foreach (var renderer in _targets)\n"
                "        {\n"
                "            if (renderer == null)\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            var dist = Vector3.SqrMagnitude(renderer.transform.position - origin);\n"
                "            if (dist < bestDist)\n"
                "            {\n"
                "                bestDist = dist;\n"
                "                nearest = renderer;\n"
                "            }\n"
                "        }\n"
                "        return nearest;\n"
                "    }\n\n"
                "    private IEnumerator PulseTarget(Renderer renderer)\n"
                "    {\n"
                "        var originalScale = renderer.transform.localScale;\n"
                "        var originalColor = renderer.material.color;\n"
                "        renderer.transform.localScale = originalScale * 1.12f;\n"
                "        renderer.material.color = Color.Lerp(originalColor, Color.yellow, 0.4f);\n"
                "        yield return new WaitForSeconds(0.2f);\n"
                "        renderer.transform.localScale = originalScale;\n"
                "        renderer.material.color = originalColor;\n"
                "    }\n\n"
                "    private void NotifyControllers(string methodName)\n"
                "    {\n"
                "        var behaviours = FindObjectsOfType<MonoBehaviour>();\n"
                "        foreach (var behaviour in behaviours)\n"
                "        {\n"
                "            if (behaviour == null || behaviour == this)\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            behaviour.SendMessage(methodName, SendMessageOptions.DontRequireReceiver);\n"
                "        }\n"
                "    }\n\n"
                "    private void NotifyControllers(string methodName, Transform payload)\n"
                "    {\n"
                "        var behaviours = FindObjectsOfType<MonoBehaviour>();\n"
                "        foreach (var behaviour in behaviours)\n"
                "        {\n"
                "            if (behaviour == null || behaviour == this)\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            behaviour.SendMessage(methodName, payload, SendMessageOptions.DontRequireReceiver);\n"
                "        }\n"
                "    }\n"
                "}\n"
            )
        if class_name.endswith("MovementController"):
            return (
                "using UnityEngine;\n\n"
                f"public class {class_name} : MonoBehaviour\n"
                "{\n"
                "    [TextArea] public string intentSummary = "
                f"\"{escaped_summary}\";\n"
                "    public float driftSpeed = 2f;\n"
                "    public string ringObjectName = \"PollenCircle\";\n\n"
                "    private Transform _target;\n"
                "    private Transform _ringTransform;\n\n"
                "    private void Start()\n"
                "    {\n"
                "        var ringObject = GameObject.Find(ringObjectName);\n"
                "        if (ringObject != null)\n"
                "        {\n"
                "            _ringTransform = ringObject.transform;\n"
                "        }\n"
                "    }\n\n"
                "    public void ApplyPollination(Transform selectedFlower)\n"
                "    {\n"
                "        _target = selectedFlower;\n"
                "    }\n\n"
                "    private void Update()\n"
                "    {\n"
                "        if (_target == null)\n"
                "        {\n"
                "            return;\n"
                "        }\n"
                "        var desired = new Vector3(_target.position.x, transform.position.y, _target.position.z);\n"
                "        transform.position = Vector3.MoveTowards(transform.position, desired, driftSpeed * Time.deltaTime);\n"
                "        if (_ringTransform != null)\n"
                "        {\n"
                "            _ringTransform.position = new Vector3(transform.position.x, _ringTransform.position.y, transform.position.z);\n"
                "        }\n"
                "        var profileManager = GameObject.Find(\"ProfileManager\");\n"
                "        if (profileManager != null)\n"
                "        {\n"
                "            profileManager.SendMessage(\"SetProfilePosition\", transform.position, SendMessageOptions.DontRequireReceiver);\n"
                "        }\n"
                "        GameManager.Instance?.UpdateProfileState(transform.position);\n"
                "    }\n"
                "}\n"
            )
        if class_name.endswith("CircleController"):
            return (
                "using System.Collections.Generic;\n"
                "using UnityEngine;\n\n"
                f"public class {class_name} : MonoBehaviour\n"
                "{\n"
                "    [TextArea] public string intentSummary = "
                f"\"{escaped_summary}\";\n"
                "    public float radius = 5f;\n"
                "    public float outsideAlpha = 0.25f;\n"
                "    public float refreshInterval = 0.25f;\n\n"
                "    private readonly List<Renderer> _allTargets = new List<Renderer>();\n"
                "    private readonly List<Renderer> _currentCandidates = new List<Renderer>();\n"
                "    private float _nextRefreshAt;\n\n"
                "    public IReadOnlyList<Renderer> CurrentCandidates => _currentCandidates;\n\n"
                "    private void Start()\n"
                "    {\n"
                "        ResolveTargets();\n"
                "        RefreshCandidates();\n"
                "    }\n\n"
                "    private void Update()\n"
                "    {\n"
                "        if (Time.time < _nextRefreshAt)\n"
                "        {\n"
                "            return;\n"
                "        }\n"
                "        _nextRefreshAt = Time.time + refreshInterval;\n"
                "        RefreshCandidates();\n"
                "    }\n\n"
                "    private void ResolveTargets()\n"
                "    {\n"
                "        _allTargets.Clear();\n"
                "        var renderers = FindObjectsOfType<Renderer>();\n"
                "        foreach (var renderer in renderers)\n"
                "        {\n"
                "            if (renderer == null || !renderer.gameObject.name.StartsWith(\"Flower\"))\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            _allTargets.Add(renderer);\n"
                "        }\n"
                "    }\n\n"
                "    public void RefreshCandidates()\n"
                "    {\n"
                "        _currentCandidates.Clear();\n"
                "        Renderer nearest = null;\n"
                "        var nearestDist = float.MaxValue;\n"
                "        foreach (var renderer in _allTargets)\n"
                "        {\n"
                "            if (renderer == null)\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            var dist = Vector3.Distance(transform.position, renderer.transform.position);\n"
                "            var inRange = dist <= radius;\n"
                "            var color = renderer.material.color;\n"
                "            color.a = inRange ? 1.0f : outsideAlpha;\n"
                "            renderer.material.color = color;\n"
                "            if (!inRange)\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            _currentCandidates.Add(renderer);\n"
                "            if (dist < nearestDist)\n"
                "            {\n"
                "                nearestDist = dist;\n"
                "                nearest = renderer;\n"
                "            }\n"
                "        }\n"
                "        var candidateManager = GameObject.Find(\"CandidateManager\");\n"
                "        if (candidateManager != null)\n"
                "        {\n"
                "            candidateManager.SendMessage(\"SetCandidateCount\", _currentCandidates.Count, SendMessageOptions.DontRequireReceiver);\n"
                "        }\n"
                "        GameManager.Instance?.UpdateCandidateCount(_currentCandidates.Count);\n"
                "        if (nearest != null)\n"
                "        {\n"
                "            GameManager.Instance?.UpdateTopRanked(nearest.gameObject.name);\n"
                "        }\n"
                "    }\n"
                "}\n"
            )
        if class_name.endswith("GrowthController"):
            return (
                "using System.Collections.Generic;\n"
                "using UnityEngine;\n\n"
                f"public class {class_name} : MonoBehaviour\n"
                "{\n"
                "    [TextArea] public string intentSummary = "
                f"\"{escaped_summary}\";\n"
                "    public int topK = 5;\n"
                "    public float rankedScale = 1.35f;\n"
                "    public float baseScale = 1.0f;\n\n"
                "    private Transform _profileAnchor;\n\n"
                "    private void Start()\n"
                "    {\n"
                "        var profile = GameObject.Find(\"Beehive\");\n"
                "        if (profile != null)\n"
                "        {\n"
                "            _profileAnchor = profile.transform;\n"
                "        }\n"
                "        RefreshRanking();\n"
                "    }\n\n"
                "    public void RefreshRanking()\n"
                "    {\n"
                "        var anchor = _profileAnchor != null ? _profileAnchor.position : transform.position;\n"
                "        var working = new List<Renderer>();\n"
                "        var renderers = FindObjectsOfType<Renderer>();\n"
                "        foreach (var renderer in renderers)\n"
                "        {\n"
                "            if (renderer == null || !renderer.gameObject.name.StartsWith(\"Flower\"))\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            if (renderer.material.color.a < 0.99f)\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            working.Add(renderer);\n"
                "        }\n"
                "        if (working.Count == 0)\n"
                "        {\n"
                "            return;\n"
                "        }\n"
                "        working.Sort((a, b) => Vector3.SqrMagnitude(a.transform.position - anchor).CompareTo(Vector3.SqrMagnitude(b.transform.position - anchor)));\n"
                "        var rankedCount = Mathf.Min(topK, working.Count);\n"
                "        for (var i = 0; i < working.Count; i++)\n"
                "        {\n"
                "            var renderer = working[i];\n"
                "            if (renderer == null)\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            renderer.transform.localScale = Vector3.one * (i < rankedCount ? rankedScale : baseScale);\n"
                "        }\n"
                "        var topName = working[0] != null ? working[0].gameObject.name : \"(none)\";\n"
                "        var rankingManager = GameObject.Find(\"RankingManager\");\n"
                "        if (rankingManager != null)\n"
                "        {\n"
                "            rankingManager.SendMessage(\"SetTopResult\", topName, SendMessageOptions.DontRequireReceiver);\n"
                "        }\n"
                "        GameManager.Instance?.UpdateTopRanked(topName);\n"
                "    }\n"
                "}\n"
            )
        if class_name.endswith("DynamicsController"):
            return (
                "using System.Collections;\n"
                "using UnityEngine;\n\n"
                f"public class {class_name} : MonoBehaviour\n"
                "{\n"
                "    [TextArea] public string intentSummary = "
                f"\"{escaped_summary}\";\n"
                "    public float delayedUpdateSeconds = 0.6f;\n\n"
                "    public void ApplyFeedback(Transform selectedTarget)\n"
                "    {\n"
                "        StartCoroutine(DelayedFeedback(selectedTarget));\n"
                "    }\n\n"
                "    private IEnumerator DelayedFeedback(Transform selectedTarget)\n"
                "    {\n"
                "        yield return new WaitForSeconds(delayedUpdateSeconds);\n"
                "        NotifyControllers(\"RefreshCandidates\");\n"
                "        NotifyControllers(\"RefreshRanking\");\n"
                "        if (selectedTarget != null)\n"
                "        {\n"
                "            selectedTarget.localScale = selectedTarget.localScale * 1.05f;\n"
                "        }\n"
                "    }\n\n"
                "    private void NotifyControllers(string methodName)\n"
                "    {\n"
                "        var behaviours = FindObjectsOfType<MonoBehaviour>();\n"
                "        foreach (var behaviour in behaviours)\n"
                "        {\n"
                "            if (behaviour == null || behaviour == this)\n"
                "            {\n"
                "                continue;\n"
                "            }\n"
                "            behaviour.SendMessage(methodName, SendMessageOptions.DontRequireReceiver);\n"
                "        }\n"
                "    }\n"
                "}\n"
            )
        return ""

    def _build_scaffold_script_contents(self, class_name: str, summary: str) -> str:
        """Build deterministic script scaffold content."""
        if class_name == "BeginnerGuideUI":
            return self._build_beginner_ui_script_contents()
        manager_script = self._build_manager_script_contents(class_name, summary)
        if manager_script:
            return manager_script
        interaction_script = self._build_interaction_script_contents(class_name, summary)
        if interaction_script:
            return interaction_script
        escaped_summary = self._escape_csharp_string(summary)
        return (
            "using UnityEngine;\n\n"
            f"public class {class_name} : MonoBehaviour\n"
            "{\n"
            "    [TextArea]\n"
            f"    public string intentSummary = \"{escaped_summary}\";\n\n"
            "    public void RunStep()\n"
            "    {\n"
            f"        Debug.Log(\"{class_name} RunStep invoked.\");\n"
            "    }\n"
            "}\n"
        )

    def _ensure_script_scaffolds(self, plan: MCPCallPlan) -> None:
        """Materialize deterministic core script scaffolds and attachment calls."""
        existing_script_paths = {
            str(call.params.get("path", "")).strip().replace("\\", "/")
            for call in plan.script_calls
            if call.tool == "create_script"
        }
        existing_component_adds = {
            (
                str(call.params.get("target", "")).strip(),
                str(call.params.get("component_type", "")).strip(),
            )
            for call in plan.component_calls
            if str(call.params.get("action", "")).lower() == "add"
        }

        scaffold_specs: list[tuple[str, str, str]] = []
        for manager in self.manager_tasks:
            scaffold_specs.append((
                manager.script_name,
                manager.attach_to,
                manager.required_reason or f"{manager.manager_name} runtime manager scaffold.",
            ))
        for task in self.script_tasks:
            scaffold_specs.append((
                task.script_name,
                task.attach_to,
                task.effect_description or f"{task.mapping_name} interaction scaffold.",
            ))
        if self.experience_plan.feedback_hud_enabled:
            scaffold_specs.append((
                "BeginnerGuideUI.cs",
                "FeedbackHUD",
                "Beginner-facing onboarding and scene guidance UI.",
            ))

        created_any_script = False
        for raw_script_name, attach_to, summary in scaffold_specs:
            class_name = self._safe_script_class_name(raw_script_name)
            script_path = f"Assets/Scripts/{class_name}.cs"
            if script_path not in existing_script_paths:
                plan.script_calls.append(MCPToolCall(
                    tool="create_script",
                    params={
                        "path": script_path,
                        "contents": self._build_scaffold_script_contents(class_name, summary),
                    },
                    description=f"Create deterministic scaffold script {class_name}",
                    phase="scripts",
                ))
                existing_script_paths.add(script_path)
                created_any_script = True

            attach_target = str(attach_to).strip() or "GameManager"
            attach_key = (attach_target, class_name)
            if attach_key in existing_component_adds:
                continue
            plan.component_calls.append(MCPToolCall(
                tool="manage_components",
                params={
                    "action": "add",
                    "target": attach_target,
                    "component_type": class_name,
                },
                description=f"Attach {class_name} to {attach_target}",
                phase="components_vfx",
            ))
            existing_component_adds.add(attach_key)

        has_compile_refresh = any(
            call.tool == "refresh_unity" and str(call.params.get("compile", "")).lower() == "request"
            for call in plan.script_calls
        )
        if created_any_script and not has_compile_refresh:
            plan.script_calls.append(MCPToolCall(
                tool="refresh_unity",
                params={"compile": "request"},
                description="Request script compilation after scaffold generation",
                phase="scripts",
            ))
            has_compile_refresh = True

        has_wait_for_ready = any(
            call.tool == "refresh_unity" and bool(call.params.get("wait_for_ready"))
            for call in plan.script_calls
        )
        if has_compile_refresh and not has_wait_for_ready:
            plan.script_calls.append(MCPToolCall(
                tool="refresh_unity",
                params={"wait_for_ready": True},
                description="Wait for Unity to finish compiling scripts before attachment",
                phase="scripts",
            ))

    def _synthesize_experience_plan(self) -> ExperienceSpec:
        """Build a robust, execution-ready experience plan from spec + interaction graph."""
        defaults = ExperienceSpec()
        plan = self.spec.experience.model_copy(deep=True)

        if not plan.objective:
            plan.objective = defaults.objective
        if not plan.success_criteria:
            plan.success_criteria = defaults.success_criteria
        if not plan.phases:
            plan.phases = defaults.phases
        if not plan.guided_prompts:
            plan.guided_prompts = defaults.guided_prompts
        if not plan.feedback_hud_sections:
            plan.feedback_hud_sections = defaults.feedback_hud_sections
        if not plan.spatial_staging:
            plan.spatial_staging = defaults.spatial_staging
        if not plan.audio_cues:
            plan.audio_cues = defaults.audio_cues
        if not plan.timing_guidelines:
            plan.timing_guidelines = defaults.timing_guidelines

        if not plan.causal_chain:
            causal_steps: list[CausalChainStep] = []
            step_index = 1
            for row in self.spec.mappings:
                if not row.interaction:
                    continue

                ix = row.interaction
                source = ix.trigger_source or row.analogy_name
                targets = ", ".join(ix.target_objects) if ix.target_objects else row.analogy_name
                effect_text = ix.effect_description or ix.effect or f"update {targets}"
                delayed_update = "Update shared manager state and propagate to dependent systems."

                component = self._canonical_component(row.structural_component)
                if component == "profile_update":
                    delayed_update = "Update profile state from interaction history."
                elif component == "candidate_generation":
                    delayed_update = "Recompute in-range candidate set."
                elif component == "ranking":
                    delayed_update = "Re-rank candidates using current profile signals."
                elif component == "feedback_loop":
                    delayed_update = "Propagate profile -> candidates -> ranking loop updates."

                causal_steps.append(CausalChainStep(
                    step=step_index,
                    trigger_event=f"{source}:{ix.trigger or 'custom'}",
                    immediate_feedback=effect_text,
                    delayed_system_update=delayed_update,
                    observable_outcome=f"Learner can observe a change on {targets}.",
                ))
                step_index += 1

            plan.causal_chain = causal_steps

        if plan.progress_target <= 0:
            plan.progress_target = defaults.progress_target
        if plan.causal_chain and plan.progress_target < min(3, len(plan.causal_chain)):
            plan.progress_target = min(3, len(plan.causal_chain))

        return plan

    @staticmethod
    def _sanitize_anchor_name(label: str) -> str:
        """Convert arbitrary labels into deterministic anchor-safe GameObject names."""
        token = re.sub(r"[^a-zA-Z0-9]+", "_", str(label).strip())
        token = token.strip("_")
        return token or "Section"

    def _planned_gameobject_names(self, plan: MCPCallPlan) -> set[str]:
        """Collect planned GameObject names from create/generate calls."""
        names: set[str] = set()
        for call in plan.environment_calls + plan.primitive_calls:
            if str(call.params.get("action", "")).lower() != "create":
                continue
            name = str(call.params.get("name", "")).strip()
            if name:
                names.add(name)
        for call in plan.trellis_calls:
            if str(call.params.get("action", "")).lower() != "generate":
                continue
            name = str(call.params.get("target_name", "")).strip()
            if name:
                names.add(name)
        return names

    def _component_add_exists(self, plan: MCPCallPlan, target: str, component_type: str) -> bool:
        """Return True when an add-component command already exists."""
        target_key = str(target).strip()
        component_key = str(component_type).strip()
        return any(
            str(call.params.get("action", "")).lower() == "add"
            and str(call.params.get("target", "")).strip() == target_key
            and str(call.params.get("component_type", "")).strip() == component_key
            for call in plan.component_calls
        )

    def _ensure_mapping_interactions(self) -> None:
        """Auto-repair missing interactions for relation/higher_order mappings."""
        learner_name = ""
        for row in self.spec.mappings:
            if self._canonical_component(row.structural_component) == "user" and str(row.analogy_name).strip():
                learner_name = str(row.analogy_name).strip()
                break

        content_names: list[str] = []
        for row in self.spec.mappings:
            if self._canonical_component(row.structural_component) != "content_item":
                continue
            content_names.extend([name for name in self._mapping_instance_names(row) if str(name).strip()])

        for row in self.spec.mappings:
            mapping_type = str(getattr(row, "mapping_type", "")).strip().lower()
            if mapping_type not in {"relation", "higher_order"}:
                continue
            if row.interaction and str(row.interaction.trigger).strip():
                continue

            component = self._canonical_component(row.structural_component)
            base_name = str(row.analogy_name).strip() or "Mapping"

            trigger = "continuous"
            if component == "user_interaction":
                trigger = "button_press"
            elif component not in {"profile_update", "candidate_generation", "ranking", "feedback_loop"}:
                trigger = "on_start"

            trigger_source = base_name
            if component == "user_interaction" and learner_name:
                trigger_source = learner_name
            elif component in {"profile_update", "candidate_generation", "ranking", "feedback_loop"}:
                trigger_source = "GameManager"

            targets = [base_name]
            if component in {"candidate_generation", "ranking", "feedback_loop", "user_interaction"} and content_names:
                targets = list(dict.fromkeys(content_names))

            effect = {
                "profile_update": "update_profile",
                "candidate_generation": "refresh_candidates",
                "ranking": "recompute_ranking",
                "feedback_loop": "propagate_feedback_loop",
                "user_interaction": "dispatch_interaction",
            }.get(component, "update_state")

            row.interaction = InteractionSpec(
                trigger=trigger,
                trigger_source=trigger_source,
                target_objects=targets,
                effect=effect,
                effect_description=(
                    f"Auto-repaired interaction for {base_name}: {trigger_source} triggers "
                    f"{effect} on {', '.join(targets)}."
                ),
                parameters={},
            )
            self._inferred_interaction_mappings.add(base_name)
            self.warnings.append(
                f"Added inferred interaction for '{base_name}' ({mapping_type}) to preserve intent completeness."
            )

    def _ensure_experience_ui_calls(self, plan: MCPCallPlan) -> None:
        """Inject minimum runtime UI anchors and manager object for learner readability."""
        planned_names = self._planned_gameobject_names(plan)

        if "GameManager" not in planned_names:
            plan.environment_calls.append(MCPToolCall(
                tool="manage_gameobject",
                params={
                    "action": "create",
                    "name": "GameManager",
                    "position": [0, 0, 0],
                },
                description="Create GameManager runtime anchor",
                phase="environment",
            ))
            planned_names.add("GameManager")
            self.warnings.append("Injected GameManager GameObject as required runtime anchor.")

        if not self.experience_plan.feedback_hud_enabled:
            self.experience_plan.feedback_hud_enabled = True
            self.warnings.append("Enabled feedback HUD to preserve learner observability.")

        if not self.experience_plan.feedback_hud_sections:
            self.experience_plan.feedback_hud_sections = ExperienceSpec().feedback_hud_sections
            self.warnings.append("Restored default feedback HUD sections for readability.")

        hud_root = "FeedbackHUD"
        if hud_root not in planned_names:
            plan.environment_calls.append(MCPToolCall(
                tool="manage_gameobject",
                params={
                    "action": "create",
                    "name": hud_root,
                    "position": [0, 1.8, 2.0],
                },
                description="Create feedback HUD root anchor",
                phase="environment",
            ))
            planned_names.add(hud_root)
            self.warnings.append("Injected feedback HUD root anchor.")
        self._runtime_ui_anchor_names.add(hud_root)

        for component_type, description in (
            ("Canvas", "Add Canvas component to feedback HUD root"),
            ("CanvasScaler", "Add CanvasScaler for resolution-aware HUD layout"),
            ("GraphicRaycaster", "Add GraphicRaycaster for interactive UI support"),
        ):
            if self._component_add_exists(plan, hud_root, component_type):
                continue
            plan.component_calls.append(MCPToolCall(
                tool="manage_components",
                params={
                    "action": "add",
                    "target": hud_root,
                    "component_type": component_type,
                },
                description=description,
                phase="components_vfx",
            ))

        existing_section_names = self._planned_gameobject_names(plan)
        for anchor_name in ("HUD_BeginnerGuide", "HUD_StatusReadout"):
            if anchor_name not in existing_section_names:
                plan.environment_calls.append(MCPToolCall(
                    tool="manage_gameobject",
                    params={
                        "action": "create",
                        "name": anchor_name,
                        "parent": hud_root,
                        "position": [0, 0, 0],
                        "scale": [0.3, 0.1, 0.3],
                    },
                    description=f"Create runtime HUD anchor '{anchor_name}'",
                    phase="environment",
                ))
                existing_section_names.add(anchor_name)
            self._runtime_ui_anchor_names.add(anchor_name)

    def _ensure_intent_completeness(self, plan: MCPCallPlan) -> None:
        """Validate core intent contract requirements and hard-fail when unrecoverable."""
        has_character = any(
            self._canonical_component(row.structural_component) == "user" and str(row.analogy_name).strip()
            for row in self.spec.mappings
        )
        if not has_character:
            self.warnings.append(
                "Character role missing in spec; runtime anchors include HUD + manager but learner role should be added."
            )

        ordered_phases = [str(phase.phase_name).strip() for phase in self.experience_plan.phases if str(phase.phase_name).strip()]
        if ordered_phases != list(REQUIRED_PHASE_FLOW):
            defaults_by_name = {phase.phase_name: phase for phase in ExperienceSpec().phases}
            repaired_phases = []
            for name in REQUIRED_PHASE_FLOW:
                existing = next((phase for phase in self.experience_plan.phases if str(phase.phase_name).strip() == name), None)
                repaired_phases.append(existing or defaults_by_name[name].model_copy(deep=True))
            self.experience_plan.phases = repaired_phases
            self.warnings.append("Repaired experience phase order to Intro -> Explore -> Trigger -> Observe Feedback Loop -> Summary.")

        if not self.experience_plan.causal_chain:
            self.experience_plan = self._synthesize_experience_plan()

        for idx, step in enumerate(self.experience_plan.causal_chain, start=1):
            if step.step <= 0:
                step.step = idx
            if not str(step.trigger_event).strip():
                step.trigger_event = f"step_{idx}:trigger"
            if not str(step.immediate_feedback).strip():
                step.immediate_feedback = "Immediate feedback is shown in scene and HUD."
            if not str(step.delayed_system_update).strip():
                step.delayed_system_update = "A delayed system update propagates through manager state."
            if not str(step.observable_outcome).strip():
                step.observable_outcome = "Learner can observe changed system output."

        has_hud = bool(self.experience_plan.feedback_hud_enabled and self.experience_plan.feedback_hud_sections)
        if not has_hud:
            self.experience_plan.feedback_hud_enabled = True
            self.experience_plan.feedback_hud_sections = ExperienceSpec().feedback_hud_sections
            self.warnings.append("Repaired missing HUD requirements to preserve readability.")

        manager_names = {task.manager_name for task in self.manager_tasks}
        if "GameManager" not in manager_names:
            self.manager_tasks.insert(0, ManagerTask(
                manager_id="manager_game_manager_auto_repair",
                manager_name="GameManager",
                script_name="GameManager.cs",
                attach_to="GameManager",
                orchestration_scope="global",
                required_reason="Auto-repair: required for intent-complete orchestration.",
            ))
            self.warnings.append("Injected missing GameManager manager task for intent completeness.")

        has_meaningful_interaction = False
        for row in self.spec.mappings:
            if not row.interaction:
                continue
            trigger = str(row.interaction.trigger).strip().lower()
            if trigger in MEANINGFUL_TRIGGERS and (
                str(row.interaction.trigger_source).strip()
                or any(str(item).strip() for item in row.interaction.target_objects)
            ):
                has_meaningful_interaction = True
                break

        if not has_meaningful_interaction:
            self._ensure_mapping_interactions()
            for row in self.spec.mappings:
                if not row.interaction:
                    continue
                trigger = str(row.interaction.trigger).strip().lower()
                if trigger in MEANINGFUL_TRIGGERS:
                    has_meaningful_interaction = True
                    break

        if not has_meaningful_interaction:
            if not self.spec.mappings:
                raise ValueError(
                    "Intent contract failed: could not recover a meaningful learner interaction trigger."
                )
            first = self.spec.mappings[0]
            first_name = str(first.analogy_name).strip() or "ExperienceAnchor"
            if not first.interaction:
                first.interaction = InteractionSpec(
                    trigger="on_start",
                    trigger_source="GameManager",
                    target_objects=[first_name],
                    effect="bootstrap_experience",
                    effect_description=(
                        "Auto-repaired bootstrap interaction so learners can observe at least one trigger path."
                    ),
                    parameters={},
                )
                self._inferred_interaction_mappings.add(first_name)
            self.warnings.append(
                f"Auto-added bootstrap interaction for '{first_name}' to satisfy intent completeness gate."
            )
            has_meaningful_interaction = True
            if not self.experience_plan.causal_chain:
                self.experience_plan = self._synthesize_experience_plan()

        if not self.experience_plan.causal_chain:
            raise ValueError(
                "Intent contract failed: causal chain is empty and could not be synthesized."
            )

    def _build_intent_contract(self) -> IntentContract:
        """Build intent-preservation contract from SceneSpec, experience plan, and inferred repairs."""
        key_relations = [str(item).strip() for item in self.spec.key_target_relations if str(item).strip()]
        if not key_relations:
            key_relations = [
                str(row.analogy_description).strip()
                for row in self.spec.mappings
                if str(getattr(row, "mapping_type", "")).strip().lower() in {"relation", "higher_order"}
                and str(row.analogy_description).strip()
            ]
        key_relations = self._unique_nonempty(key_relations)

        behavioral_mappings = self._unique_nonempty([
            str(row.analogy_name).strip()
            for row in self.spec.mappings
            if str(getattr(row, "mapping_type", "")).strip().lower() in {"relation", "higher_order"}
        ])

        explicit_mappings = self._unique_nonempty([
            str(row.analogy_name).strip()
            for row in self.spec.mappings
            if row.interaction is not None and str(row.analogy_name).strip() not in self._inferred_interaction_mappings
        ])

        inferred_mappings = sorted(self._inferred_interaction_mappings)

        ui_requirements: list[str] = []
        if self.experience_plan.feedback_hud_enabled:
            ui_requirements.append("Feedback HUD enabled")
        if self.experience_plan.feedback_hud_sections:
            ui_requirements.append(
                f"HUD sections: {', '.join(self.experience_plan.feedback_hud_sections)}"
            )
        if self._runtime_ui_anchor_names:
            ui_requirements.append(
                f"Runtime UI anchors: {', '.join(sorted(self._runtime_ui_anchor_names))}"
            )
        ui_requirements = self._unique_nonempty(ui_requirements)

        readability_requirements = self._unique_nonempty([
            "Phase order: Intro -> Explore -> Trigger -> Observe Feedback Loop -> Summary",
            "Causal chain observability: trigger -> immediate feedback -> delayed update -> observable outcome",
            "At least one meaningful trigger interaction is required",
            "GameManager orchestrates experience flow and feedback loop",
        ])

        return IntentContract(
            learner_goal=self.experience_plan.objective or self.spec.learning_goal,
            target_concept=self.spec.target_concept,
            analogy_domain=self.spec.analogy_domain,
            key_relations=key_relations,
            behavioral_mappings=behavioral_mappings,
            mappings_with_explicit_interaction=explicit_mappings,
            mappings_with_inferred_interaction=inferred_mappings,
            ui_requirements=ui_requirements,
            readability_requirements=readability_requirements,
        )
