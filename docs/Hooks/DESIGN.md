# Hooks System Design

> Design principles and architecture for the Unity Editor Hook infrastructure

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Core Components](#core-components)
- [Event Categories](#event-categories)
- [Design Decisions](#design-decisions)
- [Usage Patterns](#usage-patterns)
- [File Structure](#file-structure)

---

## Overview

The Hook system provides **general-purpose event dispatch infrastructure** for Unity Editor callbacks. It serves as a centralized event registry that other systems (like ActionTrace) can subscribe to without directly monitoring Unity callbacks.

### Key Benefits

| Benefit | Description |
|---------|-------------|
| **Decoupling** | Systems don't need to directly subscribe to Unity callbacks |
| **General Infrastructure** | Available for all systems, not tied to any specific domain |
| **Future-Proof** | New systems can leverage existing Hook events |
| **Exception Safety** | Subscriber errors don't break other subscribers |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Unity Editor                           │
│                    (Native Callbacks)                       │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│                   UnityEventHooks                           │
│                   (Hooks/UnityEventHooks/)                  │
│           Detects Unity callbacks → Notifies                │
│           Uses IGameObjectCacheProvider for tracking        │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│                    HookRegistry                              │
│                 (Hooks/HookRegistry.cs)                     │
│              Central Event Dispatcher                        │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │ ActionTrace  │  │  Future      │  │   Custom     │      │
│  │  Recorder    │  │  Systems     │  │   System     │      │
│  │ (injects     │  │              │  │              │      │
│  │  Provider)   │  │              │  │              │      │
│  └──────────────┘  └──────────────┘  └──────────────┘      │
└─────────────────────────────────────────────────────────────┘
```

**Key Design**:
- `UnityEventHooks` (in `Hooks/UnityEventHooks/`) subscribes to Unity callbacks
- `HookRegistry` (general infrastructure) dispatches events to all subscribers
- `IGameObjectCacheProvider` interface enables dependency injection for tracking capability
- ActionTrace injects its cache provider implementation, other systems can provide their own
- Multiple systems can subscribe independently without knowing about each other

---

## Core Components

### HookRegistry

Located at [`MCPForUnity/Editor/Hooks/HookRegistry.cs`](../../MCPForUnity/Editor/Hooks/HookRegistry.cs)

**Responsibilities**:
- Define public event delegates for all Unity editor callbacks
- Provide internal notification methods for event dispatchers
- Handle subscriber exceptions gracefully (isolation pattern)

**Namespace**: `MCPForUnity.Editor.Hooks`

### UnityEventHooks

Located at [`MCPForUnity/Editor/Hooks/UnityEventHooks/`](../../MCPForUnity/Editor/Hooks/UnityEventHooks/)

**Responsibilities**:
- Subscribe to Unity Editor callbacks (component, hierarchy, selection, scene, build, etc.)
- Notify HookRegistry when events occur
- Provide extension points via partial methods for advanced features

**Files**:
- `UnityEventHooks.cs` - Basic Unity event detection
- `UnityEventHooks.Advanced.cs` - Advanced tracking (script compilation, GameObject changes, component removal)

**Namespace**: `MCPForUnity.Editor.Hooks`

### IGameObjectCacheProvider

Located at [`MCPForUnity/Editor/Hooks/IGameObjectCacheProvider.cs`](../../MCPForUnity/Editor/Hooks/IGameObjectCacheProvider.cs)

**Responsibilities**:
- Define interface for GameObject cache providers
- Enable dependency injection for tracking capability
- Allow systems to provide their own cache implementation

**Namespace**: `MCPForUnity.Editor.Hooks`

### HookEventArgs

Located at [`MCPForUnity/Editor/Hooks/EventArgs/HookEventArgs.cs`](../../MCPForUnity/Editor/Hooks/EventArgs/HookEventArgs.cs)

**Responsibilities**:
- Define base `HookEventArgs` class with timestamp
- Define event-specific argument classes
- Provide extensible event data structure

**Namespace**: `MCPForUnity.Editor.Hooks.EventArgs`

---

## Event Categories

### Compilation Events

| Event | Simple | Detailed |
|-------|--------|----------|
| Script Compiled | `Action` | `Action<ScriptCompilationArgs>` |
| Compilation Failed | `Action<int>` (error count) | `Action<ScriptCompilationFailedArgs>` |

```csharp
// Simple subscription
HookRegistry.OnScriptCompiled += () => Debug.Log("Compiled!");

// Detailed subscription
HookRegistry.OnScriptCompiledDetailed += (args) =>
    Debug.Log($"Compiled {args.ScriptCount} scripts in {args.DurationMs}ms");
```

### Scene Events

| Event | Simple | Detailed |
|-------|--------|----------|
| Scene Saved | `Action<Scene>` | - |
| Scene Opened | `Action<Scene>` | `Action<Scene, SceneOpenArgs>` |
| New Scene Created | `Action<Scene>` | `Action<Scene, NewSceneArgs>` |
| Scene Loaded | `Action<Scene>` | - |
| Scene Unloaded | `Action<Scene>` | - |

### Play Mode Events

| Event | Signature |
|-------|-----------|
| Play Mode Changed | `Action<bool>` (isPlaying) |

### Hierarchy Events

| Event | Simple | Detailed |
|-------|--------|----------|
| Hierarchy Changed | `Action` | - |
| GameObject Created | `Action<GameObject>` | - |
| GameObject Destroyed | `Action<GameObject>` | `Action<GameObjectDestroyedArgs>` |

### Selection Events

| Event | Signature |
|-------|-----------|
| Selection Changed | `Action<GameObject>` |

### Project Events

| Event | Signature |
|-------|-----------|
| Project Changed | `Action` |
| Asset Imported | `Action` |
| Asset Deleted | `Action` |

### Build Events

| Event | Simple | Detailed |
|-------|--------|----------|
| Build Completed | `Action<bool>` (success) | `Action<BuildArgs>` |

### Editor State Events

| Event | Signature |
|-------|-----------|
| Editor Update | `Action` |
| Editor Idle | `Action` |

### Component Events

| Event | Simple | Detailed |
|-------|--------|----------|
| Component Added | `Action<Component>` | - |
| Component Removed | `Action<Component>` | `Action<ComponentRemovedArgs>` |

---

## Design Decisions

### Why Partial Classes for UnityEventHooks?

`UnityEventHooks` uses C# partial classes to separate basic and advanced features:

```csharp
// UnityEventHooks.cs - Basic event detection
public static partial class UnityEventHooks
{
    // Extension point declarations (MUST be static partial void)
    static partial void InitializeTracking();
    static partial void TrackScriptCompilation();
    // ...
}

// UnityEventHooks.Advanced.cs - Advanced implementations
public static partial class UnityEventHooks
{
    // Implementation of extension points
    static partial void InitializeTracking() { /* ... */ }
    static partial void TrackScriptCompilation() { /* ... */ }
    // ...
}
```

**Important**: In a static class, all partial methods must be declared as `static partial void`. The `static` modifier is required because all members of a static class must be static.

**Benefits**:
- **Separation of Concerns**: Basic detection vs. advanced tracking logic
- **Optional Features**: Advanced file can be removed without breaking basic functionality
- **Extensibility**: New features can be added as separate partial files

### Why Simple + Detailed Events?

- **Backward Compatibility**: Simple events maintain existing contracts
- **Progressive Enhancement**: Detailed events provide additional context when needed
- **Performance**: Simple events have minimal overhead

### Why Use IGameObjectCacheProvider Interface?

The GameObject tracking capability uses dependency injection via `IGameObjectCacheProvider`:

```csharp
// ActionTrace injects its cache provider
var provider = new GameObjectTrackingCacheProvider(helper);
UnityEventHooks.SetGameObjectCacheProvider(provider);
```

**Rationale**:
- **Decoupling**: UnityEventHooks doesn't depend on ActionTrace-specific classes
- **Extensibility**: Other systems can provide their own cache implementation
- **Testability**: Easy to mock for testing
- **Dependency Inversion**: UnityEventHooks depends on abstraction, not concrete implementation

### Why Exception Isolation?

Each subscriber invocation is wrapped in try-catch:

```csharp
foreach (var subscriber in handler.GetInvocationList())
{
    try { ((Action)subscriber)(); }
    catch (Exception ex)
    {
        McpLog.Warn($"[HookRegistry] Subscriber threw: {ex.Message}");
    }
}
```

**Rationale**: One misbehaving subscriber shouldn't prevent others from receiving notifications.

### Why Internal Notification API?

The `Notify*` methods are `internal` to prevent external systems from triggering Unity events manually. Only event detectors (like `UnityEventHooks`) can call these methods.

### Why Separate EventArgs Namespace?

- **Clarity**: Clear separation between infrastructure and domain logic
- **Reusability**: EventArgs can be shared across multiple systems
- **Extensibility**: Easy to add new event types without modifying HookRegistry

### Why Domain Reload Handling?

UnityEventHooks properly handles domain reloads (assembly reload) to prevent memory leaks:

```csharp
// Called before domain reload
private static void OnBeforeAssemblyReload()
{
    UnsubscribeFromUnityEvents();  // Clean up subscriptions
    ResetTracking();               // Clear cached data
    _isInitialized = false;
}
```

**Rationale**:
- **Memory Safety**: Unsubscribes from all Unity events before domain reload
- **State Consistency**: Resets tracking state to avoid stale references
- **Clean Startup**: Ensures clean initialization after reload

---

## Usage Patterns

### Basic Subscription

```csharp
using MCPForUnity.Editor.Hooks;

public class MySystem
{
    public void Initialize()
    {
        HookRegistry.OnSceneSaved += OnSceneSaved;
        HookRegistry.OnPlayModeChanged += OnPlayModeChanged;
    }

    public void Shutdown()
    {
        HookRegistry.OnSceneSaved -= OnSceneSaved;
        HookRegistry.OnPlayModeChanged -= OnPlayModeChanged;
    }

    private void OnSceneSaved(Scene scene)
    {
        Debug.Log($"Scene saved: {scene.name}");
    }

    private void OnPlayModeChanged(bool isPlaying)
    {
        Debug.Log($"Play mode: {(isPlaying ? "Playing" : "Editing")}");
    }
}
```

### Detailed Event Subscription

```csharp
using MCPForUnity.Editor.Hooks;
using MCPForUnity.Editor.Hooks.EventArgs;

public class BuildTracker
{
    public void Initialize()
    {
        HookRegistry.OnBuildCompletedDetailed += OnBuildCompleted;
    }

    private void OnBuildCompleted(BuildArgs args)
    {
        if (args.Success)
        {
            Debug.Log($"Build succeeded: {args.Platform} " +
                     $"({args.SizeBytes} bytes in {args.DurationMs}ms)");
        }
        else
        {
            Debug.LogError($"Build failed: {args.Summary}");
        }
    }
}
```

### Handling Destroyed GameObjects

```csharp
using MCPForUnity.Editor.Hooks;
using MCPForUnity.Editor.Hooks.EventArgs;

public class GameObjectTracker
{
    private readonly Dictionary<int, string> _destroyedObjects = new();

    public void Initialize()
    {
        HookRegistry.OnGameObjectDestroyedDetailed += OnGameObjectDestroyed;
    }

    private void OnGameObjectDestroyed(GameObjectDestroyedArgs args)
    {
        // Store info about destroyed object
        _destroyedObjects[args.InstanceId] = args.Name;

        // Use GlobalId for cross-session reference
        Debug.Log($"Destroyed: {args.Name} (GlobalID: {args.GlobalId})");
    }
}
```

---

## File Structure

```
MCPForUnity/Editor/Hooks/
├── HookRegistry.cs              # Central event dispatcher
├── IGameObjectCacheProvider.cs  # Interface for GameObject cache providers
├── UnityEventHooks/
│   ├── UnityEventHooks.cs       # Basic Unity event detection
│   └── UnityEventHooks.Advanced.cs # Advanced tracking features
└── EventArgs/
    └── HookEventArgs.cs         # Event argument definitions

MCPForUnity/Editor/ActionTrace/
├── Capture/
│   └── Recorder.cs              # Injects GameObjectTrackingCacheProvider
└── Sources/
    └── Helpers/
        ├── GameObjectTrackingHelper.cs        # Core tracking implementation
        └── GameObjectTrackingCacheProvider.cs # Adapter implementing IGameObjectCacheProvider
```

### Partial Class Structure

`UnityEventHooks` is split across multiple files using C# partial classes:

| File | Purpose |
|------|---------|
| `UnityEventHooks.cs` | Basic event detection, Unity callback subscriptions, partial method declarations |
| `UnityEventHooks.Advanced.cs` | Advanced features (script compilation, GameObject changes, component removal), partial method implementations |

---

## References

- **Code**: [../../MCPForUnity/Editor/Hooks/](../../MCPForUnity/Editor/Hooks/)
- **Contributing**: [CONTRIBUTING.md](CONTRIBUTING.md)
- **ActionTrace Design**: [../ActionTrace/DESIGN.md](../ActionTrace/DESIGN.md)
