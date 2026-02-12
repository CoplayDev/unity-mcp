"""Streamlit GUI for creating and editing SceneSpec JSON files.

Educator-friendly interface with three-tab workflow grounded in analogy theory:
1. Focus & Mapping (Phase 1 + Phase 2): Teacher defines concept, prerequisites, and mapping table
2. Generate & Preview: LLM suggests interactions, environment, asset strategies
3. Reflection (Phase 4): LLM evaluates analogy quality against theoretical criteria
"""
from __future__ import annotations

import asyncio
import copy
import json
import os
import re
import sys
import hashlib
from pathlib import Path
from typing import Any, Literal
from urllib import error as urlerror, request as urlrequest

import streamlit as st
import streamlit.components.v1 as components
from pydantic import ValidationError

# When run via `streamlit run`, there's no parent package, so relative imports
# fail. Add the parent of this package to sys.path so absolute imports work.
_pkg_dir = Path(__file__).resolve().parent
if str(_pkg_dir.parent) not in sys.path:
    sys.path.insert(0, str(_pkg_dir.parent))

from scene_generator.models import (
    AssetStrategy,
    BatchExecutionPlan,
    DOMAIN_TEMPLATES,
    EssenceSpec,
    ExperienceSpec,
    MCPCallPlan,
    ReflectionResult,
    SceneSpec,
    SurfaceSpec,
    SkyboxPreset,
)
from scene_generator.validator import PlanValidator

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

TEST_SPECS_DIR = Path(__file__).parent / "test_specs"

ASSET_STRATEGIES = [e.value for e in AssetStrategy]
SKYBOX_PRESETS = [e.value for e in SkyboxPreset]

DOMAIN_TEMPLATE_NAMES = list(DOMAIN_TEMPLATES.keys())

MAPPING_TYPE_OPTIONS = ["relation", "object", "attribute", "higher_order"]
MAPPING_TYPE_HELP = {
    "relation": "A causal or functional relationship (most important per SMT)",
    "object": "A one-to-one entity correspondence",
    "attribute": "A shared property or feature",
    "higher_order": "A relation between relations (deepest structural level)",
}

TRIGGER_OPTIONS = [
    "button_press", "proximity", "collision", "continuous", "on_start", "custom",
]
ANIMATION_PRESETS = [
    "", "pulse", "hover", "sway", "spin", "bounce", "grow", "shrink",
    "shake", "fade_in", "fade_out", "orbit", "wave", "breathe",
]
VFX_TYPES = [
    "", "particle_burst", "particle_continuous", "line_beam", "trail",
]
PRIMITIVE_TYPES = [
    "Cube", "Sphere", "Cylinder", "Capsule", "Plane", "Quad",
]

LLM_PROVIDERS = ["OpenAI", "Anthropic"]
DEFAULT_LLM_MODELS: dict[str, str] = {
    "OpenAI": "gpt-5.2",
    "Anthropic": "claude-sonnet-4-5-20250929",
}
DEFAULT_CLARIFICATION_QUESTIONS = [
    "What should be the primary learner action trigger?",
    "What signal should dominate ranking behavior (proximity, recency, frequency, or another)?",
    "Any constraints on pacing, difficulty, or visual style to enforce?",
]

EXPERIENCE_PHASE_SEQUENCE = [
    "Intro",
    "Explore",
    "Trigger",
    "Observe Feedback Loop",
    "Summary",
]

SURFACE_STYLE_MOODS = ["natural", "playful", "futuristic"]
SURFACE_VARIATION_LEVELS = ["low", "medium", "high"]
DEFAULT_BACKEND_URL = "http://localhost:8080"
DEFAULT_BASE_FONT_SIZE_PX = 18
DEFAULT_API_KEY_ENV = "SCENE_BUILDER_DEFAULT_API_KEY"
DEFAULT_OPENAI_API_KEY_ENV = "SCENE_BUILDER_DEFAULT_OPENAI_API_KEY"
DEFAULT_ANTHROPIC_API_KEY_ENV = "SCENE_BUILDER_DEFAULT_ANTHROPIC_API_KEY"
DEFAULT_ALLOW_TRELLIS = False


def _get_template_labels(domain: str) -> dict[str, str]:
    """Return {component_key: friendly_label} for a domain template."""
    entries = DOMAIN_TEMPLATES.get(domain, [])
    return {e["component"]: e["label"] for e in entries}


def _get_template_component_options(domain: str) -> list[str]:
    """Return list of friendly labels for a domain template."""
    entries = DOMAIN_TEMPLATES.get(domain, [])
    return [e["label"] for e in entries]


def _label_to_component(domain: str) -> dict[str, str]:
    """Return {friendly_label: component_key} for a domain template."""
    entries = DOMAIN_TEMPLATES.get(domain, [])
    return {e["label"]: e["component"] for e in entries}


def _default_spec() -> dict[str, Any]:
    """Return a minimal empty spec dict."""
    return {
        "target_concept": "",
        "analogy_domain": "",
        "learning_goal": "",
        "task_label": "",
        "prerequisite_knowledge": "",
        "key_target_relations": [],
        "environment": {
            "setting": "garden",
            "terrain_type": "plane",
            "terrain_size": [30, 1, 30],
            "terrain_color": [0.3, 0.6, 0.2, 1.0],
            "skybox": "sunny",
            "ambient_color": [0.8, 0.9, 0.7, 1.0],
            "lighting": {
                "color": [1.0, 0.95, 0.9, 1.0],
                "intensity": 1.0,
                "rotation": [50, -30, 0],
                "shadow_type": "soft",
            },
            "camera": {
                "position": [0, 1.6, -5],
                "rotation": [10, 0, 0],
                "field_of_view": 60.0,
                "is_vr": False,
            },
            "description": "",
        },
        "experience": _default_experience(),
        "essence": None,
        "surface": _default_surface(),
        "essence_hash": None,
        "mappings": [],
    }


def _default_experience() -> dict[str, Any]:
    """Return default experience settings in JSON-ready form."""
    return ExperienceSpec().model_dump(mode="json")


def _default_surface() -> dict[str, Any]:
    """Return default surface settings in JSON-ready form."""
    return SurfaceSpec().model_dump(mode="json")


def _scene_backend_url() -> str:
    """Resolve backend base URL used for health checks and execute-first mode."""
    candidate = (
        os.environ.get("SCENE_BUILDER_BACKEND_URL")
        or os.environ.get("UNITY_MCP_HTTP_URL")
        or DEFAULT_BACKEND_URL
    )
    return str(candidate).strip().rstrip("/")


def _check_backend_health(base_url: str, timeout_seconds: float = 1.0) -> tuple[bool, str]:
    """Return backend health state and diagnostic reason."""
    if not base_url:
        return False, "No backend URL configured."
    url = f"{base_url}/health"
    try:
        req = urlrequest.Request(url, method="GET")
        with urlrequest.urlopen(req, timeout=max(0.2, float(timeout_seconds))) as response:
            if getattr(response, "status", 0) != 200:
                return False, f"Health check returned HTTP {getattr(response, 'status', 'unknown')}."
            payload = response.read().decode("utf-8", errors="ignore")
            if "healthy" not in payload.lower():
                return False, "Health check responded, but did not report healthy."
            return True, "Backend is healthy."
    except urlerror.HTTPError as exc:
        return False, f"Health check failed: HTTP {exc.code}."
    except Exception as exc:
        return False, f"Health check failed: {exc!s}"


def _select_generation_mode(backend_healthy: bool) -> Literal["execute_first", "prompt_export"]:
    """Choose primary generation mode based on backend availability."""
    return "execute_first" if backend_healthy else "prompt_export"


def _apply_intent_wizard(
    experience_payload: dict[str, Any],
    primary_action: str,
    immediate_feedback: str,
    delayed_update: str,
    success_evidence: str,
    hud_sections: list[str],
) -> dict[str, Any]:
    """Write intent wizard values into existing ExperienceSpec-compatible fields."""
    exp = _normalize_experience_payload(experience_payload)

    objective_parts = [str(primary_action).strip(), str(success_evidence).strip()]
    objective = " ".join(part for part in objective_parts if part)
    if objective:
        exp["objective"] = objective

    criteria: list[str] = []
    if str(primary_action).strip():
        criteria.append(f"Primary learner action: {str(primary_action).strip()}")
    if str(immediate_feedback).strip():
        criteria.append(f"Immediate feedback: {str(immediate_feedback).strip()}")
    if str(delayed_update).strip():
        criteria.append(f"Delayed update: {str(delayed_update).strip()}")
    if str(success_evidence).strip():
        criteria.append(f"Success evidence: {str(success_evidence).strip()}")
    if criteria:
        exp["success_criteria"] = criteria

    sections = [str(section).strip() for section in hud_sections if str(section).strip()]
    if sections:
        exp["feedback_hud_enabled"] = True
        exp["feedback_hud_sections"] = sections

    return _normalize_experience_payload(exp)


# ---------------------------------------------------------------------------
# Color helpers
# ---------------------------------------------------------------------------

def _rgba_to_hex(rgba: list[float]) -> str:
    """Convert [r,g,b,a] floats (0-1) to #RRGGBB hex string."""
    r = int(max(0, min(1, rgba[0])) * 255)
    g = int(max(0, min(1, rgba[1])) * 255)
    b = int(max(0, min(1, rgba[2])) * 255)
    return f"#{r:02x}{g:02x}{b:02x}"


def _hex_to_rgba(hex_str: str, alpha: float = 1.0) -> list[float]:
    """Convert #RRGGBB hex string to [r,g,b,a] floats."""
    hex_str = hex_str.lstrip("#")
    r = int(hex_str[0:2], 16) / 255.0
    g = int(hex_str[2:4], 16) / 255.0
    b = int(hex_str[4:6], 16) / 255.0
    return [round(r, 3), round(g, 3), round(b, 3), alpha]


def _inject_readability_styles() -> None:
    """Increase default app typography for easier reading."""
    st.markdown(
        f"""
<style>
html, body, [data-testid="stAppViewContainer"] {{
  font-size: {DEFAULT_BASE_FONT_SIZE_PX}px;
}}
[data-testid="stAppViewContainer"] p,
[data-testid="stAppViewContainer"] li,
[data-testid="stAppViewContainer"] label,
[data-testid="stAppViewContainer"] span,
[data-testid="stAppViewContainer"] div {{
  line-height: 1.45;
}}
[data-testid="stMarkdownContainer"] p,
[data-testid="stMarkdownContainer"] li {{
  font-size: 1.02rem;
}}
[data-testid="stTextInput"] input,
[data-testid="stTextArea"] textarea,
[data-testid="stSelectbox"] div[role="combobox"],
[data-testid="stNumberInput"] input {{
  font-size: 1rem;
}}
[data-testid="stCaptionContainer"] {{
  font-size: 0.95rem;
}}
</style>
        """,
        unsafe_allow_html=True,
    )


def _render_copy_button(text: str, label: str, *, key: str) -> None:
    """Render a one-click clipboard button for prompt text."""
    button_id = f"copy_btn_{hashlib.sha1(key.encode('utf-8')).hexdigest()[:12]}"
    status_id = f"copy_status_{hashlib.sha1((key + '_status').encode('utf-8')).hexdigest()[:12]}"
    payload = json.dumps(str(text))
    components.html(
        f"""
<div style="display:flex;align-items:center;gap:.5rem;">
  <button id="{button_id}" style="padding:.35rem .8rem;border:1px solid #ccc;border-radius:6px;background:#fff;cursor:pointer;">
    {label}
  </button>
  <span id="{status_id}" style="font-size:.9rem;color:#2e7d32;"></span>
</div>
<script>
(() => {{
  const btn = document.getElementById("{button_id}");
  const status = document.getElementById("{status_id}");
  const text = {payload};
  if (!btn) return;
  btn.addEventListener("click", async () => {{
    try {{
      await navigator.clipboard.writeText(text);
      if (status) {{
        status.textContent = "Copied";
        setTimeout(() => {{ status.textContent = ""; }}, 1400);
      }}
    }} catch (_err) {{
      if (status) {{
        status.textContent = "Copy failed";
      }}
    }}
  }});
}})();
</script>
        """,
        height=42,
    )


def _apply_asset_policy_to_suggestions(
    suggestions: dict[str, Any],
    *,
    allow_trellis: bool,
) -> dict[str, Any]:
    """Normalize suggestions to current asset policy (primitive-first by default)."""
    if allow_trellis:
        return suggestions

    normalized = copy.deepcopy(suggestions)

    mapping_suggestions = normalized.get("mapping_suggestions", [])
    if isinstance(mapping_suggestions, list):
        for row in mapping_suggestions:
            if not isinstance(row, dict):
                continue
            if str(row.get("asset_strategy", "")).strip().lower() == "trellis":
                row["asset_strategy"] = "primitive"
                if not row.get("primitive_type"):
                    row["primitive_type"] = "Cube"
            row.pop("trellis_prompt", None)

    overrides = normalized.get("mapping_surface_overrides", [])
    if isinstance(overrides, list):
        for row in overrides:
            if isinstance(row, dict):
                row.pop("trellis_prompt", None)

    return normalized


def _apply_asset_policy_to_spec(spec: dict[str, Any], *, allow_trellis: bool) -> int:
    """Apply asset policy directly to spec mappings. Returns number of conversions."""
    if allow_trellis:
        return 0

    converted = 0
    for mapping in spec.get("mappings", []):
        if not isinstance(mapping, dict):
            continue
        strategy = str(mapping.get("asset_strategy", "")).strip().lower()
        if strategy == "trellis":
            mapping["asset_strategy"] = "primitive"
            if not mapping.get("primitive_type"):
                mapping["primitive_type"] = "Cube"
            converted += 1
        mapping.pop("trellis_prompt", None)

    return converted


# ---------------------------------------------------------------------------
# Session state init
# ---------------------------------------------------------------------------

def _init_state() -> None:
    if "spec_data" not in st.session_state:
        st.session_state["spec_data"] = _default_spec()
    if "validation_errors" not in st.session_state:
        st.session_state["validation_errors"] = []
    if "llm_provider" not in st.session_state:
        st.session_state["llm_provider"] = "OpenAI"
    if "llm_api_key" not in st.session_state:
        st.session_state["llm_api_key"] = ""
    if "llm_api_key_from_default" not in st.session_state:
        st.session_state["llm_api_key_from_default"] = False
    if "llm_api_key_provider" not in st.session_state:
        st.session_state["llm_api_key_provider"] = st.session_state["llm_provider"]
    if "llm_model_openai" not in st.session_state:
        st.session_state["llm_model_openai"] = DEFAULT_LLM_MODELS["OpenAI"]
    if "llm_model_anthropic" not in st.session_state:
        st.session_state["llm_model_anthropic"] = DEFAULT_LLM_MODELS["Anthropic"]
    if "allow_trellis_generation" not in st.session_state:
        st.session_state["allow_trellis_generation"] = DEFAULT_ALLOW_TRELLIS
    if "llm_suggestions" not in st.session_state:
        st.session_state["llm_suggestions"] = None
    if "suggestions_accepted" not in st.session_state:
        st.session_state["suggestions_accepted"] = False
    if "domain_template" not in st.session_state:
        st.session_state["domain_template"] = "AI Recommendation System"
    if "reflection_result" not in st.session_state:
        st.session_state["reflection_result"] = None
    if "clarification_questions" not in st.session_state:
        st.session_state["clarification_questions"] = list(DEFAULT_CLARIFICATION_QUESTIONS)
    if "structure_lock_warning" not in st.session_state:
        st.session_state["structure_lock_warning"] = None
    if "show_json_io_tools" not in st.session_state:
        st.session_state["show_json_io_tools"] = False
    if "user_followup_question" not in st.session_state:
        st.session_state["user_followup_question"] = ""


def _get_spec() -> dict[str, Any]:
    spec = st.session_state["spec_data"]
    spec.setdefault("experience", _default_experience())
    spec.setdefault("surface", _default_surface())
    if not isinstance(spec.get("surface"), dict):
        spec["surface"] = _default_surface()
    return spec


def _set_spec(data: dict[str, Any]) -> None:
    data.setdefault("surface", _default_surface())
    data.setdefault("essence", None)
    data.setdefault("essence_hash", None)
    st.session_state["spec_data"] = data
    st.session_state["validation_errors"] = []
    st.session_state["llm_suggestions"] = None
    st.session_state["suggestions_accepted"] = False
    st.session_state["reflection_result"] = None
    st.session_state["clarification_questions"] = list(DEFAULT_CLARIFICATION_QUESTIONS)
    st.session_state["structure_lock_warning"] = None
    _reset_refinement_feedback()


def _reset_refinement_feedback() -> None:
    """Clear follow-up question/feedback inputs used for LLM refinement."""
    for i in range(3):
        st.session_state.pop(f"clarify_q_{i}", None)
        st.session_state.pop(f"clarify_a_{i}", None)
    st.session_state.pop("clarify_extra_feedback", None)


def _normalize_clarification_questions(raw: Any) -> list[str]:
    """Normalize clarification question candidates to exactly three prompts."""
    candidate_items: Any = raw
    if isinstance(raw, dict):
        for key in ("clarification_questions", "questions", "follow_up_questions"):
            maybe_items = raw.get(key)
            if isinstance(maybe_items, list):
                candidate_items = maybe_items
                break

    cleaned: list[str] = []
    if isinstance(candidate_items, list):
        for item in candidate_items:
            text = str(item).strip()
            if text:
                cleaned.append(text)

    deduped: list[str] = []
    seen: set[str] = set()
    for question in cleaned:
        key = question.lower()
        if key in seen:
            continue
        seen.add(key)
        deduped.append(question)
        if len(deduped) == 3:
            break

    if len(deduped) < 3:
        for fallback in DEFAULT_CLARIFICATION_QUESTIONS:
            key = fallback.lower()
            if key in seen:
                continue
            seen.add(key)
            deduped.append(fallback)
            if len(deduped) == 3:
                break

    return deduped[:3]


def _canonical_component(component: str) -> str:
    text = str(component).strip().lower()
    text = "".join(ch if ch.isalnum() else "_" for ch in text)
    return "_".join(token for token in text.split("_") if token)


def _stable_hash(payload: dict[str, Any]) -> str:
    normalized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def _derive_essence_payload(spec: dict[str, Any]) -> dict[str, Any]:
    mappings = spec.get("mappings", [])
    mapping_role_ids: list[str] = []
    for m in mappings:
        role = _canonical_component(m.get("structural_component", ""))
        source = str(m.get("analogy_name", "")).strip()
        if not role:
            continue
        mapping_role_ids.append(f"{role}:{source}" if source else role)

    exp = _normalize_experience_payload(spec.get("experience", {}))
    phase_ids = [str(item.get("phase_name", "")).strip() for item in exp.get("phases", []) if str(item.get("phase_name", "")).strip()]
    success_criteria = [str(item).strip() for item in exp.get("success_criteria", []) if str(item).strip()]
    causal_chain_ids = [str(item.get("trigger_event", "")).strip() for item in exp.get("causal_chain", []) if str(item.get("trigger_event", "")).strip()]

    required_managers = ["GameManager"]
    components = {_canonical_component(m.get("structural_component", "")) for m in mappings}
    if "user_interaction" in components:
        required_managers.append("InteractionManager")
    if "profile_update" in components or "user_profile" in components:
        required_managers.append("ProfileManager")
    if "candidate_generation" in components:
        required_managers.append("CandidateManager")
    if "ranking" in components:
        required_managers.append("RankingManager")

    return EssenceSpec(
        mapping_role_ids=mapping_role_ids,
        phase_ids=phase_ids,
        success_criteria=success_criteria,
        causal_chain_ids=causal_chain_ids,
        required_managers=required_managers,
        character_role_id="user",
        ui_role_id="feedback_hud",
    ).model_dump(mode="json")


def _freeze_essence() -> tuple[bool, str]:
    spec = _get_spec()
    try:
        SceneSpec.model_validate(spec)
    except ValidationError:
        return False, "Fix validation errors before freezing Essence."

    essence = _derive_essence_payload(spec)
    spec["essence"] = essence
    spec["essence_hash"] = _stable_hash(essence)
    return True, "Lesson structure locked. Future generations will preserve the same lesson structure."


def _try_validate() -> SceneSpec | None:
    """Try to validate current spec_data, return SceneSpec or None."""
    try:
        spec = SceneSpec.model_validate(_get_spec())
        st.session_state["validation_errors"] = []
        return spec
    except ValidationError as e:
        st.session_state["validation_errors"] = [
            f"{err['loc']}: {err['msg']}" for err in e.errors()
        ]
        return None


# ---------------------------------------------------------------------------
# LLM Integration
# ---------------------------------------------------------------------------

def _get_default_api_key(provider: str) -> str | None:
    """Get app-configured default API key for provider."""
    generic = os.environ.get(DEFAULT_API_KEY_ENV)
    if provider == "OpenAI":
        return os.environ.get(DEFAULT_OPENAI_API_KEY_ENV) or generic
    return os.environ.get(DEFAULT_ANTHROPIC_API_KEY_ENV) or generic


def _get_api_key() -> str | None:
    """Get API key from session state, provider env vars, or app defaults."""
    provider = st.session_state.get("llm_provider", "OpenAI")
    key = st.session_state.get("llm_api_key", "")
    if key:
        return key
    env_var = "OPENAI_API_KEY" if provider == "OpenAI" else "ANTHROPIC_API_KEY"
    return os.environ.get(env_var) or _get_default_api_key(provider)


def _get_model_for_provider(provider: str) -> str:
    """Return selected model for provider, with env and default fallback."""
    provider_name = provider if provider in LLM_PROVIDERS else "OpenAI"
    if provider_name == "OpenAI":
        value = str(st.session_state.get("llm_model_openai", "")).strip()
        return value or os.environ.get("SCENE_BUILDER_OPENAI_MODEL", DEFAULT_LLM_MODELS["OpenAI"])
    value = str(st.session_state.get("llm_model_anthropic", "")).strip()
    return value or os.environ.get("SCENE_BUILDER_ANTHROPIC_MODEL", DEFAULT_LLM_MODELS["Anthropic"])


def _get_selected_model() -> str:
    """Return current provider model."""
    provider = st.session_state.get("llm_provider", "OpenAI")
    return _get_model_for_provider(provider)


def _build_llm_prompt(spec: dict[str, Any]) -> str:
    """Build the prompt sent to the LLM for generating suggestions.

    Enriched with relational structure context from the proposed table research.
    """
    domain = st.session_state.get("domain_template", "Custom")
    labels = _get_template_labels(domain)

    mappings_desc = []
    for m in spec.get("mappings", []):
        comp = m.get("structural_component", "")
        friendly = labels.get(comp, comp)
        mapping_type = m.get("mapping_type", "relation")
        confidence = m.get("mapping_confidence", "strong")
        mappings_desc.append(
            f"- {friendly}: \"{m.get('analogy_name', '')}\" "
            f"[type={mapping_type}, confidence={confidence}] "
            f"- {m.get('analogy_description', '')}"
        )
    mappings_text = "\n".join(mappings_desc) if mappings_desc else "(no mappings yet)"
    object_names = [
        str(m.get("analogy_name", "")).strip()
        for m in spec.get("mappings", [])
        if str(m.get("analogy_name", "")).strip()
    ]
    object_names_text = ", ".join(object_names) if object_names else "(none)"

    # Phase 1 Focus context
    prereq = spec.get("prerequisite_knowledge", "")
    key_relations = spec.get("key_target_relations", [])
    key_relations_text = ", ".join(key_relations) if key_relations else "(not specified)"
    experience_pref = _normalize_experience_payload(spec.get("experience", {}))
    experience_objective = experience_pref.get("objective", "")
    experience_target = experience_pref.get("progress_target", 3)
    surface = spec.get("surface", _default_surface())
    style_mood = str(surface.get("style_mood", "natural")).strip() or "natural"
    variation_level = str(surface.get("variation_level", "medium")).strip() or "medium"
    essence = spec.get("essence")
    essence_hash = spec.get("essence_hash")
    essence_text = json.dumps(essence, indent=2) if isinstance(essence, dict) else "(not frozen yet)"
    allow_trellis = bool(st.session_state.get("allow_trellis_generation", DEFAULT_ALLOW_TRELLIS))
    asset_policy_text = (
        "Trellis is enabled, but keep a primitive-first plan and only use Trellis if clearly necessary."
        if allow_trellis
        else "Use a primitive-first plan. Do not output Trellis strategies or Trellis prompts."
    )
    user_followup_question = str(st.session_state.get("user_followup_question", "")).strip()

    return f"""You are an expert educational game designer grounded in analogical reasoning theory (Structure-Mapping Theory, FAR Guide, embodied cognition). A teacher wants to create an interactive 3D learning experience that teaches a concept through a physical analogy.

## What the teacher provided

**Teaching concept (target):** {spec.get('target_concept', '')}
**Analogy being used (source):** {spec.get('analogy_domain', '')}
**Learning goal:** {spec.get('learning_goal', '')}
**Task label:** {spec.get('task_label', '')}
**Prerequisite knowledge:** {prereq if prereq else '(not specified)'}
**Key target relations to preserve:** {key_relations_text}
**Experience objective preference:** {experience_objective if experience_objective else '(not specified)'}
**Suggested progress target:** {experience_target}
**Surface style mood:** {style_mood}
**Surface variation level:** {variation_level}
**Asset policy:** {asset_policy_text}
**Additional teacher question:** {user_followup_question if user_followup_question else "(none)"}

**Concept mapping (how target maps to source):**
{mappings_text}

## Theoretical guidance

Per Structure-Mapping Theory (Gentner, 1983), effective analogies map **relational structure** rather than surface features. The systematicity principle holds that connected systems of relations are preferred over isolated mappings. When generating suggestions:

1. **Prioritize relational mappings** - ensure interactions capture causal/functional relationships, not just visual similarity
2. **Ensure systematicity** - interactions should form a connected system where one mapping's output feeds into another's input
3. **Respect mapping types** - "relation" and "higher_order" mappings need behavioral/interactive representations; "object" mappings primarily need visual representations
4. **Ground in embodiment** - the source domain should leverage physical, sensorimotor interactions the learner can perform in an interactive 3D scene (Niebert et al., 2012)

## ESSENCE_INVARIANTS (must not change)

Essence hash: {essence_hash if essence_hash else "(none)"}
Essence payload:
```json
{essence_text}
```

If Essence is provided, preserve it exactly: do not change mapping meaning, phase order, success criteria, or causal-chain semantics.

## SURFACE_VARIATION_BUDGET (can change)

- You may vary character look, assets/materials/colors, UI skin/layout tone, and VFX style.
- Keep required runtime anchors present: manager architecture, learner character representation, and functioning UI/HUD.
- Variation level: {variation_level} (low=subtle, medium=moderate, high=bolder visual difference).

## Your task

Generate suggestions to bring this analogy to life as a 3D scene. Return a JSON object with these fields:

0. **essence_check**:
   - "essence_hash_echo": repeat the provided hash or "" if none
   - "essence_changed": boolean (must be false when Essence exists)
   - "notes": short string

1. **environment**: Suggest appropriate environment settings
   - "setting": a short label (e.g. "garden", "ocean", "factory")
   - "description": one-sentence description of the environment
   - "skybox": one of "sunny", "sunset", "night", "overcast"
   - "terrain_color": [r, g, b, a] floats 0-1

2. **mapping_suggestions**: An array (one per mapping above, same order) where each entry has:
   - "asset_strategy": one of "primitive", "trellis", "vfx", "mechanic", "ui"
   - "primitive_type": (if primitive) one of "Cube", "Sphere", "Cylinder", "Capsule", "Plane", "Quad"
   - "trellis_prompt": (if trellis) a text prompt for 3D model generation
   - "position": [x, y, z] suggested position in scene
   - "scale": [x, y, z] suggested scale
   - "color": [r, g, b, a] or null
   - "instance_count": integer (for content_item, how many instances)
   - "instance_spread": float (spacing between instances)
   - "interaction": object or null, with fields:
     - "trigger": one of "button_press", "proximity", "collision", "continuous", "on_start", "custom"
     - "trigger_source": which object triggers this
     - "target_objects": list of object names affected
     - "effect": short action label
     - "effect_description": natural language description of what happens
     - "animation_preset": one of "pulse", "hover", "sway", "spin", "bounce", "grow", "shrink", "shake", "" (empty for none)
     - "vfx_type": one of "particle_burst", "particle_continuous", "line_beam", "trail", "" (empty for none)
     - "parameters": dict of numeric config

3. **game_loop_description**: A 2-3 sentence description of the overall interaction loop from the learner's perspective. Emphasize how the connected system of interactions maps to the relational structure of the target concept.

4. **experience_suggestions**: A learner-facing experience plan object with:
   - "objective": one clear learner objective sentence
   - "success_criteria": list of completion checks
   - "progress_metric_label": short UI label for progress (e.g., "Loop Progress")
   - "progress_target": integer target for completion (e.g., 3)
   - "phases": ordered list with these phase names:
     - "Intro"
     - "Explore"
     - "Trigger"
     - "Observe Feedback Loop"
     - "Summary"
     Each phase item includes:
     - "phase_name"
     - "objective"
     - "player_action"
     - "expected_feedback"
     - "completion_criteria"
   - "causal_chain": list of visible cause/effect steps, each containing:
     - "step"
     - "trigger_event"
     - "immediate_feedback"
     - "delayed_system_update"
     - "observable_outcome"
   - "guided_prompts": list of UI prompts with:
     - "phase_name"
     - "prompt"
     - "optional" (boolean)
   - "feedback_hud_enabled": boolean
   - "feedback_hud_sections": list of HUD panel sections to show (e.g., objective, progress, profile, candidates, ranking)
   - "spatial_staging": list of zones with:
     - "zone_name"
     - "purpose"
     - "anchor_object"
     - "suggested_center" [x, y, z]
     - "suggested_radius"
   - "audio_cues": list of cues with:
     - "cue_name"
     - "trigger"
     - "purpose"
     - "delay_seconds"
     - "volume" (0-1)
   - "timing_guidelines": dictionary of named delay recommendations in seconds

5. **surface_suggestions**:
   - "style_seed": integer
   - "style_mood": one of "natural", "playful", "futuristic"
   - "variation_level": one of "low", "medium", "high"
   - "character_style": short style label
   - "asset_style": short style label
   - "ui_skin": short style label
   - "vfx_style": short style label

## Output constraints

- Return exactly {len(spec.get("mappings", []))} entries in `mapping_suggestions` (same order as input mappings).
- Use only these object names for `trigger_source` and `target_objects`: {object_names_text}
- Follow the asset policy above for every mapping suggestion.
- If `interaction` is not null, include all of:
  - `trigger`
  - `trigger_source` (non-empty string)
  - `target_objects` (non-empty array)
  - `effect_description` (non-empty string)
- If you cannot infer a meaningful interaction for a row, set `interaction` to `null` instead of leaving partial fields.
- For "relation" and "higher_order" mapping types, strongly prefer generating interactions (not null) to capture the relational structure.
- In `experience_suggestions.phases`, include all five phases exactly once, in order.
- Ensure `causal_chain` has at least 2 steps and reflects: trigger -> immediate -> delayed -> observable.
- If Essence exists, set `essence_check.essence_changed` to false and keep Essence-identifying fields unchanged.

Return ONLY valid JSON, no markdown fences, no commentary."""


def _build_refinement_prompt(
    spec: dict[str, Any],
    current_suggestions: dict[str, Any],
    clarifications: list[dict[str, str]],
    extra_feedback: str = "",
) -> str:
    """Build prompt for refinement pass using follow-up Q/A feedback."""
    object_names = [
        str(m.get("analogy_name", "")).strip()
        for m in spec.get("mappings", [])
        if str(m.get("analogy_name", "")).strip()
    ]
    object_names_text = ", ".join(object_names) if object_names else "(none)"

    cleaned_clarifications: list[dict[str, str]] = []
    for item in clarifications:
        question = str(item.get("question", "")).strip()
        answer = str(item.get("answer", "")).strip()
        if question:
            cleaned_clarifications.append({"question": question, "answer": answer})

    essence = spec.get("essence")
    essence_hash = spec.get("essence_hash")
    essence_text = json.dumps(essence, indent=2) if isinstance(essence, dict) else "(not frozen yet)"
    surface = spec.get("surface", _default_surface())
    style_mood = str(surface.get("style_mood", "natural")).strip() or "natural"
    variation_level = str(surface.get("variation_level", "medium")).strip() or "medium"
    allow_trellis = bool(st.session_state.get("allow_trellis_generation", DEFAULT_ALLOW_TRELLIS))
    asset_policy_text = (
        "Trellis is enabled, but keep primitive-first unless a Trellis model is clearly necessary."
        if allow_trellis
        else "Primitive-first policy: do not introduce Trellis strategies or Trellis prompts."
    )
    user_followup_question = str(st.session_state.get("user_followup_question", "")).strip()

    return f"""You are refining an existing scene generation plan. Do NOT start from scratch.

## Original SceneSpec
```json
{json.dumps(spec, indent=2)}
```

## Current Suggestions (baseline to preserve)
```json
{json.dumps(current_suggestions, indent=2)}
```

## Clarification Q/A (optional user feedback)
```json
{json.dumps(cleaned_clarifications, indent=2)}
```

## Additional feedback
{extra_feedback if extra_feedback else "(none)"}

## Additional teacher question (sidebar)
{user_followup_question if user_followup_question else "(none)"}

## ESSENCE_INVARIANTS
- Essence hash: {essence_hash if essence_hash else "(none)"}
```json
{essence_text}
```
- Preserve Essence semantics exactly when provided.

## SURFACE_VARIATION_BUDGET
- style_mood: {style_mood}
- variation_level: {variation_level}
- You may vary visuals/UI/VFX style but not semantic mappings, phase flow, or success semantics.
- Asset policy: {asset_policy_text}

## Refinement rules
- Keep the same JSON schema as the current suggestions:
  - `environment`
  - `mapping_suggestions`
  - `game_loop_description`
  - `experience_suggestions`
- Keep mapping order unchanged and return exactly {len(spec.get("mappings", []))} `mapping_suggestions`.
- Preserve existing values unless feedback explicitly requires a change.
- If a clarification answer is empty, treat it as no preference and keep baseline behavior.
- Use only these object names for `trigger_source` and `target_objects`: {object_names_text}
- If `interaction` is not null, it must include:
  - `trigger`
  - `trigger_source` (non-empty string)
  - `target_objects` (non-empty array)
  - `effect_description` (non-empty string)
- Keep `experience_suggestions.phases` in this exact order:
  - Intro
  - Explore
  - Trigger
  - Observe Feedback Loop
  - Summary
- Keep `causal_chain` explicit: each step must include trigger, immediate feedback, delayed update, and observable outcome.
- Return `surface_suggestions` with style fields for this refinement pass.
- Return `essence_check` and keep `essence_changed=false` when Essence exists.

Return ONLY valid JSON, no markdown fences, no commentary."""


def _build_clarification_questions_prompt(
    spec: dict[str, Any],
    current_suggestions: dict[str, Any],
) -> str:
    """Build prompt for LLM-generated clarification questions."""
    user_followup_question = str(st.session_state.get("user_followup_question", "")).strip()
    return f"""You are helping refine a generated interactive 3D analogy scene plan.

Given the current SceneSpec and current AI suggestions, generate exactly 3 short clarification questions
that would most improve the next refinement pass.

## SceneSpec
```json
{json.dumps(spec, indent=2)}
```

## Current Suggestions
```json
{json.dumps(current_suggestions, indent=2)}
```

## Additional teacher question (sidebar)
{user_followup_question if user_followup_question else "(none)"}

## Rules
- Ask exactly 3 questions.
- Prioritize high-impact ambiguities: primary trigger behavior, ranking/profile feedback semantics, and learner experience constraints.
- Keep each question concise and educator-friendly.
- Avoid questions that ask to rewrite the full concept from scratch.
- If Essence is frozen, avoid questions that would change semantic mappings or phase order.

Return ONLY valid JSON with this exact shape:
{{
  "clarification_questions": [
    "question 1",
    "question 2",
    "question 3"
  ]
}}
"""


def _generate_clarification_questions(
    spec: dict[str, Any],
    current_suggestions: dict[str, Any],
) -> list[str]:
    """Generate follow-up clarification questions, with robust fallback."""
    prompt = _build_clarification_questions_prompt(spec, current_suggestions)
    response_text = _call_llm(prompt)
    if not response_text:
        return list(DEFAULT_CLARIFICATION_QUESTIONS)

    parsed = _parse_llm_response(response_text, show_errors=False)
    if parsed is None:
        return list(DEFAULT_CLARIFICATION_QUESTIONS)

    return _normalize_clarification_questions(parsed)


def _build_reflection_prompt(spec: dict[str, Any]) -> str:
    """Build the prompt for Phase 4 reflection/evaluation of analogy quality."""
    return f"""You are an expert in analogical reasoning theory evaluating an interactive 3D learning analogy design.

## SceneSpec to evaluate
```json
{json.dumps(spec, indent=2)}
```

## Evaluation criteria (from SMT, FAR Guide, embodied cognition research)

Evaluate this analogy design on six dimensions. For each, provide a score (0.0 to 1.0) and brief notes.

1. **Structural Completeness** (SMT systematicity): Are all key target relations mapped to source entities with interactions? Are the mappings connected into a coherent relational system, or are they isolated?

2. **Embodiment Quality** (Niebert et al., 2012): Is the source domain grounded in everyday sensorimotor experience? Can the learner physically interact with the analogy in the interactive 3D scene?

3. **Cognitive Load** (Petchey et al., 2023): Is the analog simpler and more familiar than the target concept? Could the interactive 3D scene overwhelm the learner with too many simultaneous elements? (Lower score = lower load = better)

4. **Misconception Risks** (FAR Action phase): What false inferences might the 3D representation invite? List specific risks.

5. **Unlikes / Breakdowns** (FAR Action phase): Where does the analogy fail? For each breakdown, identify the mapping, describe where it breaks down, and suggest how to address it.

6. **Overall Assessment**: List strengths and actionable suggestions for improvement.

## Required JSON output format

Return a JSON object with these exact fields:
{{
  "structural_completeness": 0.0-1.0,
  "structural_completeness_notes": "...",
  "embodiment_quality": 0.0-1.0,
  "embodiment_quality_notes": "...",
  "cognitive_load": 0.0-1.0,
  "cognitive_load_notes": "...",
  "misconception_risks": ["risk 1", "risk 2", ...],
  "unlikes": [
    {{"mapping": "name", "breakdown": "description", "suggestion": "how to address"}},
    ...
  ],
  "strengths": ["strength 1", "strength 2", ...],
  "suggestions": ["suggestion 1", "suggestion 2", ...],
  "overall_score": 0.0-1.0
}}

Return ONLY valid JSON, no markdown fences, no commentary."""


def _build_surface_variant_prompt(spec: dict[str, Any]) -> str:
    """Build prompt for generating a new surface variant while preserving frozen essence."""
    essence = spec.get("essence")
    essence_hash = spec.get("essence_hash")
    surface = spec.get("surface", _default_surface())
    mappings = spec.get("mappings", [])
    allow_trellis = bool(st.session_state.get("allow_trellis_generation", DEFAULT_ALLOW_TRELLIS))

    if not isinstance(essence, dict) or not essence_hash:
        return "Lesson structure is not locked yet. Lock it before generating a visual style variant."

    mapping_names = [str(m.get("analogy_name", "")).strip() for m in mappings if str(m.get("analogy_name", "")).strip()]
    mapping_name_text = ", ".join(mapping_names) if mapping_names else "(none)"

    return f"""You are generating a new SURFACE variant for an existing lesson.

## ESSENCE_INVARIANTS (must remain unchanged)
Essence hash: {essence_hash}
```json
{json.dumps(essence, indent=2)}
```

Do NOT change lesson semantics, mapping meaning, phase order, success criteria, or causal-chain semantics.

## Current SceneSpec
```json
{json.dumps(spec, indent=2)}
```

## SURFACE_VARIATION_BUDGET
- style_mood: {surface.get("style_mood", "natural")}
- variation_level: {surface.get("variation_level", "medium")}
- preserve character presence, manager architecture, and UI/HUD presence
- asset policy: {"trellis optional, primitive-first" if allow_trellis else "primitive-first (no trellis prompts)"}

## Output JSON (only these fields)
{{
  "essence_check": {{
    "essence_hash_echo": "{essence_hash}",
    "essence_changed": false,
    "notes": "..."
  }},
  "surface_suggestions": {{
    "style_seed": 0,
    "style_mood": "natural|playful|futuristic",
    "variation_level": "low|medium|high",
    "character_style": "...",
    "asset_style": "...",
    "ui_skin": "...",
    "vfx_style": "..."
  }},
  "environment_surface": {{
    "description": "...",
    "skybox": "sunny|sunset|night|overcast",
    "terrain_color": [0-1,0-1,0-1,0-1]
  }},
  "mapping_surface_overrides": [
    {{
      "name": "one of: {mapping_name_text}",
      "primitive_type": "Cube|Sphere|Cylinder|Capsule|Plane|Quad|null",
      "trellis_prompt": "...|null",
      "color": [0-1,0-1,0-1,0-1] | null,
      "animation_preset": "...|null",
      "vfx_type": "...|null"
    }}
  ]
}}

Return only valid JSON."""


def _normalize_interaction(interaction: Any, fallback_name: str = "") -> dict[str, Any] | None:
    """Normalize/repair a possibly incomplete interaction payload from the LLM."""
    if not isinstance(interaction, dict):
        return None

    cleaned: dict[str, Any] = {}

    trigger = str(interaction.get("trigger", "")).strip()
    if trigger:
        cleaned["trigger"] = trigger
    else:
        cleaned["trigger"] = "custom"

    source = str(interaction.get("trigger_source", "")).strip() or fallback_name
    if source:
        cleaned["trigger_source"] = source

    raw_targets = interaction.get("target_objects", [])
    targets: list[str] = []
    if isinstance(raw_targets, list):
        targets = [str(t).strip() for t in raw_targets if str(t).strip()]
    elif isinstance(raw_targets, str):
        targets = [t.strip() for t in raw_targets.split(",") if t.strip()]
    if not targets and fallback_name:
        targets = [fallback_name]
    if targets:
        cleaned["target_objects"] = targets

    effect = str(interaction.get("effect", "")).strip()
    if effect:
        cleaned["effect"] = effect

    effect_desc = str(interaction.get("effect_description", "")).strip()
    if not effect_desc:
        effect_desc = effect
    if effect_desc:
        cleaned["effect_description"] = effect_desc

    animation_preset = str(interaction.get("animation_preset", "")).strip()
    if animation_preset:
        cleaned["animation_preset"] = animation_preset

    vfx_type = str(interaction.get("vfx_type", "")).strip()
    if vfx_type:
        cleaned["vfx_type"] = vfx_type

    params = interaction.get("parameters")
    if isinstance(params, dict) and params:
        cleaned["parameters"] = params

    # Ignore clearly empty interaction payloads.
    has_core = bool(cleaned.get("effect_description")) or bool(cleaned.get("effect"))
    if not has_core:
        return None
    return cleaned


def _format_interaction_summary(interaction: dict[str, Any], fallback_name: str = "") -> str:
    """Render interaction text without placeholder symbols."""
    trigger = interaction.get("trigger", "custom")
    source = interaction.get("trigger_source") or fallback_name or "this object"
    effect_desc = interaction.get("effect_description") or interaction.get("effect") or "an interaction effect"
    targets = interaction.get("target_objects", [])
    targets_str = ", ".join(targets) if targets else "its targets"
    return (
        f"When *{trigger}*, **{source}** causes "
        f"*{effect_desc}* on **{targets_str}**"
    )


def _normalize_experience_payload(payload: Any) -> dict[str, Any]:
    """Normalize a potentially partial/invalid experience payload."""
    base = _default_experience()
    if not isinstance(payload, dict):
        return base

    normalized = dict(base)

    objective = str(payload.get("objective", "")).strip()
    if objective:
        normalized["objective"] = objective

    success_criteria = payload.get("success_criteria")
    if isinstance(success_criteria, list):
        cleaned = [str(item).strip() for item in success_criteria if str(item).strip()]
        if cleaned:
            normalized["success_criteria"] = cleaned

    progress_label = str(payload.get("progress_metric_label", "")).strip()
    if progress_label:
        normalized["progress_metric_label"] = progress_label

    progress_target = payload.get("progress_target")
    if isinstance(progress_target, (int, float)):
        normalized["progress_target"] = max(1, int(progress_target))

    phases = payload.get("phases")
    if isinstance(phases, list):
        cleaned_phases: list[dict[str, Any]] = []
        for raw in phases:
            if not isinstance(raw, dict):
                continue
            phase_name = str(raw.get("phase_name", "")).strip()
            if not phase_name:
                continue
            cleaned_phases.append({
                "phase_name": phase_name,
                "objective": str(raw.get("objective", "")).strip(),
                "player_action": str(raw.get("player_action", "")).strip(),
                "expected_feedback": str(raw.get("expected_feedback", "")).strip(),
                "completion_criteria": str(raw.get("completion_criteria", "")).strip(),
            })
        if cleaned_phases:
            normalized["phases"] = cleaned_phases

    prompts = payload.get("guided_prompts")
    if isinstance(prompts, list):
        cleaned_prompts: list[dict[str, Any]] = []
        for raw in prompts:
            if not isinstance(raw, dict):
                continue
            prompt = str(raw.get("prompt", "")).strip()
            if not prompt:
                continue
            cleaned_prompts.append({
                "phase_name": str(raw.get("phase_name", "")).strip(),
                "prompt": prompt,
                "optional": bool(raw.get("optional", True)),
            })
        if cleaned_prompts:
            normalized["guided_prompts"] = cleaned_prompts

    if isinstance(payload.get("feedback_hud_enabled"), bool):
        normalized["feedback_hud_enabled"] = payload["feedback_hud_enabled"]

    hud_sections = payload.get("feedback_hud_sections")
    if isinstance(hud_sections, list):
        cleaned_sections = [str(item).strip() for item in hud_sections if str(item).strip()]
        if cleaned_sections:
            normalized["feedback_hud_sections"] = cleaned_sections

    spatial = payload.get("spatial_staging")
    if isinstance(spatial, list):
        cleaned_spatial: list[dict[str, Any]] = []
        for raw in spatial:
            if not isinstance(raw, dict):
                continue
            zone_name = str(raw.get("zone_name", "")).strip()
            if not zone_name:
                continue
            center = raw.get("suggested_center", [0.0, 0.0, 0.0])
            if not isinstance(center, list) or len(center) < 3:
                center = [0.0, 0.0, 0.0]
            center_vals: list[float] = []
            for i in range(3):
                try:
                    center_vals.append(float(center[i]))
                except (TypeError, ValueError, IndexError):
                    center_vals.append(0.0)
            try:
                radius = float(raw.get("suggested_radius", 4.0))
            except (TypeError, ValueError):
                radius = 4.0
            cleaned_spatial.append({
                "zone_name": zone_name,
                "purpose": str(raw.get("purpose", "")).strip(),
                "anchor_object": str(raw.get("anchor_object", "")).strip(),
                "suggested_center": center_vals,
                "suggested_radius": max(0.1, radius),
            })
        if cleaned_spatial:
            normalized["spatial_staging"] = cleaned_spatial

    audio = payload.get("audio_cues")
    if isinstance(audio, list):
        cleaned_audio: list[dict[str, Any]] = []
        for raw in audio:
            if not isinstance(raw, dict):
                continue
            cue_name = str(raw.get("cue_name", "")).strip()
            if not cue_name:
                continue
            try:
                delay_seconds = float(raw.get("delay_seconds", 0.0))
            except (TypeError, ValueError):
                delay_seconds = 0.0
            try:
                volume = float(raw.get("volume", 0.6))
            except (TypeError, ValueError):
                volume = 0.6
            cleaned_audio.append({
                "cue_name": cue_name,
                "trigger": str(raw.get("trigger", "")).strip(),
                "purpose": str(raw.get("purpose", "")).strip(),
                "delay_seconds": max(0.0, delay_seconds),
                "volume": min(1.0, max(0.0, volume)),
            })
        if cleaned_audio:
            normalized["audio_cues"] = cleaned_audio

    timing = payload.get("timing_guidelines")
    if isinstance(timing, dict):
        cleaned_timing: dict[str, float] = {}
        for key, value in timing.items():
            k = str(key).strip()
            if not k:
                continue
            try:
                cleaned_timing[k] = float(value)
            except (TypeError, ValueError):
                continue
        if cleaned_timing:
            normalized["timing_guidelines"] = cleaned_timing

    causal_chain = payload.get("causal_chain")
    if isinstance(causal_chain, list):
        cleaned_chain: list[dict[str, Any]] = []
        for i, raw in enumerate(causal_chain):
            if not isinstance(raw, dict):
                continue
            step_raw = raw.get("step", i + 1)
            try:
                step = int(step_raw)
            except (TypeError, ValueError):
                step = i + 1
            cleaned_chain.append({
                "step": max(1, step),
                "trigger_event": str(raw.get("trigger_event", "")).strip(),
                "immediate_feedback": str(raw.get("immediate_feedback", "")).strip(),
                "delayed_system_update": str(raw.get("delayed_system_update", "")).strip(),
                "observable_outcome": str(raw.get("observable_outcome", "")).strip(),
            })
        if cleaned_chain:
            cleaned_chain.sort(key=lambda item: item["step"])
            normalized["causal_chain"] = cleaned_chain

    return normalized


def _render_experience_preview(experience_payload: dict[str, Any], section_title: str = "Experience Plan") -> None:
    """Render a readable learner-experience preview block."""
    exp = _normalize_experience_payload(experience_payload)

    st.markdown(f"#### {section_title}")
    st.caption("Learner-facing flow with explicit phases, guidance, and observable cause/effect.")

    st.markdown(f"**Objective:** {exp.get('objective', '')}")
    criteria = exp.get("success_criteria", [])
    if criteria:
        st.markdown("**Success Criteria**")
        for item in criteria:
            st.caption(f"- {item}")

    c1, c2, c3 = st.columns(3)
    c1.metric("Progress Label", exp.get("progress_metric_label", "Progress"))
    c2.metric("Progress Target", int(exp.get("progress_target", 1)))
    c3.metric("HUD Enabled", "Yes" if exp.get("feedback_hud_enabled", True) else "No")

    phases = exp.get("phases", [])
    if phases:
        st.markdown("**Phase Flow**")
        phase_rows = []
        for idx, phase in enumerate(phases, start=1):
            phase_rows.append({
                "Order": idx,
                "Phase": phase.get("phase_name", ""),
                "Player Action": phase.get("player_action", ""),
                "Expected Feedback": phase.get("expected_feedback", ""),
                "Completion": phase.get("completion_criteria", ""),
            })
        st.table(phase_rows)

    chain = exp.get("causal_chain", [])
    if chain:
        st.markdown("**Causal Chain (Visible Cause/Effect)**")
        chain_rows = []
        for item in chain:
            chain_rows.append({
                "Step": item.get("step", ""),
                "Trigger": item.get("trigger_event", ""),
                "Immediate": item.get("immediate_feedback", ""),
                "Delayed Update": item.get("delayed_system_update", ""),
                "Outcome": item.get("observable_outcome", ""),
            })
        st.table(chain_rows)

    prompts = exp.get("guided_prompts", [])
    if prompts:
        st.markdown("**Guided UI Prompts**")
        for item in prompts:
            phase_name = item.get("phase_name", "")
            prompt = item.get("prompt", "")
            optional = item.get("optional", True)
            suffix = " (optional)" if optional else ""
            st.caption(f"- [{phase_name}] {prompt}{suffix}")

    hud_sections = exp.get("feedback_hud_sections", [])
    if hud_sections:
        st.markdown("**Feedback HUD Sections**")
        st.caption(", ".join(hud_sections))

    spatial = exp.get("spatial_staging", [])
    if spatial:
        st.markdown("**Spatial Staging Zones**")
        zone_rows = []
        for zone in spatial:
            center = zone.get("suggested_center", [0, 0, 0])
            center_text = f"({center[0]}, {center[1]}, {center[2]})" if isinstance(center, list) and len(center) >= 3 else ""
            zone_rows.append({
                "Zone": zone.get("zone_name", ""),
                "Purpose": zone.get("purpose", ""),
                "Anchor": zone.get("anchor_object", ""),
                "Center": center_text,
                "Radius": zone.get("suggested_radius", ""),
            })
        st.table(zone_rows)

    audio = exp.get("audio_cues", [])
    if audio:
        st.markdown("**Audio & Timing Cues**")
        audio_rows = []
        for cue in audio:
            audio_rows.append({
                "Cue": cue.get("cue_name", ""),
                "Trigger": cue.get("trigger", ""),
                "Purpose": cue.get("purpose", ""),
                "Delay (s)": cue.get("delay_seconds", 0.0),
                "Volume": cue.get("volume", 0.0),
            })
        st.table(audio_rows)

    timing = exp.get("timing_guidelines", {})
    if timing:
        st.markdown("**Timing Guidelines (seconds)**")
        st.code(json.dumps(timing, indent=2), language="json")


def _call_llm(prompt: str) -> str | None:
    """Call the selected LLM provider and return the response text."""
    provider = st.session_state.get("llm_provider", "OpenAI")
    model_name = _get_model_for_provider(provider)
    api_key = _get_api_key()
    if not api_key:
        st.error("No API key configured. Set it in the sidebar or via environment variable.")
        return None

    try:
        if provider == "OpenAI":
            from openai import OpenAI
            client = OpenAI(api_key=api_key)
            response = client.chat.completions.create(
                model=model_name,
                messages=[{"role": "user", "content": prompt}],
                temperature=0.7,
                max_completion_tokens=4000,
            )
            return response.choices[0].message.content
        else:
            from anthropic import Anthropic
            client = Anthropic(api_key=api_key)
            response = client.messages.create(
                model=model_name,
                max_tokens=4000,
                messages=[{"role": "user", "content": prompt}],
            )
            return response.content[0].text
    except ImportError as e:
        missing = getattr(e, "name", None) or "unknown module"
        package_name = "openai" if provider == "OpenAI" else "anthropic"
        st.error(
            "LLM client import failed.\n"
            f"- Provider: `{provider}`\n"
            f"- Missing module: `{missing}`\n"
            f"- Python executable: `{sys.executable}`\n"
            f"- Install with this interpreter: `{sys.executable} -m pip install {package_name}`\n"
            "If this still fails, run Streamlit with the same interpreter:"
            f" `{sys.executable} -m streamlit run Server/src/scene_generator/app.py`"
        )
        return None
    except Exception as e:
        st.error(f"LLM call failed: {e}")
        return None


def _parse_llm_response(response_text: str, *, show_errors: bool = True) -> dict[str, Any] | None:
    """Parse an LLM JSON response, tolerating fences and trailing text."""
    text = str(response_text or "").strip()
    if not text:
        if show_errors:
            st.error("LLM returned an empty response.")
        return None

    candidates: list[str] = []
    fenced_matches = re.findall(r"```(?:json)?\s*([\s\S]*?)```", text, flags=re.IGNORECASE)
    for block in fenced_matches:
        block_text = str(block).strip()
        if block_text:
            candidates.append(block_text)
    candidates.append(text)

    # De-duplicate while preserving order.
    deduped: list[str] = []
    seen: set[str] = set()
    for candidate in candidates:
        if candidate in seen:
            continue
        seen.add(candidate)
        deduped.append(candidate)

    decoder = json.JSONDecoder()
    last_error: json.JSONDecodeError | None = None

    for candidate in deduped:
        try:
            parsed = json.loads(candidate)
            if isinstance(parsed, dict):
                return parsed
        except json.JSONDecodeError as exc:
            last_error = exc

        start = candidate.find("{")
        if start < 0:
            continue
        fragment = candidate[start:]
        try:
            parsed, _end = decoder.raw_decode(fragment)
            if isinstance(parsed, dict):
                return parsed
        except json.JSONDecodeError as exc:
            last_error = exc

    if last_error is not None:
        if show_errors:
            st.error(f"Could not parse LLM response as JSON: {last_error}")
    else:
        if show_errors:
            st.error("Could not parse LLM response as JSON: no JSON object found.")
    if show_errors:
        st.code(text[:500], language="json")
    return None


def _execute_batch_plan_with_tool_handler(
    batch_plan: BatchExecutionPlan,
    *,
    max_retries_per_batch: int = 2,
    retry_backoff_seconds: float = 1.5,
    stop_on_warning: bool = False,
) -> dict[str, Any]:
    """Execute a batch plan via server-side scene_generator execution handler."""
    try:
        from services.tools.scene_generator import _handle_execute_batch_plan
    except Exception as exc:
        return {
            "success": False,
            "message": (
                "Execute-first mode is unavailable because scene generator execution "
                f"handler could not be imported: {exc!s}"
            ),
        }

    class _AppContext:
        def get_state(self, _key: str) -> None:
            return None

    coroutine = _handle_execute_batch_plan(
        ctx=_AppContext(),  # type: ignore[arg-type]
        batch_plan_json=batch_plan.model_dump_json(),
        max_retries_per_batch=max_retries_per_batch,
        retry_backoff_seconds=retry_backoff_seconds,
        stop_on_warning=stop_on_warning,
    )
    try:
        return asyncio.run(coroutine)
    except RuntimeError:
        loop = asyncio.new_event_loop()
        try:
            return loop.run_until_complete(coroutine)
        finally:
            loop.close()


def _plan_and_execute_with_tool_handler(
    spec_obj: SceneSpec,
    *,
    max_retries_per_batch: int = 2,
    retry_backoff_seconds: float = 1.5,
    stop_on_warning: bool = False,
) -> dict[str, Any]:
    """Run SceneSpec-first planner+executor via backend handler."""
    try:
        from services.tools.scene_generator import _handle_plan_and_execute
    except Exception as exc:
        return {
            "success": False,
            "action": "plan_and_execute",
            "summary": "plan commands=0, phases=0, estimated_batches=0; execution=fail; failed_phase=unknown; scene_saved=false.",
            "message": (
                "Execute-first mode is unavailable because scene generator planner/executor "
                f"handler could not be imported: {exc!s}"
            ),
            "planning": {
                "success": False,
                "message": "Planner/executor handler import failed.",
                "warnings": [],
                "total_commands": 0,
                "estimated_batches": 0,
                "trellis_count": 0,
                "phase_names": [],
                "manager_count": 0,
                "script_task_count": 0,
                "batch_plan": None,
            },
            "execution": None,
            "final_decision": "fail",
            "scene_saved": False,
            "failure_stage": "planning",
        }

    class _AppContext:
        def get_state(self, _key: str) -> None:
            return None

    coroutine = _handle_plan_and_execute(
        ctx=_AppContext(),  # type: ignore[arg-type]
        spec_json=spec_obj.model_dump_json(),
        max_retries_per_batch=max_retries_per_batch,
        retry_backoff_seconds=retry_backoff_seconds,
        stop_on_warning=stop_on_warning,
    )
    try:
        return asyncio.run(coroutine)
    except RuntimeError:
        loop = asyncio.new_event_loop()
        try:
            return loop.run_until_complete(coroutine)
        finally:
            loop.close()


def _hydrate_batch_plan_from_plan_and_execute_report(report: dict[str, Any] | None) -> BatchExecutionPlan | None:
    """Extract and validate planning.batch_plan from plan_and_execute response."""
    if not isinstance(report, dict):
        return None
    if str(report.get("action", "")).strip() != "plan_and_execute":
        return None
    planning = report.get("planning")
    if not isinstance(planning, dict):
        return None
    batch_plan_data = planning.get("batch_plan")
    if not isinstance(batch_plan_data, dict):
        return None
    try:
        return BatchExecutionPlan.model_validate(batch_plan_data)
    except Exception:
        return None


def _execute_first_with_fallback(
    spec_obj: SceneSpec,
    *,
    max_retries_per_batch: int = 2,
    retry_backoff_seconds: float = 1.5,
    stop_on_warning: bool = False,
) -> tuple[BatchExecutionPlan, dict[str, Any], bool]:
    """Run plan_and_execute first, fallback to legacy local planning only when needed."""
    report = _plan_and_execute_with_tool_handler(
        spec_obj,
        max_retries_per_batch=max_retries_per_batch,
        retry_backoff_seconds=retry_backoff_seconds,
        stop_on_warning=stop_on_warning,
    )
    batch_plan = _hydrate_batch_plan_from_plan_and_execute_report(report)
    if batch_plan is not None:
        return batch_plan, report, False

    fallback_plan = MCPCallPlan()
    validator = PlanValidator(spec_obj)
    fallback_plan = validator.validate_and_repair(fallback_plan)
    fallback_batch_plan = validator.to_batch_plan(fallback_plan)
    fallback_report = _execute_batch_plan_with_tool_handler(
        fallback_batch_plan,
        max_retries_per_batch=max_retries_per_batch,
        retry_backoff_seconds=retry_backoff_seconds,
        stop_on_warning=stop_on_warning,
    )
    return fallback_batch_plan, fallback_report, True


def _merge_suggestions_into_spec(suggestions: dict[str, Any], surface_only: bool = False) -> None:
    """Merge LLM suggestions into the current spec_data.

    When surface_only=True, preserve frozen Essence and only apply presentation-level updates.
    """
    spec = _get_spec()
    allow_trellis = bool(st.session_state.get("allow_trellis_generation", DEFAULT_ALLOW_TRELLIS))
    suggestions = _apply_asset_policy_to_suggestions(suggestions, allow_trellis=allow_trellis)
    has_frozen_essence = bool(spec.get("essence_hash")) and isinstance(spec.get("essence"), dict)
    if has_frozen_essence:
        surface_only = True
    before_essence_hash = spec.get("essence_hash")
    before_essence = spec.get("essence")

    essence_check = suggestions.get("essence_check")
    if has_frozen_essence and isinstance(essence_check, dict):
        if bool(essence_check.get("essence_changed")):
            st.warning("Essence relation changed; suggestion rejected.")
            return
        echoed = str(essence_check.get("essence_hash_echo", "")).strip()
        if echoed and echoed != str(before_essence_hash):
            st.warning("Essence relation changed; suggestion rejected.")
            return

    if surface_only:
        surface_sug = suggestions.get("surface_suggestions", {})
        if isinstance(surface_sug, dict):
            current_surface = spec.setdefault("surface", _default_surface())
            for key in ("style_seed", "style_mood", "variation_level", "character_style", "asset_style", "ui_skin", "vfx_style"):
                if key in surface_sug and surface_sug.get(key) is not None:
                    current_surface[key] = surface_sug[key]

        env_surface = suggestions.get("environment_surface", {})
        if isinstance(env_surface, dict):
            env = spec.setdefault("environment", _default_spec()["environment"])
            if env_surface.get("description"):
                env["description"] = env_surface["description"]
            if env_surface.get("skybox"):
                env["skybox"] = env_surface["skybox"]
            if isinstance(env_surface.get("terrain_color"), list):
                env["terrain_color"] = env_surface["terrain_color"]

        name_to_mapping: dict[str, dict[str, Any]] = {}
        for m in spec.get("mappings", []):
            name = str(m.get("analogy_name", "")).strip()
            if name:
                name_to_mapping[name] = m
        overrides = suggestions.get("mapping_surface_overrides", [])
        if isinstance(overrides, list):
            for row in overrides:
                if not isinstance(row, dict):
                    continue
                name = str(row.get("name", "")).strip()
                if not name or name not in name_to_mapping:
                    continue
                target = name_to_mapping[name]
                for key in ("primitive_type", "trellis_prompt", "color"):
                    if key in row:
                        target[key] = row[key]
                ix = target.get("interaction")
                if isinstance(ix, dict):
                    if row.get("animation_preset") is not None:
                        ix["animation_preset"] = row.get("animation_preset") or ""
                    if row.get("vfx_type") is not None:
                        ix["vfx_type"] = row.get("vfx_type") or ""

        # Preserve frozen essence no matter what suggestions returned.
        if has_frozen_essence:
            spec["essence"] = before_essence
            spec["essence_hash"] = before_essence_hash
        return

    # Merge environment suggestions
    env_suggestions = suggestions.get("environment", {})
    env = spec.setdefault("environment", _default_spec()["environment"])
    if env_suggestions.get("setting"):
        env["setting"] = env_suggestions["setting"]
    if env_suggestions.get("description"):
        env["description"] = env_suggestions["description"]
    if env_suggestions.get("skybox"):
        env["skybox"] = env_suggestions["skybox"]
    if env_suggestions.get("terrain_color"):
        env["terrain_color"] = env_suggestions["terrain_color"]

    # Merge per-mapping suggestions
    mapping_suggestions = suggestions.get("mapping_suggestions", [])
    mappings = spec.get("mappings", [])

    for i, m_sug in enumerate(mapping_suggestions):
        if i >= len(mappings):
            break
        m = mappings[i]
        if m_sug.get("asset_strategy"):
            m["asset_strategy"] = m_sug["asset_strategy"]
        if m_sug.get("primitive_type"):
            m["primitive_type"] = m_sug["primitive_type"]
        if m_sug.get("trellis_prompt"):
            m["trellis_prompt"] = m_sug["trellis_prompt"]
        if m_sug.get("position"):
            m["position"] = m_sug["position"]
        if m_sug.get("scale"):
            m["scale"] = m_sug["scale"]
        if m_sug.get("color"):
            m["color"] = m_sug["color"]
        if m_sug.get("instance_count"):
            m["instance_count"] = m_sug["instance_count"]
        if m_sug.get("instance_spread"):
            m["instance_spread"] = m_sug["instance_spread"]
        normalized_interaction = _normalize_interaction(
            m_sug.get("interaction"),
            str(m.get("analogy_name", "")).strip(),
        )
        if normalized_interaction:
            m["interaction"] = normalized_interaction

    # Merge experience suggestions (if provided)
    raw_experience = suggestions.get("experience_suggestions")
    if isinstance(raw_experience, dict):
        existing_experience = _normalize_experience_payload(spec.get("experience", {}))
        incoming_experience = _normalize_experience_payload(raw_experience)
        for key in raw_experience.keys():
            if key in incoming_experience:
                existing_experience[key] = incoming_experience[key]
        spec["experience"] = existing_experience

    # If essence is frozen, do not allow any semantic drift via merge path.
    if has_frozen_essence:
        spec["essence"] = before_essence
        spec["essence_hash"] = before_essence_hash


# ---------------------------------------------------------------------------
# Sidebar
# ---------------------------------------------------------------------------

def _render_sidebar() -> None:
    with st.sidebar:
        st.title("Scene Builder")

        with st.expander("Developer Options", expanded=False):
            st.markdown("**Asset Plan Policy**")
            st.caption("Primitive-first is the default. Enable Trellis only when needed.")
            allow_trellis = st.checkbox(
                "Enable Trellis model generation (optional)",
                value=bool(st.session_state.get("allow_trellis_generation", DEFAULT_ALLOW_TRELLIS)),
                key="allow_trellis_generation",
                help="When disabled, Trellis strategies/prompts are normalized to primitives.",
            )
            if not allow_trellis:
                converted_count = _apply_asset_policy_to_spec(_get_spec(), allow_trellis=False)
                if converted_count > 0:
                    st.caption(
                        f"Primitive-first policy applied: converted {converted_count} Trellis mapping(s) to primitives."
                    )

            st.divider()
            st.toggle(
                "Show JSON import/export tools",
                key="show_json_io_tools",
                help="Enable manual JSON load/export utilities for debugging and developer workflows.",
            )

        if st.session_state.get("show_json_io_tools", False):
            # Load JSON file
            st.subheader("Load")
            uploaded = st.file_uploader("Import JSON", type=["json"], key="json_upload")
            if uploaded is not None:
                try:
                    data = json.loads(uploaded.read())
                    SceneSpec.model_validate(data)  # validate before accepting
                    _set_spec(data)
                    st.success("Loaded successfully")
                except (json.JSONDecodeError, ValidationError) as e:
                    st.error(f"Invalid JSON: {e}")

        # Presets
        st.subheader("Presets")
        preset_files = {
            "Bee Garden": "bee_garden.json",
            "Sprinkler": "sprinkler_garden.json",
            "Simple Demo": "simple_demo.json",
        }
        cols = st.columns(len(preset_files))
        for col, (label, filename) in zip(cols, preset_files.items()):
            with col:
                if st.button(label, width="stretch"):
                    path = TEST_SPECS_DIR / filename
                    if path.exists():
                        data = json.loads(path.read_text())
                        _set_spec(data)
                        st.rerun()

        # New Spec
        if st.button("Start Fresh", width="stretch"):
            _set_spec(_default_spec())
            st.rerun()

        if st.session_state.get("show_json_io_tools", False):
            # Export
            st.subheader("Export")
            spec_json = json.dumps(_get_spec(), indent=2)
            st.download_button(
                label="Download JSON",
                data=spec_json,
                file_name="scene_spec.json",
                mime="application/json",
                width="stretch",
            )

        # --- API Key section ---
        st.divider()
        st.subheader("AI Assistant")
        st.session_state["llm_provider"] = st.selectbox(
            "Provider", LLM_PROVIDERS,
            index=LLM_PROVIDERS.index(st.session_state.get("llm_provider", "OpenAI")),
            help="Which AI provider to use for generating suggestions.",
        )
        provider = st.session_state.get("llm_provider", "OpenAI")
        previous_key_provider = st.session_state.get("llm_api_key_provider")
        if previous_key_provider != provider and st.session_state.get("llm_api_key_from_default"):
            # Re-resolve provider-specific defaults when a default key was auto-applied.
            st.session_state["llm_api_key"] = ""
            st.session_state["llm_api_key_from_default"] = False
        if provider == "OpenAI":
            st.session_state["llm_model_openai"] = st.text_input(
                "Model",
                value=st.session_state.get("llm_model_openai", DEFAULT_LLM_MODELS["OpenAI"]),
                help="OpenAI model id used for suggestions (default tracks latest configured value).",
            )
        else:
            st.session_state["llm_model_anthropic"] = st.text_input(
                "Model",
                value=st.session_state.get("llm_model_anthropic", DEFAULT_LLM_MODELS["Anthropic"]),
                help="Anthropic model id used for suggestions (default tracks latest configured value).",
            )
        default_key = _get_default_api_key(provider)
        prefilled_from_default = bool(default_key) and not st.session_state.get("llm_api_key")
        if prefilled_from_default:
            st.session_state["llm_api_key"] = default_key

        env_var = "OPENAI_API_KEY" if provider == "OpenAI" else "ANTHROPIC_API_KEY"
        env_key = os.environ.get(env_var)
        placeholder = (
            "Using configured default key"
            if prefilled_from_default
            else ("Set via environment variable" if (env_key and not st.session_state.get("llm_api_key")) else "Paste your API key")
        )
        st.session_state["llm_api_key"] = st.text_input(
            "API Key", value=st.session_state.get("llm_api_key", ""),
            type="password", placeholder=placeholder,
            help=(
                "Supports provider env vars (OPENAI_API_KEY / ANTHROPIC_API_KEY) and app defaults "
                "(SCENE_BUILDER_DEFAULT_API_KEY, SCENE_BUILDER_DEFAULT_OPENAI_API_KEY, "
                "SCENE_BUILDER_DEFAULT_ANTHROPIC_API_KEY)."
            ),
        )
        st.session_state["llm_api_key_from_default"] = bool(
            default_key and st.session_state.get("llm_api_key", "") == default_key
        )
        st.session_state["llm_api_key_provider"] = provider
        st.caption(f"Current model: `{_get_selected_model()}`")
        if _get_api_key():
            st.success("API key configured")
        else:
            st.warning("No API key set")

        st.text_area(
            "Further question for AI (optional)",
            key="user_followup_question",
            height=90,
            placeholder="Ask any additional question or constraint for the next suggestion/refinement pass.",
            help="This note is included in AI suggestion/refinement prompts.",
        )

        # Validation status
        st.divider()
        errors = st.session_state.get("validation_errors", [])
        if errors:
            st.error(f"{len(errors)} validation error(s)")
            for err in errors:
                st.caption(f"- {err}")
        else:
            _try_validate()
            errors = st.session_state.get("validation_errors", [])
            if errors:
                st.error(f"{len(errors)} validation error(s)")
                for err in errors:
                    st.caption(f"- {err}")
            else:
                st.success("Spec is valid")


# ---------------------------------------------------------------------------
# Tab 1: Focus & Mapping
# ---------------------------------------------------------------------------

def _render_focus_and_mapping() -> None:
    spec = _get_spec()

    # --- Phase 1: Focus ---
    st.markdown("### Describe your learning experience")

    col1, col2 = st.columns(2)
    with col1:
        spec["target_concept"] = st.text_input(
            "What are you teaching?",
            value=spec.get("target_concept", ""),
            help="The concept students should learn. Example: 'AI Recommendation System'",
            placeholder="e.g. AI Recommendation System",
        )
    with col2:
        spec["analogy_domain"] = st.text_input(
            "What analogy are you using?",
            value=spec.get("analogy_domain", ""),
            help="The real-world analogy that represents the concept. Example: 'Bee Pollination in a Garden'",
            placeholder="e.g. Bee Pollination in a Garden",
        )

    spec["learning_goal"] = st.text_area(
        "What should students learn?",
        value=spec.get("learning_goal", ""),
        help="Describe the learning outcome in one or two sentences.",
        placeholder="e.g. Understand how recommendation systems use user profiles and feedback loops to personalize suggestions",
        height=80,
    )
    spec["task_label"] = st.text_input(
        "Task label (optional)",
        value=spec.get("task_label", ""),
        help="A short label for this activity.",
        placeholder="e.g. Task 1: Beehive Analogy",
    )

    # Phase 1 Focus fields
    st.divider()
    st.markdown("### Prerequisite Knowledge & Key Relations")
    st.caption(
        "From the FAR Guide's Focus phase: what do learners already know, "
        "and what core relational structures should the analogy preserve?"
    )

    spec["prerequisite_knowledge"] = st.text_area(
        "What do learners already know?",
        value=spec.get("prerequisite_knowledge", ""),
        help="Prior knowledge learners bring. This determines how accessible the analogy source should be.",
        placeholder="e.g. Basic understanding of how apps suggest content (e.g., YouTube recommendations)",
        height=80,
    )

    key_relations = spec.get("key_target_relations", [])
    key_relations_str = ", ".join(key_relations)
    key_relations_input = st.text_input(
        "Key target relations (comma-separated)",
        value=key_relations_str,
        help="Core causal/functional relationships in the target concept that the analogy must preserve (SMT systematicity).",
        placeholder="e.g. DRIVES(profile, candidates), FILTERS(range, items), RANKS(similarity, display)",
    )
    spec["key_target_relations"] = [r.strip() for r in key_relations_input.split(",") if r.strip()]

    # --- Phase 2: Mapping Table ---
    st.divider()
    st.markdown("### Map your concept to the analogy")

    # Domain template selector
    domain = st.selectbox(
        "Domain template",
        DOMAIN_TEMPLATE_NAMES,
        index=DOMAIN_TEMPLATE_NAMES.index(st.session_state.get("domain_template", "AI Recommendation System")),
        help="Select a pre-defined structural component set, or 'Custom' to define your own.",
        key="domain_template_select",
    )
    st.session_state["domain_template"] = domain

    is_custom = domain == "Custom"
    labels = _get_template_labels(domain)
    friendly_options = _get_template_component_options(domain)
    reverse_labels = _label_to_component(domain)

    if is_custom:
        st.caption(
            "Custom mode: type any structural component name in the Target Attribute column."
        )
    else:
        st.caption(
            "Each row connects a part of what you're teaching (Target Attribute) "
            "to something in your analogy (Source Attribute), with a description of how they relate."
        )

    import pandas as pd

    mappings = spec.get("mappings", [])
    rows = []
    for m in mappings:
        comp = m.get("structural_component", "user")
        if is_custom:
            target_display = comp
        else:
            target_display = labels.get(comp, comp)
        rows.append({
            "Target Attribute": target_display,
            "Source Attribute": m.get("analogy_name", ""),
            "Relationship": m.get("analogy_description", ""),
            "Mapping Type": m.get("mapping_type", "relation"),
        })

    if not rows:
        default_target = "" if is_custom else (friendly_options[0] if friendly_options else "")
        rows = [{"Target Attribute": default_target, "Source Attribute": "", "Relationship": "", "Mapping Type": "relation"}]

    df = pd.DataFrame(rows)

    if is_custom:
        column_config = {
            "Target Attribute": st.column_config.TextColumn(
                "Target Attribute",
                required=True,
                width="medium",
                help="The structural component name (free text in Custom mode).",
            ),
            "Source Attribute": st.column_config.TextColumn(
                "Source Attribute",
                required=True,
                width="medium",
                help="The analogy element (e.g. 'Bee', 'Flower', 'Beehive')",
            ),
            "Relationship": st.column_config.TextColumn(
                "Relationship",
                width="large",
                help="How does the source represent the target? What's the connection?",
            ),
            "Mapping Type": st.column_config.SelectboxColumn(
                "Mapping Type",
                options=MAPPING_TYPE_OPTIONS,
                width="small",
                help="Object=entity, Attribute=property, Relation=causal/functional, Higher-order=relation between relations",
            ),
        }
    else:
        column_config = {
            "Target Attribute": st.column_config.SelectboxColumn(
                "Target Attribute",
                options=friendly_options,
                required=True,
                width="medium",
                help="What part of the concept does this represent?",
            ),
            "Source Attribute": st.column_config.TextColumn(
                "Source Attribute",
                required=True,
                width="medium",
                help="The analogy element (e.g. 'Bee', 'Flower', 'Beehive')",
            ),
            "Relationship": st.column_config.TextColumn(
                "Relationship",
                width="large",
                help="How does the source represent the target? What's the connection?",
            ),
            "Mapping Type": st.column_config.SelectboxColumn(
                "Mapping Type",
                options=MAPPING_TYPE_OPTIONS,
                width="small",
                help="Object=entity, Attribute=property, Relation=causal/functional, Higher-order=relation between relations",
            ),
        }

    edited_df = st.data_editor(
        df,
        column_config=column_config,
        num_rows="dynamic",
        width="stretch",
        key="mapping_editor",
    )

    # Sync edited data back to spec, preserving extra fields from existing mappings
    new_mappings = []
    for i, row in edited_df.iterrows():
        target_label = row.get("Target Attribute", "")
        if is_custom:
            comp_value = target_label
        else:
            comp_value = reverse_labels.get(target_label, target_label)

        # Preserve existing mapping data (positions, interactions, etc.)
        original = mappings[i] if i < len(mappings) else {}
        m = dict(original)  # shallow copy to preserve all fields
        m["structural_component"] = comp_value
        m["analogy_name"] = row.get("Source Attribute", "")
        m["analogy_description"] = row.get("Relationship", "")
        m["mapping_type"] = row.get("Mapping Type", "relation")

        # Ensure defaults for fields the simplified view doesn't show
        m.setdefault("asset_strategy", "primitive")
        m.setdefault("position", [0, 0, 0])
        m.setdefault("scale", [1, 1, 1])
        m.setdefault("mapping_confidence", "strong")

        new_mappings.append(m)

    spec["mappings"] = new_mappings

    # --- Show interactions (read-only summary) if they exist from LLM suggestions ---
    mappings_with_interactions = [
        (i, m) for i, m in enumerate(new_mappings) if m.get("interaction")
    ]
    if mappings_with_interactions:
        st.divider()
        st.markdown("### Interactions (from AI suggestions)")
        st.caption("These were generated by the AI assistant. Edit them in the Generate & Preview tab or in Advanced Settings.")
        for i, m in mappings_with_interactions:
            ix = m["interaction"]
            name = m.get("analogy_name", "") or f"Mapping {i + 1}"
            normalized_ix = _normalize_interaction(ix, name)
            if not normalized_ix:
                st.info(f"**{name}**: Interaction details are incomplete. Edit in Advanced Settings.")
                continue
            st.info(
                f"**{name}**: {_format_interaction_summary(normalized_ix, name)}"
            )

    # --- Advanced / Variants controls ---
    st.divider()
    with st.expander("Advanced / Variants", expanded=False):
        st.markdown("### Lesson Structure Lock")
        st.caption(
            "Optional: lock the lesson structure so future generations can vary look-and-feel "
            "without changing instructional meaning."
        )

        essence = spec.get("essence")
        essence_hash = spec.get("essence_hash")
        frozen = isinstance(essence, dict) and bool(essence_hash)

        if st.button("Lock Lesson Structure", width="stretch"):
            ok, message = _freeze_essence()
            if ok:
                st.success(message)
            else:
                st.error(message)
            st.rerun()

        if frozen:
            st.success(f"Lesson structure is locked ({str(essence_hash)[:10]}...)")
            phase_ids = essence.get("phase_ids", []) if isinstance(essence, dict) else []
            role_ids = essence.get("mapping_role_ids", []) if isinstance(essence, dict) else []
            criteria = essence.get("success_criteria", []) if isinstance(essence, dict) else []
            chain_ids = essence.get("causal_chain_ids", []) if isinstance(essence, dict) else []
            managers = essence.get("required_managers", []) if isinstance(essence, dict) else []

            st.markdown("**Lock Checklist**")
            st.caption(f"- Roles: {', '.join(role_ids) if role_ids else '(none)'}")
            st.caption(f"- Phase flow: {' -> '.join(phase_ids) if phase_ids else '(none)'}")
            st.caption(f"- Success criteria: {len(criteria)} item(s)")
            st.caption(f"- Causal loop signals: {', '.join(chain_ids) if chain_ids else '(none)'}")
            st.caption(f"- Required managers: {', '.join(managers) if managers else 'GameManager'}")
        else:
            st.info("Lesson structure is not locked yet.")

        st.markdown("### Visual Style (can vary)")
        st.caption("These settings control style variation only.")
        surface = spec.setdefault("surface", _default_surface())
        c1, c2 = st.columns(2)
        with c1:
            mood = str(surface.get("style_mood", "natural"))
            surface["style_mood"] = st.selectbox(
                "Style mood",
                SURFACE_STYLE_MOODS,
                index=SURFACE_STYLE_MOODS.index(mood) if mood in SURFACE_STYLE_MOODS else 0,
            )
        with c2:
            level = str(surface.get("variation_level", "medium"))
            surface["variation_level"] = st.selectbox(
                "Variation level",
                SURFACE_VARIATION_LEVELS,
                index=SURFACE_VARIATION_LEVELS.index(level) if level in SURFACE_VARIATION_LEVELS else 1,
            )
        st.checkbox("Keep character present", value=True, disabled=True)
        st.checkbox("Keep UI/HUD present", value=True, disabled=True)
        st.checkbox("Keep manager architecture present", value=True, disabled=True)


# ---------------------------------------------------------------------------
# Tab 2: Generate & Preview
# ---------------------------------------------------------------------------

def _render_scene_generation_prompt_section(generation_mode: Literal["execute_first", "prompt_export"]) -> None:
    """Render the prompt-generation workflow separately from suggestion authoring."""
    st.markdown("### Step 2: Scene Generation Prompt")
    if generation_mode == "execute_first":
        st.caption(
            "Default path: generate the plan and execute in Unity now. "
            "A prompt export is also produced for traceability and fallback."
        )
    else:
        st.caption(
            "Fallback path: backend execution is unavailable, so only a prompt export "
            "is generated for Claude Code."
        )

    allow_trellis = bool(st.session_state.get("allow_trellis_generation", DEFAULT_ALLOW_TRELLIS))
    if not allow_trellis:
        _apply_asset_policy_to_spec(_get_spec(), allow_trellis=False)

    spec_obj = _try_validate()
    if spec_obj is None:
        errors = st.session_state.get("validation_errors", [])
        if errors:
            st.error("Your spec has validation errors. Fix them before generating.")
            for err in errors:
                st.caption(f"- {err}")
        else:
            st.info("Fill in your concept mapping and get AI suggestions first.")
        return

    prompt_mode_label = st.selectbox(
        "Prompt format",
        ["Compact (Recommended)", "Full (Verbose)"],
        index=0,
        help="Compact keeps prompts shorter for models with smaller context windows.",
        key="generation_prompt_mode",
    )

    primary_label = "Generate Prompts"
    if st.button(primary_label, type="primary", width="stretch"):
        spec_json = json.dumps(_get_spec(), indent=2)
        prompt_mode = "compact" if prompt_mode_label.startswith("Compact") else "full"
        st.session_state["execution_report"] = None

        if generation_mode == "execute_first":
            with st.spinner("Planning and executing scene in Unity..."):
                batch_plan, report, used_fallback = _execute_first_with_fallback(spec_obj)
            st.session_state["execution_report"] = report
            if used_fallback:
                st.warning(
                    "Planner-executor path was unavailable before execution. "
                    "Used local planner + legacy executor fallback."
                )
            if not report.get("success"):
                st.warning(
                    "Execution failed or backend tools were unavailable. "
                    "Prompt export remains available as fallback."
                )
            else:
                st.success("Scene execution completed successfully.")
        else:
            plan = MCPCallPlan()
            validator = PlanValidator(spec_obj)
            plan = validator.validate_and_repair(plan)
            batch_plan = validator.to_batch_plan(plan)

        prompt = _build_generation_prompt(spec_json, batch_plan, mode=prompt_mode)
        st.session_state["generated_prompt"] = prompt
        st.session_state["batch_plan"] = batch_plan

    if "generated_prompt" in st.session_state:
        batch_plan = st.session_state.get("batch_plan")
        execution_report = st.session_state.get("execution_report")
        prompt_text = str(st.session_state.get("generated_prompt", ""))

        if isinstance(execution_report, dict):
            with st.expander("Execution Report", expanded=bool(execution_report.get("success"))):
                st.code(json.dumps(execution_report, indent=2), language="json")

        # Keep prompt export in a centered, fixed-width column with a scrollable preview.
        _, prompt_col, _ = st.columns([1, 6, 1])
        with prompt_col:
            st.markdown("**Copy this prompt into Claude Code**")
            st.caption("Scrollable prompt preview.")
            _render_copy_button(
                prompt_text,
                "Copy Prompt",
                key=f"generated_prompt_copy_{hashlib.sha1(prompt_text.encode('utf-8')).hexdigest()[:8]}",
            )
            preview_key = f"generated_prompt_preview_{hashlib.sha1(prompt_text.encode('utf-8')).hexdigest()[:8]}"
            st.text_area(
                "Generated Prompt",
                value=prompt_text,
                height=460,
                key=preview_key,
                label_visibility="collapsed",
            )
            st.caption(f"Prompt size: {len(prompt_text):,} characters")
            st.download_button(
                "Download Prompt",
                data=prompt_text,
                file_name="scene_prompt.txt",
                mime="text/plain",
                width="stretch",
            )
        with st.expander("Copyable Sections", expanded=False):
            st.caption("Each block has a copy icon in the top-right corner.")
            st.markdown("**Full Prompt**")
            st.code(prompt_text, language="markdown")

            st.markdown("**SceneSpec JSON**")
            st.code(json.dumps(_get_spec(), indent=2), language="json")

            if batch_plan:
                st.markdown("**Execution Plan by Phase**")
                for phase in batch_plan.phases:
                    parallel_str = "parallel" if phase.parallel else "sequential"
                    batch_limit = phase.batch_size_limit or 40
                    fail_fast = True if phase.fail_fast is None else phase.fail_fast
                    st.markdown(
                        f"Phase {phase.phase_number}: `{phase.phase_name}` "
                        f"({len(phase.commands)} commands, {parallel_str}, "
                        f"batch_limit={batch_limit}, fail_fast={str(fail_fast).lower()})"
                    )
                    st.code(json.dumps(phase.commands, indent=2), language="json")

                if batch_plan.manager_tasks:
                    st.markdown("**Manager Tasks JSON**")
                    st.code(
                        json.dumps(
                            [task.model_dump(mode="json") for task in batch_plan.manager_tasks],
                            indent=2,
                        ),
                        language="json",
                    )

                if batch_plan.script_tasks:
                    st.markdown("**Script Tasks JSON**")
                    st.code(
                        json.dumps(
                            [task.model_dump(mode="json") for task in batch_plan.script_tasks],
                            indent=2,
                        ),
                        language="json",
                    )

                st.markdown("**Experience Plan JSON**")
                st.code(
                    json.dumps(batch_plan.experience_plan.model_dump(mode="json"), indent=2),
                    language="json",
                )

        # Batch plan preview
        if batch_plan:
            with st.expander("Execution plan details"):
                phase_rows = []
                for phase in batch_plan.phases:
                    phase_rows.append({
                        "Phase": phase.phase_name,
                        "#": phase.phase_number,
                        "Commands": len(phase.commands),
                        "Parallel": phase.parallel,
                        "Batch Limit": phase.batch_size_limit or 40,
                        "Fail Fast": True if phase.fail_fast is None else phase.fail_fast,
                        "Note": phase.note,
                    })
                if phase_rows:
                    st.table(phase_rows)

                c1, c2, c3 = st.columns(3)
                c1.metric("Total Commands", batch_plan.total_commands)
                c2.metric("Estimated Batches", batch_plan.estimated_batches)
                c3.metric("Trellis Generations", batch_plan.trellis_count)

                if batch_plan.manager_tasks:
                    st.subheader("Manager Tasks")
                    for manager in batch_plan.manager_tasks:
                        with st.expander(f"{manager.manager_name} ({manager.orchestration_scope})", expanded=False):
                            st.markdown(f"**Script:** `{manager.script_name}`")
                            st.markdown(f"**Attach To:** `{manager.attach_to}`")
                            st.caption(manager.required_reason)
                            if manager.responsibilities:
                                st.markdown("**Responsibilities:**")
                                for item in manager.responsibilities:
                                    st.caption(f"- {item}")
                            if manager.creates_or_updates:
                                st.markdown("**Creates / Updates:**")
                                for item in manager.creates_or_updates:
                                    st.caption(f"- {item}")
                            if manager.managed_mappings:
                                st.markdown(
                                    f"**Managed Mappings:** {', '.join(manager.managed_mappings)}"
                                )

                if batch_plan.script_tasks:
                    st.subheader("Script Tasks")
                    for task in batch_plan.script_tasks:
                        with st.expander(f"{task.mapping_name} ({task.task_kind})", expanded=False):
                            st.markdown(f"**Script:** `{task.script_name}`")
                            st.markdown(f"**Attach To:** `{task.attach_to}`")
                            st.markdown(f"**Trigger:** `{task.trigger}` from `{task.trigger_source}`")
                            st.markdown(f"**Targets:** {', '.join(task.target_objects) if task.target_objects else '(none)'}")
                            if task.effect_description:
                                st.caption(task.effect_description)
                            if task.preconditions:
                                st.markdown("**Preconditions:**")
                                for precondition in task.preconditions:
                                    st.caption(f"- {precondition}")
                            if task.notes:
                                st.markdown("**Notes:**")
                                for note in task.notes:
                                    st.caption(f"- {note}")
                if batch_plan.experience_plan:
                    _render_experience_preview(
                        batch_plan.experience_plan.model_dump(mode="json"),
                        section_title="Validated Experience Plan",
                    )
                warnings = batch_plan.warnings
                if warnings:
                    st.subheader("Warnings")
                    for w in warnings:
                        st.warning(w)


def _render_generate_preview() -> None:
    spec = _get_spec()
    allow_trellis = bool(st.session_state.get("allow_trellis_generation", DEFAULT_ALLOW_TRELLIS))
    if not allow_trellis:
        _apply_asset_policy_to_spec(spec, allow_trellis=False)

    mappings = spec.get("mappings", [])
    frozen_essence = bool(spec.get("essence_hash")) and isinstance(spec.get("essence"), dict)
    domain = st.session_state.get("domain_template", "Custom")
    labels = _get_template_labels(domain)
    experience_payload = _normalize_experience_payload(spec.get("experience", {}))
    spec["experience"] = experience_payload

    # --- Intent Wizard ---
    st.markdown("### Intent Wizard")
    st.caption(
        "Capture learner intent explicitly so generated scenes preserve trigger, feedback, "
        "delayed update, success evidence, and HUD readability."
    )
    iw1, iw2 = st.columns(2)
    with iw1:
        primary_action = st.text_input(
            "Primary learner action",
            value="Trigger the core interaction once and observe the system response.",
            key="intent_primary_action",
        )
        immediate_feedback = st.text_input(
            "Immediate feedback",
            value="A visible local response confirms the trigger fired.",
            key="intent_immediate_feedback",
        )
    with iw2:
        delayed_update = st.text_input(
            "Delayed system update",
            value="Manager state updates propagate to candidates/ranking after a short delay.",
            key="intent_delayed_update",
        )
        success_evidence = st.text_input(
            "Success evidence",
            value="Learner can explain what changed and why after one full loop.",
            key="intent_success_evidence",
        )
    hud_csv = st.text_input(
        "HUD sections (comma separated)",
        value=", ".join(experience_payload.get("feedback_hud_sections", ExperienceSpec().feedback_hud_sections)),
        key="intent_hud_sections",
    )
    hud_sections = [item.strip() for item in hud_csv.split(",") if item.strip()]
    spec["experience"] = _apply_intent_wizard(
        experience_payload=spec.get("experience", {}),
        primary_action=primary_action,
        immediate_feedback=immediate_feedback,
        delayed_update=delayed_update,
        success_evidence=success_evidence,
        hud_sections=hud_sections,
    )

    backend_url = _scene_backend_url()
    backend_healthy, _backend_status = _check_backend_health(backend_url)
    generation_mode = _select_generation_mode(backend_healthy)
    if backend_healthy:
        st.success(f"Execute-first mode enabled (backend healthy at `{backend_url}`).")

    st.markdown("### Workflow")
    st.caption(
        "Follow the flow: 1) review and refine the Proposed Scene, then 2) generate the Scene Generation Prompt."
    )
    workflow_view = st.radio(
        "View",
        ["Proposed Scene", "Scene Generation Prompt"],
        horizontal=True,
        key="generate_preview_workflow_view",
    )
    if workflow_view == "Scene Generation Prompt":
        _render_scene_generation_prompt_section(generation_mode)
        return

    # --- Step 1: Get LLM Suggestions ---
    st.markdown("### Step 1: Get AI suggestions")
    st.caption(
        "The AI will read your concept mapping and suggest how to build "
        "the 3D scene - what objects look like, how they interact, and the environment."
    )

    has_content = bool(spec.get("target_concept")) and bool(mappings)
    if not has_content:
        st.warning("Fill in your concept and at least one mapping in the Focus & Mapping tab first.")

    col1, col2 = st.columns([3, 1])
    with col1:
        suggest_clicked = st.button(
            "Get Suggestions from AI",
            type="primary",
            width="stretch",
            disabled=not has_content or not _get_api_key(),
            help="Sends your mapping table to the AI to get scene suggestions.",
        )
    with col2:
        if not _get_api_key():
            st.caption("Set API key in sidebar")

    if suggest_clicked:
        with st.spinner("Asking AI for suggestions..."):
            prompt = _build_llm_prompt(spec)
            response_text = _call_llm(prompt)
            if response_text:
                suggestions = _parse_llm_response(response_text)
                if suggestions:
                    suggestions = _apply_asset_policy_to_suggestions(suggestions, allow_trellis=allow_trellis)
                    clarification_questions = _generate_clarification_questions(spec, suggestions)
                    _reset_refinement_feedback()
                    st.session_state["llm_suggestions"] = suggestions
                    st.session_state["clarification_questions"] = clarification_questions
                    st.session_state["suggestions_accepted"] = False
                    st.rerun()

    # Display suggestions if we have them
    suggestions = st.session_state.get("llm_suggestions")
    if suggestions:
        suggestions = _apply_asset_policy_to_suggestions(suggestions, allow_trellis=allow_trellis)
        st.session_state["llm_suggestions"] = suggestions
        st.divider()
        st.markdown("#### AI Suggestions")

        if frozen_essence:
            left, right = st.columns(2)
            with left:
                st.markdown("**Lesson structure unchanged**")
                st.caption(f"Hash: {spec.get('essence_hash', '')}")
                essence = spec.get("essence", {})
                if isinstance(essence, dict):
                    st.caption(f"Roles: {len(essence.get('mapping_role_ids', []))}")
                    st.caption(f"Phases: {' -> '.join(essence.get('phase_ids', []))}")
                    st.caption(f"Criteria: {len(essence.get('success_criteria', []))}")
            with right:
                st.markdown("**Visual style changed**")
                surface_sug = suggestions.get("surface_suggestions", {})
                if isinstance(surface_sug, dict) and surface_sug:
                    st.caption(f"Mood: {surface_sug.get('style_mood', '(unchanged)')}")
                    st.caption(f"Variation: {surface_sug.get('variation_level', '(unchanged)')}")
                    st.caption(f"Character: {surface_sug.get('character_style', '(unchanged)')}")
                    st.caption(f"UI: {surface_sug.get('ui_skin', '(unchanged)')}")
                else:
                    st.caption("No explicit surface block returned.")

        # Environment suggestion
        env_sug = suggestions.get("environment", {})
        if env_sug:
            setting = env_sug.get("setting", "")
            desc = env_sug.get("description", "")
            skybox = env_sug.get("skybox", "")
            st.success(
                f"**Environment: {setting.title()}** ({skybox})\n\n{desc}"
            )

        # Game loop description
        game_loop = suggestions.get("game_loop_description", "")
        if game_loop:
            st.info(f"**How it works:** {game_loop}")

        # Experience suggestion
        exp_sug = suggestions.get("experience_suggestions")
        if isinstance(exp_sug, dict):
            _render_experience_preview(exp_sug, section_title="AI Experience Suggestions")

        # Per-mapping suggestion cards
        mapping_suggestions = suggestions.get("mapping_suggestions", [])
        for i, m_sug in enumerate(mapping_suggestions):
            if i >= len(mappings):
                break
            m = mappings[i]
            name = m.get("analogy_name", f"Mapping {i + 1}")
            comp = m.get("structural_component", "")
            friendly = labels.get(comp, comp)
            strategy = m_sug.get("asset_strategy", "primitive")

            with st.expander(f"{name} ({friendly})", expanded=True):
                cols = st.columns(3)
                cols[0].markdown(f"**Strategy:** {strategy}")
                if m_sug.get("trellis_prompt"):
                    cols[1].markdown(f"**3D Model:** {m_sug['trellis_prompt']}")
                if m_sug.get("primitive_type"):
                    cols[1].markdown(f"**Shape:** {m_sug['primitive_type']}")
                if m_sug.get("instance_count") and m_sug["instance_count"] > 1:
                    cols[2].markdown(f"**Instances:** {m_sug['instance_count']}")

                ix = m_sug.get("interaction")
                if ix:
                    normalized_ix = _normalize_interaction(ix, name)
                    if not normalized_ix:
                        st.caption("Interaction details incomplete for this suggestion.")
                        continue

                    st.markdown(
                        _format_interaction_summary(normalized_ix, name)
                    )

                    if normalized_ix.get("animation_preset"):
                        st.caption(f"Animation: {normalized_ix['animation_preset']}")
                    if normalized_ix.get("vfx_type"):
                        st.caption(f"Visual effect: {normalized_ix['vfx_type']}")

        # Optional follow-up refinement
        st.divider()
        st.markdown("#### Refine with follow-up feedback")
        st.caption(
            "Answer up to 3 questions. Your feedback is appended to the current plan "
            "for a guided refinement pass instead of a full re-roll."
        )

        clarification_defaults = _normalize_clarification_questions(
            st.session_state.get("clarification_questions", DEFAULT_CLARIFICATION_QUESTIONS)
        )

        clarification_pairs: list[dict[str, str]] = []
        for i, default_question in enumerate(clarification_defaults):
            q_key = f"clarify_q_{i}"
            a_key = f"clarify_a_{i}"
            question = st.text_input(
                f"Question {i + 1}",
                value=default_question,
                key=q_key,
            )
            answer = st.text_area(
                f"Answer {i + 1} (optional)",
                value="",
                key=a_key,
                height=70,
                placeholder="Leave blank if no preference.",
            )
            clarification_pairs.append({"question": question, "answer": answer})

        extra_feedback = st.text_area(
            "Additional feedback (optional)",
            value="",
            key="clarify_extra_feedback",
            height=90,
            placeholder="Any extra constraints or corrections.",
        )

        if st.button(
            "Apply Feedback to Suggestions",
            width="stretch",
            help="Refines the current suggestions using your answers.",
            disabled=not _get_api_key(),
        ):
            with st.spinner("Refining suggestions with your feedback..."):
                refine_prompt = _build_refinement_prompt(
                    spec=spec,
                    current_suggestions=suggestions,
                    clarifications=clarification_pairs,
                    extra_feedback=extra_feedback.strip(),
                )
                response_text = _call_llm(refine_prompt)
                if response_text:
                    refined = _parse_llm_response(response_text)
                    if refined:
                        refined = _apply_asset_policy_to_suggestions(refined, allow_trellis=allow_trellis)
                        clarification_questions = _generate_clarification_questions(spec, refined)
                        _reset_refinement_feedback()
                        st.session_state["llm_suggestions"] = refined
                        st.session_state["clarification_questions"] = clarification_questions
                        st.session_state["suggestions_accepted"] = False
                        st.rerun()

        # Accept / reset buttons
        st.divider()
        col_accept, col_reset = st.columns(2)
        with col_accept:
            if st.button("Accept Suggestions", type="primary", width="stretch"):
                _merge_suggestions_into_spec(suggestions, surface_only=frozen_essence)
                if not frozen_essence:
                    ok, message = _freeze_essence()
                    if not ok:
                        st.session_state["structure_lock_warning"] = (
                            "Suggestions were applied, but lesson structure could not be locked automatically: "
                            f"{message}"
                        )
                st.session_state["suggestions_accepted"] = True
                st.session_state["generate_preview_workflow_view"] = "Scene Generation Prompt"
                st.rerun()
        with col_reset:
            if st.button("Reset Suggestions", width="stretch"):
                _reset_refinement_feedback()
                st.session_state["llm_suggestions"] = None
                st.session_state["clarification_questions"] = list(DEFAULT_CLARIFICATION_QUESTIONS)
                st.session_state["suggestions_accepted"] = False
                st.rerun()

    if st.session_state.get("suggestions_accepted"):
        st.success("Suggestions applied to your spec.")
    structure_lock_warning = st.session_state.pop("structure_lock_warning", None)
    if structure_lock_warning:
        st.warning(structure_lock_warning)

    st.divider()
    st.info(
        "After accepting suggestions, this view automatically switches to "
        "`Scene Generation Prompt` so you can generate and copy the build prompt."
    )


# ---------------------------------------------------------------------------
# Tab 3: Reflection
# ---------------------------------------------------------------------------

def _render_reflection() -> None:
    spec = _get_spec()
    mappings = spec.get("mappings", [])

    st.markdown("### Evaluate Analogy Quality")
    st.caption(
        "Phase 4 of the FAR Guide: reflect on the analogy design. "
        "The AI evaluates your spec against six criteria from analogy theory "
        "(SMT, FAR Guide, embodied cognition)."
    )

    has_content = bool(spec.get("target_concept")) and bool(mappings)
    if not has_content:
        st.warning("Fill in your concept and at least one mapping first.")

    if st.button(
        "Evaluate Analogy",
        type="primary",
        width="stretch",
        disabled=not has_content or not _get_api_key(),
        help="Sends your complete spec to the AI for evaluation against analogy quality criteria.",
    ):
        with st.spinner("Evaluating analogy quality..."):
            prompt = _build_reflection_prompt(spec)
            response_text = _call_llm(prompt)
            if response_text:
                parsed = _parse_llm_response(response_text)
                if parsed:
                    try:
                        result = ReflectionResult.model_validate(parsed)
                        st.session_state["reflection_result"] = result
                    except ValidationError as e:
                        st.error(f"Could not parse reflection result: {e}")
                    st.rerun()

    result: ReflectionResult | None = st.session_state.get("reflection_result")
    if not result:
        if not _get_api_key():
            st.info("Set your API key in the sidebar to enable evaluation.")
        return

    # --- Score cards ---
    st.divider()
    st.markdown("#### Scores")

    c1, c2, c3, c4 = st.columns(4)
    c1.metric("Structural Completeness", f"{result.structural_completeness:.0%}")
    c2.metric("Embodiment Quality", f"{result.embodiment_quality:.0%}")
    c3.metric("Cognitive Load", f"{result.cognitive_load:.0%}", help="Lower is better")
    c4.metric("Overall", f"{result.overall_score:.0%}")

    # Notes for each dimension
    if result.structural_completeness_notes:
        st.caption(f"**Structural Completeness:** {result.structural_completeness_notes}")
    if result.embodiment_quality_notes:
        st.caption(f"**Embodiment Quality:** {result.embodiment_quality_notes}")
    if result.cognitive_load_notes:
        st.caption(f"**Cognitive Load:** {result.cognitive_load_notes}")

    # --- Misconception Risks ---
    if result.misconception_risks:
        st.divider()
        st.markdown("#### Misconception Risks")
        for risk in result.misconception_risks:
            st.warning(risk)

    # --- Unlikes / Breakdowns ---
    if result.unlikes:
        st.divider()
        st.markdown("#### Unlikes / Breakdowns")
        st.caption("Where the analogy fails and how to address it (FAR Action phase).")
        unlike_rows = []
        for unlike in result.unlikes:
            unlike_rows.append({
                "Mapping": unlike.get("mapping", ""),
                "Breakdown": unlike.get("breakdown", ""),
                "Suggestion": unlike.get("suggestion", ""),
            })
        st.table(unlike_rows)

    # --- Strengths & Suggestions ---
    col_s, col_g = st.columns(2)
    with col_s:
        if result.strengths:
            st.markdown("#### Strengths")
            for s in result.strengths:
                st.markdown(f"- {s}")
    with col_g:
        if result.suggestions:
            st.markdown("#### Suggestions")
            for s in result.suggestions:
                st.markdown(f"- {s}")


# ---------------------------------------------------------------------------
# Advanced Settings (expander)
# ---------------------------------------------------------------------------

def _render_advanced_settings() -> None:
    spec = _get_spec()
    env = spec.setdefault("environment", _default_spec()["environment"])
    experience = _normalize_experience_payload(spec.get("experience", {}))
    spec["experience"] = experience

    with st.expander("Advanced Settings", expanded=False):
        st.caption("Technical environment and per-mapping overrides. Most educators can skip this section.")

        # --- Environment controls ---
        st.markdown("#### Environment")
        env["description"] = st.text_input(
            "Environment Description",
            value=env.get("description", ""),
            help="A short description of the environment for context.",
        )
        col1, col2 = st.columns(2)
        with col1:
            env["setting"] = st.text_input("Setting", value=env.get("setting", "garden"))
        with col2:
            env["skybox"] = st.selectbox(
                "Skybox", SKYBOX_PRESETS,
                index=SKYBOX_PRESETS.index(env.get("skybox", "sunny")),
            )

        # Terrain
        st.markdown("##### Terrain")
        ts = env.get("terrain_size", [30, 1, 30])
        tc1, tc2, tc3 = st.columns(3)
        ts[0] = tc1.slider("Size X", 1.0, 100.0, float(ts[0]), 1.0)
        ts[1] = tc2.slider("Size Y", 0.1, 10.0, float(ts[1]), 0.1)
        ts[2] = tc3.slider("Size Z", 1.0, 100.0, float(ts[2]), 1.0)
        env["terrain_size"] = ts

        tc = env.get("terrain_color", [0.3, 0.6, 0.2, 1.0])
        tc_hex = st.color_picker("Terrain Color", _rgba_to_hex(tc))
        tc_alpha = st.slider("Terrain Alpha", 0.0, 1.0, float(tc[3] if len(tc) > 3 else 1.0), 0.05, key="terrain_alpha")
        env["terrain_color"] = _hex_to_rgba(tc_hex, tc_alpha)

        # Lighting
        st.markdown("##### Lighting")
        light = env.setdefault("lighting", {"color": [1.0, 0.95, 0.9, 1.0], "intensity": 1.0, "rotation": [50, -30, 0]})
        light["intensity"] = st.slider("Intensity", 0.0, 2.0, float(light.get("intensity", 1.0)), 0.05)

        lr = light.get("rotation", [50, -30, 0])
        lc1, lc2, lc3 = st.columns(3)
        lr[0] = lc1.slider("Light Rot X", -180.0, 180.0, float(lr[0]), 1.0)
        lr[1] = lc2.slider("Light Rot Y", -180.0, 180.0, float(lr[1]), 1.0)
        lr[2] = lc3.slider("Light Rot Z", -180.0, 180.0, float(lr[2]), 1.0)
        light["rotation"] = lr

        lcolor = light.get("color", [1.0, 0.95, 0.9, 1.0])
        lcolor_hex = st.color_picker("Light Color", _rgba_to_hex(lcolor))
        env["lighting"]["color"] = _hex_to_rgba(lcolor_hex, lcolor[3] if len(lcolor) > 3 else 1.0)

        # Camera
        st.markdown("##### Camera")
        cam = env.setdefault("camera", {"position": [0, 1.6, -5], "rotation": [10, 0, 0], "field_of_view": 60.0, "is_vr": False})

        cp = cam.get("position", [0, 1.6, -5])
        cc1, cc2, cc3 = st.columns(3)
        cp[0] = cc1.number_input("Cam Pos X", value=float(cp[0]), step=0.5, key="cam_px")
        cp[1] = cc2.number_input("Cam Pos Y", value=float(cp[1]), step=0.5, key="cam_py")
        cp[2] = cc3.number_input("Cam Pos Z", value=float(cp[2]), step=0.5, key="cam_pz")
        cam["position"] = cp

        cr = cam.get("rotation", [10, 0, 0])
        cr1, cr2, cr3 = st.columns(3)
        cr[0] = cr1.number_input("Cam Rot X", value=float(cr[0]), step=1.0, key="cam_rx")
        cr[1] = cr2.number_input("Cam Rot Y", value=float(cr[1]), step=1.0, key="cam_ry")
        cr[2] = cr3.number_input("Cam Rot Z", value=float(cr[2]), step=1.0, key="cam_rz")
        cam["rotation"] = cr

        cam["field_of_view"] = st.slider("FOV", 20.0, 120.0, float(cam.get("field_of_view", 60.0)), 1.0)
        cam["is_vr"] = st.checkbox(
            "Use alternate immersive camera rig (optional)",
            value=cam.get("is_vr", False),
            help="Leave off for the default interactive 3D camera setup.",
        )

        # --- Experience controls ---
        st.divider()
        st.markdown("#### Experience Design")
        st.caption(
            "Define learner-facing experience flow: objective, phases, causal chain, UI guidance, "
            "feedback HUD, spatial staging, and audio timing."
        )

        experience["objective"] = st.text_area(
            "Primary Objective",
            value=experience.get("objective", ""),
            height=70,
        )

        criteria_text = "\n".join(experience.get("success_criteria", []))
        criteria_input = st.text_area(
            "Success Criteria (one per line)",
            value=criteria_text,
            height=100,
        )
        experience["success_criteria"] = [
            line.strip() for line in criteria_input.splitlines() if line.strip()
        ]

        ex_col1, ex_col2 = st.columns(2)
        with ex_col1:
            experience["progress_metric_label"] = st.text_input(
                "Progress Metric Label",
                value=experience.get("progress_metric_label", "Loop Progress"),
            )
        with ex_col2:
            experience["progress_target"] = st.number_input(
                "Progress Target",
                min_value=1,
                value=int(experience.get("progress_target", 3)),
            )

        import pandas as pd

        st.markdown("##### Phase Flow")
        phases_df = pd.DataFrame(experience.get("phases", []))
        if phases_df.empty:
            phases_df = pd.DataFrame([{
                "phase_name": name,
                "objective": "",
                "player_action": "",
                "expected_feedback": "",
                "completion_criteria": "",
            } for name in EXPERIENCE_PHASE_SEQUENCE])
        edited_phases = st.data_editor(
            phases_df,
            width="stretch",
            num_rows="dynamic",
            key="adv_experience_phases",
        )
        phase_rows: list[dict[str, Any]] = []
        for _, row in edited_phases.iterrows():
            phase_name = str(row.get("phase_name", "")).strip()
            if not phase_name:
                continue
            phase_rows.append({
                "phase_name": phase_name,
                "objective": str(row.get("objective", "")).strip(),
                "player_action": str(row.get("player_action", "")).strip(),
                "expected_feedback": str(row.get("expected_feedback", "")).strip(),
                "completion_criteria": str(row.get("completion_criteria", "")).strip(),
            })
        experience["phases"] = phase_rows

        st.markdown("##### Causal Chain")
        chain_df = pd.DataFrame(experience.get("causal_chain", []))
        if chain_df.empty:
            chain_df = pd.DataFrame([{
                "step": 1,
                "trigger_event": "",
                "immediate_feedback": "",
                "delayed_system_update": "",
                "observable_outcome": "",
            }])
        edited_chain = st.data_editor(
            chain_df,
            width="stretch",
            num_rows="dynamic",
            key="adv_experience_chain",
        )
        chain_rows: list[dict[str, Any]] = []
        for i, row in edited_chain.iterrows():
            try:
                step_val = int(row.get("step", i + 1))
            except (TypeError, ValueError):
                step_val = i + 1
            chain_rows.append({
                "step": max(1, step_val),
                "trigger_event": str(row.get("trigger_event", "")).strip(),
                "immediate_feedback": str(row.get("immediate_feedback", "")).strip(),
                "delayed_system_update": str(row.get("delayed_system_update", "")).strip(),
                "observable_outcome": str(row.get("observable_outcome", "")).strip(),
            })
        chain_rows.sort(key=lambda item: item["step"])
        experience["causal_chain"] = chain_rows

        st.markdown("##### Guided UI Prompts")
        prompts_df = pd.DataFrame(experience.get("guided_prompts", []))
        if prompts_df.empty:
            prompts_df = pd.DataFrame([{
                "phase_name": "Trigger",
                "prompt": "Activate the trigger source to start the system response.",
                "optional": True,
            }])
        edited_prompts = st.data_editor(
            prompts_df,
            width="stretch",
            num_rows="dynamic",
            key="adv_experience_prompts",
        )
        prompt_rows: list[dict[str, Any]] = []
        for _, row in edited_prompts.iterrows():
            prompt_text = str(row.get("prompt", "")).strip()
            if not prompt_text:
                continue
            prompt_rows.append({
                "phase_name": str(row.get("phase_name", "")).strip(),
                "prompt": prompt_text,
                "optional": bool(row.get("optional", True)),
            })
        experience["guided_prompts"] = prompt_rows

        st.markdown("##### Feedback HUD")
        experience["feedback_hud_enabled"] = st.checkbox(
            "Enable Feedback HUD",
            value=bool(experience.get("feedback_hud_enabled", True)),
        )
        hud_sections_str = ", ".join(experience.get("feedback_hud_sections", []))
        hud_sections_input = st.text_input(
            "HUD Sections (comma-separated)",
            value=hud_sections_str,
        )
        experience["feedback_hud_sections"] = [
            item.strip() for item in hud_sections_input.split(",") if item.strip()
        ]

        st.markdown("##### Spatial Staging")
        spatial_rows = []
        for zone in experience.get("spatial_staging", []):
            center = zone.get("suggested_center", [0.0, 0.0, 0.0])
            if not isinstance(center, list) or len(center) < 3:
                center = [0.0, 0.0, 0.0]
            spatial_rows.append({
                "zone_name": zone.get("zone_name", ""),
                "purpose": zone.get("purpose", ""),
                "anchor_object": zone.get("anchor_object", ""),
                "center_x": center[0],
                "center_y": center[1],
                "center_z": center[2],
                "suggested_radius": zone.get("suggested_radius", 4.0),
            })
        spatial_df = pd.DataFrame(spatial_rows)
        if spatial_df.empty:
            spatial_df = pd.DataFrame([{
                "zone_name": "Interaction Zone",
                "purpose": "",
                "anchor_object": "",
                "center_x": 0.0,
                "center_y": 0.0,
                "center_z": 0.0,
                "suggested_radius": 4.0,
            }])
        edited_spatial = st.data_editor(
            spatial_df,
            width="stretch",
            num_rows="dynamic",
            key="adv_experience_spatial",
        )
        spatial_clean: list[dict[str, Any]] = []
        for _, row in edited_spatial.iterrows():
            zone_name = str(row.get("zone_name", "")).strip()
            if not zone_name:
                continue
            try:
                center_x = float(row.get("center_x", 0.0))
                center_y = float(row.get("center_y", 0.0))
                center_z = float(row.get("center_z", 0.0))
            except (TypeError, ValueError):
                center_x, center_y, center_z = 0.0, 0.0, 0.0
            try:
                radius = float(row.get("suggested_radius", 4.0))
            except (TypeError, ValueError):
                radius = 4.0
            spatial_clean.append({
                "zone_name": zone_name,
                "purpose": str(row.get("purpose", "")).strip(),
                "anchor_object": str(row.get("anchor_object", "")).strip(),
                "suggested_center": [center_x, center_y, center_z],
                "suggested_radius": max(0.1, radius),
            })
        experience["spatial_staging"] = spatial_clean

        st.markdown("##### Audio & Timing")
        audio_df = pd.DataFrame(experience.get("audio_cues", []))
        if audio_df.empty:
            audio_df = pd.DataFrame([{
                "cue_name": "trigger_click",
                "trigger": "on_trigger",
                "purpose": "Confirm action",
                "delay_seconds": 0.0,
                "volume": 0.7,
            }])
        edited_audio = st.data_editor(
            audio_df,
            width="stretch",
            num_rows="dynamic",
            key="adv_experience_audio",
        )
        audio_clean: list[dict[str, Any]] = []
        for _, row in edited_audio.iterrows():
            cue_name = str(row.get("cue_name", "")).strip()
            if not cue_name:
                continue
            try:
                delay_seconds = float(row.get("delay_seconds", 0.0))
            except (TypeError, ValueError):
                delay_seconds = 0.0
            try:
                volume = float(row.get("volume", 0.6))
            except (TypeError, ValueError):
                volume = 0.6
            audio_clean.append({
                "cue_name": cue_name,
                "trigger": str(row.get("trigger", "")).strip(),
                "purpose": str(row.get("purpose", "")).strip(),
                "delay_seconds": max(0.0, delay_seconds),
                "volume": min(1.0, max(0.0, volume)),
            })
        experience["audio_cues"] = audio_clean

        timing_json = json.dumps(experience.get("timing_guidelines", {}), indent=2)
        timing_input = st.text_area(
            "Timing Guidelines (JSON)",
            value=timing_json,
            height=100,
        )
        try:
            parsed_timing = json.loads(timing_input) if timing_input.strip() else {}
            if isinstance(parsed_timing, dict):
                cleaned_timing = {}
                for key, value in parsed_timing.items():
                    k = str(key).strip()
                    if not k:
                        continue
                    try:
                        cleaned_timing[k] = float(value)
                    except (TypeError, ValueError):
                        continue
                experience["timing_guidelines"] = cleaned_timing
        except json.JSONDecodeError:
            st.warning("Invalid JSON for timing guidelines")

        spec["experience"] = _normalize_experience_payload(experience)

        # --- Per-mapping overrides ---
        st.divider()
        st.markdown("#### Per-mapping overrides")
        st.caption("Override position, scale, color, asset strategy, and interactions for individual mappings.")

        mappings = spec.get("mappings", [])
        if not mappings:
            st.info("Add mappings in the Focus & Mapping tab first.")
            return

        mapping_names = [f"{i}: {m.get('analogy_name', '?')}" for i, m in enumerate(mappings)]
        selected = st.selectbox("Select mapping", mapping_names, key="adv_mapping_select")
        if selected is None:
            return

        idx = int(selected.split(":")[0])
        mapping = mappings[idx]

        # Asset strategy
        current_strategy = mapping.get("asset_strategy", "primitive")
        strategy_idx = ASSET_STRATEGIES.index(current_strategy) if current_strategy in ASSET_STRATEGIES else 0
        mapping["asset_strategy"] = st.selectbox(
            "Asset Strategy", ASSET_STRATEGIES, index=strategy_idx, key=f"adv_strategy_{idx}",
        )

        if mapping["asset_strategy"] == "primitive":
            current_prim = mapping.get("primitive_type", "Cube")
            prim_idx = PRIMITIVE_TYPES.index(current_prim) if current_prim in PRIMITIVE_TYPES else 0
            mapping["primitive_type"] = st.selectbox(
                "Primitive Type", PRIMITIVE_TYPES, index=prim_idx, key=f"adv_prim_{idx}",
            )
        elif mapping["asset_strategy"] == "trellis":
            mapping["trellis_prompt"] = st.text_input(
                "Trellis Prompt", value=mapping.get("trellis_prompt", ""),
                key=f"adv_trellis_{idx}",
                help="Text prompt for AI 3D model generation.",
            )

        # Position
        pos = mapping.get("position", [0, 0, 0])
        pc1, pc2, pc3 = st.columns(3)
        pos[0] = pc1.number_input("Pos X", value=float(pos[0]), step=0.5, key=f"adv_px_{idx}")
        pos[1] = pc2.number_input("Pos Y", value=float(pos[1]), step=0.5, key=f"adv_py_{idx}")
        pos[2] = pc3.number_input("Pos Z", value=float(pos[2]), step=0.5, key=f"adv_pz_{idx}")
        mapping["position"] = pos

        # Scale
        scl = mapping.get("scale", [1, 1, 1])
        sc1, sc2, sc3 = st.columns(3)
        scl[0] = sc1.number_input("Scale X", value=float(scl[0]), step=0.1, key=f"adv_sx_{idx}")
        scl[1] = sc2.number_input("Scale Y", value=float(scl[1]), step=0.1, key=f"adv_sy_{idx}")
        scl[2] = sc3.number_input("Scale Z", value=float(scl[2]), step=0.1, key=f"adv_sz_{idx}")
        mapping["scale"] = scl

        # Color
        col = mapping.get("color")
        col_hex = st.color_picker("Color", _rgba_to_hex(col) if col else "#b3b3b3", key=f"adv_col_{idx}")
        col_alpha = st.slider("Alpha", 0.0, 1.0, float(col[3] if col and len(col) > 3 else 1.0), 0.05, key=f"adv_alpha_{idx}")
        if col_hex != "#b3b3b3":
            mapping["color"] = _hex_to_rgba(col_hex, col_alpha)

        # Instance count / spread
        if mapping.get("structural_component") == "content_item":
            mapping["instance_count"] = st.number_input(
                "Instance Count", min_value=1, value=int(mapping.get("instance_count", 1)),
                key=f"adv_count_{idx}",
            )
            mapping["instance_spread"] = st.number_input(
                "Instance Spread", min_value=0.0, value=float(mapping.get("instance_spread", 3.0)),
                step=0.5, key=f"adv_spread_{idx}",
            )

        # Mapping confidence
        confidence_options = ["strong", "moderate", "weak"]
        current_confidence = mapping.get("mapping_confidence", "strong")
        conf_idx = confidence_options.index(current_confidence) if current_confidence in confidence_options else 0
        mapping["mapping_confidence"] = st.selectbox(
            "Mapping Confidence", confidence_options, index=conf_idx, key=f"adv_conf_{idx}",
            help="How strong is the structural parallel? (From multi-constraint theory)",
        )

        # --- Interaction Editor ---
        st.markdown("##### Interaction")
        ix = mapping.get("interaction") or {}

        add_ix = st.checkbox("Has interaction", value=bool(ix), key=f"adv_has_ix_{idx}")
        if not add_ix:
            mapping.pop("interaction", None)
        else:
            if not ix:
                ix = {}
                mapping["interaction"] = ix

            current_trigger = ix.get("trigger", "")
            trigger_idx = TRIGGER_OPTIONS.index(current_trigger) if current_trigger in TRIGGER_OPTIONS else 0
            ix["trigger"] = st.selectbox("Trigger", TRIGGER_OPTIONS, index=trigger_idx, key=f"adv_trigger_{idx}")

            c1, c2 = st.columns(2)
            with c1:
                ix["trigger_source"] = st.text_input(
                    "Trigger Source", value=ix.get("trigger_source", ""), key=f"adv_src_{idx}",
                )
            with c2:
                targets_str = ", ".join(ix.get("target_objects", []))
                targets_input = st.text_input(
                    "Target Objects (comma-sep)", value=targets_str, key=f"adv_targets_{idx}",
                )
                ix["target_objects"] = [t.strip() for t in targets_input.split(",") if t.strip()]

            ix["effect"] = st.text_input("Effect", value=ix.get("effect", ""), key=f"adv_effect_{idx}")
            ix["effect_description"] = st.text_area(
                "Effect Description", value=ix.get("effect_description", ""), key=f"adv_effdesc_{idx}",
            )

            c3, c4 = st.columns(2)
            with c3:
                current_anim = ix.get("animation_preset", "")
                anim_idx = ANIMATION_PRESETS.index(current_anim) if current_anim in ANIMATION_PRESETS else 0
                ix["animation_preset"] = st.selectbox(
                    "Animation Preset", ANIMATION_PRESETS, index=anim_idx, key=f"adv_anim_{idx}",
                )
            with c4:
                current_vfx = ix.get("vfx_type", "")
                vfx_idx = VFX_TYPES.index(current_vfx) if current_vfx in VFX_TYPES else 0
                ix["vfx_type"] = st.selectbox(
                    "VFX Type", VFX_TYPES, index=vfx_idx, key=f"adv_vfx_{idx}",
                )

            params_str = json.dumps(ix.get("parameters", {}), indent=2)
            params_input = st.text_area(
                "Parameters (JSON)", value=params_str, height=120, key=f"adv_params_{idx}",
            )
            try:
                ix["parameters"] = json.loads(params_input) if params_input.strip() else {}
            except json.JSONDecodeError:
                st.warning("Invalid JSON in parameters field")

            # Clean empty string fields
            for key in ["animation_preset", "vfx_type", "trigger_source", "effect"]:
                if not ix.get(key):
                    ix.pop(key, None)
            if not ix.get("target_objects"):
                ix.pop("target_objects", None)
            if not ix.get("parameters"):
                ix.pop("parameters", None)

            mapping["interaction"] = ix


# ---------------------------------------------------------------------------
# Prompt builder
# ---------------------------------------------------------------------------

def _sanitize_prompt_command(command: dict[str, Any]) -> dict[str, Any]:
    """Remove heavy inline code bodies from prompt-export commands."""
    sanitized = copy.deepcopy(command)
    tool = str(sanitized.get("tool", "")).strip().lower()
    params = sanitized.get("params")
    if tool == "create_script" and isinstance(params, dict) and "contents" in params:
        params.pop("contents", None)
        params["contents_omitted"] = True
    return sanitized


def _sanitize_prompt_commands(commands: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Return prompt-safe command payloads without detailed script bodies."""
    sanitized: list[dict[str, Any]] = []
    for command in commands:
        if isinstance(command, dict):
            sanitized.append(_sanitize_prompt_command(command))
    return sanitized


def _build_generation_prompt_full(spec_json: str, batch_plan: BatchExecutionPlan) -> str:
    """Build a verbose ready-to-paste prompt for Claude Code."""
    manager_tasks = [task.model_dump(mode="json") for task in batch_plan.manager_tasks]
    script_tasks = [task.model_dump(mode="json") for task in batch_plan.script_tasks]
    experience_plan = batch_plan.experience_plan.model_dump(mode="json")
    warnings = batch_plan.warnings
    audit_rules = batch_plan.audit_rules or {}
    smoke_test_plan = batch_plan.smoke_test_plan or {}
    try:
        spec_obj = json.loads(spec_json)
    except json.JSONDecodeError:
        spec_obj = {}
    essence_hash = spec_obj.get("essence_hash")
    surface_obj = spec_obj.get("surface")
    allow_trellis = bool(st.session_state.get("allow_trellis_generation", DEFAULT_ALLOW_TRELLIS))

    def _chunk_commands(commands: list[dict[str, Any]], chunk_size: int) -> list[list[dict[str, Any]]]:
        safe_size = max(1, int(chunk_size or 1))
        return [commands[i:i + safe_size] for i in range(0, len(commands), safe_size)]

    lines = [
        "# Scene Generation Request",
        "",
        "Execute the scene generation pipeline using the SceneSpec below.",
        "The validator has already computed the batch execution plan.",
        "Use Unity-MCP tools only for all operations in this request.",
        "Execute each phase sequentially, honoring per-phase batch limits and fail-fast rules.",
        "",
        "## SceneSpec JSON",
        "",
        "```json",
        spec_json,
        "```",
        "",
        f"## Execution Plan ({batch_plan.total_commands} commands, {batch_plan.estimated_batches} batches)",
        "",
    ]

    if essence_hash:
        lines.extend([
            "## Essence Guard",
            "",
            f"- Frozen essence hash: `{essence_hash}`",
            "- Keep semantic mappings and phase semantics unchanged; apply presentation variance only.",
            "",
        ])
    if isinstance(surface_obj, dict):
        lines.extend([
            "## Surface Profile",
            "",
            "```json",
            json.dumps(surface_obj, indent=2),
            "```",
            "",
        ])

    for phase in batch_plan.phases:
        phase_commands = _sanitize_prompt_commands(phase.commands)
        parallel_str = "parallel" if phase.parallel else "sequential"
        batch_limit = int(phase.batch_size_limit or 40)
        fail_fast = True if phase.fail_fast is None else bool(phase.fail_fast)
        lines.append(
            f"### Phase {phase.phase_number}: {phase.phase_name} "
            f"({len(phase_commands)} commands, {parallel_str}, batch_limit={batch_limit}, fail_fast={str(fail_fast).lower()})"
        )
        lines.append(f"{phase.note}")
        lines.append("")

        if phase.phase_name == "smoke_test":
            lines.append("Run this phase directly using `scene_generator` (do not wrap in `batch_execute`):")
            lines.append("```json")
            smoke_command = phase_commands[0] if phase_commands else {
                "tool": "scene_generator",
                "params": {"action": "smoke_test_scene"},
            }
            lines.append(json.dumps(smoke_command, indent=2))
            lines.append("```")
            lines.append("")
            continue

        chunks = _chunk_commands(phase_commands, batch_limit)
        for idx, chunk in enumerate(chunks, start=1):
            lines.append(f"Batch {idx}/{len(chunks)} for phase `{phase.phase_name}`:")
            lines.append("```json")
            lines.append(
                json.dumps(
                    {
                        "commands": chunk,
                        "parallel": phase.parallel,
                        "failFast": fail_fast,
                    },
                    indent=2,
                )
            )
            lines.append("```")
            lines.append("")
            lines.append("Audit this batch result before continuing:")
            lines.append("```json")
            lines.append(
                json.dumps(
                    {
                        "tool": "scene_generator",
                        "params": {
                            "action": "audit_batch_result",
                            "phase_name": phase.phase_name,
                            "phase_number": phase.phase_number,
                            "batch_result_json": "<paste batch_execute result JSON>",
                            "phase_context_json": json.dumps(
                                {
                                    "phase_name": phase.phase_name,
                                    "phase_number": phase.phase_number,
                                    "commands": chunk,
                                }
                            ),
                        },
                    },
                    indent=2,
                )
            )
            lines.append("```")
            lines.append("")

    if manager_tasks:
        lines.append("## Manager Tasks")
        lines.append("")
        lines.append("```json")
        lines.append(json.dumps(manager_tasks, indent=2))
        lines.append("```")
        lines.append("")

    if script_tasks:
        lines.append("## Script Tasks")
        lines.append("")
        lines.append("```json")
        lines.append(json.dumps(script_tasks, indent=2))
        lines.append("```")
        lines.append("")

    lines.append("## Experience Plan")
    lines.append("")
    lines.append("```json")
    lines.append(json.dumps(experience_plan, indent=2))
    lines.append("```")
    lines.append("")

    if audit_rules:
        lines.append("## Audit Rules")
        lines.append("")
        lines.append("```json")
        lines.append(json.dumps(audit_rules, indent=2))
        lines.append("```")
        lines.append("")

    if smoke_test_plan:
        lines.append("## Smoke Test Plan")
        lines.append("")
        lines.append("```json")
        lines.append(json.dumps(smoke_test_plan, indent=2))
        lines.append("```")
        lines.append("")

    if warnings:
        lines.append("## Warnings")
        lines.append("")
        for w in warnings:
            lines.append(f"- {w}")
        lines.append("")

    if batch_plan.trellis_count > 0:
        lines.append(f"**Note:** This scene includes {batch_plan.trellis_count} Trellis 3D generation(s). ")
        lines.append("These are async - poll `manage_3d_gen` action=`status` after submitting.")
        lines.append("For detailed Trellis import diagnostics, inspect `data.trellisImport.importLogs` in each status response.")
        lines.append("")

    lines.append("## Instructions")
    lines.append("")
    lines.append("1. See the `unity-mcp-orchestrator` skill first and follow its best-practice sequencing and safeguards with Unity-MCP.")
    lines.append("2. Use Unity-MCP tools only. For mutating phases, execute command chunks via `batch_execute` exactly as listed.")
    lines.append("3. Respect each phase's `batch_limit` and `fail_fast` settings; do not merge chunks across phases.")
    lines.append("4. After each `batch_execute` call, run `scene_generator(action='audit_batch_result', ...)` and obey decision: pass -> continue, retry -> bounded retry, fail -> stop.")
    lines.append("5. For script phases, keep `parallel=false`, wait for compilation completion before proceeding, then continue.")
    lines.append("6. Create `GameManager` first and implement manager scripts exactly as specified in `Manager Tasks`.")
    lines.append("7. Keep feedback-loop orchestration in `GameManager`; focused managers should remain narrow.")
    lines.append("8. `create_script` command bodies are intentionally omitted in this prompt export. Generate script code from `Manager Tasks`, `Script Tasks`, and `Experience Plan` before execution.")
    lines.append("9. Implement script tasks exactly as specified in the `Script Tasks` JSON section.")
    lines.append("10. Do not use tag-based lookups in scripts (`CompareTag`, `FindGameObjectsWithTag`). Use explicit references or explicit object lists.")
    lines.append("11. Run `scene_generator(action='smoke_test_scene', ...)` as a required gate. If it fails, do not run scene save.")
    lines.append("12. Save the scene only after smoke test passes.")
    lines.append("13. Keep experience phases in order: Intro -> Explore -> Trigger -> Observe Feedback Loop -> Summary.")
    lines.append("14. Preserve Essence semantics when essence_hash is present; only vary Surface fields.")
    if not allow_trellis:
        lines.append("15. Primitive-first policy is active: do not create Trellis assets or `manage_3d_gen` calls.")
    else:
        lines.append("15. Trellis is optional: keep primitive-first unless a Trellis asset is clearly necessary.")

    return "\n".join(lines)


def _compact_spec_for_prompt(spec_obj: dict[str, Any]) -> dict[str, Any]:
    """Return only prompt-critical spec fields to reduce token usage."""
    mappings = []
    for row in spec_obj.get("mappings", []):
        if not isinstance(row, dict):
            continue
        mappings.append({
            "structural_component": row.get("structural_component"),
            "analogy_name": row.get("analogy_name"),
            "mapping_type": row.get("mapping_type"),
            "asset_strategy": row.get("asset_strategy"),
            "instance_count": row.get("instance_count"),
            "instance_spread": row.get("instance_spread"),
        })

    compact = {
        "target_concept": spec_obj.get("target_concept"),
        "analogy_domain": spec_obj.get("analogy_domain"),
        "learning_goal": spec_obj.get("learning_goal"),
        "task_label": spec_obj.get("task_label"),
        "essence_hash": spec_obj.get("essence_hash"),
        "surface": spec_obj.get("surface"),
        "mappings": mappings,
    }
    return {k: v for k, v in compact.items() if v not in (None, "", [], {})}


def _build_generation_prompt_compact(spec_json: str, batch_plan: BatchExecutionPlan) -> str:
    """Build a compact prompt that minimizes tokens while preserving executable detail."""
    try:
        spec_obj = json.loads(spec_json)
    except json.JSONDecodeError:
        spec_obj = {}
    allow_trellis = bool(st.session_state.get("allow_trellis_generation", DEFAULT_ALLOW_TRELLIS))

    sanitized_phases = []
    for phase in batch_plan.phases:
        phase_payload = phase.model_dump(mode="json")
        phase_payload["commands"] = _sanitize_prompt_commands(phase.commands)
        sanitized_phases.append(phase_payload)

    spec_min = _compact_spec_for_prompt(spec_obj)
    execution_payload = {
        "summary": {
            "total_commands": batch_plan.total_commands,
            "estimated_batches": batch_plan.estimated_batches,
            "trellis_count": batch_plan.trellis_count,
        },
        "phases": sanitized_phases,
        "manager_tasks": [task.model_dump(mode="json") for task in batch_plan.manager_tasks],
        "script_tasks": [task.model_dump(mode="json") for task in batch_plan.script_tasks],
        "experience_plan": batch_plan.experience_plan.model_dump(mode="json"),
        "audit_rules": batch_plan.audit_rules or {},
        "smoke_test_plan": batch_plan.smoke_test_plan or {},
        "warnings": batch_plan.warnings,
    }

    spec_min_json = json.dumps(spec_min, separators=(",", ":"), ensure_ascii=True)
    execution_json = json.dumps(execution_payload, separators=(",", ":"), ensure_ascii=True)

    lines = [
        "# Scene Build Request (Compact)",
        "Use Unity-MCP tools only.",
        "",
        "Rules:",
        "R1 Use the `unity-mcp-orchestrator` skill first and follow its best-practice workflow.",
        "R2 Execute phases in order; obey each phase batch_size_limit and fail_fast.",
        "R3 For mutating phases, use batch_execute with each phase's commands.",
        "R4 After each batch_execute, run scene_generator(action='audit_batch_result').",
        "R5 If audit decision=retry, bounded retry. If fail, stop.",
        "R6 Smoke test is mandatory before scene save.",
        "R7 If essence_hash exists, preserve semantics and phase meaning (surface-only variation).",
        "R8 Avoid tag lookups in scripts (CompareTag / FindGameObjectsWithTag).",
        "R9 create_script code contents are omitted in this export; generate code from manager/script tasks before execution.",
        "R10 Keep phase order: Intro -> Explore -> Trigger -> Observe Feedback Loop -> Summary.",
        (
            "R11 Primitive-first policy active: do not use Trellis or manage_3d_gen."
            if not allow_trellis
            else "R11 Trellis optional: still prefer primitives unless clearly necessary."
        ),
        "",
        "SCENE_SPEC_MIN_JSON:",
        spec_min_json,
        "",
        "EXECUTION_PLAN_JSON:",
        execution_json,
    ]
    return "\n".join(lines)


def _build_generation_prompt(
    spec_json: str,
    batch_plan: BatchExecutionPlan,
    *,
    mode: Literal["compact", "full"] = "compact",
) -> str:
    """Build generation prompt in either compact or verbose format."""
    if mode == "full":
        return _build_generation_prompt_full(spec_json, batch_plan)
    return _build_generation_prompt_compact(spec_json, batch_plan)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> None:
    st.set_page_config(page_title="Scene Builder", layout="wide")
    _inject_readability_styles()
    _init_state()
    _render_sidebar()

    tab1, tab2, tab3 = st.tabs([
        "Focus & Mapping",
        "Generate & Preview",
        "Reflection",
    ])

    with tab1:
        _render_focus_and_mapping()
    with tab2:
        _render_generate_preview()
    with tab3:
        _render_reflection()

    # Advanced Settings at the bottom of the page
    _render_advanced_settings()


if __name__ == "__main__":
    main()


