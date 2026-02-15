#!/usr/bin/env python3
"""Interactive test harness for the multi-agent brainstorm pipeline.

Run from the Server directory:
    uv run python -m scene_generator.test_pipeline

Or with explicit API key:
    OPENAI_API_KEY=sk-... uv run python -m scene_generator.test_pipeline

Options:
    --spec <path>       Path to a SceneSpec JSON file (default: bee_garden.json)
    --skip-merge        Skip the merge agent (show raw agent outputs)
    --skip-codegen      Skip the script code generation test
    --model <name>      Override brainstorm model (default: gpt-5.2)
    --codex-model <n>   Override codegen model (default: gpt-5.2-codex)
    --key <api_key>     Provide API key directly (or use OPENAI_API_KEY env var)
    --quiet             Only show pass/fail summary
    --verbose           Show DEBUG-level logs from brainstorm/codegen modules
    --save <path>       Save full results to a JSON file
"""
from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
import time
from pathlib import Path
from typing import Any

# Ensure the src directory is on the path
_src_dir = Path(__file__).resolve().parent.parent
if str(_src_dir) not in sys.path:
    sys.path.insert(0, str(_src_dir))

from scene_generator.config import cfg
from scene_generator.models import (
    BatchExecutionPlan,
    BrainstormResult,
    MCPCallPlan,
    SceneSpec,
    ScriptBlueprint,
)
from scene_generator.brainstorm import (
    brainstorm_causal_chain,
    brainstorm_interactions,
    brainstorm_script_architecture,
    merge_brainstorm_results,
    run_brainstorm,
    apply_brainstorm_to_spec,
)
from scene_generator.script_author import (
    _build_generate_prompt,
    _call_codex,
    _extract_csharp,
    build_scene_context,
)
from scene_generator.validator import PlanValidator

TEST_SPECS_DIR = Path(__file__).resolve().parent / "test_specs"

# ---------------------------------------------------------------------------
# ANSI colors
# ---------------------------------------------------------------------------
_GREEN = "\033[92m"
_RED = "\033[91m"
_YELLOW = "\033[93m"
_CYAN = "\033[96m"
_DIM = "\033[2m"
_BOLD = "\033[1m"
_RESET = "\033[0m"


def _ok(msg: str) -> str:
    return f"  {_GREEN}PASS{_RESET} {msg}"


def _fail(msg: str) -> str:
    return f"  {_RED}FAIL{_RESET} {msg}"


def _info(msg: str) -> str:
    return f"  {_CYAN}INFO{_RESET} {msg}"


def _warn(msg: str) -> str:
    return f"  {_YELLOW}WARN{_RESET} {msg}"


def _header(msg: str) -> str:
    return f"\n{_BOLD}{msg}{_RESET}"


# ---------------------------------------------------------------------------
# Step 1: API Key validation
# ---------------------------------------------------------------------------

async def test_api_key(api_key: str, model: str) -> tuple[bool, str, float]:
    """Test that the API key can reach OpenAI and the model responds."""
    from scene_generator.brainstorm import _call_openai

    start = time.time()
    try:
        response = await _call_openai(
            "Reply with exactly: OK",
            api_key=api_key,
            model=model,
        )
        elapsed = time.time() - start
        if response and "OK" in response.upper():
            return True, response.strip(), elapsed
        return False, response or "(empty response)", elapsed
    except Exception as e:
        elapsed = time.time() - start
        return False, str(e), elapsed


# ---------------------------------------------------------------------------
# Step 2: Individual agent tests
# ---------------------------------------------------------------------------

async def test_causal_chain(spec: SceneSpec, api_key: str) -> tuple[bool, list, float]:
    """Test the Causal Chain Agent."""
    start = time.time()
    try:
        result = await brainstorm_causal_chain(spec, api_key=api_key)
        elapsed = time.time() - start
        return len(result) > 0, result, elapsed
    except Exception as e:
        return False, [str(e)], time.time() - start


async def test_interactions(spec: SceneSpec, api_key: str) -> tuple[bool, dict, float]:
    """Test the Interaction Designer Agent."""
    start = time.time()
    try:
        result = await brainstorm_interactions(spec, api_key=api_key)
        elapsed = time.time() - start
        return len(result) > 0, result, elapsed
    except Exception as e:
        return False, {"error": str(e)}, time.time() - start


async def test_script_architect(spec: SceneSpec, api_key: str) -> tuple[bool, list | dict, float]:
    """Test the Script Architect Agent.

    On failure returns a diagnostic dict with parse/validation breakdown.
    On success returns a list of validated ScriptBlueprint objects.
    """
    from scene_generator.brainstorm import (
        _build_script_architect_prompt,
        _call_openai,
        _parse_json_response,
    )
    start = time.time()
    try:
        prompt = _build_script_architect_prompt(spec)
        raw = await _call_openai(prompt, api_key=api_key, model=cfg.script_architect_model)
        elapsed = time.time() - start
        if raw is None:
            return False, {"error": "no response from model", "raw_snippet": ""}, elapsed
        parsed = _parse_json_response(raw)
        if not isinstance(parsed, list) or len(parsed) == 0:
            return False, {
                "error": "JSON parse returned non-list or empty",
                "parsed_type": type(parsed).__name__,
                "raw_snippet": raw[:600],
            }, elapsed
        # Try to validate each item, collecting errors
        from scene_generator.models import ScriptBlueprint
        from pydantic import ValidationError
        blueprints: list[ScriptBlueprint] = []
        validation_errors: list[str] = []
        for i, item in enumerate(parsed):
            if isinstance(item, dict):
                try:
                    blueprints.append(ScriptBlueprint.model_validate(item))
                except ValidationError as ve:
                    validation_errors.append(f"[{i}] {ve.error_count()} errors: {ve.errors()[0]['msg']}")
                except Exception as ex:
                    validation_errors.append(f"[{i}] {type(ex).__name__}: {ex}")
        if blueprints:
            return True, blueprints, elapsed
        return False, {
            "error": "all items failed validation",
            "parsed_items": len(parsed),
            "valid_items": 0,
            "first_errors": validation_errors[:5],
            "raw_snippet": raw[:600],
        }, elapsed
    except Exception as e:
        return False, {"error": str(e), "raw_snippet": ""}, time.time() - start


# ---------------------------------------------------------------------------
# Step 3: Full brainstorm pipeline
# ---------------------------------------------------------------------------

async def test_full_brainstorm(
    spec: SceneSpec, api_key: str, skip_merge: bool = False,
) -> tuple[bool, BrainstormResult | None, float]:
    """Test the full brainstorm pipeline (parallel + merge)."""
    start = time.time()
    try:
        result = await run_brainstorm(spec, api_key=api_key, skip_merge=skip_merge)
        elapsed = time.time() - start
        ok = bool(result.causal_chain or result.enriched_interactions or result.script_blueprints)
        return ok, result, elapsed
    except Exception as e:
        return False, None, time.time() - start


async def test_merge_only(
    spec: SceneSpec,
    api_key: str,
    causal_chain: list,
    interactions: dict,
    blueprints: list[ScriptBlueprint],
    skip_merge: bool = False,
) -> tuple[bool, BrainstormResult | None, float]:
    """Test only the merge step, reusing pre-computed agent outputs."""
    start = time.time()
    try:
        if skip_merge:
            result = BrainstormResult(
                causal_chain=causal_chain,
                enriched_interactions=interactions,
                script_blueprints=blueprints,
                merge_notes=["Merge skipped"],
            )
        else:
            result = await merge_brainstorm_results(
                spec, causal_chain, interactions, blueprints,
                api_key=api_key,
            )
        elapsed = time.time() - start
        ok = bool(result.causal_chain or result.enriched_interactions or result.script_blueprints)
        return ok, result, elapsed
    except Exception as e:
        return False, None, time.time() - start


# ---------------------------------------------------------------------------
# Step 4: Script codegen test (without Unity — just prompt → code)
# ---------------------------------------------------------------------------

async def test_script_codegen(
    spec: SceneSpec,
    blueprints: list[ScriptBlueprint],
    api_key: str,
    codex_model: str,
) -> tuple[bool, dict[str, Any], float]:
    """Test script code generation for one manager task (no Unity compile)."""
    # Build a batch plan to get script_tasks and manager_tasks
    validator = PlanValidator(spec)
    plan = validator.validate_and_repair(MCPCallPlan())
    batch = validator.to_batch_plan(plan)

    if not batch.manager_tasks and not batch.script_tasks:
        return False, {"error": "No script tasks generated from spec"}, 0.0

    # Pick the first manager task to test code generation
    task = batch.manager_tasks[0] if batch.manager_tasks else batch.script_tasks[0]
    bp_lookup = {bp.class_name: bp for bp in blueprints}
    class_name = task.script_name.replace(".cs", "")
    blueprint = bp_lookup.get(class_name)

    scene_context = build_scene_context(
        batch.script_tasks, batch.manager_tasks, blueprints,
        target_concept=spec.target_concept,
        analogy_domain=spec.analogy_domain,
    )

    prompt = _build_generate_prompt(task, blueprint, scene_context)

    start = time.time()
    try:
        raw_code = await _call_codex(prompt, api_key=api_key, model=codex_model)
        elapsed = time.time() - start
        code = _extract_csharp(raw_code)
        if code and len(code) > 50:
            # Basic sanity checks
            has_class = f"class {class_name}" in code
            has_monobehaviour = "MonoBehaviour" in code
            has_serialize = "[SerializeField]" in code or "[Header" in code
            return True, {
                "script_name": task.script_name,
                "code_length": len(code),
                "has_class": has_class,
                "has_monobehaviour": has_monobehaviour,
                "has_serialize_fields": has_serialize,
                "preview": code[:500] + ("..." if len(code) > 500 else ""),
            }, elapsed
        return False, {"error": "Generated code too short or empty", "raw": raw_code[:200] if raw_code else None}, elapsed
    except Exception as e:
        return False, {"error": str(e)}, time.time() - start


# ---------------------------------------------------------------------------
# Step 5: Enriched prompt generation test
# ---------------------------------------------------------------------------

def test_prompt_generation(
    spec: SceneSpec, brainstorm_result: BrainstormResult | None,
) -> tuple[bool, dict[str, Any]]:
    """Test that the validator produces a valid BatchExecutionPlan with blueprints."""
    try:
        if brainstorm_result:
            enriched = apply_brainstorm_to_spec(spec, brainstorm_result)
        else:
            enriched = spec

        validator = PlanValidator(enriched)
        plan = validator.validate_and_repair(MCPCallPlan())
        batch = validator.to_batch_plan(plan)

        # Attach blueprints from brainstorm if available
        if brainstorm_result and brainstorm_result.script_blueprints:
            batch.script_blueprints = brainstorm_result.script_blueprints

        return True, {
            "total_commands": batch.total_commands,
            "phases": len(batch.phases),
            "phase_names": [p.phase_name for p in batch.phases],
            "script_tasks": len(batch.script_tasks),
            "manager_tasks": len(batch.manager_tasks),
            "blueprints_attached": len(batch.script_blueprints),
            "warnings": batch.warnings[:5],
        }
    except Exception as e:
        return False, {"error": str(e)}


# ---------------------------------------------------------------------------
# Main runner
# ---------------------------------------------------------------------------

async def run_tests(args: argparse.Namespace) -> dict[str, Any]:
    """Run all pipeline tests and return structured results."""
    results: dict[str, Any] = {"tests": {}, "summary": {"passed": 0, "failed": 0}}
    quiet = args.quiet
    total_start = time.time()

    # Resolve API key
    api_key = args.key or cfg.openai_api_key

    if not api_key:
        print(f"\n{_RED}ERROR: No API key found.{_RESET}")
        print("Set OPENAI_API_KEY in .env file, env var, or pass --key <api_key>")
        results["summary"]["failed"] = 1
        return results

    # Override models via env if requested through CLI args
    if args.model:
        os.environ["BRAINSTORM_MODEL"] = args.model
        os.environ["MERGE_MODEL"] = args.model
    if args.codex_model:
        os.environ["SCRIPT_ARCHITECT_MODEL"] = args.codex_model
        os.environ["CODEGEN_MODEL"] = args.codex_model

    brainstorm_model = cfg.brainstorm_model
    codex_model = cfg.codegen_model

    # Load spec
    spec_path = Path(args.spec) if args.spec else TEST_SPECS_DIR / "bee_garden.json"
    if not spec_path.exists():
        print(f"{_RED}ERROR: Spec file not found: {spec_path}{_RESET}")
        results["summary"]["failed"] = 1
        return results

    spec = SceneSpec.model_validate_json(spec_path.read_text(encoding="utf-8"))
    print(f"\n{_BOLD}Multi-Agent Pipeline Test{_RESET}")
    print(f"  Spec: {spec_path.name}")
    print(f"  Concept: {spec.target_concept} via {spec.analogy_domain}")
    print(f"  Mappings: {len(spec.mappings)}")
    print(f"  Brainstorm model: {brainstorm_model}")
    print(f"  Codegen model: {codex_model}")
    print(f"  Max output tokens: {cfg.max_output_tokens}")

    # ── Test 1: API Key ──────────────────────────────────────────────
    print(_header("1. API Key Validation"))
    ok, detail, elapsed = await test_api_key(api_key, brainstorm_model)
    results["tests"]["api_key"] = {"passed": ok, "elapsed": round(elapsed, 2), "detail": detail}
    if ok:
        print(_ok(f"API key works ({elapsed:.1f}s, response: {detail!r})"))
        results["summary"]["passed"] += 1
    else:
        print(_fail(f"API key test failed ({elapsed:.1f}s): {detail}"))
        results["summary"]["failed"] += 1
        print(f"\n{_RED}Cannot continue without a working API key.{_RESET}")
        return results

    # ── Test 2: Individual Agents (parallel) ─────────────────────────
    print(_header("2. Individual Brainstorm Agents (parallel)"))
    t2_start = time.time()
    causal_task = test_causal_chain(spec, api_key)
    interaction_task = test_interactions(spec, api_key)
    architect_task = test_script_architect(spec, api_key)

    (causal_ok, causal_data, causal_t), \
    (inter_ok, inter_data, inter_t), \
    (arch_ok, arch_data, arch_t) = await asyncio.gather(
        causal_task, interaction_task, architect_task,
    )
    t2_total = time.time() - t2_start

    # Causal Chain
    if causal_ok:
        steps = causal_data
        print(_ok(f"Causal Chain: {len(steps)} steps ({causal_t:.1f}s)"))
        if not quiet:
            for s in steps[:3]:
                trigger = s.trigger_event if hasattr(s, "trigger_event") else s.get("trigger_event", "")
                outcome = s.observable_outcome if hasattr(s, "observable_outcome") else s.get("observable_outcome", "")
                print(f"    {_DIM}→ {trigger} ⟹ {outcome}{_RESET}")
            if len(steps) > 3:
                print(f"    {_DIM}... and {len(steps) - 3} more{_RESET}")
        results["summary"]["passed"] += 1
    else:
        print(_fail(f"Causal Chain failed ({causal_t:.1f}s)"))
        results["summary"]["failed"] += 1
    results["tests"]["causal_chain"] = {
        "passed": causal_ok, "elapsed": round(causal_t, 2),
        "count": len(causal_data) if isinstance(causal_data, list) else 0,
    }

    # Interactions
    if inter_ok:
        print(_ok(f"Interaction Designer: {len(inter_data)} mappings ({inter_t:.1f}s)"))
        if not quiet:
            for name, ix in list(inter_data.items())[:3]:
                effect = ix.effect_description if hasattr(ix, "effect_description") else str(ix)[:60]
                print(f"    {_DIM}→ {name}: {effect}{_RESET}")
        results["summary"]["passed"] += 1
    else:
        print(_fail(f"Interaction Designer failed ({inter_t:.1f}s)"))
        results["summary"]["failed"] += 1
    results["tests"]["interactions"] = {
        "passed": inter_ok, "elapsed": round(inter_t, 2),
        "count": len(inter_data) if isinstance(inter_data, dict) else 0,
    }

    # Script Architect
    blueprints: list[ScriptBlueprint] = []
    if arch_ok:
        blueprints = arch_data if isinstance(arch_data, list) else []
        print(_ok(f"Script Architect: {len(blueprints)} blueprints ({arch_t:.1f}s)"))
        if not quiet:
            for bp in blueprints[:3]:
                fields_n = len(bp.fields) if hasattr(bp, "fields") else 0
                methods_n = len(bp.methods) if hasattr(bp, "methods") else 0
                print(f"    {_DIM}→ {bp.class_name}: {fields_n} fields, {methods_n} methods{_RESET}")
            if len(blueprints) > 3:
                print(f"    {_DIM}... and {len(blueprints) - 3} more{_RESET}")
        results["summary"]["passed"] += 1
    else:
        print(_fail(f"Script Architect failed ({arch_t:.1f}s)"))
        if not quiet and isinstance(arch_data, dict):
            parsed_n = arch_data.get("parsed_items", "?")
            valid_n = arch_data.get("valid_items", "?")
            err = arch_data.get("error", "unknown")
            print(f"    {_DIM}Reason: {err}{_RESET}")
            if parsed_n != "?":
                print(f"    {_DIM}Parsed {parsed_n} items, {valid_n} valid blueprints{_RESET}")
            for ve in arch_data.get("first_errors", []):
                print(f"    {_RED}  {ve}{_RESET}")
            snippet = arch_data.get("raw_snippet", "")
            if snippet:
                snippet = snippet.replace("\n", "\n    ")
                print(f"    {_DIM}Raw response (first 600 chars):{_RESET}")
                print(f"    {_DIM}{snippet}{_RESET}")
        results["summary"]["failed"] += 1
    results["tests"]["script_architect"] = {
        "passed": arch_ok, "elapsed": round(arch_t, 2),
        "count": len(arch_data) if isinstance(arch_data, list) else 0,
    }

    print(_info(f"All 3 agents completed in {t2_total:.1f}s (parallel)"))

    # ── Test 3: Merge Step (reuses agent outputs from test 2) ──────
    print(_header("3. Merge Step (reuses test 2 agent outputs)"))
    # Build inputs for merge from test 2 data
    merge_causal: list = causal_data if causal_ok and isinstance(causal_data, list) else []
    merge_inter: dict = inter_data if inter_ok and isinstance(inter_data, dict) else {}
    merge_bps: list[ScriptBlueprint] = blueprints  # already computed above

    if not merge_causal and not merge_inter and not merge_bps:
        print(_warn("No valid agent outputs from test 2; running full brainstorm instead"))
        ok3, brainstorm_result, t3 = await test_full_brainstorm(spec, api_key, skip_merge=args.skip_merge)
    else:
        ok3, brainstorm_result, t3 = await test_merge_only(
            spec, api_key, merge_causal, merge_inter, merge_bps, skip_merge=args.skip_merge,
        )
    if ok3 and brainstorm_result:
        chain_n = len(brainstorm_result.causal_chain)
        inter_n = len(brainstorm_result.enriched_interactions)
        bp_n = len(brainstorm_result.script_blueprints)
        notes_n = len(brainstorm_result.merge_notes)
        status = "merge skipped" if args.skip_merge else f"{notes_n} merge notes"
        print(_ok(f"Brainstorm complete ({t3:.1f}s): {chain_n} chain, {inter_n} interactions, {bp_n} blueprints, {status}"))
        if not quiet and brainstorm_result.merge_notes:
            for note in brainstorm_result.merge_notes[:5]:
                print(f"    {_DIM}→ {note}{_RESET}")
        results["summary"]["passed"] += 1
        # Use these blueprints for the codegen test
        if brainstorm_result.script_blueprints:
            blueprints = brainstorm_result.script_blueprints
    else:
        print(_fail(f"Full brainstorm failed ({t3:.1f}s)"))
        results["summary"]["failed"] += 1
        brainstorm_result = None
    results["tests"]["full_brainstorm"] = {
        "passed": ok3, "elapsed": round(t3, 2),
        "merge_skipped": args.skip_merge,
    }

    # ── Test 4: Script Code Generation ───────────────────────────────
    if not args.skip_codegen:
        print(_header("4. Script Code Generation (single script, no Unity)"))
        ok4, codegen_data, t4 = await test_script_codegen(spec, blueprints, api_key, codex_model)
        if ok4:
            sn = codegen_data.get("script_name", "?")
            cl = codegen_data.get("code_length", 0)
            checks = []
            if codegen_data.get("has_class"):
                checks.append("class")
            if codegen_data.get("has_monobehaviour"):
                checks.append("MonoBehaviour")
            if codegen_data.get("has_serialize_fields"):
                checks.append("SerializeField")
            print(_ok(f"{sn}: {cl} chars, checks=[{', '.join(checks)}] ({t4:.1f}s)"))
            if not quiet:
                preview = codegen_data.get("preview", "")
                # Show first few lines
                for line in preview.split("\n")[:8]:
                    print(f"    {_DIM}{line}{_RESET}")
                if preview.endswith("..."):
                    print(f"    {_DIM}...{_RESET}")
            results["summary"]["passed"] += 1
        else:
            print(_fail(f"Code generation failed ({t4:.1f}s): {codegen_data.get('error', '?')}"))
            results["summary"]["failed"] += 1
        results["tests"]["script_codegen"] = {
            "passed": ok4, "elapsed": round(t4, 2),
            "detail": {k: v for k, v in codegen_data.items() if k != "preview"},
        }

    # ── Test 5: Prompt Generation ────────────────────────────────────
    test_num = "5" if not args.skip_codegen else "4"
    print(_header(f"{test_num}. Enriched BatchExecutionPlan Generation"))
    ok5, plan_data = test_prompt_generation(spec, brainstorm_result)
    if ok5:
        cmds = plan_data.get("total_commands", 0)
        phases = plan_data.get("phases", 0)
        scripts = plan_data.get("script_tasks", 0)
        managers = plan_data.get("manager_tasks", 0)
        bps = plan_data.get("blueprints_attached", 0)
        print(_ok(
            f"BatchExecutionPlan: {cmds} commands, {phases} phases, "
            f"{scripts} scripts, {managers} managers, {bps} blueprints"
        ))
        if not quiet:
            for pn in plan_data.get("phase_names", []):
                print(f"    {_DIM}→ {pn}{_RESET}")
            for w in plan_data.get("warnings", []):
                print(_warn(w))
        results["summary"]["passed"] += 1
    else:
        print(_fail(f"Plan generation failed: {plan_data.get('error', '?')}"))
        results["summary"]["failed"] += 1
    results["tests"]["prompt_generation"] = {"passed": ok5, "detail": plan_data}

    # ── Summary ──────────────────────────────────────────────────────
    p = results["summary"]["passed"]
    f = results["summary"]["failed"]
    total = p + f
    total_elapsed = time.time() - total_start
    print(_header("Summary"))
    color = _GREEN if f == 0 else _RED
    print(f"  {color}{p}/{total} passed{_RESET}  ({total_elapsed:.1f}s total)")
    if f > 0:
        failed_names = [name for name, data in results["tests"].items() if not data.get("passed")]
        print(f"  {_RED}Failed: {', '.join(failed_names)}{_RESET}")
    results["summary"]["total_elapsed"] = round(total_elapsed, 2)

    return results


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Test the multi-agent brainstorm pipeline end-to-end.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  uv run python -m scene_generator.test_pipeline
  uv run python -m scene_generator.test_pipeline --spec path/to/spec.json
  uv run python -m scene_generator.test_pipeline --skip-merge --quiet
  uv run python -m scene_generator.test_pipeline --key sk-... --save results.json
  uv run python -m scene_generator.test_pipeline --model gpt-4o --codex-model gpt-4o
""",
    )
    parser.add_argument("--spec", help="Path to SceneSpec JSON file (default: bee_garden.json)")
    parser.add_argument("--skip-merge", action="store_true", help="Skip the merge agent")
    parser.add_argument("--skip-codegen", action="store_true", help="Skip script code generation test")
    parser.add_argument("--model", help=f"Override brainstorm model (default: {cfg.brainstorm_model})")
    parser.add_argument("--codex-model", help=f"Override codegen model (default: {cfg.codegen_model})")
    parser.add_argument("--key", help="OpenAI API key (or set OPENAI_API_KEY env var)")
    parser.add_argument("--quiet", action="store_true", help="Only show pass/fail summary")
    parser.add_argument("--verbose", action="store_true", help="Show DEBUG-level logs from brainstorm/codegen modules")
    parser.add_argument("--save", help="Save full results to JSON file")
    args = parser.parse_args()

    import logging
    log_level = logging.DEBUG if args.verbose else logging.WARNING
    logging.basicConfig(level=log_level, format="%(name)s %(levelname)s: %(message)s")

    results = asyncio.run(run_tests(args))

    if args.save:
        # Serialize results (convert non-serializable objects)
        def _serialize(obj: Any) -> Any:
            if hasattr(obj, "model_dump"):
                return obj.model_dump(mode="json")
            if hasattr(obj, "to_dict"):
                return obj.to_dict()
            return str(obj)

        save_path = Path(args.save)
        save_path.write_text(
            json.dumps(results, indent=2, default=_serialize),
            encoding="utf-8",
        )
        print(f"\n  Results saved to {save_path}")

    sys.exit(1 if results["summary"]["failed"] > 0 else 0)


if __name__ == "__main__":
    main()
