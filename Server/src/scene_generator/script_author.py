"""Script Author Agent — Evaluator-Optimizer pattern for C# script generation.

Generates complete MonoBehaviour C# code for each script task, then calls
create_script → refresh_unity → read_console in a compile-check-fix loop.

Model and API key configuration lives in scene_generator/config.py
(reads from .env file or environment variables).
"""
from __future__ import annotations

import asyncio
import json
import logging
from typing import Any

from .config import cfg
from .models import (
    ManagerTask,
    ScriptBlueprint,
    ScriptTask,
)

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

MAX_COMPILE_RETRIES = 3


# ---------------------------------------------------------------------------
# Low-level LLM call (async, OpenAI)
# ---------------------------------------------------------------------------


async def _call_codex(
    prompt: str,
    *,
    api_key: str,
    model: str | None = None,
) -> str | None:
    """Call OpenAI Responses API for code generation."""
    resolved_model = model or cfg.codegen_model

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
        logger.exception("Codex call failed (model=%s)", resolved_model)
        return None


# ---------------------------------------------------------------------------
# Prompt builders
# ---------------------------------------------------------------------------


def _build_generate_prompt(
    task: ScriptTask | ManagerTask,
    blueprint: ScriptBlueprint | None,
    scene_context: str,
) -> str:
    """Build a prompt to generate complete C# code for one script."""
    is_manager = isinstance(task, ManagerTask)

    # Blueprint section
    blueprint_text = ""
    if blueprint:
        fields_text = "\n".join(
            f"  [SerializeField] {f.field_type} {f.field_name}; // {f.purpose}"
            for f in blueprint.fields
        )
        methods_text = "\n".join(
            f"  {m.return_type} {m.method_name}({', '.join(m.parameters)}) "
            f"// {m.purpose}\n    // {m.pseudocode}"
            for m in blueprint.methods
        )
        deps_text = ", ".join(blueprint.dependencies) if blueprint.dependencies else "none"
        events_emit = ", ".join(blueprint.events_emitted) if blueprint.events_emitted else "none"
        events_listen = ", ".join(blueprint.events_listened) if blueprint.events_listened else "none"
        blueprint_text = f"""
## Script Blueprint (from architecture agent)

**Purpose:** {blueprint.purpose}
**Dependencies:** {deps_text}
**Events emitted:** {events_emit}
**Events listened:** {events_listen}

**Fields:**
{fields_text}

**Methods:**
{methods_text}
"""

    # Task-specific section
    if is_manager:
        task_section = f"""## Manager Task
- **Manager:** {task.manager_name} ({task.manager_id})
- **Scope:** {task.orchestration_scope}
- **Attach to:** {task.attach_to}
- **Reason:** {task.required_reason}
- **Responsibilities:** {', '.join(task.responsibilities)}
- **Creates/Updates:** {', '.join(task.creates_or_updates)}
- **Listens to events:** {', '.join(task.listens_to)}
- **Emits events:** {', '.join(task.emits)}
- **Managed mappings:** {', '.join(task.managed_mappings)}"""
    else:
        task_section = f"""## Script Task
- **Task:** {task.task_id} ({task.task_kind})
- **Mapping:** {task.mapping_name}
- **Script name:** {task.script_name}
- **Attach to:** {task.attach_to}
- **Trigger:** {task.trigger} (source: {task.trigger_source})
- **Target objects:** {', '.join(task.target_objects)}
- **Effect:** {task.effect}
- **Effect description:** {task.effect_description}
- **Parameters:** {json.dumps(task.parameters)}
- **Animation preset:** {task.animation_preset}
- **VFX type:** {task.vfx_type}
- **Preconditions:** {', '.join(task.preconditions)}
- **Notes:** {', '.join(task.notes)}"""

    script_name = task.script_name if hasattr(task, "script_name") else task.manager_name

    return f"""You are an expert Unity C# developer. Generate a COMPLETE, COMPILABLE MonoBehaviour script.

{task_section}
{blueprint_text}

## Scene Context
{scene_context}

## Requirements

1. The class name MUST be `{script_name.replace('.cs', '')}` and inherit from `MonoBehaviour`
2. Use `[SerializeField]` for ALL cross-object references (never use FindObjectOfType/FindObjectsOfType)
3. Use C# events (System.Action or UnityEngine.Events.UnityEvent) for inter-script communication — never SendMessage
4. Include proper null checks for all SerializeField references in Awake()/Start()
5. Use coroutines (IEnumerator + StartCoroutine) for any delayed effects
6. Include descriptive `[Header("...")]` attributes to group SerializeFields
7. Add `[Tooltip("...")]` to complex fields
8. Handle edge cases: missing references, repeated triggers, disabled components
9. Use `Debug.LogWarning` for recoverable errors, never throw exceptions
10. The script MUST compile standalone — do not reference types that aren't defined in this file unless they're Unity built-in types or types you list as dependencies

## Output

Return ONLY the complete C# file content. No markdown fences, no explanation.
Start with `using` statements, end with the closing brace of the namespace or class."""


def _build_fix_prompt(
    script_name: str,
    code: str,
    errors: list[str],
) -> str:
    """Build a prompt to fix compilation errors in a script."""
    errors_text = "\n".join(f"- {err}" for err in errors[:20])  # Cap at 20 errors

    return f"""You are an expert Unity C# developer. Fix the compilation errors in this script.

## Script: {script_name}

```csharp
{code}
```

## Compilation Errors
{errors_text}

## Rules
1. Fix ALL listed errors
2. Do not remove functionality — fix the errors while preserving intent
3. If an error is about a missing type, either define it inline or remove the dependency
4. Keep all [SerializeField] attributes
5. Ensure the class name stays `{script_name.replace('.cs', '')}`

Return ONLY the complete fixed C# file. No markdown fences, no explanation."""


# ---------------------------------------------------------------------------
# Code extraction
# ---------------------------------------------------------------------------


def _extract_csharp(text: str | None) -> str | None:
    """Extract C# code from LLM response, stripping fences if present."""
    if not text:
        return None
    import re
    # Try fenced code blocks
    fenced = re.findall(r"```(?:csharp|cs)?\s*([\s\S]*?)```", text, flags=re.IGNORECASE)
    if fenced:
        return fenced[0].strip()
    # If text starts with 'using' or 'namespace', it's raw code
    stripped = text.strip()
    if stripped.startswith("using ") or stripped.startswith("namespace "):
        return stripped
    return stripped


# ---------------------------------------------------------------------------
# Build scene context string for code generation prompts
# ---------------------------------------------------------------------------


def build_scene_context(
    script_tasks: list[ScriptTask],
    manager_tasks: list[ManagerTask],
    blueprints: list[ScriptBlueprint],
    target_concept: str = "",
    analogy_domain: str = "",
    learning_goal: str = "",
) -> str:
    """Build a compact scene context string for code generation prompts."""
    lines = []
    if target_concept:
        lines.append(f"Teaching: {target_concept} via {analogy_domain}")
    if learning_goal:
        lines.append(f"Goal: {learning_goal}")

    lines.append("\nAll scripts in scene:")
    for mt in manager_tasks:
        lines.append(f"  - {mt.script_name} (manager, attached to {mt.attach_to})")
    for st in script_tasks:
        lines.append(f"  - {st.script_name} (interaction, attached to {st.attach_to})")

    if blueprints:
        lines.append("\nScript API contracts:")
        for bp in blueprints:
            events = ", ".join(bp.events_emitted) if bp.events_emitted else "none"
            listens = ", ".join(bp.events_listened) if bp.events_listened else "none"
            lines.append(f"  {bp.class_name}: emits [{events}], listens [{listens}]")
            for f in bp.fields:
                lines.append(f"    - {f.field_type} {f.field_name}")

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Script Author Agent — the compile-check-fix loop
# ---------------------------------------------------------------------------


class ScriptAuthorResult:
    """Result of authoring a single script."""

    def __init__(self, script_name: str):
        self.script_name = script_name
        self.code: str | None = None
        self.success: bool = False
        self.attempts: int = 0
        self.errors: list[str] = []

    def to_dict(self) -> dict[str, Any]:
        return {
            "script_name": self.script_name,
            "success": self.success,
            "attempts": self.attempts,
            "errors": self.errors,
        }


async def author_single_script(
    task: ScriptTask | ManagerTask,
    blueprint: ScriptBlueprint | None,
    scene_context: str,
    *,
    api_key: str,
    send_unity_command: Any,  # async callable(tool, params) -> dict
    max_retries: int = MAX_COMPILE_RETRIES,
) -> ScriptAuthorResult:
    """Generate, create, compile, and verify a single script.

    Implements the Evaluator-Optimizer loop:
    1. LLM generates code
    2. create_script sends it to Unity
    3. refresh_unity triggers compilation
    4. read_console checks for errors
    5. If errors: LLM fixes code (up to max_retries)

    Args:
        task: The ScriptTask or ManagerTask to implement.
        blueprint: Optional ScriptBlueprint from the brainstorm architect.
        scene_context: Context string with all scripts and their APIs.
        api_key: OpenAI API key.
        send_unity_command: async callable that sends a command to Unity.
            Signature: async (tool: str, params: dict) -> dict
        max_retries: Max compilation fix attempts.

    Returns:
        ScriptAuthorResult with success/failure status.
    """
    script_name = task.script_name if hasattr(task, "script_name") else task.manager_name
    result = ScriptAuthorResult(script_name)

    # Step 1: Generate initial code
    prompt = _build_generate_prompt(task, blueprint, scene_context)
    raw_code = await _call_codex(prompt, api_key=api_key)
    code = _extract_csharp(raw_code)
    if not code:
        result.errors = ["Code generation returned empty response"]
        return result

    result.code = code

    for attempt in range(1, max_retries + 1):
        result.attempts = attempt

        # Step 2: Create script in Unity
        create_result = await send_unity_command("create_script", {
            "name": script_name,
            "code": code,
        })
        if not isinstance(create_result, dict) or not create_result.get("success", False):
            msg = create_result.get("message", "Unknown error") if isinstance(create_result, dict) else str(create_result)
            result.errors.append(f"create_script failed: {msg}")
            # Don't retry create failures — they're usually path issues
            return result

        # Step 3: Trigger compilation
        await send_unity_command("refresh_unity", {"compile": "request"})
        # Wait for compilation to complete
        await send_unity_command("refresh_unity", {"wait_for_ready": True})

        # Step 4: Check for errors
        console_result = await send_unity_command("read_console", {
            "types": ["error"],
            "count": 50,
        })
        errors: list[str] = []
        if isinstance(console_result, dict):
            entries = console_result.get("entries", [])
            if isinstance(entries, list):
                for entry in entries:
                    msg = entry.get("message", "") if isinstance(entry, dict) else str(entry)
                    if msg and script_name.replace(".cs", "") in msg:
                        errors.append(msg)

        if not errors:
            result.success = True
            result.errors = []
            logger.info("Script %s compiled successfully on attempt %d", script_name, attempt)
            return result

        result.errors = errors
        logger.warning(
            "Script %s has %d errors on attempt %d, fixing...",
            script_name, len(errors), attempt,
        )

        if attempt >= max_retries:
            break

        # Step 5: Fix code
        fix_prompt = _build_fix_prompt(script_name, code, errors)
        fixed_raw = await _call_codex(fix_prompt, api_key=api_key)
        fixed_code = _extract_csharp(fixed_raw)
        if not fixed_code:
            result.errors.append("Fix attempt returned empty response")
            break
        code = fixed_code
        result.code = code

    return result


async def author_all_scripts(
    script_tasks: list[ScriptTask],
    manager_tasks: list[ManagerTask],
    blueprints: list[ScriptBlueprint],
    *,
    api_key: str,
    send_unity_command: Any,
    target_concept: str = "",
    analogy_domain: str = "",
    learning_goal: str = "",
    max_retries: int = MAX_COMPILE_RETRIES,
) -> list[ScriptAuthorResult]:
    """Author all scripts sequentially (scripts must compile in order).

    Manager scripts are authored first (they define events), then interaction
    scripts (they subscribe to events).

    Args:
        script_tasks: Interaction script tasks from the batch plan.
        manager_tasks: Manager orchestration tasks from the batch plan.
        blueprints: Script blueprints from brainstorm (if available).
        api_key: OpenAI API key.
        send_unity_command: async callable(tool, params) -> dict.
        target_concept: For context string.
        analogy_domain: For context string.
        learning_goal: For context string.
        max_retries: Per-script retry limit.

    Returns:
        List of ScriptAuthorResult for each script.
    """
    # Build blueprint lookup by class_name
    bp_lookup: dict[str, ScriptBlueprint] = {}
    for bp in blueprints:
        bp_lookup[bp.class_name] = bp
        # Also try with .cs extension removed
        if bp.class_name.endswith(".cs"):
            bp_lookup[bp.class_name[:-3]] = bp

    scene_context = build_scene_context(
        script_tasks, manager_tasks, blueprints,
        target_concept, analogy_domain, learning_goal,
    )

    results: list[ScriptAuthorResult] = []

    # Author managers first (they define events)
    for task in manager_tasks:
        class_name = task.script_name.replace(".cs", "")
        blueprint = bp_lookup.get(class_name) or bp_lookup.get(task.script_name)
        r = await author_single_script(
            task, blueprint, scene_context,
            api_key=api_key,
            send_unity_command=send_unity_command,
            max_retries=max_retries,
        )
        results.append(r)
        if r.success:
            # Recompile after each successful manager so its types are available
            logger.info("Manager %s ready, types available for next scripts", task.script_name)

    # Then author interaction scripts
    for task in script_tasks:
        class_name = task.script_name.replace(".cs", "")
        blueprint = bp_lookup.get(class_name) or bp_lookup.get(task.script_name)
        r = await author_single_script(
            task, blueprint, scene_context,
            api_key=api_key,
            send_unity_command=send_unity_command,
            max_retries=max_retries,
        )
        results.append(r)

    return results
