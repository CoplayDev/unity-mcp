# manage_physics — Complete Feature Patch

A new `manage_physics` MCP tool that gives AI assistants full control over Unity's 3D and 2D physics systems. **21 actions** across 9 categories, with C# Editor implementation, Python MCP service, CLI commands, and tests.

## Actions by Category

### Settings
| Action | Description |
|--------|-------------|
| `ping` | Health check — returns gravity, solver settings, simulation mode |
| `get_settings` | Read physics project settings (3D or 2D) |
| `set_settings` | Write physics project settings (gravity, solver iterations, thresholds, etc.) |

### Collision Matrix
| Action | Description |
|--------|-------------|
| `get_collision_matrix` | Read per-layer collision matrix |
| `set_collision_matrix` | Enable/disable collision between layer pairs |

### Materials
| Action | Description |
|--------|-------------|
| `create_physics_material` | Create PhysicMaterial (3D) or PhysicsMaterial2D assets with friction/bounciness/combine modes |
| `configure_physics_material` | Update properties on an existing physics material asset |
| `assign_physics_material` | Assign a physics material to a GameObject's collider |

### Joints
| Action | Description |
|--------|-------------|
| `add_joint` | Add a joint (hinge, spring, fixed, configurable, etc.) with optional connected body |
| `configure_joint` | Configure motor, limits, spring, drive, and direct properties on an existing joint |
| `remove_joint` | Remove joint(s) from a GameObject by type or all |

### Queries
| Action | Description |
|--------|-------------|
| `raycast` | Single-hit raycast from origin along direction |
| `raycast_all` | Multi-hit raycast returning all intersections |
| `linecast` | Check if anything intersects the line between two points |
| `shapecast` | Cast a shape (sphere, box, capsule) along a direction |
| `overlap` | Find all colliders within a shape (sphere, box, capsule) at a position |

### Forces
| Action | Description |
|--------|-------------|
| `apply_force` | Apply force, torque (or both), force-at-position, or explosion force to Rigidbodies. Supports all ForceModes for 3D and 2D |

### Rigidbody
| Action | Description |
|--------|-------------|
| `get_rigidbody` | Read full Rigidbody state: mass, velocity, position, rotation, damping, constraints, sleep state, centerOfMass |
| `configure_rigidbody` | Set Rigidbody properties (mass, damping, gravity, kinematic, interpolation, collision detection, constraints) |

### Validation
| Action | Description |
|--------|-------------|
| `validate` | Scan scene (or a single target) for physics issues. Paginated results (`page_size`/`cursor`/`next_cursor`) with per-category summary. 7 check categories: non-convex mesh, missing rigidbody, non-uniform scale, fast object with discrete detection, missing physics material, collision matrix, mixed 2D/3D. Smart warning levels — static colliders without Rigidbodies are downgraded to `[Info]` unless the object has an Animator |

### Simulation
| Action | Description |
|--------|-------------|
| `simulate_step` | Step physics simulation in Edit mode (1–100 steps). Returns positions, velocities, and angular velocities of active Rigidbodies after stepping. Optional `target` to filter to a specific object |

## Architecture

### C# — 10 files in `MCPForUnity/Editor/Tools/Physics/`

| File | Purpose |
|------|---------|
| `ManagePhysics.cs` | Action dispatcher with `[McpForUnityTool]` registration |
| `PhysicsSettingsOps.cs` | ping, get_settings, set_settings |
| `CollisionMatrixOps.cs` | get_collision_matrix, set_collision_matrix |
| `PhysicsMaterialOps.cs` | create, configure, assign physics materials |
| `JointOps.cs` | add_joint, configure_joint, remove_joint |
| `PhysicsQueryOps.cs` | raycast, raycast_all, linecast, shapecast, overlap |
| `PhysicsForceOps.cs` | apply_force (normal, explosion, torque) |
| `PhysicsRigidbodyOps.cs` | get_rigidbody, configure_rigidbody |
| `PhysicsValidationOps.cs` | validate with pagination and smart warnings |
| `PhysicsSimulationOps.cs` | simulate_step with state reporting |

### Python — 3 files

| File | Purpose |
|------|---------|
| `Server/src/services/tools/manage_physics.py` | MCP tool definition with 21-action Literal type, forwards all params to Unity |
| `Server/src/cli/commands/physics.py` | Full CLI with commands for every action category |
| `Server/tests/test_manage_physics.py` | 19 unit tests covering action forwarding and validation |

### Unity Tests

| File | Purpose |
|------|---------|
| `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ManagePhysicsTests.cs` | 973-line EditMode test suite |

## Key Design Decisions

- **Modular ops classes**: Each physics domain (settings, materials, joints, queries, forces, etc.) gets its own C# class rather than one monolithic handler
- **Auto-dimension detection**: All actions auto-detect 2D vs 3D based on Rigidbody/Rigidbody2D component presence, with optional `dimension` override
- **Unity version compatibility**: Uses `#if UNITY_6000_0_OR_NEWER` for renamed APIs (drag → linearDamping, angularDrag → angularDamping)
- **Pagination on validate**: Large scenes can produce hundreds of warnings — paginated by default (50 per page) with a summary that always shows category counts regardless of page
- **Smart warning levels**: "Collider without Rigidbody" only escalates to a warning if the object has an Animator (suggesting runtime movement); otherwise downgraded to `[Info]`
- **Enriched responses**: Force/explosion actions echo back all applied values. Simulation returns Rigidbody states. Validation returns category breakdowns

## Stats

- **Net change**: +5,985 / −2,965 lines
- **New C# files**: 10 (+ 10 .meta)
- **New Python files**: 3
- **Actions**: 21
- **Tests**: 19 Python + EditMode C# suite
