"""Tests for scene generator reliability and schema guardrails."""
from __future__ import annotations

from pathlib import Path

import pytest
from pydantic import ValidationError

from scene_generator.models import MCPCallPlan, MCPToolCall, SceneSpec
from scene_generator.validator import PlanValidator
from services.tools.scene_generator import _handle_load_spec


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
    assert "No USER structural component in mappings. VR scenes require a user representation." not in validator.warnings

    batch = validator.to_batch_plan(plan)
    manager_names = [m.manager_name for m in batch.manager_tasks]
    assert "GameManager" in manager_names


def test_validator_generates_focused_managers_when_required() -> None:
    spec = SceneSpec.model_validate_json(
        Path("Server/src/scene_generator/test_specs/bee_garden.json").read_text(encoding="utf-8")
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
    assert "particle_create" in vfx_actions
    assert "particle_set_main" in vfx_actions

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
