# Claude Code Task: Build the Scene Generation Pipeline for EmbodiedCreate

## Context

I'm building **EmbodiedCreate** (aka VR-MCP), a system that transforms educational analogical mappings into interactive 3D VR scenes via Unity-MCP. The core workflow is:

1. An expert (teacher) fills out an **Object Table** and **Structure Mapping Table** describing a learning analogy (e.g., "bee pollination → AI recommendation systems", the current draft can be found at DesignDocBeeTrapV2.md)
2. The system generates a complete, polished 3D scene in Unity from these tables (MAJOR STEP)
3. The user then iterates on the scene via natural language text commands — this iteration is handled directly by **Unity-MCP itself** (the LLM calls MCP tools in response to user requests). This is NOT part of this task.

**This task is to build Step 2: the automated pipeline that takes the completed tables and produces a Unity scene via MCP tool calls.**

## What Already Exists (DO NOT REBUILD)

### Unity-MCP (CoplayDev/unity-mcp)
Already installed and working as a Package in the Unity project (`Packages/com.coplaydev.unity-mcp`). This is a live MCP server that the LLM can call directly. The tools available include:

| MCP Tool | What It Does | Key Actions/Params |
|---|---|---|
| `manage_gameobject` | Create, modify, delete, duplicate, look_at GameObjects | `action`: create/modify/delete/duplicate/look_at. `primitive_type`: Cube/Sphere/Cylinder/Plane/Capsule. `position`, `rotation`, `scale` as `[x,y,z]`. `parent` for hierarchy. `tag`, `layer`. `look_at_target` (vector or GO name). |
| `manage_scene` | Scene hierarchy, load/save, screenshot, scene view control | `action`: get_hierarchy/get_active/save/screenshot/scene_view_frame. `camera`: select camera by name/path/ID. `include_image`: return inline base64 PNG. `max_resolution`: downscale cap (default 512). `batch`: 'surround' for 6-angle capture. `look_at`/`view_position`/`view_rotation`: positioned capture. |
| `manage_asset` | Import, create, search, modify assets | `action`: import/create/search/get_info. `path` for asset location. `search_pattern` for globbing. |
| `manage_material` | Create materials, set colors/properties | `action`: create/set_renderer_color/set_material_color/assign_material_to_renderer. `color` as `[r,g,b,a]`. |
| `manage_components` | Add/remove/configure components on GameObjects | `action`: add/remove/set_property. `component_type`: e.g., "Rigidbody", "BoxCollider". |
| `manage_vfx` | Particle systems, trails, line renderers | `action`: particle_create/particle_set_emission/trail_create. Attach to targets. |
| `manage_animation` | Animator control, clip creation | `action`: animator_play/controller_create/clip_create. |
| `manage_shader` | Create/read/update shaders | CRUD on shader files. |
| `manage_script` | Create/read/delete C# scripts | `action`: create/read/delete. |
| `manage_editor` | Play/pause/stop, tags/layers | `action`: play/pause/stop/add_tag/add_layer. |
| `read_console` | Read Unity console logs | `action`: get/clear. Filter by type. |
| `batch_execute` | **Run multiple commands in one call** | `commands`: array of `{tool, params}`. `parallel`: true for concurrent. **This is the key performance tool — 10-100× faster than sequential calls.** |
| `find_gameobjects` | Search for objects by name/tag/component | `search_method`: by_name/by_tag/by_component/by_path. |

### Draft `manage_3dgen` Tool (INCOMPLETE - Need to check)
There is an existing **draft** MCP tool for 3D asset generation at:
- **C# side**: `Packages/com.coplaydev.unity-mcp/Editor/Tools/Manage3DGen.cs` (exists, handles Unity-side import/instantiation)
- **Python side**: A corresponding Python tool definition should exist in the MCP server's tool registry

### Unity Project: EmbodiedCreate
- Active scene: `Assets/_Scenes/EmbodiedCreate.unity`
- Has XR Toolkit for VR (hand tracking, gaze, XR Interaction)
- Has GLTFast for GLB import
- Has Trellis integration code at `Assets/TrellisPlugin` (Trellis2Client.cs, Trellis2Window.cs, Trellis2Demo.cs)
- Has results folders: `Assets/TrellisResults/`
- We will start at the EmbodiedCreate Unity Scene which is brand new.

---

## What To Build

A Python module called `scene_generator` that:
1. Takes a structured `SceneSpec` (Object Table + Mapping Table) as input
2. Plans the scene by generating an **ordered list of MCP tool calls** (NOT an intermediate JSON — the output IS the execution plan)
3. Validates the plan for completeness and fills in defaults for anything missing
4. Executes the plan against Unity via MCP tools, using `batch_execute` for parallelism
5. Verifies the scene was created correctly

### File Structure

```
scene_generator/
├── __init__.py
├── models.py           # Pydantic data models for SceneSpec, MCP call representations
├── planner.py          # LLM-based scene planning (tables → list of MCP tool calls)
├── validator.py        # Pre-execution: validate plan completeness, fill defaults
├── executor.py         # Execute MCP tool calls against Unity-MCP
├── trellis_bridge.py   # Bridge to the manage_3dgen MCP tool
├── prompts.py          # System prompts for the planner LLM
└── cli.py              # CLI entry point for testing
```

---

## Part 1: Data Models (`models.py`)

Define Pydantic models for the input and the MCP call plan.

### Understanding the Expert's Table

The expert fills out a **Comparative Framework of Embodied Analogies** — a table where:
- Each **row** is a **structural component** of the target concept (User, Content Item, User Profile, User Interaction, Profile Update, Candidate Generation, Ranking, Feedback Loop)
- Each **column** is a different **analogy representation** (e.g., "Beehive Analogy" vs. "Sprinkler Analogy" vs. a new Task 3)
- Each **cell** describes how that structural component is embodied in that analogy

This is NOT a flat object list — it's a structured mapping grounded in Gentner's Structure Mapping Theory. The structural components are the **abstract relational structure** of the target domain, and each analogy column provides a concrete embodiment.

Here is a real example of the table the expert fills out:

| Structural Component | Beehive Analogy (Task 1) | Sprinkler Analogy (Task 2) |
|---|---|---|
| **User** | **Bee:** First-person flight controls | **Gardener:** Handheld tool + backpack tank |
| **Content Item** | **Flower:** 3D flowers with varying attributes | **Data Plant:** Futuristic plants with life stages (seed→sprout→bloom→wilt) |
| **User Profile** | **Beehive:** Central 3D model that moves in space | **Profile Gauge:** Wrist gauge with fluid level and color |
| **User Interaction** | **Pollination:** Aim + button press, visual/audio effect | **Targeted Watering:** Aim sprinkler, fire focused water stream |
| **Profile Update** | **Beehive Movement:** Hive drifts toward pollinated flowers | **Tank Color Change:** Fluid changes to weighted average of watered plant colors |
| **Candidate Generation** | **Pollen Circle:** Visible circular boundary centered on beehive | Water stream has maximum effective distance |
| **Similarity/Diversity Ranking** | **Bud Growth:** Buds closest to beehive grow first | **Proximity Growth:** Plants matching tank color grow faster |
| **Feedback Loop** | Pollinating → moves hive → similar flowers grow nearby → more similar pollination | Watering color → changes tank → accelerates same-color growth → specialized watering |

### Input: SceneSpec

The SceneSpec maps this table into a machine-readable format. The key insight is that each row produces **one or more 3D objects/behaviors** and the structural component type tells the system what KIND of thing it is (which informs spatial layout, interaction design, and visual priority).

```python
from pydantic import BaseModel
from typing import Optional
from enum import Enum

class StructuralComponent(str, Enum):
    """The abstract structural roles from the target domain.
    Based on Gentner's SMT: these are RELATIONAL structures, not surface features.
    The system uses these to infer spatial layout and interaction patterns."""
    USER = "user"                          # The embodied agent (player avatar)
    CONTENT_ITEM = "content_item"          # Items the user interacts with
    USER_PROFILE = "user_profile"          # Observable representation of user state
    USER_INTERACTION = "user_interaction"  # The core action/mechanic
    PROFILE_UPDATE = "profile_update"      # How the profile changes after interaction
    CANDIDATE_GENERATION = "candidate_generation"  # How the system selects what to show
    RANKING = "ranking"                    # How items are prioritized/ordered
    FEEDBACK_LOOP = "feedback_loop"        # The self-reinforcing cycle

class AssetStrategy(str, Enum):
    PRIMITIVE = "primitive"      # Unity primitive (Cube, Sphere, Cylinder, Plane, Capsule)
    TRELLIS = "trellis"          # Generate via manage_3dgen MCP tool
    VFX = "vfx"                  # Particle system / visual effect
    MECHANIC = "mechanic"        # Interaction logic (script + components, no visible asset)
    UI = "ui"                    # UI element (Canvas, TextMesh, gauge)

class MappingRow(BaseModel):
    """One row of the Comparative Framework table.
    Maps a structural component to its concrete analogy representation."""
    structural_component: StructuralComponent
    
    # The concrete analogy representation (what the expert writes in the cell)
    analogy_name: str            # e.g., "Bee", "Beehive", "Pollination"
    analogy_description: str     # Full description from the cell, e.g., 
                                  # "The user embodies a bee, navigating the garden 
                                  # with first-person flight controls."
    
    # 3D realization (how to build it — expert can specify or system infers)
    asset_strategy: AssetStrategy = AssetStrategy.TRELLIS  # default: generate
    primitive_type: Optional[str] = None     # if primitive: "Cube", "Sphere", etc.
    trellis_prompt: Optional[str] = None     # if trellis: image generation prompt
    
    # Optional: expert can provide spatial/visual hints
    appearance_hint: Optional[str] = None    # "warm brown color", "glowing blue"
    spatial_hint: Optional[str] = None       # "central position", "on user's wrist"
    
    # For mechanics/feedback that don't have a single object
    involves_objects: list[str] = []         # analogy_names of other rows involved
                                              # e.g., Feedback Loop involves ["Bee", "Flower", "Beehive"]

class EnvironmentSpec(BaseModel):
    """Global environment settings inferred from the analogy domain."""
    setting: str = "garden"          # "garden", "laboratory", "ocean", "city"
    terrain: str = "grass_plane"     # Unity terrain type
    skybox: str = "sunny"            # "sunny", "sunset", "night", "overcast"
    ambient_color: list[float] = [0.8, 0.9, 0.7]
    description: str = ""            # Free-text: "A sunny garden with flowers and a central beehive"

class SceneSpec(BaseModel):
    """Complete input derived from the expert's Comparative Framework table.
    Represents ONE analogy column (one task/representation)."""
    
    # Metadata
    target_concept: str              # What we're teaching, e.g., "AI Recommendation System"
    analogy_domain: str              # The source analogy, e.g., "Bee Pollination"
    learning_goal: str               # e.g., "Teach how recommendation algorithms create filter bubbles"
    task_label: str = ""             # e.g., "Task 1: Beehive Analogy"
    
    # The mapping table (one column from the Comparative Framework)
    mappings: list[MappingRow]       # One entry per structural component
    
    # Environment
    environment: EnvironmentSpec = EnvironmentSpec()
```

### Why This Structure Matters

The `StructuralComponent` enum is critical for the planner because different component types have different spatial and design implications:

| Component Type | Spatial Implication | Design Implication |
|---|---|---|
| `USER` | At camera/player spawn point | Needs VR avatar components, movement script |
| `CONTENT_ITEM` | Distributed across scene, multiple instances | Often repeated/varied, needs visual variety |
| `USER_PROFILE` | Near/attached to user OR central landmark | Must be visible and readable |
| `USER_INTERACTION` | Connects user to content items | VFX, audio, animation — not a static object |
| `PROFILE_UPDATE` | Co-located with user_profile | Animation/VFX showing change |
| `CANDIDATE_GENERATION` | Spatial boundary or range indicator | Transparent collider, particle boundary |
| `RANKING` | Affects content_item appearance/position | Growth animation, sorting, highlighting |
| `FEEDBACK_LOOP` | Connects multiple components | No single object — emergent from other mechanics |

The planner uses this table to decide:
- What to create as a 3D asset (USER, CONTENT_ITEM, USER_PROFILE)
- What to create as VFX/mechanics (USER_INTERACTION, PROFILE_UPDATE, CANDIDATE_GENERATION)
- What to express through spatial layout (RANKING, FEEDBACK_LOOP)

### Output: MCPCallPlan

**The planner's output is NOT an intermediate JSON schema — it's a list of MCP tool calls ready to execute.** Each call maps directly to one of the Unity-MCP tools above.

```python
class MCPToolCall(BaseModel):
    """A single MCP tool call. Maps directly to batch_execute command format."""
    tool: str                        # e.g., "manage_gameobject", "manage_material", "manage_3dgen"
    params: dict                     # tool-specific parameters
    description: str = ""            # human-readable note for debugging
    depends_on: list[str] = []       # IDs of calls this depends on (for ordering)
    call_id: str = ""                # unique ID for dependency tracking

class MCPCallPlan(BaseModel):
    """Complete execution plan as ordered MCP tool calls."""
    # Phase 1: Environment + Lighting (parallel, no dependencies)
    environment_calls: list[MCPToolCall] = []
    
    # Phase 2: Primitive objects (parallel, no dependencies)
    primitive_calls: list[MCPToolCall] = []
    
    # Phase 3: Trellis generation (parallel, via manage_3dgen)
    trellis_calls: list[MCPToolCall] = []
    
    # Phase 4: Materials (depends on objects existing)
    material_calls: list[MCPToolCall] = []
    
    # Phase 5: Components, VFX, scripts (depends on objects)
    component_calls: list[MCPToolCall] = []
    vfx_calls: list[MCPToolCall] = []
    
    # Phase 6: Hierarchy / parenting (depends on all objects)
    hierarchy_calls: list[MCPToolCall] = []
    
    def all_calls_ordered(self) -> list[list[MCPToolCall]]:
        """Return calls grouped by execution phase (each group can run in parallel)."""
        return [
            self.environment_calls,
            self.primitive_calls + self.trellis_calls,  # run in parallel
            self.material_calls,
            self.component_calls + self.vfx_calls,
            self.hierarchy_calls,
        ]
```

---

## Part 2: Scene Planner (`planner.py`)

The planner uses an LLM to read the SceneSpec and produce a **list of concrete MCP tool calls**. The LLM must have knowledge of the MCP tool signatures to generate valid calls.

### Key Design Decisions

1. **Output is MCP calls, not intermediate JSON.** The LLM directly generates `{tool, params}` dicts that can be passed to `batch_execute`. No intermediate ScenePlan representation.

2. **LLM generates coordinates directly.** Based on relational context from the Structure Mapping Table (e.g., "flowers surround beehive" → radial placement), the LLM infers reasonable `[x, y, z]` positions. No Z3 constraint solver.

3. **The LLM needs MCP tool knowledge.** The system prompt must include the tool signatures from the table above so it knows what parameters each tool accepts.

```python
import anthropic
import json
from .models import SceneSpec, MCPCallPlan, MCPToolCall

SYSTEM_PROMPT = """You are a Unity scene builder that generates MCP tool calls to create 3D scenes.

You will receive an Object Table and Structure Mapping Table describing an educational analogy.
Your job is to output a JSON object containing ordered lists of MCP tool calls that will build the complete scene in Unity.

## Available MCP Tools

### manage_gameobject
Create/modify/delete GameObjects.
```json
{"tool": "manage_gameobject", "params": {
    "action": "create",
    "name": "MyObject",
    "primitive_type": "Cube",  // Cube, Sphere, Cylinder, Plane, Capsule
    "position": [0, 0, 0],
    "rotation": [0, 0, 0],
    "scale": [1, 1, 1],
    "parent": "ParentName",  // optional
    "tag": "Untagged"        // optional
}}
```

### manage_material
Set colors and material properties.
```json
{"tool": "manage_material", "params": {
    "action": "set_renderer_color",
    "target": "MyObject",
    "color": [1.0, 0.0, 0.0, 1.0]  // RGBA 0-1
}}
```
Or create a new material:
```json
{"tool": "manage_material", "params": {
    "action": "create",
    "material_path": "Assets/Materials/MyMat.mat",
    "shader": "Universal Render Pipeline/Lit",
    "properties": {"_BaseColor": [1, 0, 0, 1]}
}}
```

### manage_components
Add components to GameObjects.
```json
{"tool": "manage_components", "params": {
    "action": "add",
    "target": "MyObject",
    "component_type": "Rigidbody",
    "properties": {"mass": 1.0, "useGravity": true}
}}
```

### manage_vfx
Create particle systems and effects.
```json
{"tool": "manage_vfx", "params": {
    "action": "particle_create",
    "target": "MyObject",
    "properties": {
        "startColor": [1, 1, 0, 1],
        "startSize": 0.1,
        "startLifetime": 2.0,
        "emissionRate": 20
    }
}}
```

### manage_3dgen
Generate 3D assets via Trellis. Returns a GLB that gets imported and instantiated.
```json
{"tool": "manage_3dgen", "params": {
    "action": "generate",
    "prompt": "a stylized cartoon wooden beehive, game asset, white background",
    "name": "Beehive",
    "position": [0, 0.5, 0],
    "scale": [1, 1, 1]
}}
```
NOTE: This is async and slow (3-35 seconds). Use for complex organic objects only. Prefer primitives for simple shapes.

### manage_scene
Scene operations.
```json
{"tool": "manage_scene", "params": {"action": "save"}}
```

## Coordinate Rules
- Y is up, X is right, Z is forward. Ground is at Y=0.
- Place ground-level objects with base at Y=0 (adjust Y by half the scale height).
- Spread objects out: don't cluster at origin.
- Objects that interact frequently: within 5-10 units.
- "surrounds" relations → radial placement (circle/semicircle).
- "near"/"next to" → within 2-3 units.
- "central" → near origin.
- "scattered"/"distributed" → spread across radius 5-15 units.
- Scale reasonably: tree ~3-5 units tall, flower ~0.5-1 unit, building ~5-10 units.

## Material/Color Rules
- Parse appearance description for color cues.
- Colors as [R, G, B, A] with values 0.0-1.0.
- Organic objects: metallic=0.0, smoothness=0.3.
- Mechanical objects: metallic=0.5-0.8, smoothness=0.7.

## Lighting Rules
- Always include one Directional light (the sun) as a manage_gameobject create (Unity creates it with Light component).
- Sunny: warm white [1, 0.95, 0.9], rotation [50, -30, 0].
- Sunset: orange [1, 0.7, 0.4], rotation [15, -30, 0].
- Night: cool blue [0.5, 0.6, 0.8], rotation [50, -30, 0], intensity 0.3.

## Output Format
Return ONLY valid JSON matching this schema:
{
    "environment_calls": [...],   // terrain, skybox setup
    "primitive_calls": [...],     // all primitive GameObjects
    "trellis_calls": [...],       // all manage_3dgen calls
    "material_calls": [...],      // colors applied to objects
    "component_calls": [...],     // Rigidbody, Collider, etc.
    "vfx_calls": [...],           // particle systems
    "hierarchy_calls": [...]      // parenting, final adjustments
}
Each call is: {"tool": "tool_name", "params": {...}, "description": "what this does"}
No markdown, no explanation outside the JSON."""


async def plan_scene(spec: SceneSpec) -> MCPCallPlan:
    """Convert a SceneSpec into a list of MCP tool calls."""
    client = anthropic.AsyncAnthropic()
    
    user_prompt = f"""Create MCP tool calls for this educational analogy scene.

TARGET CONCEPT: {spec.target_concept}
ANALOGY DOMAIN: {spec.analogy_domain}
LEARNING GOAL: {spec.learning_goal}
TASK: {spec.task_label}

MAPPING TABLE (structural component → analogy representation):
{_format_object_table(spec.mappings)}

ENVIRONMENT:
- Setting: {spec.environment.setting}
- Terrain: {spec.environment.terrain}
- Skybox: {spec.environment.skybox}
- Description: {spec.environment.description}

RULES FOR STRUCTURAL COMPONENTS:
- USER → Place at spawn point. This is the player avatar.
- CONTENT_ITEM → Distribute across scene. Create multiple instances if description says "multiple".
- USER_PROFILE → Place centrally or attach to user. Must be visible.
- USER_INTERACTION → Express as VFX/particles between user and content. Not a static object.
- PROFILE_UPDATE → Animation/mechanic on the user_profile object. Often not a separate object.
- CANDIDATE_GENERATION → Spatial boundary or range indicator. Semi-transparent.
- RANKING → Affects content_item appearance. Often expressed through growth/scaling.
- FEEDBACK_LOOP → Not a single object. Describe as a comment, no MCP calls needed.

For MECHANIC and FEEDBACK_LOOP types: add a comment in the description field explaining what scripts/logic would be needed, but don't create MCP calls for them (they require custom C# scripts).

Generate the MCP tool calls JSON."""

    response = await client.messages.create(
        model="claude-sonnet-4-20250514",
        max_tokens=8192,
        system=SYSTEM_PROMPT,
        messages=[{"role": "user", "content": user_prompt}]
    )
    
    plan_json = json.loads(response.content[0].text)
    return MCPCallPlan(
        environment_calls=[MCPToolCall(**c) for c in plan_json.get("environment_calls", [])],
        primitive_calls=[MCPToolCall(**c) for c in plan_json.get("primitive_calls", [])],
        trellis_calls=[MCPToolCall(**c) for c in plan_json.get("trellis_calls", [])],
        material_calls=[MCPToolCall(**c) for c in plan_json.get("material_calls", [])],
        component_calls=[MCPToolCall(**c) for c in plan_json.get("component_calls", [])],
        vfx_calls=[MCPToolCall(**c) for c in plan_json.get("vfx_calls", [])],
        hierarchy_calls=[MCPToolCall(**c) for c in plan_json.get("hierarchy_calls", [])],
    )


def _format_object_table(mappings: list) -> str:
    lines = []
    for m in mappings:
        lines.append(f"[{m.structural_component.value}] {m.analogy_name}")
        lines.append(f"  Description: {m.analogy_description}")
        lines.append(f"  Strategy: {m.asset_strategy.value}")
        if m.primitive_type:
            lines.append(f"  Primitive: {m.primitive_type}")
        if m.trellis_prompt:
            lines.append(f"  Trellis prompt: {m.trellis_prompt}")
        if m.appearance_hint:
            lines.append(f"  Appearance: {m.appearance_hint}")
        if m.spatial_hint:
            lines.append(f"  Spatial: {m.spatial_hint}")
        if m.involves_objects:
            lines.append(f"  Involves: {', '.join(m.involves_objects)}")
        lines.append("")
    return "\n".join(lines)
```

---

## Part 3: Plan Validator (`validator.py`)

**The validator runs BEFORE execution.** It checks the generated MCP call plan for completeness and fills in defaults for anything missing.

```python
from .models import MCPCallPlan, MCPToolCall, SceneSpec

class PlanValidator:
    """Validate and repair an MCPCallPlan before execution."""
    
    def validate_and_repair(self, plan: MCPCallPlan, spec: SceneSpec) -> tuple[MCPCallPlan, list[str]]:
        """
        Check the plan for issues and auto-repair where possible.
        Returns (repaired_plan, list_of_warnings).
        """
        warnings = []
        
        # 1. Check every ASSET-producing mapping has at least one create call
        #    (mechanic and feedback_loop types don't produce objects)
        ASSET_STRATEGIES = {"primitive", "trellis", "vfx", "ui"}
        planned_names = self._extract_created_names(plan)
        for mapping in spec.mappings:
            if mapping.asset_strategy.value in ASSET_STRATEGIES:
                if mapping.analogy_name not in planned_names:
                    warnings.append(
                        f"MISSING: [{mapping.structural_component.value}] '{mapping.analogy_name}' "
                        f"has no create call. Adding default placeholder."
                    )
                    plan = self._add_default_from_mapping(plan, mapping)
        
        # 2. Check every primitive has a material call (default: gray)
        colored_targets = self._extract_material_targets(plan)
        for call in plan.primitive_calls:
            obj_name = call.params.get("name", "")
            if obj_name and obj_name not in colored_targets:
                warnings.append(f"MISSING MATERIAL: '{obj_name}' has no color. Adding default gray.")
                plan.material_calls.append(MCPToolCall(
                    tool="manage_material",
                    params={"action": "set_renderer_color", "target": obj_name, "color": [0.6, 0.6, 0.6, 1.0]},
                    description=f"Default gray material for {obj_name}"
                ))
        
        # 3. Check terrain exists
        has_terrain = any(
            c.params.get("primitive_type") == "Plane"
            for c in plan.environment_calls + plan.primitive_calls
        )
        if not has_terrain:
            warnings.append("MISSING TERRAIN: No ground plane found. Adding default.")
            plan.environment_calls.insert(0, MCPToolCall(
                tool="manage_gameobject",
                params={
                    "action": "create", "name": "Ground",
                    "primitive_type": "Plane",
                    "position": [0, 0, 0], "scale": [3, 1, 3],
                },
                description="Default ground plane"
            ))
        
        # 4. Check lighting exists
        has_light = any(
            "light" in c.params.get("name", "").lower() or "light" in c.description.lower()
            for c in plan.environment_calls
        )
        if not has_light:
            warnings.append("MISSING LIGHT: No directional light. Adding default sun.")
            plan.environment_calls.append(MCPToolCall(
                tool="manage_gameobject",
                params={
                    "action": "create", "name": "Sun Light",
                    "position": [0, 10, 0], "rotation": [50, -30, 0],
                },
                description="Default directional light"
            ))
        
        # 5. Check no duplicate names
        name_counts = {}
        for call in plan.primitive_calls + plan.trellis_calls + plan.environment_calls:
            name = call.params.get("name", "")
            if name:
                name_counts[name] = name_counts.get(name, 0) + 1
        for name, count in name_counts.items():
            if count > 1:
                warnings.append(f"DUPLICATE: '{name}' created {count} times. Suffixing duplicates.")
                seen = 0
                for phase in [plan.environment_calls, plan.primitive_calls, plan.trellis_calls]:
                    for call in phase:
                        if call.params.get("name") == name:
                            seen += 1
                            if seen > 1:
                                call.params["name"] = f"{name}_{seen}"
        
        # 6. Validate MCP tool names
        VALID_TOOLS = {
            "manage_gameobject", "manage_material", "manage_components",
            "manage_vfx", "manage_scene", "manage_asset", "manage_animation",
            "manage_shader", "manage_script", "manage_editor", "manage_3dgen",
            "batch_execute", "find_gameobjects", "manage_prefabs",
            "manage_texture", "manage_scriptable_object",
        }
        for phase_calls in plan.all_calls_ordered():
            for call in phase_calls:
                if call.tool not in VALID_TOOLS:
                    warnings.append(f"INVALID TOOL: '{call.tool}' is not a known MCP tool.")
        
        # 7. Validate trellis calls have prompts
        for call in plan.trellis_calls:
            if not call.params.get("prompt"):
                warnings.append(f"TRELLIS MISSING PROMPT: {call.params.get('name', '?')}. Using name as prompt.")
                call.params["prompt"] = call.params.get("name", "3d object")
        
        # 8. Check USER component exists (required for VR scenes)
        user_mappings = [m for m in spec.mappings if m.structural_component.value == "user"]
        if user_mappings:
            user_name = user_mappings[0].analogy_name
            if user_name not in planned_names:
                warnings.append(f"MISSING USER: '{user_name}' not in plan. Scene needs a player spawn.")
        
        # 9. Check CONTENT_ITEM count — if description says "multiple", ensure >1 instance
        for mapping in spec.mappings:
            if mapping.structural_component.value == "content_item":
                desc_lower = mapping.analogy_description.lower()
                if any(w in desc_lower for w in ["multiple", "varied", "several", "various", "many"]):
                    instances = sum(1 for c in plan.primitive_calls + plan.trellis_calls
                                    if mapping.analogy_name.lower() in c.params.get("name", "").lower())
                    if instances < 3:
                        warnings.append(
                            f"FEW CONTENT_ITEMS: '{mapping.analogy_name}' description says multiple "
                            f"but only {instances} instance(s) found. Consider adding more."
                        )
        
        return plan, warnings
    
    def _extract_created_names(self, plan: MCPCallPlan) -> set[str]:
        names = set()
        for phase_calls in plan.all_calls_ordered():
            for call in phase_calls:
                if call.params.get("action") == "create" and call.params.get("name"):
                    names.add(call.params["name"])
        return names
    
    def _extract_material_targets(self, plan: MCPCallPlan) -> set[str]:
        targets = set()
        for call in plan.material_calls:
            if call.params.get("target"):
                targets.add(call.params["target"])
        return targets
    
    def _add_default_from_mapping(self, plan: MCPCallPlan, mapping) -> MCPCallPlan:
        """Add a default placeholder based on the mapping's asset strategy."""
        if mapping.asset_strategy.value == "primitive":
            plan.primitive_calls.append(MCPToolCall(
                tool="manage_gameobject",
                params={
                    "action": "create",
                    "name": mapping.analogy_name,
                    "primitive_type": mapping.primitive_type or "Cube",
                    "position": [0, 0.5, 0],
                    "scale": [1, 1, 1],
                },
                description=f"Default placeholder for [{mapping.structural_component.value}] {mapping.analogy_name}"
            ))
        elif mapping.asset_strategy.value == "trellis":
            plan.trellis_calls.append(MCPToolCall(
                tool="manage_3dgen",
                params={
                    "action": "generate",
                    "prompt": mapping.trellis_prompt or f"{mapping.analogy_name}, game asset, white background",
                    "name": mapping.analogy_name,
                    "position": [0, 0.5, 0],
                },
                description=f"Default trellis generation for [{mapping.structural_component.value}] {mapping.analogy_name}"
            ))
        elif mapping.asset_strategy.value == "vfx":
            plan.vfx_calls.append(MCPToolCall(
                tool="manage_vfx",
                params={
                    "action": "particle_create",
                    "target": mapping.involves_objects[0] if mapping.involves_objects else mapping.analogy_name,
                    "properties": {"startColor": [1, 1, 0, 1], "startSize": 0.1, "emissionRate": 10},
                },
                description=f"Default VFX for [{mapping.structural_component.value}] {mapping.analogy_name}"
            ))
        return plan
```

---

## Part 4: Executor (`executor.py`)

Executes the validated MCP call plan against Unity-MCP. Groups calls into `batch_execute` for performance.

### IMPORTANT: How to Call MCP Tools

**You are running inside a context where Unity-MCP tools are available as MCP tool calls.** The exact invocation depends on how you're connected:

**Option A — If running as an MCP client (recommended):**
The MCP server is already running. Use the `mcp` Python library to call tools:
```python
# This is pseudo-code — adapt to your MCP client setup
result = await mcp_client.call_tool("manage_gameobject", {
    "action": "create",
    "name": "RedBall",
    "primitive_type": "Sphere",
    "position": [0, 1, 0],
})
```

**Option B — If calling via HTTP (JSON-RPC):**
```python
async def call_mcp_tool(tool_name: str, params: dict) -> dict:
    payload = {
        "jsonrpc": "2.0",
        "id": str(uuid.uuid4()),
        "method": "tools/call",
        "params": {"name": tool_name, "arguments": params}
    }
    response = await httpx.AsyncClient().post("http://localhost:8080/mcp", json=payload)
    return response.json()
```

**Option C — batch_execute (preferred for multiple calls):**
```python
await call_mcp_tool("batch_execute", {
    "commands": [
        {"tool": "manage_gameobject", "params": {"action": "create", "name": "Obj1", ...}},
        {"tool": "manage_gameobject", "params": {"action": "create", "name": "Obj2", ...}},
    ],
    "parallel": True
})
```

### Executor Implementation

```python
import asyncio
from .models import MCPCallPlan, MCPToolCall

class SceneExecutor:
    """Execute an MCPCallPlan against Unity-MCP."""
    
    async def execute(self, plan: MCPCallPlan) -> dict:
        """Execute the plan phase by phase."""
        results = {}
        
        for phase_idx, phase_calls in enumerate(plan.all_calls_ordered()):
            if not phase_calls:
                continue
            
            phase_name = ["environment", "objects", "materials", "components+vfx", "hierarchy"][phase_idx]
            print(f"Phase {phase_idx + 1}: {phase_name} ({len(phase_calls)} calls)")
            
            # Split trellis calls from non-trellis (trellis is async/slow)
            trellis = [c for c in phase_calls if c.tool == "manage_3dgen"]
            non_trellis = [c for c in phase_calls if c.tool != "manage_3dgen"]
            
            # Execute non-trellis calls as batch
            if non_trellis:
                batch_result = await self._batch_execute(non_trellis)
                results[f"phase_{phase_idx}_batch"] = batch_result
            
            # Execute trellis calls (they may be async — handle wait/poll)
            if trellis:
                trellis_results = await asyncio.gather(
                    *[self._execute_single(c) for c in trellis],
                    return_exceptions=True
                )
                results[f"phase_{phase_idx}_trellis"] = trellis_results
        
        return results
    
    async def _batch_execute(self, calls: list[MCPToolCall]) -> dict:
        """Send multiple calls as one batch_execute."""
        commands = [{"tool": c.tool, "params": c.params} for c in calls]
        return await self._call_mcp("batch_execute", {
            "commands": commands,
            "parallel": True,
        })
    
    async def _execute_single(self, call: MCPToolCall) -> dict:
        """Execute a single MCP tool call."""
        return await self._call_mcp(call.tool, call.params)
    
    async def _call_mcp(self, tool_name: str, params: dict) -> dict:
        """
        Call an MCP tool. 
        
        IMPORTANT: Adapt this to your actual MCP client connection method.
        The implementation depends on whether you're using:
        - mcp Python SDK
        - HTTP/JSON-RPC
        - Direct subprocess communication
        
        Before implementing, VERIFY with the Unity project:
        1. How does the MCP server accept connections? (stdio? HTTP? WebSocket?)
        2. What port is it running on?
        3. Does batch_execute work with parallel=True?
        """
        raise NotImplementedError(
            "Implement this based on your MCP client connection method. "
            "Check the Unity-MCP server configuration for connection details."
        )
```

---

## Part 5: Trellis Bridge (`trellis_bridge.py`)

This bridges the scene generator to the `manage_3dgen` MCP tool. **Do NOT reimplement Trellis generation** — the existing `Manage3DGen.cs` and the Trellis2Client.cs in the Unity project handle the actual generation. Your job is to:

1. **Read the existing `Manage3DGen.cs`** to understand what parameters it expects
2. **Read the existing Python tool definition** (if any) in the MCP server's tool registry
3. **Create any missing files**: `.meta` files, Python tool wrappers, registration code
4. **Verify the tool is callable** via MCP by testing `manage_3dgen` with a simple prompt

```python
class TrellisBridge:
    """
    Bridge to the manage_3dgen MCP tool.
    
    FIRST TASK: Inspect the existing code:
    
    1. Read Packages/com.coplaydev.unity-mcp/Editor/Tools/Manage3DGen.cs
       - What actions does it support? (generate, status, cancel?)
       - What params does it expect? (prompt, name, position, scale?)
       - Does it handle async generation with polling?
       - Does it auto-import the GLB and instantiate?
    
    2. Read the corresponding Python tool definition in the MCP server
       - Is there a manage_3dgen.py or similar?
       - Is it registered in the tool registry?
       - If missing, create it following the pattern of other tools
    
    3. Check for .meta files
       - Does Manage3DGen.cs have a .meta file? If not, Unity won't load it.
       - Create .meta files following the pattern of other .cs files in the same directory
    
    4. Read Assets/trellis.2/Trellis2Client.cs
       - How does it communicate with the Trellis server?
       - What's the server URL/endpoint?
       - Does it support the API we need?
    
    AFTER INSPECTION: Update this class with the actual tool params.
    """
    
    async def generate_asset(self, prompt: str, name: str, 
                              position: list[float] = [0, 0, 0],
                              scale: list[float] = [1, 1, 1]) -> dict:
        """Generate a 3D asset via the manage_3dgen MCP tool."""
        # The actual params depend on what Manage3DGen.cs expects
        # This is a TEMPLATE — update after reading the source
        return {
            "tool": "manage_3dgen",
            "params": {
                "action": "generate",
                "prompt": prompt,
                "name": name,
                "position": position,
                "scale": scale,
            }
        }
    
    async def check_status(self, job_id: str) -> dict:
        """Check generation status (if manage_3dgen supports async polling)."""
        return {
            "tool": "manage_3dgen",
            "params": {
                "action": "status",
                "job_id": job_id,
            }
        }
```

### Task: Complete the manage_3dgen Integration

## Part 6: CLI (`cli.py`)

```python
import asyncio
import json
from .models import SceneSpec
from .planner import plan_scene
from .validator import PlanValidator
from .executor import SceneExecutor

async def main(spec_path: str):
    # Load SceneSpec
    with open(spec_path) as f:
        spec = SceneSpec.model_validate_json(f.read())
    
    # Phase 1: Plan
    print("=== Planning ===")
    plan = await plan_scene(spec)
    total_calls = sum(len(phase) for phase in plan.all_calls_ordered())
    print(f"Generated {total_calls} MCP tool calls")
    print(f"  Environment: {len(plan.environment_calls)}")
    print(f"  Primitives: {len(plan.primitive_calls)}")
    print(f"  Trellis: {len(plan.trellis_calls)}")
    print(f"  Materials: {len(plan.material_calls)}")
    print(f"  Components: {len(plan.component_calls)}")
    print(f"  VFX: {len(plan.vfx_calls)}")
    
    # Phase 2: Validate & Repair
    print("\n=== Validating ===")
    validator = PlanValidator()
    plan, warnings = validator.validate_and_repair(plan, spec)
    if warnings:
        for w in warnings:
            print(f"  ⚠️  {w}")
    else:
        print("  ✅ Plan is complete")
    
    # Save plan for inspection
    with open("output/mcp_call_plan.json", "w") as f:
        plan_data = {
            key: [{"tool": c.tool, "params": c.params, "description": c.description}
                  for c in getattr(plan, key)]
            for key in ["environment_calls", "primitive_calls", "trellis_calls",
                        "material_calls", "component_calls", "vfx_calls", "hierarchy_calls"]
        }
        json.dump(plan_data, f, indent=2)
    print("  Saved plan to output/mcp_call_plan.json")
    
    # Phase 3: Execute
    print("\n=== Executing ===")
    executor = SceneExecutor()
    result = await executor.execute(plan)
    print(f"Execution complete: {result}")
    
    # Phase 4: Post-execution verification
    print("\n=== Verifying ===")
    # Call manage_scene get_hierarchy to confirm objects were created
    # Compare against the plan
    # Report any missing objects

if __name__ == "__main__":
    import sys
    spec_path = sys.argv[1] if len(sys.argv) > 1 else "test_specs/bee_garden.json"
    asyncio.run(main(spec_path))
```

---

## Part 7: Test Fixtures

### `test_specs/simple_demo.json` — Primitives Only (fast, no Trellis)

```json
{
  "target_concept": "Simple Test",
  "analogy_domain": "Shapes",
  "learning_goal": "Test scene with basic shapes",
  "task_label": "Test: Simple Primitives",
  "environment": {
    "setting": "flat",
    "terrain": "grass_plane",
    "skybox": "sunny",
    "ambient_color": [0.8, 0.9, 0.7],
    "description": "Simple test environment"
  },
  "mappings": [
    {
      "structural_component": "user",
      "analogy_name": "Player",
      "analogy_description": "A simple player represented by a capsule at the spawn point.",
      "asset_strategy": "primitive",
      "primitive_type": "Capsule",
      "spatial_hint": "center of scene"
    },
    {
      "structural_component": "content_item",
      "analogy_name": "Red Cube",
      "analogy_description": "A red cube representing content item A.",
      "asset_strategy": "primitive",
      "primitive_type": "Cube",
      "appearance_hint": "red",
      "spatial_hint": "3 units to the right of player"
    },
    {
      "structural_component": "content_item",
      "analogy_name": "Blue Sphere",
      "analogy_description": "A blue sphere representing content item B.",
      "asset_strategy": "primitive",
      "primitive_type": "Sphere",
      "appearance_hint": "blue",
      "spatial_hint": "3 units to the left of player"
    }
  ]
}
```

### `test_specs/bee_garden.json` — Beehive Analogy (Task 1 from the real table)

This maps directly from the Comparative Framework table's "Beehive Analogy" column:

```json
{
  "target_concept": "AI Content Recommendation System",
  "analogy_domain": "Bee Pollination",
  "learning_goal": "Teach middle school students how recommendation algorithms create filter bubbles through embodied bee pollination",
  "task_label": "Task 1: Beehive Analogy",
  "environment": {
    "setting": "garden",
    "terrain": "grass_plane",
    "skybox": "sunny",
    "ambient_color": [0.8, 0.9, 0.7],
    "description": "A sunny garden with flowers distributed around a central beehive"
  },
  "mappings": [
    {
      "structural_component": "user",
      "analogy_name": "Bee",
      "analogy_description": "The user embodies a bee, navigating the garden with first-person flight controls.",
      "asset_strategy": "trellis",
      "trellis_prompt": "cute cartoon bee character, yellow black stripes, translucent wings, game asset, white background",
      "spatial_hint": "starts near beehive at center"
    },
    {
      "structural_component": "content_item",
      "analogy_name": "Flower",
      "analogy_description": "3D models of flowers with varying attributes (color, petal shape, size). Multiple instances scattered around the garden.",
      "asset_strategy": "trellis",
      "trellis_prompt": "stylized colorful garden flower, game asset, white background",
      "appearance_hint": "varied colors: red, blue, yellow, purple",
      "spatial_hint": "distributed in a rough circle around the beehive, radius 5-10 units"
    },
    {
      "structural_component": "user_profile",
      "analogy_name": "Beehive",
      "analogy_description": "A central 3D model of a beehive that physically moves within the garden space. Makes the user profile tangible and observable.",
      "asset_strategy": "trellis",
      "trellis_prompt": "stylized cartoon wooden beehive with hexagonal honeycomb pattern, game asset, white background",
      "spatial_hint": "central position at origin, slightly elevated"
    },
    {
      "structural_component": "user_interaction",
      "analogy_name": "Pollination",
      "analogy_description": "The user aims at a specific flower and presses a controller button, triggering a visual/audio effect (pollen particles transfer from flower to bee).",
      "asset_strategy": "vfx",
      "appearance_hint": "yellow glowing pollen particles",
      "involves_objects": ["Bee", "Flower"]
    },
    {
      "structural_component": "profile_update",
      "analogy_name": "Beehive Movement",
      "analogy_description": "The beehive's position visibly drifts toward the location of pollinated flowers, making the profile update a spatial change.",
      "asset_strategy": "mechanic",
      "involves_objects": ["Beehive", "Flower"]
    },
    {
      "structural_component": "candidate_generation",
      "analogy_name": "Pollen Circle",
      "analogy_description": "A visible, circular boundary on the ground centered on the beehive, defining which flowers are close enough to be considered.",
      "asset_strategy": "vfx",
      "appearance_hint": "semi-transparent yellow circle on ground",
      "spatial_hint": "centered on beehive, radius ~8 units",
      "involves_objects": ["Beehive"]
    },
    {
      "structural_component": "ranking",
      "analogy_name": "Bud Growth",
      "analogy_description": "Flower buds closest to the beehive grow into full flowers first, representing ranking through physical proximity.",
      "asset_strategy": "mechanic",
      "involves_objects": ["Flower", "Beehive"]
    },
    {
      "structural_component": "feedback_loop",
      "analogy_name": "Garden Dynamics",
      "analogy_description": "Pollinating flowers moves the beehive, which causes similar flowers to grow nearby, encouraging further similar pollination. This creates a self-reinforcing filter bubble.",
      "asset_strategy": "mechanic",
      "involves_objects": ["Bee", "Flower", "Beehive", "Pollination", "Beehive Movement", "Bud Growth"]
    }
  ]
}
```

### `test_specs/sprinkler_garden.json` — Sprinkler Analogy (Task 2 from the real table)

This maps the "Redesigned Sprinkler Analogy" column:

```json
{
  "target_concept": "AI Content Recommendation System",
  "analogy_domain": "Garden Watering",
  "learning_goal": "Teach recommendation algorithms through garden watering metaphor with attribute-based (color) similarity rather than spatial proximity",
  "task_label": "Task 2: Sprinkler Analogy",
  "environment": {
    "setting": "garden",
    "terrain": "grass_plane",
    "skybox": "sunny",
    "ambient_color": [0.7, 0.9, 0.8],
    "description": "A futuristic garden with stylized data plants and a watering system"
  },
  "mappings": [
    {
      "structural_component": "user",
      "analogy_name": "Gardener",
      "analogy_description": "The user embodies a gardener, equipped with a handheld watering tool and a backpack tank, navigating the garden.",
      "asset_strategy": "trellis",
      "trellis_prompt": "cartoon gardener character with watering tool and backpack tank, game asset, white background",
      "spatial_hint": "starts at garden entrance"
    },
    {
      "structural_component": "content_item",
      "analogy_name": "Data Plant",
      "analogy_description": "Stylized, futuristic plant models that progress through life stages (seed, sprout, bloom, wilt). Multiple instances with varying colors.",
      "asset_strategy": "trellis",
      "trellis_prompt": "stylized futuristic glowing plant, bioluminescent, game asset, white background",
      "appearance_hint": "futuristic, glowing, varied colors",
      "spatial_hint": "distributed across garden"
    },
    {
      "structural_component": "user_profile",
      "analogy_name": "Profile Gauge",
      "analogy_description": "A gauge on the user's wrist with a visible fluid level and color. The fluid's color changes based on the plants watered.",
      "asset_strategy": "ui",
      "appearance_hint": "wrist-mounted gauge with colored fluid",
      "spatial_hint": "attached to user's wrist (UI overlay)"
    },
    {
      "structural_component": "user_interaction",
      "analogy_name": "Targeted Watering",
      "analogy_description": "A discrete, targeted action where the user aims the sprinkler and fires a focused water stream at a specific plant.",
      "asset_strategy": "vfx",
      "appearance_hint": "blue water stream particles",
      "involves_objects": ["Gardener", "Data Plant"]
    },
    {
      "structural_component": "profile_update",
      "analogy_name": "Tank Color Change",
      "analogy_description": "The fluid in the Profile Tank changes color to a weighted average of the colors of the watered plants, providing immediate visual feedback.",
      "asset_strategy": "mechanic",
      "involves_objects": ["Profile Gauge", "Data Plant"]
    },
    {
      "structural_component": "candidate_generation",
      "analogy_name": "Water Range",
      "analogy_description": "The water range stream has a maximum effective distance. Only plants within this range can be interacted with.",
      "asset_strategy": "mechanic",
      "involves_objects": ["Gardener", "Data Plant"]
    },
    {
      "structural_component": "ranking",
      "analogy_name": "Proximity Growth",
      "analogy_description": "Plants with a color attribute most similar to the Profile Tank's fluid color grow faster, representing ranking through attribute similarity.",
      "asset_strategy": "mechanic",
      "involves_objects": ["Data Plant", "Profile Gauge"]
    },
    {
      "structural_component": "feedback_loop",
      "analogy_name": "Garden Cultivation",
      "analogy_description": "Watering plants of a certain color changes the tank's color, which in turn accelerates the growth of other plants of that same color, encouraging further specialized watering.",
      "asset_strategy": "mechanic",
      "involves_objects": ["Gardener", "Data Plant", "Profile Gauge", "Targeted Watering", "Tank Color Change", "Proximity Growth"]
    }
  ]
}
```

---

## Critical Instructions for Claude Code

### Before Writing Any Code

1. **VALIDATE THE MCP CONNECTION**: Before implementing the executor, check how the Unity-MCP server accepts connections. Run `manage_scene` with `action=get_active` to confirm the connection works.

2. **READ THE EXISTING CODE**: Before touching manage_3dgen:
   - Read `Packages/com.coplaydev.unity-mcp/Editor/Tools/Manage3DGen.cs` 
   - Read nearby files to understand the tool registration pattern (e.g., `ManageGameObject.cs`, `ManageAsset.cs`)
   - Read the Python MCP server to find where tools are registered
   - Read `Assets/Editor/TrellisPlugin` to understand the Trellis server communication

3. **CHECK WHAT'S ACTUALLY IMPLEMENTED**: The MCP tools listed above are the standard ones from CoplayDev/unity-mcp. But `manage_3dgen` is a custom addition. Verify it actually works by attempting a test call. If it fails, you need to fix the registration/wiring.

### Implementation Order

1. `models.py` — straightforward, no dependencies
2. `prompts.py` — extract the system prompt, test with a dry run
3. `planner.py` — test that it generates valid MCP call JSON
4. `validator.py` — test against the simple_demo spec
5. `trellis_bridge.py` — inspect existing code FIRST, then build
6. `executor.py` — needs working MCP connection
7. `cli.py` — integration test

### Error Handling Strategy

- If `manage_3dgen` is not callable → fall back to creating a colored primitive placeholder and log a warning
- If `batch_execute` fails partially → retry individual failed commands one at a time
- If the planner LLM returns invalid JSON → retry with stricter prompt (max 2 retries)
- If a material/component call fails because the target object doesn't exist yet → reorder and retry
- **Never crash the pipeline** — produce the best scene possible and report what failed

### Performance Expectations

- Planning phase: 2-3 seconds (single LLM call)
- Primitives + environment + lighting via batch_execute: 1-2 seconds
- Per Trellis asset via manage_3dgen: 3-35 seconds (depends on server)
- Total (primitives only): ~3-5 seconds
- Total (with 3 Trellis assets): ~20-40 seconds

### What NOT To Build

- Do NOT build a custom Trellis server — the Unity project already has one
- Do NOT build Z3 constraint solving — LLM coordinates are sufficient
- Do NOT build VR gesture handling — text-only for now
- Do NOT modify the core Unity-MCP tools (manage_gameobject etc.) — treat them as stable
- Do NOT build post-generation iteration — that's handled by Unity-MCP + user text commands directly
- Do NOT build any intermediate JSON format between the plan and execution — the plan IS MCP calls