"""Pydantic data models for the scene generation pipeline."""
from __future__ import annotations

import math
from enum import Enum
from typing import Any, Literal

from pydantic import BaseModel, Field, model_validator

DEFAULT_BATCH_SIZE_LIMIT = 40


# Domain templates: pre-defined structural component sets for common analogy domains.
# Each entry maps a domain name to a list of component definitions.
DOMAIN_TEMPLATES: dict[str, list[dict[str, str]]] = {
    "AI Recommendation System": [
        {"component": "user", "label": "Learner Role", "description": "The user/learner representation"},
        {"component": "content_item", "label": "Content Items", "description": "Items being recommended"},
        {"component": "user_profile", "label": "User Profile", "description": "Accumulated preferences"},
        {"component": "user_interaction", "label": "User Interaction", "description": "How the user acts"},
        {"component": "profile_update", "label": "Profile Update", "description": "How preferences change"},
        {"component": "candidate_generation", "label": "Candidate Generation", "description": "Narrowing options"},
        {"component": "ranking", "label": "Ranking / Sorting", "description": "Ordering candidates"},
        {"component": "feedback_loop", "label": "Feedback Loop", "description": "Self-reinforcing cycle"},
    ],
    "Custom": [],
}


class AssetStrategy(str, Enum):
    """How to create the 3D representation of a mapping row."""
    PRIMITIVE = "primitive"   # Unity primitive (cube, sphere, plane, etc.)
    TRELLIS = "trellis"       # AI-generated 3D model via manage_3d_gen
    VFX = "vfx"               # Particle system or visual effect
    MECHANIC = "mechanic"     # Game logic / script-based (no visual asset)
    UI = "ui"                 # UI element (canvas, text, gauge)


class SkyboxPreset(str, Enum):
    """Predefined skybox lighting configurations."""
    SUNNY = "sunny"
    SUNSET = "sunset"
    NIGHT = "night"
    OVERCAST = "overcast"


class LightingSpec(BaseModel):
    """Directional light configuration."""
    color: list[float] = Field(default=[1.0, 0.95, 0.9, 1.0])
    intensity: float = 1.0
    rotation: list[float] = Field(default=[50, -30, 0])
    shadow_type: str = "soft"


class CameraSpec(BaseModel):
    """Main camera setup."""
    position: list[float] = Field(default=[0, 1.6, -5])
    rotation: list[float] = Field(default=[10, 0, 0])
    field_of_view: float = 60.0
    is_vr: bool = False


class EnvironmentSpec(BaseModel):
    """Complete scene environment. Validator ensures all fields have defaults."""
    setting: str = "garden"
    terrain_type: str = "plane"
    terrain_size: list[float] = Field(default=[30, 1, 30])
    terrain_color: list[float] = Field(default=[0.3, 0.6, 0.2, 1.0])
    skybox: SkyboxPreset = SkyboxPreset.SUNNY
    skybox_material_path: str | None = None
    ambient_color: list[float] = Field(default=[0.8, 0.9, 0.7, 1.0])
    lighting: LightingSpec = Field(default_factory=LightingSpec)
    camera: CameraSpec = Field(default_factory=CameraSpec)
    description: str = ""


class InteractionSpec(BaseModel):
    """Describes the behavioral/interactive aspect of a mapping."""
    trigger: str = ""              # "button_press", "proximity", "collision", "continuous", "on_start"
    trigger_source: str = ""       # Which object triggers: "Bee", "Gardener", etc.
    target_objects: list[str] = Field(default_factory=list)  # Objects affected
    effect: str = ""               # "move_toward", "change_color", "grow", "emit_particles", "spawn"
    effect_description: str = ""   # Natural language for the LLM
    parameters: dict[str, Any] = Field(default_factory=dict)  # Numeric config
    animation_preset: str = ""     # ClipPreset: "pulse", "hover", "sway", etc.
    vfx_type: str = ""             # "particle_burst", "particle_continuous", "line_beam", "trail"


class ExperiencePhaseSpec(BaseModel):
    """One guided phase in the learner-facing experience flow."""
    phase_name: str
    objective: str = ""
    player_action: str = ""
    expected_feedback: str = ""
    completion_criteria: str = ""


class CausalChainStep(BaseModel):
    """A visible cause-and-effect step shown to the learner."""
    step: int
    trigger_event: str = ""
    immediate_feedback: str = ""
    delayed_system_update: str = ""
    observable_outcome: str = ""


class GuidedPromptSpec(BaseModel):
    """Contextual in-experience guidance shown to the learner."""
    phase_name: str = ""
    prompt: str = ""
    optional: bool = True


class SpatialZoneSpec(BaseModel):
    """Recommended spatial staging area to separate mechanics."""
    zone_name: str
    purpose: str = ""
    anchor_object: str = ""
    suggested_center: list[float] = Field(default_factory=lambda: [0.0, 0.0, 0.0])
    suggested_radius: float = 4.0


class AudioCueSpec(BaseModel):
    """Audio and timing cue to make cause/effect legible."""
    cue_name: str
    trigger: str = ""
    purpose: str = ""
    delay_seconds: float = 0.0
    volume: float = 0.6


class ExperienceSpec(BaseModel):
    """High-level learner experience design for runtime orchestration."""
    objective: str = "Complete the core interaction loop and observe how feedback changes outcomes."
    success_criteria: list[str] = Field(default_factory=lambda: [
        "Trigger at least one learner interaction.",
        "Observe one immediate visual response.",
        "Observe one delayed system update.",
        "Observe one ranking/result change.",
    ])
    progress_metric_label: str = "Loop Progress"
    progress_target: int = 3
    phases: list[ExperiencePhaseSpec] = Field(default_factory=lambda: [
        ExperiencePhaseSpec(
            phase_name="Intro",
            objective="Orient the learner to goal and controls.",
            player_action="Read objective and locate key objects.",
            expected_feedback="UI goal text and highlighted key objects.",
            completion_criteria="Learner enters Explore phase area.",
        ),
        ExperiencePhaseSpec(
            phase_name="Explore",
            objective="Understand object roles and affordances.",
            player_action="Inspect main objects and labels.",
            expected_feedback="Context prompts and role labels appear.",
            completion_criteria="Learner interacts with the trigger source at least once.",
        ),
        ExperiencePhaseSpec(
            phase_name="Trigger",
            objective="Perform the key interaction that starts the loop.",
            player_action="Activate trigger source (button/proximity/collision).",
            expected_feedback="Immediate local VFX/animation response.",
            completion_criteria="Trigger event fired and acknowledged in HUD.",
        ),
        ExperiencePhaseSpec(
            phase_name="Observe Feedback Loop",
            objective="Watch profile/candidate/ranking updates propagate.",
            player_action="Track HUD and scene changes for system updates.",
            expected_feedback="Delayed manager updates and visible outcome changes.",
            completion_criteria="At least one full cause-effect cycle observed.",
        ),
        ExperiencePhaseSpec(
            phase_name="Summary",
            objective="Consolidate what changed and why.",
            player_action="Review recap panel.",
            expected_feedback="Short explanation of causal chain and final state.",
            completion_criteria="Learner acknowledges summary.",
        ),
    ])
    guided_prompts: list[GuidedPromptSpec] = Field(default_factory=lambda: [
        GuidedPromptSpec(phase_name="Intro", prompt="Your goal: complete one full interaction loop."),
        GuidedPromptSpec(phase_name="Explore", prompt="Move closer to key objects to discover their roles."),
        GuidedPromptSpec(phase_name="Trigger", prompt="Activate the trigger source to start the system response."),
        GuidedPromptSpec(phase_name="Observe Feedback Loop", prompt="Watch HUD updates: profile, candidates, ranking."),
        GuidedPromptSpec(phase_name="Summary", prompt="Review how your action changed recommendations."),
    ])
    feedback_hud_enabled: bool = True
    feedback_hud_sections: list[str] = Field(default_factory=lambda: [
        "Current objective",
        "Progress",
        "Last trigger",
        "Profile state",
        "Candidates",
        "Top-ranked result",
    ])
    spatial_staging: list[SpatialZoneSpec] = Field(default_factory=lambda: [
        SpatialZoneSpec(zone_name="Intro Zone", purpose="Onboarding and objective briefing", suggested_center=[0.0, 0.0, -6.0], suggested_radius=3.0),
        SpatialZoneSpec(zone_name="Interaction Zone", purpose="Primary trigger actions", suggested_center=[0.0, 0.0, 0.0], suggested_radius=4.5),
        SpatialZoneSpec(zone_name="System Response Zone", purpose="Observe delayed updates and outcomes", suggested_center=[8.0, 0.0, 0.0], suggested_radius=4.5),
    ])
    audio_cues: list[AudioCueSpec] = Field(default_factory=lambda: [
        AudioCueSpec(cue_name="trigger_click", trigger="on_trigger", purpose="Confirm action occurred", delay_seconds=0.0, volume=0.7),
        AudioCueSpec(cue_name="system_update", trigger="on_profile_or_candidate_update", purpose="Signal delayed system response", delay_seconds=0.4, volume=0.55),
        AudioCueSpec(cue_name="success_chime", trigger="on_success_criteria_met", purpose="Reinforce completion", delay_seconds=0.0, volume=0.75),
    ])
    timing_guidelines: dict[str, float] = Field(default_factory=lambda: {
        "immediate_feedback_delay_seconds": 0.1,
        "delayed_update_delay_seconds": 0.6,
        "summary_delay_seconds": 0.5,
    })
    causal_chain: list[CausalChainStep] = Field(default_factory=list)


class EssenceSpec(BaseModel):
    """Semantic structure that should remain unchanged across surface variants."""
    mapping_role_ids: list[str] = Field(default_factory=list)
    phase_ids: list[str] = Field(default_factory=list)
    success_criteria: list[str] = Field(default_factory=list)
    causal_chain_ids: list[str] = Field(default_factory=list)
    required_managers: list[str] = Field(default_factory=lambda: ["GameManager"])
    character_role_id: str = "user"
    ui_role_id: str = "feedback_hud"


class SurfaceSpec(BaseModel):
    """Presentation layer that can vary while preserving the essence."""
    style_seed: int = 0
    style_mood: Literal["natural", "playful", "futuristic"] = "natural"
    variation_level: Literal["low", "medium", "high"] = "medium"
    character_style: str = "default"
    asset_style: str = "default"
    ui_skin: str = "default"
    vfx_style: str = "default"


class MappingRow(BaseModel):
    """One row of the teacher's mapping table."""
    structural_component: str
    analogy_name: str
    analogy_description: str = ""
    asset_strategy: AssetStrategy = AssetStrategy.PRIMITIVE

    # Mapping enrichment fields (from proposed table Phase 2)
    mapping_type: Literal["object", "attribute", "relation", "higher_order"] = "relation"
    mapping_confidence: Literal["strong", "moderate", "weak"] = "strong"

    # Asset parameters (strategy-dependent)
    primitive_type: str | None = None       # "Cube", "Sphere", "Cylinder", etc.
    trellis_prompt: str | None = None       # Text prompt for Trellis generation
    position: list[float] = Field(default=[0, 0, 0])
    rotation: list[float] = Field(default=[0, 0, 0])
    scale: list[float] = Field(default=[1, 1, 1])
    color: list[float] | None = None        # RGBA
    parent: str | None = None

    # For content_item with multiple instances
    instance_count: int = 1
    instance_spread: float = 3.0            # Spacing between instances

    # Interaction/behavior specification (optional)
    interaction: InteractionSpec | None = None

    @model_validator(mode="after")
    def _default_primitive_type(self) -> "MappingRow":
        if self.asset_strategy == AssetStrategy.PRIMITIVE and self.primitive_type is None:
            self.primitive_type = "Cube"
        return self


class SceneSpec(BaseModel):
    """Top-level scene specification written by the teacher."""
    target_concept: str                     # e.g. "AI Recommendation System"
    analogy_domain: str                     # e.g. "Bee Pollination in a Garden"
    learning_goal: str = ""
    task_label: str = ""                    # e.g. "Task 1: Beehive Analogy"
    # Phase 1 Focus fields (from proposed table)
    prerequisite_knowledge: str = ""
    key_target_relations: list[str] = Field(default_factory=list)
    mappings: list[MappingRow]
    environment: EnvironmentSpec = Field(default_factory=EnvironmentSpec)
    experience: ExperienceSpec = Field(default_factory=ExperienceSpec)
    essence: EssenceSpec | None = None
    surface: SurfaceSpec = Field(default_factory=SurfaceSpec)
    essence_hash: str | None = None


# --- Reflection model (Phase 4 output) ---

class ReflectionResult(BaseModel):
    """LLM-generated evaluation of analogy quality (Phase 4)."""
    structural_completeness: float = 0.0        # 0-1 score
    structural_completeness_notes: str = ""
    embodiment_quality: float = 0.0
    embodiment_quality_notes: str = ""
    cognitive_load: float = 0.0                  # 0-1, lower is better
    cognitive_load_notes: str = ""
    misconception_risks: list[str] = Field(default_factory=list)
    unlikes: list[dict[str, str]] = Field(default_factory=list)  # [{mapping, breakdown, suggestion}]
    strengths: list[str] = Field(default_factory=list)
    suggestions: list[str] = Field(default_factory=list)
    overall_score: float = 0.0


# --- Plan models ---

class MCPToolCall(BaseModel):
    """A single MCP tool call to be executed."""
    tool: str
    params: dict[str, Any]
    description: str = ""
    phase: str = ""                         # Which execution phase this belongs to


class MCPCallPlan(BaseModel):
    """Raw plan of MCP tool calls, organized by category."""
    environment_calls: list[MCPToolCall] = Field(default_factory=list)
    primitive_calls: list[MCPToolCall] = Field(default_factory=list)
    trellis_calls: list[MCPToolCall] = Field(default_factory=list)
    material_calls: list[MCPToolCall] = Field(default_factory=list)
    script_calls: list[MCPToolCall] = Field(default_factory=list)
    component_calls: list[MCPToolCall] = Field(default_factory=list)
    vfx_calls: list[MCPToolCall] = Field(default_factory=list)
    animation_calls: list[MCPToolCall] = Field(default_factory=list)
    hierarchy_calls: list[MCPToolCall] = Field(default_factory=list)
    scene_save_calls: list[MCPToolCall] = Field(default_factory=list)

    def all_calls_flat(self) -> list[MCPToolCall]:
        """Return all calls in phase order as a flat list."""
        return (
            self.environment_calls
            + self.primitive_calls
            + self.trellis_calls
            + self.material_calls
            + self.script_calls
            + self.component_calls
            + self.vfx_calls
            + self.animation_calls
            + self.hierarchy_calls
            + self.scene_save_calls
        )


class ExecutionPhase(BaseModel):
    """One phase of the batch execution plan."""
    phase_name: str
    phase_number: int
    commands: list[dict[str, Any]]          # [{tool, params}] ready for batch_execute
    parallel: bool = True
    note: str = ""
    batch_size_limit: int | None = None
    fail_fast: bool | None = None


class ScriptTask(BaseModel):
    """Structured script-writing task derived from an interaction mapping."""
    task_id: str
    task_kind: str
    mapping_name: str
    structural_component: str
    asset_strategy: str
    script_name: str
    attach_to: str
    trigger: str = ""
    trigger_source: str = ""
    target_objects: list[str] = Field(default_factory=list)
    effect: str = ""
    effect_description: str = ""
    parameters: dict[str, Any] = Field(default_factory=dict)
    animation_preset: str = ""
    vfx_type: str = ""
    preconditions: list[str] = Field(default_factory=list)
    notes: list[str] = Field(default_factory=list)


class ManagerTask(BaseModel):
    """Structured manager orchestration task for scene runtime architecture."""
    manager_id: str
    manager_name: str
    script_name: str
    attach_to: str
    orchestration_scope: Literal["global", "focused"] = "focused"
    required_reason: str = ""
    responsibilities: list[str] = Field(default_factory=list)
    creates_or_updates: list[str] = Field(default_factory=list)
    listens_to: list[str] = Field(default_factory=list)
    emits: list[str] = Field(default_factory=list)
    managed_mappings: list[str] = Field(default_factory=list)


class IntentContract(BaseModel):
    """Execution-time contract that preserves learner intent in generated scenes."""
    learner_goal: str = ""
    target_concept: str = ""
    analogy_domain: str = ""
    key_relations: list[str] = Field(default_factory=list)
    behavioral_mappings: list[str] = Field(default_factory=list)
    mappings_with_explicit_interaction: list[str] = Field(default_factory=list)
    mappings_with_inferred_interaction: list[str] = Field(default_factory=list)
    ui_requirements: list[str] = Field(default_factory=list)
    readability_requirements: list[str] = Field(default_factory=list)


class BatchExecutionPlan(BaseModel):
    """The final output of validate_plan — ready for sequential batch_execute calls."""
    phases: list[ExecutionPhase]
    total_commands: int = 0
    estimated_batches: int = 0
    trellis_count: int = 0
    warnings: list[str] = Field(default_factory=list)
    script_tasks: list[ScriptTask] = Field(default_factory=list)
    manager_tasks: list[ManagerTask] = Field(default_factory=list)
    experience_plan: ExperienceSpec = Field(default_factory=ExperienceSpec)
    intent_contract: IntentContract = Field(default_factory=IntentContract)
    audit_rules: dict[str, Any] = Field(default_factory=dict)
    smoke_test_plan: dict[str, Any] = Field(default_factory=dict)

    @model_validator(mode="after")
    def _compute_stats(self) -> "BatchExecutionPlan":
        self.total_commands = sum(len(p.commands) for p in self.phases)
        estimated_batches = 0
        for phase in self.phases:
            command_count = len(phase.commands)
            if command_count == 0:
                continue
            limit = phase.batch_size_limit or DEFAULT_BATCH_SIZE_LIMIT
            if limit <= 0:
                limit = DEFAULT_BATCH_SIZE_LIMIT
            estimated_batches += max(1, math.ceil(command_count / limit))
        self.estimated_batches = estimated_batches
        self.trellis_count = sum(
            1 for p in self.phases
            for cmd in p.commands
            if cmd.get("tool") == "manage_3d_gen"
        )
        return self
