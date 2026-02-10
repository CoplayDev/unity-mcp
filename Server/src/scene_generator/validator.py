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

MAX_BATCH_SIZE = 25

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
    "create",
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
        self._ensure_vfx_configuration(plan)
        self._ensure_animation_calls(plan)
        self._ensure_colliders_for_interactions(plan)
        self._generate_script_tasks()
        self.experience_plan = self._synthesize_experience_plan()
        self._generate_manager_tasks()
        self._deduplicate_names(plan)
        self._validate_tool_names(plan)
        self._validate_trellis_calls(plan)
        self._ensure_user_component(plan)
        self._add_scene_save(plan)
        return plan

    def to_batch_plan(self, plan: MCPCallPlan) -> BatchExecutionPlan:
        """Convert a validated MCPCallPlan into a BatchExecutionPlan with sequential phases."""
        phase_defs = [
            ("environment", 1, plan.environment_calls, True,
             "Ground plane, directional light, camera setup"),
            ("objects", 2, plan.primitive_calls + plan.trellis_calls, True,
             "Create all primitives and start Trellis generations"),
            ("materials", 3, plan.material_calls, True,
             "Apply colors and materials to objects"),
            ("scripts", 4, plan.script_calls, False,
             "Create interaction scripts and trigger compilation"),
            ("components_vfx", 5, plan.component_calls + plan.vfx_calls, True,
             "Add Rigidbody, colliders, particle systems, script attachment"),
            ("animations", 6, plan.animation_calls, True,
             "Create animation clips, controllers, and assign to objects"),
            ("hierarchy", 7, plan.hierarchy_calls, False,
             "Parent objects and final position adjustments"),
            ("scene_save", 8, plan.scene_save_calls, False,
             "Save the scene"),
        ]

        phases: list[ExecutionPhase] = []
        for name, number, calls, parallel, note in phase_defs:
            if not calls:
                continue
            commands = [{"tool": c.tool, "params": c.params} for c in calls]
            phases.append(ExecutionPhase(
                phase_name=name,
                phase_number=number,
                commands=commands,
                parallel=parallel,
                note=note,
            ))

        return BatchExecutionPlan(
            phases=phases,
            warnings=self.warnings,
            script_tasks=self.script_tasks,
            manager_tasks=self.manager_tasks,
            experience_plan=self.experience_plan,
        )

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

        # Camera (non-VR standard camera)
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
                    plan.vfx_calls.append(MCPToolCall(
                        tool="manage_vfx",
                        params={
                            "action": "particle_create",
                            "target": name,
                            "properties": {
                                "position": pos,
                            },
                        },
                        description=f"Create VFX for {name}",
                        phase="components_vfx",
                    ))
                elif row.asset_strategy == AssetStrategy.UI:
                    plan.primitive_calls.append(MCPToolCall(
                        tool="manage_gameobject",
                        params={
                            "action": "create",
                            "name": name,
                            "primitive_type": "Cube",
                            "position": pos,
                            "rotation": row.rotation,
                            "scale": [s * 0.3 for s in row.scale],
                        },
                        description=f"Create UI placeholder for {name}",
                        phase="objects",
                    ))
                    self.warnings.append(
                        f"UI asset '{name}' created as placeholder Cube. Replace with Canvas/UI in follow-up."
                    )
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
            call.params["primitive_type"] = "Cube"
            name = call.params.get("name", "(unnamed)")
            self.warnings.append(
                f"Primitive create call for '{name}' was missing primitive_type. Defaulted to 'Cube'."
            )

    def _filter_invalid_material_calls(self, plan: MCPCallPlan) -> None:
        """Drop/repair material calls that target non-visual template rows."""
        valid_targets = self._visual_object_names()
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
                "No USER structural component in mappings. VR scenes require a user representation."
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

                plan.animation_calls.append(MCPToolCall(
                    tool="manage_animation",
                    params={"action": "clip_create_preset", "target": target, "properties": clip_props},
                    description=f"Create {preset} animation clip for {target}",
                    phase="animations",
                ))

                plan.animation_calls.append(MCPToolCall(
                    tool="manage_animation",
                    params={"action": "controller_create", "controller_path": controller_path},
                    description=f"Create animator controller for {target}",
                    phase="animations",
                ))

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

                plan.animation_calls.append(MCPToolCall(
                    tool="manage_animation",
                    params={"action": "controller_assign", "target": target, "controller_path": controller_path},
                    description=f"Assign animator controller to {target}",
                    phase="animations",
                ))

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
