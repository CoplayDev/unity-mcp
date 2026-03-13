# MCP Tool Design Guidelines

Principles for designing MCP tools in unity-mcp. Based on the [MCP Specification](https://modelcontextprotocol.io/docs/concepts/tools), [Anthropic's tool authoring guide](https://www.anthropic.com/engineering/writing-tools-for-agents), and [MCP-Bench benchmarks](https://arxiv.org/abs/2508.20453).

---

## 1. Intent-Based, Not Operation-Based

Tools should map to **user intents** (what the LLM wants to accomplish), not raw API calls.

```
# Bad — atomic operations, 3 tools for one workflow
get_scene_list()
open_scene(name)
save_scene()

# Good — one composite tool with action enum
manage_scene(action="list|open|save", ...)
```

**Rule**: If two tools are chained >80% of the time, merge them into one tool with an `action` parameter.

**Why this matters**: Each tool definition permanently consumes context tokens regardless of whether it is called. Fewer tools = better LLM accuracy + less context waste.

---

## 2. Keep Tool Count Low

Benchmark data (MCP-Bench, 2025):

| Active Tools | LLM Accuracy |
|-------------|-------------|
| 10 | 98.4% |
| 50 | ~93% |
| 100 | ~88% |

**~10% accuracy drop per 10× tool count increase.** Larger context windows make it worse, not better (more options = weaker attention).

**Target: under 20 active tools** for optimal LLM performance.

The `manage_*` + `action` enum pattern compresses what would be 100+ atomic tools into ~30 composite tools. This is the right tradeoff.

---

## 3. When to Split vs Merge

### Keep as one tool when:
- Actions share the same domain (scene, material, gameobject)
- Actions share most parameters
- Tool description stays under ~500 tokens

### Split into separate tools when:
- Actions have **completely different parameter sets** (>5 unique params per action)
- The tool description exceeds ~500 tokens (context bloat offsets consolidation)
- The domain has **no conceptual overlap** (e.g., `manage_vfx` + `manage_audio` should stay separate)

---

## 4. Parameter Design

### Flat over nested
LLMs handle flat parameter structures better than deeply nested JSON.

```python
# Good — flat
action: str        # "create"
name: str          # "Player"
position_x: float  # 0.0
position_y: float  # 1.0
position_z: float  # 0.0

# Acceptable — shallow nesting for semantic groups
position: {"x": 0, "y": 1, "z": 0}

# Bad — deep nesting
config: {"transform": {"position": {"x": 0, "y": 1, "z": 0}}}
```

### Every parameter needs a description
The `description` field is the LLM's instruction manual. Be specific about format, valid values, and defaults.

```python
# Bad
@mcp_for_unity_tool(description="Manage entities")
async def manage_dots(action: str, ...):

# Good
@mcp_for_unity_tool(description="Query, inspect, and modify ECS entities and systems")
async def manage_dots(
    action: str,  # "query_entities|get_entity|list_systems|..."
    component_types: str = "",  # Comma-separated, e.g. "Health,TeamId"
    ...
):
```

### response_format parameter
Add `response_format: "concise" | "detailed"` to tools that return large payloads. This lets the LLM control verbosity and save context in multi-step chains.

### Pagination
Always page results that could exceed 50 items. Use `page_size` + `cursor` parameters. Return `next_cursor` in the response when more results exist.

---

## 5. Error Handling — Two Tiers

The MCP spec defines two error surfaces:

| Tier | When | How |
|------|------|-----|
| **Protocol error** (JSON-RPC `error`) | Unknown tool, malformed request, server crash | `{"error": {"code": -32601, "message": "..."}}` |
| **Execution error** (`isError: true`) | Bad input, Unity API failure, missing component | `{"isError": true, "content": [{"type": "text", "text": "..."}]}` |

### Execution errors must be actionable

```
# Bad — raw error, no guidance
"NullReferenceException at ManageGameObject.cs:142"

# Good — actionable, includes correction
"GameObject 'Player' not found in active scene. Active scene is 'MainMenu' with 23 root objects. Did you mean 'PlayerSpawn'? Use find_gameobjects to search."
```

**Include in error messages:**
- What went wrong (concisely)
- Current state that caused the failure
- Suggested corrective action or alternative tool call

---

## 6. Response Design

### Consistent shapes across related tools
Tools in the same domain should return responses with the same JSON structure so LLMs can parse them predictably.

### Batch support
When a tool operates on single items, consider adding array parameter support to avoid forcing N sequential calls:

```python
# Instead of forcing 5 calls to get_entity:
entity_ids: str  # "1,5,12,30,44" — comma-separated batch
```

### Response size limit
Target max ~25K tokens per response. For larger results, use pagination. Truncate with a clear message:

```json
{"results": [...], "truncated": true, "total": 500, "next_cursor": "eyJ..."}
```

---

## 7. Naming Conventions

### Tool names
`manage_<domain>` for composite tools, `<verb>_<noun>` for single-purpose tools.

```
manage_scene          # composite — list, open, save, screenshot
manage_gameobject     # composite — create, delete, modify, find
find_gameobjects      # single-purpose — search only
read_console          # single-purpose — read only
run_tests             # single-purpose — execute tests
```

### Action names
Lowercase, underscore-separated, verb-first:

```
list_worlds, query_entities, get_entity, create_entity, toggle_system
```

### Parameter names
`snake_case`, matching both Python and C# conventions (the ToolParams class handles automatic camelCase conversion).

---

## 8. C# Handler Patterns

### Use ToolParams for validation
```csharp
var p = new ToolParams(parameters);
var name = p.GetRequired("name");         // returns Result<string>
var size = p.GetInt("page_size") ?? 50;   // nullable with default
```

### Guard optional dependencies
```csharp
#if UNITY_ENTITIES
[McpForUnityTool("manage_dots")]
public static class ManageDots { ... }
#endif
```

### Keep handlers stateless
Tool handlers should not store state between calls. If state is needed (e.g., async job tracking), use Unity's built-in systems (EditorPrefs, ScriptableObject, static fields with clear lifecycle).

---

## 9. Dynamic Loading (Future Direction)

For 50+ tools, the recommended pattern is on-demand/dynamic loading — only expose tools relevant to the current task context. Anthropic achieved 98.7% token reduction in production with this approach.

Current unity-mcp registers all tools at startup. If the tool count grows beyond ~40, consider:
- **Category-based activation**: LLM requests a category ("dots", "scene", "materials"), server exposes only those tools
- **Project-scoped filtering**: `--project-scoped-tools` flag already exists — extend it to also filter by installed Unity packages (no DOTS package → no DOTS tools)

---

## 10. Checklist for New Tools

Before adding a new tool, verify:

- [ ] **Intent check** — Does this map to a user intent, not a raw operation? Could it be an action on an existing `manage_*` tool instead?
- [ ] **Count check** — Will this push total tool count above 20? If yes, can you merge with an existing tool?
- [ ] **Description** — Tool and all parameters have clear descriptions with format/example hints
- [ ] **Error messages** — All error paths return actionable messages (not raw exceptions)
- [ ] **Pagination** — Results that could exceed 50 items are paged
- [ ] **Both sides** — Python MCP tool + C# handler + CLI command (optional) are all created
- [ ] **Tests** — Integration tests cover happy path + error cases
- [ ] **Studio tools** — If studio-specific, goes in a separate file (not modifying upstream files)
