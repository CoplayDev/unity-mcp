# ActionTrace Design Philosophy

> Design principles and architectural decisions for the Unity Editor event tracking system

---

## Table of Contents

- [Core Design Principles](#core-design-principles)
- [Architectural Principles](#architectural-principles)
- [Core Concepts](#core-concepts)
- [Key Design Decisions](#key-design-decisions)
- [Data Flow Design](#data-flow-design)
- [Extensibility Design](#extensibility-design)
- [Performance & Memory Management](#performance--memory-management)
- [MCP Integration](#mcp-integration)

---

## Core Design Principles

### 1. Immutable Events

Events, once recorded, are never modified.

```csharp
public readonly struct EditorEvent
{
    public readonly long Sequence;
    public readonly long TimestampUnixMs;
    public readonly string Type;
    public readonly string Payload;  // JSON
}
```

**Rationale**:
- **Data Integrity**: Event history is an immutable source of truth
- **Thread Safety**: Immutable structures support concurrent reads
- **Simplified Reasoning**: No need to consider event modifications

### 2. Layered Architecture

```
Query (Query) → Semantics (Semantics) → Context (Context)
    → Capture (Capture) → Sources (Event Sources) → Core (Data)
```

Each layer has a single responsibility, independently testable and replaceable.

### 3. Side-Table Pattern

Core event data and context metadata are stored separately:

| Event | ContextMapping |
|-------|----------------|
| Sequence | EventSequence |
| Timestamp | ContextId |
| Type | Source (Human/AI/System) |
| Payload | AgentId |

**Benefits**: Events remain pure, context can be flexibly associated.

---

## Architectural Principles

### System Architecture

ActionTrace depends on the general-purpose Hook infrastructure:

```
┌─────────────────────────────────────────────────────────────┐
│                    General Infrastructure                    │
│                         (Hooks/)                            │
│  ┌──────────────────┐         ┌──────────────────────────┐ │
│  │   HookRegistry   │◄────────┤   HookEventArgs         │ │
│  │  (Event Dispatch)│         │   (Event Args Classes)   │ │
│  └────────┬─────────┘         └──────────────────────────┘ │
└───────────┼─────────────────────────────────────────────────┘
            │ Subscribes
            ▼
┌─────────────────────────────────────────────────────────────┐
│                   ActionTrace System                        │
│                      (ActionTrace/)                         │
│  ┌──────────────────┐         ┌──────────────────────────┐ │
│  │ UnityEventHooks  │────────►│  ActionTraceRecorder    │ │
│  │ (Unity Detect)   │         │  (Record to EventStore)  │ │
│  └──────────────────┘         └──────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

**Key Design**: HookRegistry is general infrastructure in `MCPForUnity/Editor/Hooks/`. ActionTrace is one of its consumers.

### SOLID Principles Application

| Layer | Component | Single Responsibility |
|-------|-----------|---------------------|
| **Hooks** (General) | `HookRegistry` | Event dispatch for all Unity callbacks |
| **Hooks** (General) | `HookEventArgs` | Event argument definitions |
| **ActionTrace** | `UnityEventHooks` | Detect Unity callbacks, notify HookRegistry |
| **ActionTrace** | `ActionTraceRecorder` | Record to EventStore |
| **ActionTrace** | `SamplingMiddleware` | Sampling for flood protection |
| **ActionTrace** | `EventStore` | Storage and query API |
| **ActionTrace** | `ContextMapping` | Context mapping |

**Extension**: Through interfaces (`IEventScorer`, `IEventCapturePoint`) rather than modifying core code.

---

## Core Concepts

### Event Lifecycle

```
Detection → Dispatch → Filter → Sample → Store → Query
Unity → HookRegistry → Filter → Sampling → EventStore → Query
```

### Hot/Cold Event Separation

| State | Count | Payload | Size |
|-------|-------|---------|------|
| Hot Events | Latest 150 | Full JSON | ~10KB |
| Cold Events | Remaining | Summary only | ~100B |

Old events are automatically dehydrated: `Payload = null`, retain `PrecomputedSummary`.

### Event Merging

High-frequency events are automatically merged within a time window (default 100ms):

```
Original: position.x = 1.0, 1.1, 1.2, 1.3, 1.4
Merged: position.x (1.0 → 1.4)
```

### Operation Context

```csharp
public class OperationContext
{
    public string ContextId { get; }
    public OperationSource Source { get; }  // Human/AI/System
    public string AgentId { get; }         // Claude/Cursor/...
}
```

### Sampling Strategies

| Mode | Description | Use Case |
|------|-------------|----------|
| None | Record all | Low-frequency events |
| Throttle | Record first in window | Slider dragging |
| Debounce | Record last in window | Property modification |
| DebounceByKey | Record last per key | Batch operations |

---

## Key Design Decisions

### Why use `struct` for events?

- Memory efficiency (no GC pressure)
- Immutability guarantee
- Thread safety
- Cache-friendly

### Why separate HookRegistry from ActionTrace?

- **General Infrastructure**: HookRegistry is in `Hooks/` namespace, available for all systems
- **Decoupling**: ActionTrace doesn't own the event dispatch mechanism
- **Future-Proof**: Other systems can subscribe to Unity events without ActionTrace dependency
- **Testability**: Independent testing of dispatch logic

See [Hooks Design Doc](../Hooks/DESIGN.md) for details on the Hook system.

### Why use JSON for Payload?

- Flexibility: Supports arbitrary structures
- Version compatibility: Old events can contain new fields
- Alignment with MCP protocol

### Why use the side-table pattern?

- Events remain purely immutable
- Context can be lazily associated
- Optional loading during queries
- Storage efficient (most events have no context)

### Why use partial classes for EventStore?

- Related features grouped together (Core/Merging/Persistence/Context)
- Supports parallel editing
- Unified API entry point

---

## Data Flow Design

### Event Capture Flow

```
Unity Callbacks → UnityEventHooks → HookRegistry
    → ActionTraceRecorder → SamplingMiddleware → EventStore
```

### Context Tracking Flow

```
MCP Tool Start → ToolCallScope.Begin → OperationContext
    → ContextMapping.Push → (Record Events) → ContextMapping.Pop
```

### Query Flow

```
Query Request → EventStore.Query
    → Filter(Time/Type/Importance) → Paginate → (Optional)Load Context → Result
```

---

## Extensibility Design

### Add Event Type

Add constant and metadata configuration in `EventTypes.cs`.

### Add Capture Point

```csharp
[EventCapturePoint]
public class MyCapturePoint : EventCapturePointBase
{
    public override void Initialize() { }
    public override void Shutdown() { }
}
```

### Custom Scorer

```csharp
public class MyScorer : IEventScorer
{
    public float Score(in EditorEvent e) => 0.5f;
}
```

### Extend Query

```csharp
public static ActionTraceQuery ByAgent(
    this ActionTraceQuery query, string agentId)
{
    return query.AddFilter(e => /* ... */);
}
```

---

## Performance & Memory Management

### Memory Optimization

1. **Hot/Cold Separation**: 150 hot events (~1.5MB), rest cold (~100B each)
2. **Event Dehydration**: Auto-release Payload when limit exceeded
3. **Sampling Throttling**: PropertyModified(50ms), HierarchyChanged(100ms)
4. **Batch Notifications**: Accumulate 100ms or 50 events before batch notify

### Thread Safety

`EventStore` uses `ReaderWriterLockSlim`:
- Multi-threaded safe reads (`Query`)
- Single-threaded writes (`Record`)

---

## MCP Integration

### Dual-End Architecture

```
MCP Client (Claude/Cursor/...)
    ↓ JSON-RPC 2.0
Python Server (FastMCP)
    ↓ HTTP/WebSocket
Unity Bridge (C# Tools)
    ↓ Internal API
ActionTrace System (EventStore/Recorder/Context)
```

### Data Transformation

```
Python Query → Unity Bridge (JObject) → ActionTraceQuery
    → EventStore.Query → List<EditorEvent>
    → JSON Serialization → Python Response
```

---

## Design Evolution History

| Version | Key Features |
|---------|-------------|
| v1.0 | Basic event tracking |
| v2.0 | Semantic layer (scoring, categorization, summarization) |
| v3.0 | Context tracking (AI/Human/System) |
| v4.0 | Performance optimization (hot/cold separation, sampling, merging) |
| v5.0 | Enhanced extensibility (pluggable capture points, VCS integration) |

---

## References

- **Code Structure**: [../MCPForUnity/Editor/ActionTrace/](../MCPForUnity/Editor/ActionTrace/)
- **Contributing Guide**: [CONTRIBUTING.md](CONTRIBUTING.md)
- **Hook System Design**: [../Hooks/DESIGN.md](../Hooks/DESIGN.md)
- **Hook Contributing**: [../Hooks/CONTRIBUTING.md](../Hooks/CONTRIBUTING.md)
