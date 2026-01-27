# Unity-MCP Repository Overview

**Purpose**: Dual-language MCP (Model Context Protocol) bridge enabling AI assistants (Claude, Cursor, etc.) to control Unity Editor automation.

**Lead Maintainer**: David Sarno

**Languages**: Python (Server), C# (Unity Editor Client), Shell (Build scripts)

---

## Directory Structure

```
unity-mcp/
├── MCPForUnity/                    # Unity Editor Package (C#, 163 files)
│   ├── Editor/                     # Editor tools and UI
│   ├── Runtime/                    # Runtime components (minimal)
│   └── UnityMcpServer~/            # Embedded server (submodule)
├── Server/                         # Python MCP Server (94 files)
│   ├── src/                        # Main source code
│   └── tests/                      # Test suite
├── tools/                          # Utility/build scripts (7 files)
├── TestProjects/                   # Sample Unity projects
├── UnityMcpBridge/                 # Legacy bridge code
├── CustomTools/                    # Custom tool templates
├── docs/                           # Documentation
└── manifest.json                   # MCP manifest (v0.3)
```

---

## Architecture

### High-Level Flow
```
Client (AI/IDE)
        ↓ (MCP Protocol)
MCP Server (Python, HTTP/Stdio, Starlette)
        ↓ (HTTP Bridge)
Unity Editor (C# Plugin)
        ↓ (Editor API)
Editor State & Assets
```

### Layers

| Layer | Location | Purpose |
|-------|----------|---------|
| **MCP Transport** | `Server/src/transport/` | Protocol handling, instance routing, plugin discovery |
| **CLI Commands** | `Server/src/cli/commands/` | 20 domain-specific command modules |
| **Services** | `Server/src/services/` | Custom tools, resource handlers |
| **Core Infrastructure** | `Server/src/core/` | Logging, telemetry, configuration |
| **Models** | `Server/src/models/` | Request/response structures |
| **Unity Bridge** | `MCPForUnity/Editor/Services/` | Bridge control, state management |
| **Editor Tools** | `MCPForUnity/Editor/Tools/` | 42 C# implementations (mirrored from CLI) |
| **Helpers** | `MCPForUnity/Editor/Helpers/` | 27 utility modules |
| **UI/Configuration** | `MCPForUnity/Editor/Windows/`, `Clients/` | Editor windows and MCP config |
| **Build/Release** | `tools/` | Version mgmt, stress testing, releases |

---

## Major Components (10 Domains)

### 1. **Transport & Communication**
- **Files**: `Server/src/transport/` (7 files)
- **Purpose**: MCP protocol handling, Unity instance routing, plugin discovery
- **Key Files**: `unity_instance_middleware.py`, `plugin_hub.py`, `http_server.py`

### 2. **CLI Commands (Server-Side Tools)**
- **Files**: `Server/src/cli/commands/` (20 domain modules)
- **Purpose**: Tool implementations for asset, animation, audio, components, scenes, etc.
- **Pattern**: Each domain gets a dedicated module (e.g., `commands/prefabs.py`, `commands/materials.py`)

### 3. **Editor Tools Implementation**
- **Files**: `MCPForUnity/Editor/Tools/` (42 files)
- **Purpose**: C# mirror implementations of Python CLI commands
- **Pattern**: Tools are symmetrical across Python/C# (same domain, same functionality)

### 4. **Unity Editor Integration**
- **Files**: `MCPForUnity/Editor/Services/` (34 files)
- **Purpose**: Bridge control, state caching, communication with server
- **Key Services**: Bridge control, config management, health monitoring

### 5. **Client Configuration & Setup**
- **Files**: `MCPForUnity/Editor/Clients/` (18 files), `Models/` (6 files)
- **Purpose**: MCP client configurators, registry, initial setup
- **Key Files**: `McpClientConfigurator.cs`, `McpClient.cs`, `McpConfig.cs`

### 6. **Core Infrastructure**
- **Files**: `Server/src/core/` (5 files)
- **Purpose**: Telemetry, logging, configuration management (cross-cutting concerns)

### 7. **Helper Utilities**
- **Files**: `MCPForUnity/Editor/Helpers/` (27 files) + `Server/src/utils/` (3 files)
- **Purpose**: Asset path helpers, component operations, configuration builders
- **Pattern**: Reusable functions supporting main tool implementations

### 8. **UI & Windows**
- **Files**: `MCPForUnity/Editor/Windows/` (8 files)
- **Purpose**: Editor windows, preferences, UI components
- **Pattern**: Each window typically handles one configuration or setup area

### 9. **Models & Data Structures**
- **Files**: `Server/src/models/` (3 files) + `MCPForUnity/Editor/Models/` (6 files)
- **Purpose**: Request/response structures, configuration schemas
- **Pattern**: Shared data definitions across Python/C#

### 10. **Build, Release & Testing**
- **Files**: `tools/` (7 files), `Server/tests/`, `MCPForUnity/Editor/Tests/`
- **Purpose**: Version management, stress testing, asset store packaging, test suites

---

## Key Patterns & Principles

1. **Domain-Driven Symmetry**: Each domain (Prefabs, Materials, Scripts, etc.) exists in both Python (CLI) and C# (Editor Tools)
2. **Multi-Instance Support**: Can target multiple Unity Editor instances simultaneously
3. **Extensible Plugin System**: Custom tools can be registered without modifying core
4. **Bidirectional Communication**: Server polls/controls Editor; Editor can push state updates
5. **Cross-Cutting Concerns**: Logging, telemetry, configuration centralized in core infrastructure

---

## Total Stats

| Metric | Count |
|--------|-------|
| Python Source Files | 94 |
| C# Source Files | 163 |
| CLI Command Domains | 20 |
| Editor Tool Modules | 42 |
| Service Modules (Unity) | 34 |
| Helper Modules | 30+ |
| MCP Tools Exposed | 27 |
| Entry Points | 3 (main.py, Editor Windows, Editor Menu) |

---

## Common Improvement Areas (Known/Suspected)

- Repeated patterns across domain-specific tool implementations
- Possible dead code or legacy CLI commands
- Over-engineering in helper utilities
- Bloated configuration or setup workflows
- Asymmetries between Python and C# implementations

---

## For Future Cleanup Passes

Use the 10 domains listed above as your primary analysis units. Each domain is self-contained enough for parallel review and refactoring recommendations.
