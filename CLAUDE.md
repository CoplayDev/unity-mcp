# CLAUDE.md - Project Overview for AI Assistants

## What This Project Is

**MCP for Unity** is a bridge that lets AI assistants (Claude, Cursor, Windsurf, etc.) control the Unity Editor through the Model Context Protocol (MCP). It enables AI-driven game development workflows - creating GameObjects, editing scripts, managing assets, running tests, and more.

## Architecture

```text
AI Assistant (Claude/Cursor)
        ↓ MCP Protocol (stdio/SSE)
Python Server (Server/src/)
        ↓ WebSocket + HTTP
Unity Editor Plugin (MCPForUnity/)
        ↓ Unity Editor API
Scene, Assets, Scripts
```

**Two codebases, one system:**
- `Server/` - Python MCP server using FastMCP
- `MCPForUnity/` - Unity C# Editor package

## Directory Structure

```text
├── Server/                     # Python MCP Server
│   ├── src/
│   │   ├── cli/commands/       # Tool implementations (20 domain modules)
│   │   ├── transport/          # MCP protocol, WebSocket bridge
│   │   ├── services/           # Custom tools, resources
│   │   └── core/               # Telemetry, logging, config
│   └── tests/                  # 502 Python tests
├── MCPForUnity/                # Unity Editor Package
│   └── Editor/
│       ├── Tools/              # C# tool implementations (42 files)
│       ├── Services/           # Bridge, state management
│       ├── Helpers/            # Utilities (27 files)
│       └── Windows/            # Editor UI
├── TestProjects/UnityMCPTests/ # Unity test project (605 tests)
└── tools/                      # Build/release scripts
```

## Code Philosophy

### 1. Domain Symmetry
Python CLI commands mirror C# Editor tools. Each domain (materials, prefabs, scripts, etc.) exists in both:
- `Server/src/cli/commands/materials.py` ↔ `MCPForUnity/Editor/Tools/ManageMaterial.cs`

### 2. Minimal Abstraction
Avoid premature abstraction. Three similar lines of code is better than a helper that's used once. Only abstract when you have 3+ genuine use cases.

### 3. Delete Rather Than Deprecate
When removing functionality, delete it completely. No `_unused` renames, no `// removed` comments, no backwards-compatibility shims for internal code.

### 4. Test Coverage Required
Every new feature needs tests. We have 1100+ tests across Python and C#. Run them before PRs.

### 5. Keep Tools Focused
Each MCP tool does one thing well. Resist the urge to add "convenient" parameters that bloat the API surface.

### 6. Use Resources for reading.
Keep them smart and focused rather than "read everything" type resources. That way resources are quick and LLM-friendly. There are plenty of examples in the codebase to model on (gameobject, prefab, etc.)

## Key Patterns

### Parameter Handling (C#)
Use `ToolParams` for consistent parameter validation:
```csharp
var p = new ToolParams(parameters);
var pageSize = p.GetInt("page_size", "pageSize") ?? 50;
var name = p.RequireString("name");
```

### Error Handling (Python CLI)
Use the `@handle_unity_errors` decorator:
```python
@handle_unity_errors
async def my_command(ctx, ...):
    result = await call_unity_tool(...)
```

### Paging Large Results
Always page results that could be large (hierarchies, components, search results):
- Use `page_size` and `cursor` parameters
- Return `next_cursor` when more results exist

## Common Tasks

### Running Tests
```bash
# Python
cd Server && uv run pytest tests/ -v

# Unity - open TestProjects/UnityMCPTests in Unity, use Test Runner window
```

### Local Development
1. Set **Server Source Override** in MCP for Unity Advanced Settings to your local `Server/` path
2. Enable **Dev Mode** checkbox to force fresh installs
3. Use `mcp_source.py` to switch Unity package sources
4. Test on Windows and Mac if possible, and multiple clients (Claude Desktop and Claude Code are tricky for configuration       as of this writing)

### Adding a New Tool
1. Add Python command in `Server/src/cli/commands/<domain>.py`
2. Add C# implementation in `MCPForUnity/Editor/Tools/Manage<Domain>.cs`
3. Add tests in both `Server/tests/` and `TestProjects/UnityMCPTests/Assets/Tests/`

## What Not To Do

- Don't add features without tests
- Don't create helper functions for one-time operations
- Don't add error handling for scenarios that can't happen
- Don't commit to `main` directly - branch off `beta` for PRs
- Don't add docstrings/comments to code you didn't change

---

## Scene Generator Framework

The scene generator (`Server/src/scene_generator/`) is a standalone pipeline that converts a teacher's `SceneSpec` into a fully executable Unity scene plan. It has its own Streamlit UI, multi-agent LLM pipeline, and test harness.

### Data Flow

```text
SceneSpec (JSON)
    ↓  brainstorm.py: 3 parallel LLM agents + merge
BrainstormResult (causal chain, interactions, blueprints)
    ↓  apply_brainstorm_to_spec()
Enriched SceneSpec
    ↓  validator.py: PlanValidator.validate_and_repair() → to_batch_plan()
BatchExecutionPlan (phased MCP commands + ScriptTasks + ManagerTasks)
    ↓  script_author.py: per-script codegen with compile-check-fix loop
Complete C# scripts ready for Unity
```

### Key Files

| File | Purpose |
|---|---|
| `models.py` | All Pydantic models: `SceneSpec`, `ScriptBlueprint`, `BrainstormResult`, `BatchExecutionPlan`, `ScriptTask`, `ManagerTask` |
| `config.py` | Centralized config via `cfg` singleton — reads `.env` then env vars |
| `brainstorm.py` | Multi-agent pipeline: `_call_openai()`, 3 agents, `merge_brainstorm_results()`, `run_brainstorm()` |
| `script_author.py` | Code generation: `_call_codex()`, prompt builders, `author_single_script()`, `author_all_scripts()` |
| `validator.py` | `PlanValidator` — converts SceneSpec → MCPCallPlan → BatchExecutionPlan with repair/injection |
| `app.py` | Streamlit UI (~4100 lines) — 3-tab educator workflow |
| `test_pipeline.py` | Standalone CLI test harness — 5-stage end-to-end pipeline test |
| `test_specs/` | Example SceneSpec JSONs: `bee_garden.json`, `simple_demo.json`, `sprinkler_garden.json` |

### Configuration Pattern

All config is centralized in `config.py`. Import and use:
```python
from scene_generator.config import cfg

api_key = cfg.openai_api_key      # resolves OPENAI_API_KEY from .env / env
model = cfg.brainstorm_model      # resolves BRAINSTORM_MODEL, default "gpt-5.2"
tokens = cfg.max_output_tokens    # resolves MAX_OUTPUT_TOKENS, default 16000
```

Settings live in `Server/src/scene_generator/.env` (git-ignored). Copy `.env.example` to get started. Real env vars always override `.env` values. All properties resolve at access time — no caching.

### LLM Call Pattern

Both `_call_openai()` (brainstorm) and `_call_codex()` (codegen) follow the same pattern:
```python
async def _call_openai(prompt: str, *, api_key: str, model: str | None = None) -> str | None:
    resolved_model = model or cfg.brainstorm_model
    def _sync_call() -> str | None:
        from openai import OpenAI
        client = OpenAI(api_key=api_key)
        response = client.responses.create(
            model=resolved_model, input=prompt, max_output_tokens=cfg.max_output_tokens,
        )
        return response.output_text
    return await asyncio.to_thread(_sync_call)
```
- Uses **OpenAI Responses API** (`client.responses.create`), NOT chat completions
- Wraps sync client in `asyncio.to_thread` (each call gets its own client instance)
- Returns `None` on failure (logged, never raised)
- Model defaults come from `cfg`, callers can override via `model=` kwarg

### Pydantic Model Conventions

- Every field has a default so partial LLM output still parses (use `Field(default_factory=list)` for collections)
- Use `field_validator(mode="before")` to coerce LLM output shapes — e.g. `ScriptMethodSpec._coerce_pseudocode` joins `list[str]` → `str` because LLMs return pseudocode as arrays
- Use `model_validator(mode="after")` for computed fields (see `BatchExecutionPlan._compute_stats`)
- When parsing LLM JSON, always use `try/except ValidationError` per item and skip failures — never let one bad item abort the whole list
- `_parse_json_response()` in `brainstorm.py` handles code fences, raw JSON, and partial decoding

### Multi-Agent Brainstorm

Three parallel specialist agents → LLM merge agent:

| Agent | Function | Returns |
|---|---|---|
| Causal Chain | `brainstorm_causal_chain()` | `list[CausalChainStep]` |
| Interaction Designer | `brainstorm_interactions()` | `dict[str, InteractionSpec]` |
| Script Architect | `brainstorm_script_architecture()` | `list[ScriptBlueprint]` |
| Merge Agent | `merge_brainstorm_results()` | `BrainstormResult` |

Orchestrated by `run_brainstorm(spec, api_key=, skip_merge=)` using `asyncio.gather` for the 3 agents. Each agent has its own `_build_*_prompt()` function.

### Validator Pipeline

`PlanValidator(spec)` does deterministic plan generation (no LLM):
1. `validate_and_repair(MCPCallPlan())` — injects environment, objects, materials, scripts, components, animations, field wiring, scene save
2. `to_batch_plan(plan)` — groups calls into 10 ordered `ExecutionPhase`s, generates `ScriptTask` and `ManagerTask` lists, resolves targets, expands instances

### Running Tests

```bash
# Unit tests (no API key needed)
cd Server && uv run pytest tests/ -v

# Pipeline integration test (requires OPENAI_API_KEY in .env)
cd Server/src && uv run python -m scene_generator.test_pipeline

# Pipeline test options
--spec path/to/spec.json     # Custom spec (default: bee_garden.json)
--skip-merge                 # Skip merge agent
--skip-codegen               # Skip script code generation
--model gpt-4o               # Override brainstorm model
--codex-model gpt-4o         # Override codegen model
--quiet                      # Summary only
--verbose                    # DEBUG-level logs
--save results.json          # Save full results
```

The test pipeline runs 5 stages: API key → individual agents → merge step → script codegen → BatchExecutionPlan generation.

### Adding a New Agent

1. Add the agent function in `brainstorm.py`: `async def brainstorm_<name>(spec, *, api_key) -> <ReturnType>`
2. Add a `_build_<name>_prompt(spec)` function returning the prompt string
3. Add the return model to `models.py` if needed
4. Wire it into `run_brainstorm()` via `asyncio.gather`
5. Update `merge_brainstorm_results()` and `_build_merge_prompt()` to include the new output
6. Add a test function in `test_pipeline.py`
7. Add unit tests in `Server/tests/`
