# Scene Builder — Multi-Agent Pipeline Guide

## Overview

The Scene Builder generates interactive 3D educational scenes from educator-defined analogy mappings. The multi-agent pipeline enhances this with three parallel brainstorm agents and an LLM-powered script author that writes, compiles, and fixes C# MonoBehaviour scripts automatically.

### Architecture

```
┌────────────────────────────────────────────────────────────┐
│                    Streamlit GUI (app.py)                   │
│  Educator fills mapping table → clicks "Brainstorm + Suggest" │
└───────────────────────┬────────────────────────────────────┘
                        │
            ┌───────────▼───────────┐
            │   Brainstorm Phase    │
            │  (brainstorm.py)      │
            │                       │
            │  ┌─────────────────┐  │       ┌──────────────────┐
            │  │ Causal Chain    │──┼──────▶│                  │
            │  │ Agent           │  │       │                  │
            │  └─────────────────┘  │       │  LLM Merge Agent │
            │  ┌─────────────────┐  │       │  (gpt-5.2)       │
            │  │ Interaction     │──┼──────▶│                  │
            │  │ Designer Agent  │  │       │  Reconciles all  │
            │  └─────────────────┘  │       │  3 outputs into  │
            │  ┌─────────────────┐  │       │  BrainstormResult│
            │  │ Script          │──┼──────▶│                  │
            │  │ Architect Agent │  │       └────────┬─────────┘
            │  └─────────────────┘  │                │
            └───────────────────────┘                │
                                                     ▼
                              ┌───────────────────────────────┐
                              │        Enriched SceneSpec      │
                              │  + ScriptBlueprints            │
                              └──────────────┬────────────────┘
                                             │
                              ┌──────────────▼────────────────┐
                              │      PlanValidator             │
                              │  SceneSpec → MCPCallPlan →     │
                              │  BatchExecutionPlan            │
                              └──────────────┬────────────────┘
                                             │
                    ┌────────────────────────▼──────────────────────┐
                    │              Execution Loop                    │
                    │          (scene_generator.py)                  │
                    │                                               │
                    │  Phase 0: Validate Essence                    │
                    │  Phase 1: Environment (terrain, sky, lights)  │
                    │  Phase 2: Objects (GameObjects, transforms)   │
                    │  Phase 3: Materials & Colors                  │
                    │  Phase 4: Scripts  ◀── Script Author Agent    │
                    │  Phase 5: Components & VFX                    │
                    │  Phase 6: Field Wiring (SerializeField refs)  │
                    │  Phase 7: Animations                          │
                    │  Phase 8: Hierarchy                           │
                    │  Phase 9: Smoke Test                          │
                    │  Phase 10: Scene Save                         │
                    └───────────────────────────────────────────────┘
```

**Phase 4 — Script Author intercept:** When an OpenAI API key is available and the plan includes script tasks or blueprints, the execution loop delegates Phase 4 to the Script Author agent instead of sending stub `create_script` commands. The Script Author:

1. Generates complete C# code using `gpt-5.2-codex`
2. Calls `create_script` to write the file in Unity
3. Calls `refresh_unity` to trigger compilation
4. Calls `read_console` to check for errors
5. If errors exist: LLM generates a fix and loops (up to 3 retries)

---

## Configuration

### API Keys

The pipeline uses OpenAI models for all LLM calls. API keys are resolved in this priority order:

| Priority | Source | Used by |
|----------|--------|---------|
| 1 | Sidebar "API Key" field in Streamlit | Brainstorm + Suggest flow |
| 2 | `OPENAI_API_KEY` env var | Both Streamlit and server-side Script Author |
| 3 | `SCENE_BUILDER_DEFAULT_OPENAI_API_KEY` env var | Fallback for both |
| 4 | `SCENE_BUILDER_DEFAULT_API_KEY` env var | Generic fallback |

**Set your API key using any of these methods:**

```bash
# Option A: Environment variable (recommended for headless/CI)
export OPENAI_API_KEY="sk-..."

# Option B: App-specific default
export SCENE_BUILDER_DEFAULT_OPENAI_API_KEY="sk-..."

# Option C: Paste directly in the Streamlit sidebar (ephemeral, per-session)
```

> **Important:** The Script Author agent runs server-side during `execute_batch_plan` and resolves its key from environment variables only (`OPENAI_API_KEY` → `SCENE_BUILDER_DEFAULT_OPENAI_API_KEY` → `SCENE_BUILDER_DEFAULT_API_KEY`). Make sure at least one is set if you want the Script Author to activate during execution.

### Models

| Agent | Model | Config Location |
|-------|-------|-----------------|
| Brainstorm (Causal Chain, Interaction, Merge) | `gpt-5.2` | `brainstorm.py::BRAINSTORM_MODEL` |
| Script Architect | `gpt-5.2-codex` | `brainstorm.py::SCRIPT_ARCHITECT_MODEL` |
| Script Author (code gen + fix) | `gpt-5.2-codex` | `script_author.py::CODEGEN_MODEL` |
| Single-agent Suggest (fallback) | Sidebar selection | Streamlit sidebar "Model" field |

All LLM calls use the **OpenAI Responses API** (`client.responses.create`) rather than the Chat Completions endpoint.

---

## Workflow: Step by Step

### 1. Define Your Scene (Focus & Mapping Tab)

1. Set the **Target Concept** (what you're teaching, e.g., "AI Recommendation Systems")
2. Choose an **Analogy Domain** (the metaphor, e.g., "Bee Pollination Garden")
3. Fill in the **Mapping Table**: each row maps a structural component (user, content_item, ranking, etc.) to an analogy source attribute (Bee, Flower, Dance, etc.) with a relationship description

### 2. Get AI Suggestions (Generate & Preview Tab)

1. **Without brainstorm**: Click "Get Suggestions from AI" — sends one LLM call to suggest environment, interactions, and asset strategies
2. **With brainstorm**: Check "Use Multi-Agent Brainstorm" then click "Brainstorm + Suggest":
   - Three agents run in parallel (~10-20 seconds)
   - A merge agent reconciles their outputs (~5-10 seconds)
   - The enriched spec is then sent to the single-agent suggest for final formatting
   - Total time: ~20-40 seconds depending on model latency

3. Review the visual diagram showing:
   - Environment setting and skybox
   - Object relationships and interactions
   - Causal chain flow
   - Per-mapping interaction details (trigger, effect, targets)

4. **Edit suggestions inline**: Click on any suggested description/interaction to modify it directly

5. **Refine with follow-up feedback**: Answer the 3 clarification questions and click "Apply Feedback"

6. **Accept Suggestions**: Merges AI suggestions into your spec

### 3. Generate & Execute

Two modes available:

**Prompt Export** (for Claude Code / Cursor / manual):
- Click "Generate Prompts" → copies a structured prompt to clipboard
- The prompt includes brainstorm results (script blueprints, merge notes) when available
- Paste into your AI assistant with Unity-MCP tools

**Direct Execution** (if connected to Unity):
- Select "Execute first, then export prompt" mode
- The system builds a `BatchExecutionPlan` and executes all phases
- Phase 4 (scripts) uses the Script Author agent to generate real C# code
- Smoke test runs automatically at the end

---

## File Structure

```
Server/src/scene_generator/
├── app.py                  # Streamlit GUI — educator workflow, suggest flow, export
├── brainstorm.py           # 3 parallel agents + LLM merge
├── script_author.py        # Compile-check-fix loop for C# scripts
├── models.py               # Pydantic models (SceneSpec, ScriptBlueprint, BrainstormResult, etc.)
├── validator.py            # PlanValidator: SceneSpec → MCPCallPlan → BatchExecutionPlan
└── test_specs/             # Sample SceneSpec JSON files (Bee Garden, Sprinkler, etc.)

Server/src/services/tools/
└── scene_generator.py      # MCP tool handler — execution loop, audit, smoke test
```

---

## Brainstorm Agents in Detail

### Causal Chain Agent
- **Input**: SceneSpec (concept, analogy, mappings)
- **Output**: Ordered list of `CausalChainStep` (trigger → immediate feedback → delayed update → outcome)
- **Purpose**: Defines the observable cause-and-effect sequence a learner should see

### Interaction Designer Agent
- **Input**: SceneSpec (mappings with structural components)
- **Output**: `dict[str, InteractionSpec]` keyed by mapping `analogy_name`
- **Purpose**: Designs triggers, effects, targets, and parameters for each mapping relationship

### Script Architect Agent
- **Input**: SceneSpec (mappings, interactions, experience phases)
- **Output**: List of `ScriptBlueprint` (class name, fields, methods, events, dependencies)
- **Purpose**: Defines the API contracts for all MonoBehaviour scripts without writing full code

### Merge Agent
- **Input**: All three agent outputs + original SceneSpec
- **Output**: Reconciled `BrainstormResult` with `merge_notes` documenting decisions
- **Purpose**: Resolves naming conflicts, missing references, and circular dependencies between the three agents' outputs

---

## Script Author Agent in Detail

The Script Author activates during `execute_batch_plan` Phase 4 when:
1. An OpenAI API key is available in environment variables, AND
2. The plan contains `script_tasks`, `manager_tasks`, or `script_blueprints`

**Execution order**:
1. Manager scripts first (they define events that interaction scripts subscribe to)
2. Interaction scripts second (they reference manager-defined events)

**Compile-check-fix loop** (per script):
```
Generate code (gpt-5.2-codex)
    → create_script (Unity)
    → refresh_unity (compile)
    → read_console (check errors)
    → If errors: generate fix → loop (max 3 retries)
```

**Fallback**: If no API key is found, the original stub script flow runs (creates `// TODO: Implement` placeholders).

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| "No API key configured" in sidebar | No key set | Set `OPENAI_API_KEY` env var or paste in sidebar |
| Brainstorm runs but suggestions empty | Merge agent failed to parse | Check console for JSON parse errors; try again |
| Script Author doesn't activate | No env var API key | Set `OPENAI_API_KEY` (sidebar key isn't visible to the execution loop) |
| Scripts compile but don't work at runtime | Missing field wiring | Phase 6 (field wiring) handles SerializeField references — check that it ran |
| Smoke test fails | Runtime errors in scripts | Check Unity console for NullReferenceException — usually a missing SerializeField reference |
| "Brainstorm failed" error | Network or API issue | Check API key validity, network connectivity, and model availability |

---

## Cost Estimation

| Operation | Model | Approx. Token Usage | Cost (est.) |
|-----------|-------|---------------------|-------------|
| Brainstorm (3 agents) | gpt-5.2 | ~4,000 input + ~3,000 output | ~$0.05-0.15 |
| Merge agent | gpt-5.2 | ~3,000 input + ~2,000 output | ~$0.03-0.08 |
| Script Author (per script) | gpt-5.2-codex | ~2,000 input + ~3,000 output | ~$0.03-0.10 |
| **Total per scene (5 scripts)** | | | **~$0.20-0.60** |

Costs vary based on scene complexity (number of mappings/scripts) and retry count.

---

## Test Coverage

### Running Tests

```bash
cd Server
uv run pytest tests/ -q --tb=short
```

All tests should pass (652+ passed, ~15 skipped). The skipped tests require a live Unity connection.

### Test Files

#### `tests/test_scene_generator_improvements.py` (~51 tests)

Tests the core scene generator pipeline end-to-end:

| Category | What It Tests |
|----------|--------------|
| **Schema validation** | `SceneSpec` rejects invalid mapping types, confidence values; includes surface defaults |
| **PlanValidator** | Canonicalizes components, generates focused managers (GameManager, InteractionManager, etc.), normalizes VFX aliases, repairs missing primitives, assigns default colors, auto-repairs missing interactions, injects UI anchors, creates manager anchor GameObjects |
| **Experience plan** | Phase flow structure, causal chain generation, batch metadata, smoke gates |
| **Essence/surface** | Freeze essence hashing, validate essence-surface invariants, generate surface variants |
| **Batch audit** | Hard fails on banned tag lookup patterns (CompareTag, FindGameObjectsWithTag), classifies retryable failures (busy, compiling, timeout) |
| **Intent contract** | UI requirements, readability requirements, learner goal preservation |
| **Script generation** | Functional runtime scripts (not log-only), compile readiness phase ordering |
| **Plan-and-execute** | Happy path, invalid spec handling, validator error propagation, execution failure propagation, retry parameters, action dispatch |
| **Execute-batch-plan** | Preflight validation (unresolved targets), happy path execution + scene save, retry logic, smoke failure blocking |
| **App-level** | Generation mode selection, LLM response parsing, prompt generation (compact/full), clarification question generation, asset policy (Trellis stripping), execute-first mode |

#### `tests/integration/test_logging_stdout.py` (1 test)

Code hygiene test that scans all `.py` files under `Server/src/` to ensure:
- No files have syntax errors (catches BOM encoding, invalid Python, etc.)
- No stray `print()` or `sys.stdout.write()` calls in production code

#### `tests/integration/test_manage_scene_paging_params.py`

Tests scene management paging parameter handling for the Unity MCP tool.

### Pre-existing Issues Fixed

| Issue | Root Cause | Fix Applied |
|-------|-----------|-------------|
| BOM encoding in `app.py` and `scene_generator.py` | UTF-8 BOM bytes (`EF BB BF`) at file start caused `ast.parse()` to fail in Python 3.13 | Stripped BOM bytes from both files |
| Relative path in 5 tests | `Path("Server/src/...")` resolved relative to CWD, not test file | Changed to `Path(__file__).resolve().parent.parent / "src" / ...` |
