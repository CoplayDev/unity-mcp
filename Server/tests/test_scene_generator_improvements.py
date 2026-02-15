"""Tests for scene generator reliability and schema guardrails."""
from __future__ import annotations

import asyncio
import json
from pathlib import Path
from typing import Any

import pytest
from pydantic import ValidationError

from scene_generator.models import BatchExecutionPlan, ExecutionPhase, MCPCallPlan, MCPToolCall, SceneSpec
from scene_generator.validator import PlanValidator
from services.tools.scene_generator import (
    _audit_batch_result_payload,
    _handle_execute_batch_plan,
    _handle_plan_and_execute,
    _handle_freeze_essence,
    _handle_generate_surface_variant,
    _handle_load_spec,
    _handle_validate_plan,
    _handle_validate_essence_surface,
    scene_generator as scene_generator_tool,
)

# Resolve test_specs directory relative to source tree, not CWD.
_SRC_DIR = Path(__file__).resolve().parent.parent / "src"
TEST_SPECS_DIR = _SRC_DIR / "scene_generator" / "test_specs"


def _sample_spec(mapping_overrides: dict | None = None) -> dict:
    mapping = {
        "structural_component": "user",
        "analogy_name": "Bee",
        "asset_strategy": "mechanic",
        "mapping_type": "relation",
        "mapping_confidence": "strong",
    }
    if mapping_overrides:
        mapping.update(mapping_overrides)
    return {
        "target_concept": "AI Recommendation System",
        "analogy_domain": "Bee Garden",
        "learning_goal": "Understand profile updates",
        "task_label": "Task 1",
        "mappings": [mapping],
    }


def test_load_spec_accepts_string_structural_components() -> None:
    """load_spec should work when structural_component is a plain string."""
    repo_root = Path(__file__).resolve().parents[2]
    spec_path = repo_root / "Server" / "src" / "scene_generator" / "test_specs" / "bee_garden.json"

    result = _handle_load_spec(str(spec_path), None)

    assert result["success"] is True
    assert result["planning_hints"]
    first_hint = result["planning_hints"][0]
    assert isinstance(first_hint["structural_component"], str)
    assert first_hint["structural_component"] == "user"


def test_scene_spec_rejects_invalid_mapping_type() -> None:
    payload = _sample_spec({"mapping_type": "not_a_valid_type"})

    with pytest.raises(ValidationError):
        SceneSpec.model_validate(payload)


def test_scene_spec_rejects_invalid_mapping_confidence() -> None:
    payload = _sample_spec({"mapping_confidence": "uncertain"})

    with pytest.raises(ValidationError):
        SceneSpec.model_validate(payload)


def test_scene_spec_includes_surface_defaults() -> None:
    spec = SceneSpec.model_validate(_sample_spec())
    assert spec.surface.style_mood == "natural"
    assert spec.surface.variation_level == "medium"
    assert spec.essence is None
    assert spec.essence_hash is None


def test_validator_canonicalizes_known_components_for_behavior() -> None:
    """Known components with user-entered formatting should still trigger expected logic."""
    spec = SceneSpec.model_validate(
        {
            "target_concept": "AI Recommendation System",
            "analogy_domain": "Garden",
            "learning_goal": "test",
            "task_label": "test",
            "mappings": [
                {
                    "structural_component": "User",
                    "analogy_name": "LearnerAvatar",
                    "asset_strategy": "mechanic",
                    "mapping_type": "object",
                    "mapping_confidence": "strong",
                },
                {
                    "structural_component": "Content Item",
                    "analogy_name": "Flower",
                    "asset_strategy": "primitive",
                    "instance_count": 3,
                    "instance_spread": 2.0,
                    "mapping_type": "object",
                    "mapping_confidence": "strong",
                },
            ],
        }
    )

    validator = PlanValidator(spec)
    plan = validator.validate_and_repair(MCPCallPlan())

    assert len(plan.primitive_calls) == 3
    names = [call.params["name"] for call in plan.primitive_calls]
    assert names == ["Flower_1", "Flower_2", "Flower_3"]
    assert "No USER structural component in mappings. Interactive 3D scenes require a user representation." not in validator.warnings

    batch = validator.to_batch_plan(plan)
    manager_names = [m.manager_name for m in batch.manager_tasks]
    assert "GameManager" in manager_names


def test_validator_generates_focused_managers_when_required() -> None:
    spec = SceneSpec.model_validate_json(
        (TEST_SPECS_DIR / "bee_garden.json").read_text(encoding="utf-8")
    )

    validator = PlanValidator(spec)
    plan = validator.validate_and_repair(MCPCallPlan())
    batch = validator.to_batch_plan(plan)

    manager_names = [m.manager_name for m in batch.manager_tasks]
    assert "GameManager" in manager_names
    assert "InteractionManager" in manager_names
    assert "ProfileManager" in manager_names
    assert "CandidateManager" in manager_names
    assert "RankingManager" in manager_names

    game_manager = next(m for m in batch.manager_tasks if m.manager_name == "GameManager")
    assert any("feedback loop" in item.lower() for item in game_manager.responsibilities)


def test_validator_keeps_only_game_manager_for_minimal_non_interaction_spec() -> None:
    spec = SceneSpec.model_validate(_sample_spec())

    validator = PlanValidator(spec)
    plan = validator.validate_and_repair(MCPCallPlan())
    batch = validator.to_batch_plan(plan)

    assert len(batch.manager_tasks) == 1
    assert batch.manager_tasks[0].manager_name == "GameManager"


def test_validator_normalizes_vfx_aliases_and_expands_animation_targets() -> None:
    spec = SceneSpec.model_validate(
        {
            "target_concept": "AI Recommendation System",
            "analogy_domain": "Garden",
            "learning_goal": "test",
            "task_label": "test",
            "mappings": [
                {
                    "structural_component": "User",
                    "analogy_name": "Bee",
                    "asset_strategy": "mechanic",
                    "mapping_type": "object",
                    "mapping_confidence": "strong",
                },
                {
                    "structural_component": "Content Item",
                    "analogy_name": "Flower",
                    "asset_strategy": "primitive",
                    "instance_count": 2,
                    "mapping_type": "object",
                    "mapping_confidence": "strong",
                },
                {
                    "structural_component": "Ranking",
                    "analogy_name": "BudGrowth",
                    "asset_strategy": "mechanic",
                    "mapping_type": "relation",
                    "mapping_confidence": "strong",
                    "interaction": {
                        "trigger": "continuous",
                        "target_objects": ["Flower"],
                        "animation_preset": "grow",
                    },
                },
            ],
        }
    )

    plan = MCPCallPlan(
        vfx_calls=[
            MCPToolCall(tool="manage_vfx", params={"action": "create", "target": "BudGrowth"}),
            MCPToolCall(tool="manage_vfx", params={"action": "set_main", "target": "BudGrowth"}),
        ]
    )
    validator = PlanValidator(spec)
    repaired = validator.validate_and_repair(plan)

    vfx_actions = [call.params["action"] for call in repaired.vfx_calls]
    assert "particle_set_main" in vfx_actions
    assert "particle_create" not in vfx_actions

    animation_targets = {
        call.params.get("target")
        for call in repaired.animation_calls
        if call.params.get("action") == "clip_create_preset"
    }
    assert animation_targets == {"Flower_1", "Flower_2"}


def test_validator_repairs_missing_primitive_type_and_prunes_invalid_material_targets() -> None:
    spec = SceneSpec.model_validate(
        {
            "target_concept": "AI Recommendation System",
            "analogy_domain": "Garden",
            "learning_goal": "test",
            "task_label": "test",
            "mappings": [
                {
                    "structural_component": "User",
                    "analogy_name": "Bee",
                    "asset_strategy": "mechanic",
                    "mapping_type": "object",
                    "mapping_confidence": "strong",
                },
                {
                    "structural_component": "Content Item",
                    "analogy_name": "Flower",
                    "asset_strategy": "primitive",
                    "instance_count": 2,
                    "mapping_type": "object",
                    "mapping_confidence": "strong",
                },
            ],
        }
    )

    plan = MCPCallPlan(
        primitive_calls=[
            MCPToolCall(
                tool="manage_gameobject",
                params={"action": "create", "name": "CustomObject"},
            )
        ],
        material_calls=[
            MCPToolCall(
                tool="manage_material",
                params={"action": "set_renderer_color", "target": "Bee", "color": [1, 1, 1, 1]},
            ),
            MCPToolCall(
                tool="manage_material",
                params={"action": "set_renderer_color", "target": "Flower", "color": [1, 1, 1, 1]},
            ),
        ],
    )

    validator = PlanValidator(spec)
    repaired = validator.validate_and_repair(plan)

    repaired_custom = next(
        call for call in repaired.primitive_calls if call.params.get("name") == "CustomObject"
    )
    assert repaired_custom.params.get("primitive_type") == "Cube"

    material_targets = [call.params.get("target") for call in repaired.material_calls]
    assert "Bee" not in material_targets
    assert "Flower" not in material_targets
    assert "Flower_1" in material_targets
    assert "Flower_2" in material_targets


def test_validator_assigns_non_gray_default_colors_for_uncolored_mappings() -> None:
    spec = SceneSpec.model_validate(
        {
            "target_concept": "AI Recommendation System",
            "analogy_domain": "Garden",
            "learning_goal": "test",
            "task_label": "test",
            "mappings": [
                {
                    "structural_component": "user",
                    "analogy_name": "Bee",
                    "asset_strategy": "primitive",
                    "mapping_type": "object",
                    "mapping_confidence": "strong",
                },
                {
                    "structural_component": "content_item",
                    "analogy_name": "Flower",
                    "asset_strategy": "primitive",
                    "instance_count": 2,
                    "mapping_type": "object",
                    "mapping_confidence": "strong",
                },
            ],
        }
    )

    validator = PlanValidator(spec)
    repaired = validator.validate_and_repair(MCPCallPlan())

    colors_by_target = {
        str(call.params.get("target")): call.params.get("color")
        for call in repaired.material_calls
        if call.params.get("action") == "set_renderer_color"
    }

    assert colors_by_target.get("Bee") == [1.0, 0.82, 0.2, 1.0]
    assert colors_by_target.get("Flower_1") == [0.95, 0.44, 0.58, 1.0]
    assert colors_by_target.get("Flower_2") == [0.42, 0.72, 0.94, 1.0]


def test_validator_outputs_experience_plan_with_phase_flow_and_causal_chain() -> None:
    spec = SceneSpec.model_validate(
        {
            "target_concept": "AI Recommendation System",
            "analogy_domain": "Garden",
            "learning_goal": "test",
            "task_label": "test",
            "mappings": [
                {
                    "structural_component": "User Interaction",
                    "analogy_name": "Pollination",
                    "asset_strategy": "mechanic",
                    "mapping_type": "relation",
                    "mapping_confidence": "strong",
                    "interaction": {
                        "trigger": "button_press",
                        "trigger_source": "Bee",
                        "target_objects": ["Flower"],
                        "effect_description": "Pollen burst appears on flower.",
                    },
                },
                {
                    "structural_component": "Profile Update",
                    "analogy_name": "BeehiveMovement",
                    "asset_strategy": "mechanic",
                    "mapping_type": "relation",
                    "mapping_confidence": "strong",
                    "interaction": {
                        "trigger": "continuous",
                        "trigger_source": "Beehive",
                        "target_objects": ["Flower"],
                        "effect_description": "Beehive drifts toward frequently pollinated flowers.",
                    },
                },
            ],
        }
    )

    validator = PlanValidator(spec)
    plan = validator.validate_and_repair(MCPCallPlan())
    batch = validator.to_batch_plan(plan)

    phase_names = [phase.phase_name for phase in batch.experience_plan.phases]
    assert phase_names == [
        "Intro",
        "Explore",
        "Trigger",
        "Observe Feedback Loop",
        "Summary",
    ]
    assert batch.experience_plan.progress_target >= 1
    assert len(batch.experience_plan.causal_chain) >= 1

    game_manager = next(m for m in batch.manager_tasks if m.manager_name == "GameManager")
    assert any("ExperienceDirector" in item for item in game_manager.responsibilities)


def test_validator_emits_phase_batch_metadata_and_smoke_gate() -> None:
    spec = SceneSpec.model_validate(_sample_spec())

    validator = PlanValidator(spec)
    plan = validator.validate_and_repair(MCPCallPlan())
    batch = validator.to_batch_plan(plan)

    phase_names = [phase.phase_name for phase in batch.phases]
    assert "validate_essence" in phase_names
    assert "smoke_test" in phase_names
    assert "scene_save" in phase_names

    scripts_phase = next((p for p in batch.phases if p.phase_name == "scripts"), None)
    if scripts_phase is not None:
        assert scripts_phase.batch_size_limit == 8
        assert scripts_phase.fail_fast is True

    smoke_phase = next(p for p in batch.phases if p.phase_name == "smoke_test")
    save_phase = next(p for p in batch.phases if p.phase_name == "scene_save")
    assert smoke_phase.phase_number < save_phase.phase_number
    assert smoke_phase.batch_size_limit == 1

    assert batch.smoke_test_plan.get("required") is True
    assert "CompareTag(" in batch.audit_rules.get("banned_script_lookup_patterns", [])


def test_freeze_essence_returns_hash_and_payload() -> None:
    spec_json = json.dumps(_sample_spec())
    result = _handle_freeze_essence(spec_path=None, spec_json=spec_json)
    assert result["success"] is True
    assert result["essence_hash"]
    assert "mapping_role_ids" in result["essence"]


def test_validate_essence_surface_reports_missing_character() -> None:
    payload = _sample_spec({"structural_component": "ranking"})
    result = _handle_validate_essence_surface(json.dumps(payload))
    assert result["success"] is False
    assert any("Character role missing" in item for item in result["issues"])


def test_generate_surface_variant_returns_surface_suggestions() -> None:
    spec = SceneSpec.model_validate(_sample_spec())
    payload = spec.model_dump(mode="json")
    payload["surface"]["variation_level"] = "high"
    result = _handle_generate_surface_variant(json.dumps(payload))
    assert result["success"] is True
    suggestions = result["surface_suggestions"]
    assert suggestions["variation_level"] == "high"
    assert suggestions["style_seed"] == 1


def test_audit_batch_result_hard_fails_on_tag_lookup_patterns() -> None:
    batch_result = {
        "success": True,
        "data": {
            "results": [
                {"tool": "create_script", "callSucceeded": True, "result": {"success": True, "message": "ok"}},
            ]
        },
    }
    phase_context = {
        "commands": [
            {
                "tool": "create_script",
                "params": {"contents": "if (go.CompareTag(\"Flower\")) { }"},
            }
        ]
    }

    audit = _audit_batch_result_payload(batch_result, "scripts", 4, phase_context)

    assert audit["decision"] == "fail"
    assert any(item.get("reason") == "banned_tag_lookup_pattern" for item in audit["failures"])


def test_audit_batch_result_classifies_retryable_failures() -> None:
    batch_result = {
        "success": False,
        "message": "Unity is compiling scripts, please try again",
        "data": {
            "results": [
                {
                    "tool": "manage_components",
                    "callSucceeded": False,
                    "error": "Editor busy compiling",
                }
            ]
        },
    }

    audit = _audit_batch_result_payload(batch_result, "components_vfx", 5, None)

    assert audit["decision"] == "retry"
    assert audit["retryable"]
    assert not audit["failures"]


def test_intent_contract_includes_ui_and_readability_requirements() -> None:
    spec = SceneSpec.model_validate_json(
        (TEST_SPECS_DIR / "bee_garden.json").read_text(encoding="utf-8")
    )
    validator = PlanValidator(spec)
    plan = validator.validate_and_repair(MCPCallPlan())
    batch = validator.to_batch_plan(plan)
    contract = batch.intent_contract

    assert contract.ui_requirements
    assert any("HUD" in item or "UI" in item for item in contract.ui_requirements)
    assert contract.readability_requirements
    assert any("Phase order" in item for item in contract.readability_requirements)


def test_relation_mapping_auto_repairs_missing_interactions() -> None:
    spec = SceneSpec.model_validate(
        {
            "target_concept": "Causal reasoning",
            "analogy_domain": "Garden",
            "learning_goal": "test",
            "task_label": "test",
            "mappings": [
                {
                    "structural_component": "User",
                    "analogy_name": "Learner",
                    "asset_strategy": "mechanic",
                    "mapping_type": "object",
                    "mapping_confidence": "strong",
                },
                {
                    "structural_component": "Feedback Loop",
                    "analogy_name": "GardenDynamics",
                    "asset_strategy": "mechanic",
                    "mapping_type": "higher_order",
                    "mapping_confidence": "strong",
                },
            ],
        }
    )

    validator = PlanValidator(spec)
    validator.validate_and_repair(MCPCallPlan())

    repaired = next(row for row in validator.spec.mappings if row.analogy_name == "GardenDynamics")
    assert repaired.interaction is not None
    assert repaired.interaction.trigger in {"continuous", "on_start", "button_press"}
    assert "GardenDynamics" in validator._inferred_interaction_mappings


def test_validator_injects_runtime_ui_anchors() -> None:
    spec = SceneSpec.model_validate(_sample_spec())
    validator = PlanValidator(spec)
    repaired = validator.validate_and_repair(MCPCallPlan())

    created_names = [
        call.params.get("name")
        for call in repaired.environment_calls
        if call.tool == "manage_gameobject" and call.params.get("action") == "create"
    ]
    assert "GameManager" in created_names
    assert "FeedbackHUD" in created_names
    assert "HUD_BeginnerGuide" in created_names
    assert "HUD_StatusReadout" in created_names
    assert "HUD_Current_objective" not in created_names
    assert "HUD_Profile_state" not in created_names

    feedback_hud_components = {
        call.params.get("component_type")
        for call in repaired.component_calls
        if call.tool == "manage_components"
        and call.params.get("action") == "add"
        and call.params.get("target") == "FeedbackHUD"
    }
    assert {"Canvas", "CanvasScaler", "GraphicRaycaster", "BeginnerGuideUI"} <= feedback_hud_components

    textmesh_targets = {
        str(call.params.get("target"))
        for call in repaired.component_calls
        if call.tool == "manage_components"
        and call.params.get("action") == "add"
        and call.params.get("component_type") == "TextMesh"
    }
    assert {"HUD_BeginnerGuide", "HUD_StatusReadout"} <= textmesh_targets

    textmesh_text_targets = {
        str(call.params.get("target"))
        for call in repaired.component_calls
        if call.tool == "manage_components"
        and call.params.get("action") == "set_property"
        and call.params.get("component_type") == "TextMesh"
        and call.params.get("property") == "text"
        and str(call.params.get("value", "")).strip()
    }
    assert {"HUD_BeginnerGuide", "HUD_StatusReadout"} <= textmesh_text_targets

    script_paths = [
        call.params.get("path")
        for call in repaired.script_calls
        if call.tool == "create_script"
    ]
    assert "Assets/Scripts/BeginnerGuideUI.cs" in script_paths


def test_validator_creates_manager_anchor_gameobjects_for_focused_managers() -> None:
    spec = SceneSpec.model_validate_json(
        (TEST_SPECS_DIR / "bee_garden.json").read_text(encoding="utf-8")
    )
    validator = PlanValidator(spec)
    repaired = validator.validate_and_repair(MCPCallPlan())

    created_names = {
        call.params.get("name")
        for call in repaired.environment_calls
        if call.tool == "manage_gameobject" and call.params.get("action") == "create"
    }
    assert {"GameManager", "ProfileManager", "CandidateManager", "RankingManager", "InteractionManager"} <= created_names


def test_validator_generates_functional_runtime_scripts_not_log_only() -> None:
    spec = SceneSpec.model_validate_json(
        (TEST_SPECS_DIR / "bee_garden.json").read_text(encoding="utf-8")
    )
    validator = PlanValidator(spec)
    repaired = validator.validate_and_repair(MCPCallPlan())

    script_by_path = {
        str(call.params.get("path")): str(call.params.get("contents", ""))
        for call in repaired.script_calls
        if call.tool == "create_script"
    }
    game_manager_script = script_by_path.get("Assets/Scripts/GameManager.cs", "")
    trigger_script = script_by_path.get("Assets/Scripts/PollinationTrigger.cs", "")
    assert "public void RecordTrigger" in game_manager_script
    assert "NotifyControllers(\"ApplyPollination\"" in trigger_script


def test_validator_waits_for_compile_readiness_before_component_attachment() -> None:
    spec = SceneSpec.model_validate_json(
        (TEST_SPECS_DIR / "bee_garden.json").read_text(encoding="utf-8")
    )
    validator = PlanValidator(spec)
    repaired = validator.validate_and_repair(MCPCallPlan())

    refresh_calls = [
        call for call in repaired.script_calls
        if call.tool == "refresh_unity"
    ]
    assert any(str(call.params.get("compile", "")).lower() == "request" for call in refresh_calls)
    assert any(bool(call.params.get("wait_for_ready")) for call in refresh_calls)

    compile_index = next(
        index
        for index, call in enumerate(repaired.script_calls)
        if call.tool == "refresh_unity" and str(call.params.get("compile", "")).lower() == "request"
    )
    wait_index = next(
        index
        for index, call in enumerate(repaired.script_calls)
        if call.tool == "refresh_unity" and bool(call.params.get("wait_for_ready"))
    )
    assert wait_index > compile_index


def test_validator_hard_fails_when_intent_trigger_is_unrecoverable() -> None:
    spec = SceneSpec.model_validate(
        {
            "target_concept": "Empty",
            "analogy_domain": "Empty",
            "learning_goal": "test",
            "task_label": "test",
            "mappings": [],
        }
    )

    validator = PlanValidator(spec)
    with pytest.raises(ValueError):
        validator.validate_and_repair(MCPCallPlan())


class _DummyCtx:
    def get_state(self, _key: str) -> None:
        return None


def test_plan_and_execute_happy_path(monkeypatch: pytest.MonkeyPatch) -> None:
    async def fake_execute(ctx, batch_plan_json, max_retries_per_batch, retry_backoff_seconds, stop_on_warning):
        return {
            "success": True,
            "final_decision": "pass",
            "message": "Batch plan executed successfully.",
            "scene_saved": True,
            "phase_results": [],
            "warnings": [],
            "failures": [],
            "smoke_report": {"summary": {"errors": 0, "warnings": 0}},
        }

    monkeypatch.setattr("services.tools.scene_generator._handle_execute_batch_plan", fake_execute)

    result = asyncio.run(_handle_plan_and_execute(
        ctx=_DummyCtx(),
        spec_json=json.dumps(_sample_spec()),
        max_retries_per_batch=2,
        retry_backoff_seconds=1.5,
        stop_on_warning=False,
    ))

    assert result["success"] is True
    assert result["action"] == "plan_and_execute"
    assert result["final_decision"] == "pass"
    assert result["scene_saved"] is True
    assert result["failure_stage"] is None
    assert isinstance(result["summary"], str) and result["summary"]
    assert result["planning"]["success"] is True
    assert isinstance(result["planning"]["batch_plan"], dict)
    assert isinstance(result["execution"], dict)
    assert result["execution"]["success"] is True


def test_plan_and_execute_invalid_spec_json_fails_in_planning() -> None:
    result = asyncio.run(_handle_plan_and_execute(
        ctx=_DummyCtx(),
        spec_json="{invalid-json",
        max_retries_per_batch=2,
        retry_backoff_seconds=1.5,
        stop_on_warning=False,
    ))

    assert result["success"] is False
    assert result["final_decision"] == "fail"
    assert result["failure_stage"] == "planning"
    assert result["execution"] is None
    assert result["planning"]["success"] is False
    assert result["planning"]["batch_plan"] is None
    assert isinstance(result["summary"], str) and result["summary"]


def test_plan_and_execute_validator_value_error_is_planning_failure(monkeypatch: pytest.MonkeyPatch) -> None:
    def fake_validate_and_repair(self, _plan):
        raise ValueError("forced planning hard-fail")

    monkeypatch.setattr("services.tools.scene_generator.PlanValidator.validate_and_repair", fake_validate_and_repair)

    result = asyncio.run(_handle_plan_and_execute(
        ctx=_DummyCtx(),
        spec_json=json.dumps(_sample_spec()),
        max_retries_per_batch=2,
        retry_backoff_seconds=1.5,
        stop_on_warning=False,
    ))

    assert result["success"] is False
    assert result["failure_stage"] == "planning"
    assert result["execution"] is None
    assert "forced planning hard-fail" in result["message"]


def test_plan_and_execute_propagates_execution_failure(monkeypatch: pytest.MonkeyPatch) -> None:
    async def fake_execute(ctx, batch_plan_json, max_retries_per_batch, retry_backoff_seconds, stop_on_warning):
        return {
            "success": False,
            "final_decision": "fail",
            "message": "Execution failed in phase 'smoke_test'.",
            "scene_saved": False,
            "phase_results": [{"phase_name": "smoke_test", "status": "fail"}],
            "warnings": [],
            "failures": [{"tool": "scene_generator", "message": "Smoke failed"}],
            "smoke_report": {"summary": {"errors": 1, "warnings": 0}},
        }

    monkeypatch.setattr("services.tools.scene_generator._handle_execute_batch_plan", fake_execute)

    result = asyncio.run(_handle_plan_and_execute(
        ctx=_DummyCtx(),
        spec_json=json.dumps(_sample_spec()),
        max_retries_per_batch=2,
        retry_backoff_seconds=1.5,
        stop_on_warning=False,
    ))

    assert result["planning"]["success"] is True
    assert result["execution"]["success"] is False
    assert result["success"] is False
    assert result["failure_stage"] == "execution"
    assert result["final_decision"] == "fail"
    assert isinstance(result["summary"], str) and result["summary"]


def test_plan_and_execute_forwards_retry_parameters(monkeypatch: pytest.MonkeyPatch) -> None:
    captured: dict[str, Any] = {}

    async def fake_execute(ctx, batch_plan_json, max_retries_per_batch, retry_backoff_seconds, stop_on_warning):
        captured["max_retries_per_batch"] = max_retries_per_batch
        captured["retry_backoff_seconds"] = retry_backoff_seconds
        captured["stop_on_warning"] = stop_on_warning
        return {
            "success": True,
            "final_decision": "pass",
            "message": "ok",
            "scene_saved": True,
            "phase_results": [],
            "warnings": [],
            "failures": [],
            "smoke_report": None,
        }

    monkeypatch.setattr("services.tools.scene_generator._handle_execute_batch_plan", fake_execute)

    result = asyncio.run(_handle_plan_and_execute(
        ctx=_DummyCtx(),
        spec_json=json.dumps(_sample_spec()),
        max_retries_per_batch=7,
        retry_backoff_seconds=3.25,
        stop_on_warning=True,
    ))

    assert result["success"] is True
    assert captured == {
        "max_retries_per_batch": 7,
        "retry_backoff_seconds": 3.25,
        "stop_on_warning": True,
    }


def test_scene_generator_dispatch_supports_plan_and_execute_action(monkeypatch: pytest.MonkeyPatch) -> None:
    async def fake_plan_execute(ctx, spec_json, max_retries_per_batch, retry_backoff_seconds, stop_on_warning):
        return {
            "success": True,
            "action": "plan_and_execute",
            "summary": "ok",
            "message": "ok",
            "planning": {"success": True, "batch_plan": {}},
            "execution": {"success": True},
            "final_decision": "pass",
            "scene_saved": True,
            "failure_stage": None,
        }

    monkeypatch.setattr("services.tools.scene_generator._handle_plan_and_execute", fake_plan_execute)

    result = asyncio.run(scene_generator_tool(
        ctx=_DummyCtx(),  # type: ignore[arg-type]
        action="plan_and_execute",
        spec_json=json.dumps(_sample_spec()),
    ))

    assert result["success"] is True
    assert result["action"] == "plan_and_execute"
    assert "planning" in result
    assert "execution" in result
    assert "summary" in result


def test_plan_and_execute_success_derivation_and_failure_stage(monkeypatch: pytest.MonkeyPatch) -> None:
    async def fake_execute(ctx, batch_plan_json, max_retries_per_batch, retry_backoff_seconds, stop_on_warning):
        return {
            "success": False,
            "final_decision": "fail",
            "message": "phase failure",
            "scene_saved": False,
            "phase_results": [{"phase_name": "environment", "status": "fail"}],
            "warnings": [],
            "failures": [],
            "smoke_report": None,
        }

    monkeypatch.setattr("services.tools.scene_generator._handle_execute_batch_plan", fake_execute)
    result = asyncio.run(_handle_plan_and_execute(
        ctx=_DummyCtx(),
        spec_json=json.dumps(_sample_spec()),
        max_retries_per_batch=1,
        retry_backoff_seconds=0.0,
        stop_on_warning=False,
    ))

    assert result["planning"]["success"] is True
    assert result["execution"]["success"] is False
    assert result["success"] is False
    assert result["failure_stage"] == "execution"
    assert result["final_decision"] == "fail"


def test_validate_plan_contract_unchanged_after_helper_refactor() -> None:
    result = _handle_validate_plan(
        spec_json=json.dumps(_sample_spec()),
        plan_json=MCPCallPlan().model_dump_json(),
    )

    assert result["success"] is True
    assert set(result.keys()) == {"success", "batch_plan", "manager_tasks", "script_tasks", "message", "warnings"}
    assert isinstance(result["batch_plan"], dict)
    assert isinstance(result["manager_tasks"], list)
    assert isinstance(result["script_tasks"], list)


def test_execute_batch_plan_preflight_blocks_missing_targets(monkeypatch: pytest.MonkeyPatch) -> None:
    calls = {"count": 0}

    async def fake_send(*_args, **_kwargs):
        calls["count"] += 1
        return {"success": True}

    monkeypatch.setattr("services.tools.scene_generator.send_with_unity_instance", fake_send)

    plan = BatchExecutionPlan(
        phases=[
            ExecutionPhase(
                phase_name="components_vfx",
                phase_number=1,
                commands=[{
                    "tool": "manage_components",
                    "params": {"action": "add", "target": "MissingAnchor", "component_type": "BoxCollider"},
                }],
                parallel=True,
                batch_size_limit=40,
                fail_fast=True,
            ),
        ]
    )

    result = asyncio.run(_handle_execute_batch_plan(
        ctx=_DummyCtx(),
        batch_plan_json=plan.model_dump_json(),
        max_retries_per_batch=0,
        retry_backoff_seconds=0.0,
        stop_on_warning=False,
    ))

    assert result["success"] is False
    assert result["final_decision"] == "fail"
    assert "preflight" in result["message"].lower()
    assert result.get("preflight_failures_total", 0) >= 1
    assert calls["count"] == 0


def test_execute_batch_plan_happy_path_executes_and_saves(monkeypatch: pytest.MonkeyPatch) -> None:
    async def fake_send(_send_fn, _unity_instance, command_type, params, **_kwargs):
        if command_type == "batch_execute":
            return {
                "success": True,
                "data": {
                    "results": [
                        {"tool": cmd["tool"], "callSucceeded": True, "result": {"success": True}}
                        for cmd in params.get("commands", [])
                    ]
                },
            }
        return {"success": True}

    async def fake_smoke(*_args, **_kwargs):
        return {
            "success": True,
            "decision": "pass",
            "smoke_report": {"summary": {"errors": 0, "warnings": 0}},
        }

    monkeypatch.setattr("services.tools.scene_generator.send_with_unity_instance", fake_send)
    monkeypatch.setattr("services.tools.scene_generator._handle_smoke_test_scene", fake_smoke)

    plan = BatchExecutionPlan(
        phases=[
            ExecutionPhase(
                phase_name="validate_essence",
                phase_number=0,
                commands=[{
                    "tool": "scene_generator",
                    "params": {"action": "validate_essence_surface", "spec_json": json.dumps(_sample_spec())},
                }],
                parallel=False,
                batch_size_limit=1,
                fail_fast=True,
            ),
            ExecutionPhase(
                phase_name="environment",
                phase_number=1,
                commands=[{
                    "tool": "manage_gameobject",
                    "params": {"action": "create", "name": "Cube", "primitive_type": "Cube"},
                }],
                parallel=True,
                batch_size_limit=40,
                fail_fast=True,
            ),
            ExecutionPhase(
                phase_name="smoke_test",
                phase_number=2,
                commands=[{"tool": "scene_generator", "params": {"action": "smoke_test_scene"}}],
                parallel=False,
                batch_size_limit=1,
                fail_fast=True,
            ),
            ExecutionPhase(
                phase_name="scene_save",
                phase_number=3,
                commands=[{"tool": "manage_scene", "params": {"action": "save"}}],
                parallel=False,
                batch_size_limit=1,
                fail_fast=True,
            ),
        ]
    )

    result = asyncio.run(_handle_execute_batch_plan(
        ctx=_DummyCtx(),
        batch_plan_json=plan.model_dump_json(),
        max_retries_per_batch=2,
        retry_backoff_seconds=0.0,
        stop_on_warning=False,
    ))

    assert result["success"] is True
    assert result["final_decision"] == "pass"
    assert result["scene_saved"] is True


def test_execute_batch_plan_retries_retryable_failures(monkeypatch: pytest.MonkeyPatch) -> None:
    calls = {"count": 0}

    async def fake_send(_send_fn, _unity_instance, command_type, params, **_kwargs):
        if command_type != "batch_execute":
            return {"success": True}
        calls["count"] += 1
        if calls["count"] == 1:
            return {
                "success": False,
                "message": "Unity is compiling scripts, please try again",
                "data": {"results": [{"tool": "manage_gameobject", "callSucceeded": False, "error": "Editor busy compiling"}]},
            }
        return {
            "success": True,
            "data": {"results": [{"tool": "manage_gameobject", "callSucceeded": True, "result": {"success": True}}]},
        }

    monkeypatch.setattr("services.tools.scene_generator.send_with_unity_instance", fake_send)

    plan = BatchExecutionPlan(
        phases=[
            ExecutionPhase(
                phase_name="environment",
                phase_number=1,
                commands=[{"tool": "manage_gameobject", "params": {"action": "create", "name": "A", "primitive_type": "Cube"}}],
                parallel=True,
                batch_size_limit=40,
                fail_fast=True,
            ),
        ]
    )

    result = asyncio.run(_handle_execute_batch_plan(
        ctx=_DummyCtx(),
        batch_plan_json=plan.model_dump_json(),
        max_retries_per_batch=2,
        retry_backoff_seconds=0.0,
        stop_on_warning=False,
    ))

    assert result["success"] is True
    assert calls["count"] == 2
    assert result["phase_results"][0]["retries_used"] == 1


def test_execute_batch_plan_blocks_scene_save_on_smoke_failure(monkeypatch: pytest.MonkeyPatch) -> None:
    async def fake_send(_send_fn, _unity_instance, command_type, params, **_kwargs):
        if command_type == "batch_execute":
            return {
                "success": True,
                "data": {
                    "results": [
                        {"tool": cmd["tool"], "callSucceeded": True, "result": {"success": True}}
                        for cmd in params.get("commands", [])
                    ]
                },
            }
        return {"success": True}

    async def fake_smoke_fail(*_args, **_kwargs):
        return {
            "success": False,
            "decision": "fail",
            "message": "Smoke failed",
            "smoke_report": {"summary": {"errors": 1, "warnings": 0}},
        }

    monkeypatch.setattr("services.tools.scene_generator.send_with_unity_instance", fake_send)
    monkeypatch.setattr("services.tools.scene_generator._handle_smoke_test_scene", fake_smoke_fail)

    plan = BatchExecutionPlan(
        phases=[
            ExecutionPhase(
                phase_name="environment",
                phase_number=1,
                commands=[{"tool": "manage_gameobject", "params": {"action": "create", "name": "A", "primitive_type": "Cube"}}],
                parallel=True,
                batch_size_limit=40,
                fail_fast=True,
            ),
            ExecutionPhase(
                phase_name="smoke_test",
                phase_number=2,
                commands=[{"tool": "scene_generator", "params": {"action": "smoke_test_scene"}}],
                parallel=False,
                batch_size_limit=1,
                fail_fast=True,
            ),
            ExecutionPhase(
                phase_name="scene_save",
                phase_number=3,
                commands=[{"tool": "manage_scene", "params": {"action": "save"}}],
                parallel=False,
                batch_size_limit=1,
                fail_fast=True,
            ),
        ]
    )

    result = asyncio.run(_handle_execute_batch_plan(
        ctx=_DummyCtx(),
        batch_plan_json=plan.model_dump_json(),
        max_retries_per_batch=0,
        retry_backoff_seconds=0.0,
        stop_on_warning=False,
    ))

    assert result["success"] is False
    assert result["scene_saved"] is False
    assert all(phase["phase_name"] != "scene_save" for phase in result["phase_results"])


def test_app_select_generation_mode_prefers_execute_when_backend_healthy() -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    from scene_generator.app import _select_generation_mode

    assert _select_generation_mode(True) == "execute_first"


def test_app_select_generation_mode_falls_back_when_backend_unavailable() -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    from scene_generator.app import _select_generation_mode

    assert _select_generation_mode(False) == "prompt_export"


def test_parse_llm_response_accepts_trailing_extra_text() -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    from scene_generator.app import _parse_llm_response

    payload = (
        "{\n"
        '  "essence_check": {"essence_hash_echo": "", "essence_changed": false},\n'
        '  "environment": {"setting": "garden"}\n'
        "}\n"
        "Some extra non-JSON text from the model."
    )

    parsed = _parse_llm_response(payload)
    assert isinstance(parsed, dict)
    assert parsed.get("environment", {}).get("setting") == "garden"


def test_parse_llm_response_accepts_json_fence_with_surrounding_text() -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    from scene_generator.app import _parse_llm_response

    payload = (
        "Here is the result:\n"
        "```json\n"
        "{\n"
        '  "environment": {"setting": "garden"},\n'
        '  "mapping_suggestions": []\n'
        "}\n"
        "```\n"
        "Done."
    )

    parsed = _parse_llm_response(payload)
    assert isinstance(parsed, dict)
    assert parsed.get("environment", {}).get("setting") == "garden"


def test_generation_prompt_compact_strips_create_script_contents() -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    streamlit.session_state["allow_trellis_generation"] = False
    batch = BatchExecutionPlan(
        phases=[
            ExecutionPhase(
                phase_name="scripts",
                phase_number=4,
                commands=[
                    {
                        "tool": "create_script",
                        "params": {
                            "path": "Assets/Scripts/TestScript.cs",
                            "contents": "using UnityEngine; public class TestScript : MonoBehaviour { void Start(){ Debug.Log(\"SHOULD_NOT_LEAK\"); } }",
                        },
                    }
                ],
                parallel=False,
                batch_size_limit=8,
                fail_fast=True,
            )
        ]
    )

    prompt = app_module._build_generation_prompt_compact(json.dumps(_sample_spec()), batch)
    assert "create_script" in prompt
    assert "contents_omitted" in prompt
    assert "SHOULD_NOT_LEAK" not in prompt


def test_generation_prompt_full_strips_create_script_contents() -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    streamlit.session_state["allow_trellis_generation"] = False
    batch = BatchExecutionPlan(
        phases=[
            ExecutionPhase(
                phase_name="scripts",
                phase_number=4,
                commands=[
                    {
                        "tool": "create_script",
                        "params": {
                            "path": "Assets/Scripts/TestScript.cs",
                            "contents": "using UnityEngine; public class TestScript : MonoBehaviour { void Start(){ Debug.Log(\"SHOULD_NOT_LEAK_FULL\"); } }",
                        },
                    }
                ],
                parallel=False,
                batch_size_limit=8,
                fail_fast=True,
            )
        ]
    )

    prompt = app_module._build_generation_prompt_full(json.dumps(_sample_spec()), batch)
    assert "create_script" in prompt
    assert "contents_omitted" in prompt
    assert "SHOULD_NOT_LEAK_FULL" not in prompt
    assert "command bodies are intentionally omitted" in prompt


def test_generate_clarification_questions_uses_llm_output(monkeypatch: pytest.MonkeyPatch) -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    monkeypatch.setattr(
        app_module,
        "_call_llm",
        lambda _prompt: json.dumps({
            "clarification_questions": [
                "What should the primary action be?",
                "What ranking signal should dominate?",
                "Any pacing or visual constraints?",
            ]
        }),
    )

    questions = app_module._generate_clarification_questions(_sample_spec(), {"mapping_suggestions": []})
    assert questions == [
        "What should the primary action be?",
        "What ranking signal should dominate?",
        "Any pacing or visual constraints?",
    ]


def test_generate_clarification_questions_falls_back_when_output_partial(monkeypatch: pytest.MonkeyPatch) -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    monkeypatch.setattr(
        app_module,
        "_call_llm",
        lambda _prompt: json.dumps({"clarification_questions": ["Only one question?"]}),
    )

    questions = app_module._generate_clarification_questions(_sample_spec(), {"mapping_suggestions": []})
    assert len(questions) == 3
    assert questions[0] == "Only one question?"


def test_asset_policy_strips_trellis_from_suggestions() -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    suggestions = {
        "mapping_suggestions": [
            {
                "asset_strategy": "trellis",
                "trellis_prompt": "high detail flower",
            }
        ],
        "mapping_surface_overrides": [
            {"name": "Flower", "trellis_prompt": "alt flower"},
        ],
    }

    normalized = app_module._apply_asset_policy_to_suggestions(suggestions, allow_trellis=False)
    row = normalized["mapping_suggestions"][0]
    assert row["asset_strategy"] == "primitive"
    assert row["primitive_type"] == "Cube"
    assert "trellis_prompt" not in row
    assert "trellis_prompt" not in normalized["mapping_surface_overrides"][0]


def test_asset_policy_converts_trellis_spec_rows_to_primitive() -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    spec = {
        "mappings": [
            {
                "structural_component": "content_item",
                "analogy_name": "Flower",
                "asset_strategy": "trellis",
                "trellis_prompt": "flower model",
            },
            {
                "structural_component": "user",
                "analogy_name": "Bee",
                "asset_strategy": "mechanic",
            },
        ]
    }

    converted = app_module._apply_asset_policy_to_spec(spec, allow_trellis=False)
    assert converted == 1
    assert spec["mappings"][0]["asset_strategy"] == "primitive"
    assert spec["mappings"][0]["primitive_type"] == "Cube"
    assert "trellis_prompt" not in spec["mappings"][0]


def _sample_batch_plan_for_app_tests() -> BatchExecutionPlan:
    return BatchExecutionPlan(
        phases=[
            ExecutionPhase(
                phase_name="environment",
                phase_number=1,
                commands=[{"tool": "manage_gameobject", "params": {"action": "create", "name": "A", "primitive_type": "Cube"}}],
                parallel=True,
                batch_size_limit=40,
                fail_fast=True,
            )
        ]
    )


def test_execute_first_prefers_plan_and_execute_when_available(monkeypatch: pytest.MonkeyPatch) -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    batch = _sample_batch_plan_for_app_tests()
    expected_report = {
        "success": True,
        "action": "plan_and_execute",
        "summary": "ok",
        "message": "ok",
        "planning": {
            "success": True,
            "message": "ok",
            "warnings": [],
            "total_commands": batch.total_commands,
            "estimated_batches": batch.estimated_batches,
            "trellis_count": batch.trellis_count,
            "phase_names": [phase.phase_name for phase in batch.phases],
            "manager_count": 0,
            "script_task_count": 0,
            "batch_plan": batch.model_dump(mode="json"),
        },
        "execution": {"success": True},
        "final_decision": "pass",
        "scene_saved": True,
        "failure_stage": None,
    }

    monkeypatch.setattr(app_module, "_plan_and_execute_with_tool_handler", lambda *_args, **_kwargs: expected_report)
    monkeypatch.setattr(
        app_module,
        "_execute_batch_plan_with_tool_handler",
        lambda *_args, **_kwargs: pytest.fail("Legacy executor should not run when plan_and_execute provides a valid plan."),
    )

    spec_obj = SceneSpec.model_validate(_sample_spec())
    hydrated_batch, report, used_fallback = app_module._execute_first_with_fallback(spec_obj)

    assert used_fallback is False
    assert report is expected_report
    assert hydrated_batch.total_commands == batch.total_commands


def test_execute_first_uses_planning_batch_plan_for_prompt_generation() -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    batch = _sample_batch_plan_for_app_tests()
    report = {
        "action": "plan_and_execute",
        "planning": {"batch_plan": batch.model_dump(mode="json")},
    }

    hydrated = app_module._hydrate_batch_plan_from_plan_and_execute_report(report)
    assert hydrated is not None
    prompt = app_module._build_generation_prompt_compact(json.dumps(_sample_spec()), hydrated)
    assert "EXECUTION_PLAN_JSON" in prompt
    assert f"\"total_commands\":{batch.total_commands}" in prompt


def test_execute_first_falls_back_only_on_pre_execution_failure(monkeypatch: pytest.MonkeyPatch) -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    calls = {"legacy": 0}
    monkeypatch.setattr(
        app_module,
        "_plan_and_execute_with_tool_handler",
        lambda *_args, **_kwargs: {
            "success": False,
            "action": "plan_and_execute",
            "summary": "planning failed",
            "message": "planning failed",
            "planning": {"success": False, "batch_plan": None},
            "execution": None,
            "final_decision": "fail",
            "scene_saved": False,
            "failure_stage": "planning",
        },
    )
    monkeypatch.setattr(
        app_module,
        "_execute_batch_plan_with_tool_handler",
        lambda *_args, **_kwargs: (
            calls.__setitem__("legacy", calls["legacy"] + 1) or {"success": True, "final_decision": "pass", "scene_saved": True}
        ),
    )

    spec_obj = SceneSpec.model_validate(_sample_spec())
    _batch, _report, used_fallback = app_module._execute_first_with_fallback(spec_obj)

    assert used_fallback is True
    assert calls["legacy"] == 1


def test_execute_first_does_not_fallback_on_execution_failure(monkeypatch: pytest.MonkeyPatch) -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    batch = _sample_batch_plan_for_app_tests()
    report = {
        "success": False,
        "action": "plan_and_execute",
        "summary": "failed in execution",
        "message": "Execution failed in phase 'smoke_test'.",
        "planning": {
            "success": True,
            "message": "ok",
            "warnings": [],
            "total_commands": batch.total_commands,
            "estimated_batches": batch.estimated_batches,
            "trellis_count": batch.trellis_count,
            "phase_names": [phase.phase_name for phase in batch.phases],
            "manager_count": 0,
            "script_task_count": 0,
            "batch_plan": batch.model_dump(mode="json"),
        },
        "execution": {"success": False},
        "final_decision": "fail",
        "scene_saved": False,
        "failure_stage": "execution",
    }
    monkeypatch.setattr(app_module, "_plan_and_execute_with_tool_handler", lambda *_args, **_kwargs: report)
    monkeypatch.setattr(
        app_module,
        "_execute_batch_plan_with_tool_handler",
        lambda *_args, **_kwargs: pytest.fail("Legacy executor must not run after execution-stage failure."),
    )

    spec_obj = SceneSpec.model_validate(_sample_spec())
    hydrated_batch, returned_report, used_fallback = app_module._execute_first_with_fallback(spec_obj)

    assert used_fallback is False
    assert returned_report is report
    assert hydrated_batch.total_commands == batch.total_commands


def test_execute_first_falls_back_on_import_error(monkeypatch: pytest.MonkeyPatch) -> None:
    streamlit = pytest.importorskip("streamlit")
    assert streamlit is not None
    import scene_generator.app as app_module

    calls = {"legacy": 0}
    monkeypatch.setattr(
        app_module,
        "_plan_and_execute_with_tool_handler",
        lambda *_args, **_kwargs: {
            "success": False,
            "action": "plan_and_execute",
            "summary": "import failed",
            "message": "handler import failed",
            "planning": {"success": False, "batch_plan": None},
            "execution": None,
            "final_decision": "fail",
            "scene_saved": False,
            "failure_stage": "planning",
        },
    )
    monkeypatch.setattr(
        app_module,
        "_execute_batch_plan_with_tool_handler",
        lambda *_args, **_kwargs: (
            calls.__setitem__("legacy", calls["legacy"] + 1) or {"success": True, "final_decision": "pass", "scene_saved": True}
        ),
    )

    spec_obj = SceneSpec.model_validate(_sample_spec())
    _batch, _report, used_fallback = app_module._execute_first_with_fallback(spec_obj)

    assert used_fallback is True
    assert calls["legacy"] == 1
