"""Multi-agent brainstorm pipeline for scene generation.

Implements the Parallelization pattern (Anthropic "Building Effective Agents"):
three focused LLM agents run concurrently, then an LLM-powered merge agent
reconciles their outputs into an enriched SceneSpec.

Model and API key configuration lives in scene_generator/config.py
(reads from .env file or environment variables).

Uses the OpenAI Responses API (client.responses.create) for all LLM calls.
"""
from __future__ import annotations

import asyncio
import json
import logging
from typing import Any

from pydantic import ValidationError

from .config import cfg
from .models import (
    BrainstormResult,
    CausalChainStep,
    InteractionSpec,
    SceneSpec,
    ScriptBlueprint,
    ScriptFieldSpec,
    ScriptMethodSpec,
)

logger = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# Low-level LLM call (async, OpenAI-only for brainstorm agents)
# ---------------------------------------------------------------------------


async def _call_openai(
    prompt: str,
    *,
    api_key: str,
    model: str | None = None,
) -> str | None:
    """Call OpenAI Responses API asynchronously.

    Uses the synchronous OpenAI client in a thread executor to avoid blocking
    the event loop — asyncio.to_thread is safe for this since each call
    instantiates its own client.
    """
    resolved_model = model or cfg.brainstorm_model
    def _sync_call() -> str | None:
        from openai import OpenAI
        client = OpenAI(api_key=api_key)
        response = client.responses.create(
            model=resolved_model,
            input=prompt,
            max_output_tokens=cfg.max_output_tokens,
        )
        return response.output_text

    try:
        return await asyncio.to_thread(_sync_call)
    except Exception:
        logger.exception("OpenAI Responses API call failed (model=%s)", resolved_model)
        return None


def _parse_json_response(text: str | None) -> dict[str, Any] | list[Any] | None:
    """Parse LLM JSON response, tolerating code fences."""
    if not text:
        return None
    import re
    # Try fenced blocks first
    fenced = re.findall(r"```(?:json)?\s*([\s\S]*?)```", text, flags=re.IGNORECASE)
    candidates = [block.strip() for block in fenced if block.strip()]
    candidates.append(text.strip())

    for candidate in candidates:
        try:
            parsed = json.loads(candidate)
            if isinstance(parsed, (dict, list)):
                return parsed
        except json.JSONDecodeError:
            pass
        # Try raw_decode from first {
        start = candidate.find("{")
        if start < 0:
            start = candidate.find("[")
        if start >= 0:
            try:
                parsed, _ = json.JSONDecoder().raw_decode(candidate[start:])
                if isinstance(parsed, (dict, list)):
                    return parsed
            except json.JSONDecodeError:
                pass
    return None


# ---------------------------------------------------------------------------
# Prompt builders (one per brainstorm agent)
# ---------------------------------------------------------------------------


def _build_causal_chain_prompt(spec: SceneSpec) -> str:
    """Build prompt for the Causal Chain Agent."""
    mappings_desc = []
    for m in spec.mappings:
        mappings_desc.append(
            f"- {m.structural_component}: \"{m.analogy_name}\" — {m.analogy_description}"
        )
    mappings_text = "\n".join(mappings_desc) if mappings_desc else "(no mappings)"

    existing_chain = ""
    if spec.experience.causal_chain:
        existing_chain = json.dumps(
            [step.model_dump(mode="json") for step in spec.experience.causal_chain],
            indent=2,
        )
    else:
        existing_chain = "(empty — you need to generate this)"

    return f"""You are an expert in causal reasoning and educational design.

## Context
A teacher is building an interactive 3D scene to teach **{spec.target_concept}** through the analogy of **{spec.analogy_domain}**.

**Learning goal:** {spec.learning_goal}

**Concept mappings (target → source):**
{mappings_text}

**Existing causal chain:**
{existing_chain}

## Your task
Generate a detailed causal chain: the sequence of observable cause-and-effect steps a learner should see when interacting with this scene. Each step must be grounded in both the source analogy AND the target concept.

Return a JSON array of objects, each with:
- "step": integer (1-indexed)
- "trigger_event": what the learner or system does to initiate this step
- "immediate_feedback": the instant visible/audible response (within 0.2s)
- "delayed_system_update": the behind-the-scenes state change (0.5-2s later)
- "observable_outcome": what the learner sees as the result

Requirements:
- At least 4 steps, at most 8
- First step should be a learner-initiated action
- Last step should show a visible outcome that demonstrates the target concept
- Each step's trigger_event should logically follow the previous step's observable_outcome
- Use the mapping object names (e.g. "{spec.mappings[0].analogy_name if spec.mappings else 'object'}") not abstract concepts

Return ONLY valid JSON array, no markdown fences, no commentary."""


def _build_interaction_prompt(spec: SceneSpec) -> str:
    """Build prompt for the Interaction Designer Agent."""
    mappings_desc = []
    object_names = []
    for m in spec.mappings:
        interaction_text = ""
        if m.interaction:
            interaction_text = (
                f" [current: trigger={m.interaction.trigger}, "
                f"effect={m.interaction.effect}]"
            )
        mappings_desc.append(
            f"- {m.structural_component}: \"{m.analogy_name}\" "
            f"(type={m.mapping_type}, confidence={m.mapping_confidence})"
            f"{interaction_text}"
            f"\n  Description: {m.analogy_description}"
        )
        if m.analogy_name.strip():
            object_names.append(m.analogy_name.strip())
    mappings_text = "\n".join(mappings_desc) if mappings_desc else "(no mappings)"
    names_text = ", ".join(object_names) if object_names else "(none)"

    return f"""You are an expert interaction designer for educational 3D experiences.

## Context
Teaching **{spec.target_concept}** through the analogy of **{spec.analogy_domain}**.
**Learning goal:** {spec.learning_goal}

**Mappings:**
{mappings_text}

**Valid object names for trigger_source and target_objects:** {names_text}

## Your task
For EACH mapping that has a relational or behavioral meaning (mapping_type "relation" or "higher_order"), design a rich interaction specification. For "object" type mappings, you may return null.

Return a JSON object keyed by analogy_name, where each value is either null or an object with:
- "trigger": one of "button_press", "proximity", "collision", "continuous", "on_start", "custom"
- "trigger_source": which object triggers this (must be from the valid names list)
- "target_objects": list of affected object names (from the valid names list)
- "effect": short action verb (e.g. "move_toward", "change_color", "grow", "emit_particles")
- "effect_description": 1-2 sentence description of what visually happens and why it teaches the concept
- "parameters": dict of numeric config (speeds, distances, durations)
- "animation_preset": one of "pulse", "hover", "sway", "spin", "bounce", "grow", "shrink", "shake", ""
- "vfx_type": one of "particle_burst", "particle_continuous", "line_beam", "trail", ""

Design principles:
- Interactions should form a CONNECTED SYSTEM where one mapping's output feeds another's input
- "relation" mappings MUST have interactions (not null)
- Use trigger_source and target_objects to create dependencies between mappings
- Make effects visually distinct so learners can tell them apart
- Parameters should be reasonable for a 30x30 unit scene

Return ONLY valid JSON object, no markdown fences, no commentary."""


def _build_script_architect_prompt(spec: SceneSpec) -> str:
    """Build prompt for the Script Architect Agent."""
    mappings_desc = []
    for m in spec.mappings:
        interaction_text = ""
        if m.interaction:
            interaction_text = (
                f"\n  Interaction: trigger={m.interaction.trigger}, "
                f"source={m.interaction.trigger_source}, "
                f"targets={m.interaction.target_objects}, "
                f"effect={m.interaction.effect}"
            )
        mappings_desc.append(
            f"- {m.structural_component}: \"{m.analogy_name}\""
            f"{interaction_text}"
        )
    mappings_text = "\n".join(mappings_desc) if mappings_desc else "(no mappings)"

    object_names = [m.analogy_name.strip() for m in spec.mappings if m.analogy_name.strip()]
    names_text = ", ".join(object_names) if object_names else "(none)"

    # Include manager architecture from experience
    managers = ["GameManager"]
    components = {m.structural_component for m in spec.mappings}
    if "user_interaction" in components:
        managers.append("InteractionManager")
    if "profile_update" in components or "user_profile" in components:
        managers.append("ProfileManager")
    if "candidate_generation" in components:
        managers.append("CandidateManager")
    if "ranking" in components:
        managers.append("RankingManager")

    return f"""You are an expert Unity C# architect designing MonoBehaviour scripts for an educational 3D scene.

## Context
Teaching **{spec.target_concept}** through **{spec.analogy_domain}**.
**Learning goal:** {spec.learning_goal}

**Scene objects:** {names_text}
**Required managers:** {", ".join(managers)}

**Mappings with interactions:**
{mappings_text}

## Your task
Design the complete script architecture: what MonoBehaviour classes are needed, their SerializeFields, method signatures, and how they communicate.

Return a JSON array of script blueprints, each with:
- "class_name": PascalCase C# class name (e.g. "BeeController", "GardenManager")
- "base_class": "MonoBehaviour" (always)
- "attach_to": which GameObject this attaches to (use exact object names or "GameManager" for globals)
- "purpose": one sentence explaining this script's role
- "fields": array of SerializeField specs:
  - "field_name": camelCase name
  - "field_type": C# type (e.g. "Transform", "float", "GameObject[]", "TextMeshProUGUI")
  - "purpose": what this field is for
  - "default_value": C# default literal or null
- "methods": array of method specs:
  - "method_name": exact C# method name (e.g. "Start", "Update", "OnTriggerEnter", "HandleInteraction")
  - "return_type": "void", "bool", "IEnumerator", etc.
  - "parameters": list of C# parameter strings (e.g. ["Collider other", "float amount"])
  - "purpose": what this method does
  - "pseudocode": 3-8 lines of pseudocode for the implementation logic
- "dependencies": list of other script class_names this script references via SerializeField or GetComponent
- "events_emitted": list of C# event/UnityEvent names this script invokes
- "events_listened": list of events this script subscribes to

Architecture rules:
- Every interactive mapping needs at least one script
- Managers coordinate between scripts — they should NOT contain interaction logic directly
- Use C# events (not SendMessage) for inter-script communication
- GameManager is the central orchestrator: tracks game state, progress, phase transitions
- Include a HUDController for the feedback HUD
- Script names follow pattern: [ObjectName]Controller, [Domain]Manager, HUDController
- Use SerializeField for all cross-object references (no FindObjectOfType in methods)
- Include OnTriggerEnter/OnCollisionEnter for proximity/collision triggers
- Include coroutines (IEnumerator) for delayed effects

Return ONLY valid JSON array, no markdown fences, no commentary."""


# ---------------------------------------------------------------------------
# Merge agent
# ---------------------------------------------------------------------------


def _build_merge_prompt(
    spec: SceneSpec,
    causal_chain: list[dict[str, Any]],
    interactions: dict[str, Any],
    blueprints: list[dict[str, Any]],
) -> str:
    """Build prompt for the LLM-powered Merge Agent."""
    return f"""You are a consistency checker and reconciler for a multi-agent scene generation pipeline.

Three specialist agents produced outputs for a 3D educational scene teaching **{spec.target_concept}** through **{spec.analogy_domain}**.

## Agent 1: Causal Chain
```json
{json.dumps(causal_chain, indent=2)}
```

## Agent 2: Interaction Designs
```json
{json.dumps(interactions, indent=2)}
```

## Agent 3: Script Architecture
```json
{json.dumps(blueprints, indent=2)}
```

## Scene object names
{", ".join(m.analogy_name for m in spec.mappings if m.analogy_name.strip())}

## Your task
Reconcile these three outputs into a coherent, consistent plan. Check for and resolve:

1. **Missing coverage**: Every causal chain step should have at least one interaction and script that implements it
2. **Name mismatches**: Object names in interactions/scripts must match exact scene object names
3. **Orphaned scripts**: Every script blueprint must be referenced by at least one interaction
4. **Missing dependencies**: If script A references script B in dependencies, B must exist
5. **Event wiring**: Every events_emitted must have a corresponding events_listened somewhere
6. **Trigger consistency**: Causal chain trigger_events should map to interaction triggers

Return a JSON object with:
- "causal_chain": the reconciled causal chain array (fix any gaps, keep all valid steps)
- "interactions": the reconciled interactions dict (fix names, add missing triggers)
- "script_blueprints": the reconciled blueprints array (fix dependencies, add missing methods)
- "merge_notes": array of strings describing each change you made and why

Be conservative: prefer keeping agent outputs intact when they're consistent. Only modify to fix actual conflicts or gaps.

Return ONLY valid JSON object, no markdown fences, no commentary."""


# ---------------------------------------------------------------------------
# Individual brainstorm agents
# ---------------------------------------------------------------------------


async def brainstorm_causal_chain(
    spec: SceneSpec,
    *,
    api_key: str,
) -> list[CausalChainStep]:
    """Run the Causal Chain Agent. Returns parsed chain steps."""
    prompt = _build_causal_chain_prompt(spec)
    raw = await _call_openai(prompt, api_key=api_key, model=cfg.brainstorm_model)
    parsed = _parse_json_response(raw)
    if not isinstance(parsed, list):
        logger.warning("Causal chain agent returned non-list: %s", type(parsed))
        return []

    steps: list[CausalChainStep] = []
    for item in parsed:
        if not isinstance(item, dict):
            continue
        try:
            steps.append(CausalChainStep.model_validate(item))
        except ValidationError:
            logger.debug("Skipping invalid causal chain step: %s", item)
    return steps


async def brainstorm_interactions(
    spec: SceneSpec,
    *,
    api_key: str,
) -> dict[str, InteractionSpec]:
    """Run the Interaction Designer Agent. Returns mapping name → InteractionSpec."""
    prompt = _build_interaction_prompt(spec)
    raw = await _call_openai(prompt, api_key=api_key, model=cfg.brainstorm_model)
    parsed = _parse_json_response(raw)
    if not isinstance(parsed, dict):
        logger.warning("Interaction agent returned non-dict: %s", type(parsed))
        return {}

    result: dict[str, InteractionSpec] = {}
    for name, data in parsed.items():
        if data is None:
            continue
        if not isinstance(data, dict):
            continue
        try:
            result[name] = InteractionSpec.model_validate(data)
        except ValidationError:
            logger.debug("Skipping invalid interaction for %s: %s", name, data)
    return result


async def brainstorm_script_architecture(
    spec: SceneSpec,
    *,
    api_key: str,
) -> list[ScriptBlueprint]:
    """Run the Script Architect Agent. Returns script blueprints."""
    prompt = _build_script_architect_prompt(spec)
    raw = await _call_openai(
        prompt, api_key=api_key, model=cfg.script_architect_model,
    )
    parsed = _parse_json_response(raw)
    if not isinstance(parsed, list):
        logger.warning("Script architect returned non-list: %s", type(parsed))
        return []

    blueprints: list[ScriptBlueprint] = []
    for item in parsed:
        if not isinstance(item, dict):
            continue
        try:
            blueprints.append(ScriptBlueprint.model_validate(item))
        except ValidationError:
            logger.debug("Skipping invalid blueprint: %s", item)
    return blueprints


# ---------------------------------------------------------------------------
# Merge step (LLM-powered)
# ---------------------------------------------------------------------------


async def merge_brainstorm_results(
    spec: SceneSpec,
    causal_chain: list[CausalChainStep],
    interactions: dict[str, InteractionSpec],
    blueprints: list[ScriptBlueprint],
    *,
    api_key: str,
) -> BrainstormResult:
    """Run the LLM Merge Agent to reconcile brainstorm outputs."""
    causal_dicts = [step.model_dump(mode="json") for step in causal_chain]
    interaction_dicts = {
        name: spec.model_dump(mode="json") for name, spec in interactions.items()
    }
    blueprint_dicts = [bp.model_dump(mode="json") for bp in blueprints]

    prompt = _build_merge_prompt(spec, causal_dicts, interaction_dicts, blueprint_dicts)
    raw = await _call_openai(prompt, api_key=api_key, model=cfg.merge_model)
    parsed = _parse_json_response(raw)

    if not isinstance(parsed, dict):
        logger.warning("Merge agent returned non-dict, using unmerged results")
        return BrainstormResult(
            causal_chain=causal_chain,
            enriched_interactions=interactions,
            script_blueprints=blueprints,
            merge_notes=["Merge agent failed — using raw brainstorm outputs"],
        )

    # Parse merged causal chain
    merged_chain: list[CausalChainStep] = []
    for item in parsed.get("causal_chain", []):
        if isinstance(item, dict):
            try:
                merged_chain.append(CausalChainStep.model_validate(item))
            except ValidationError:
                pass

    # Parse merged interactions
    merged_interactions: dict[str, InteractionSpec] = {}
    for name, data in parsed.get("interactions", {}).items():
        if data is None or not isinstance(data, dict):
            continue
        try:
            merged_interactions[name] = InteractionSpec.model_validate(data)
        except ValidationError:
            pass

    # Parse merged blueprints
    merged_blueprints: list[ScriptBlueprint] = []
    for item in parsed.get("script_blueprints", []):
        if isinstance(item, dict):
            try:
                merged_blueprints.append(ScriptBlueprint.model_validate(item))
            except ValidationError:
                pass

    merge_notes = parsed.get("merge_notes", [])
    if not isinstance(merge_notes, list):
        merge_notes = [str(merge_notes)]

    return BrainstormResult(
        causal_chain=merged_chain or causal_chain,
        enriched_interactions=merged_interactions or interactions,
        script_blueprints=merged_blueprints or blueprints,
        merge_notes=[str(n) for n in merge_notes],
    )


# ---------------------------------------------------------------------------
# Top-level brainstorm orchestrator
# ---------------------------------------------------------------------------


async def run_brainstorm(
    spec: SceneSpec,
    *,
    api_key: str,
    skip_merge: bool = False,
) -> BrainstormResult:
    """Run the full brainstorm pipeline: parallel agents → merge.

    This is the main entry point called from app.py.

    Args:
        spec: The teacher's SceneSpec.
        api_key: OpenAI API key for all brainstorm agents.
        skip_merge: If True, skip the merge agent and return raw results.

    Returns:
        BrainstormResult with enriched causal chain, interactions, and blueprints.
    """
    logger.info("Starting brainstorm pipeline (3 agents in parallel)")

    # Fan-out: run all three agents concurrently
    causal_task = brainstorm_causal_chain(spec, api_key=api_key)
    interaction_task = brainstorm_interactions(spec, api_key=api_key)
    architect_task = brainstorm_script_architecture(spec, api_key=api_key)

    causal_chain, interactions, blueprints = await asyncio.gather(
        causal_task, interaction_task, architect_task,
    )

    logger.info(
        "Brainstorm results: %d chain steps, %d interactions, %d blueprints",
        len(causal_chain), len(interactions), len(blueprints),
    )

    if skip_merge:
        return BrainstormResult(
            causal_chain=causal_chain,
            enriched_interactions=interactions,
            script_blueprints=blueprints,
            merge_notes=["Merge skipped"],
        )

    # Merge: reconcile the three outputs
    logger.info("Running LLM merge agent")
    result = await merge_brainstorm_results(
        spec, causal_chain, interactions, blueprints,
        api_key=api_key,
    )
    logger.info("Brainstorm complete: %d merge notes", len(result.merge_notes))
    return result


def apply_brainstorm_to_spec(
    spec: SceneSpec,
    result: BrainstormResult,
) -> SceneSpec:
    """Apply brainstorm results back into a SceneSpec (returns new copy).

    Enriches:
    - experience.causal_chain with brainstorm chain steps
    - mapping interactions with brainstorm interaction designs
    - (script_blueprints are carried separately in BatchExecutionPlan, not in SceneSpec)
    """
    spec_dict = spec.model_dump(mode="json")

    # Enrich causal chain
    if result.causal_chain:
        spec_dict["experience"]["causal_chain"] = [
            step.model_dump(mode="json") for step in result.causal_chain
        ]

    # Enrich interactions per mapping
    if result.enriched_interactions:
        for mapping in spec_dict.get("mappings", []):
            name = mapping.get("analogy_name", "")
            if name in result.enriched_interactions:
                mapping["interaction"] = result.enriched_interactions[name].model_dump(mode="json")

    return SceneSpec.model_validate(spec_dict)
