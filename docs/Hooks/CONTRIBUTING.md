# Hooks System Contributing Guide

> Guide for contributing to the Unity Editor Hook infrastructure

---

## Table of Contents

- [Overview](#overview)
- [Adding New Hook Events](#adding-new-hook-events)
- [Adding Event Arguments](#adding-event-arguments)
- [Code Standards](#code-standards)
- [Testing](#testing)
- [Common Patterns](#common-patterns)
- [Commit Message Format](#commit-message-format)
- [FAQ](#faq)

---

## Overview

The Hook system is **general-purpose infrastructure** in `MCPForUnity/Editor/Hooks/`. It provides centralized event dispatch for Unity Editor callbacks.

### When to Modify Hooks

| Scenario | Location |
|----------|----------|
| Add new event type | `HookRegistry.cs`, `HookEventArgs.cs` |
| Add event arguments | `HookEventArgs.cs` |
| Detect Unity callbacks | `Hooks/UnityEventHooks/UnityEventHooks.cs` |
| Subscribe to events | Your system's code |

**Important**: HookRegistry should only contain **Unity Editor callback events**, not business logic.

---

## Adding New Hook Events

> **Important: Static Class Requirements**
>
> `UnityEventHooks` is declared as `public static partial class`. When adding partial methods:
> - **All partial methods MUST use `static partial void` syntax**
> - The `static` modifier is required because all members of a static class must be static
> - Omitting `static` will cause CS0708: "cannot declare instance members in a static class"
>
> ```csharp
> // Correct (UnityEventHooks.cs):
> static partial void InitializeTracking();
>
> // Incorrect - will cause compilation error:
> partial void InitializeTracking();
> ```

### Step 1: Add Event to HookRegistry

Edit [`MCPForUnity/Editor/Hooks/HookRegistry.cs`](../../MCPForUnity/Editor/Hooks/HookRegistry.cs):

```csharp
namespace MCPForUnity.Editor.Hooks
{
    public static class HookRegistry
    {
        #region My Custom Events

        public static event Action<MyCustomArgs> OnMyCustomEvent;

        #endregion

        #region Internal Notification API

        internal static void NotifyMyCustomEvent(MyCustomArgs args)
        {
            var handler = OnMyCustomEvent;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<MyCustomArgs>)subscriber)(args); }
                catch (Exception ex)
                {
                    McpLog.Warn($"[HookRegistry] OnMyCustomEvent subscriber threw: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
```

### Step 2: Add Unity Callback Detection

Edit [`MCPForUnity/Editor/Hooks/UnityEventHooks/UnityEventHooks.cs`](../../MCPForUnity/Editor/Hooks/UnityEventHooks/UnityEventHooks.cs) for basic event detection or [`UnityEventHooks.Advanced.cs`](../../MCPForUnity/Editor/Hooks/UnityEventHooks/UnityEventHooks.Advanced.cs) for advanced features:

```csharp
namespace MCPForUnity.Editor.Hooks
{
    public static partial class UnityEventHooks
    {
        private static void SubscribeToUnityEvents()
        {
            // Add your Unity callback subscription
            SomeUnityEvent += OnMyCustomCallback;
        }

        private static void UnsubscribeFromUnityEvents()
        {
            SomeUnityEvent -= OnMyCustomCallback;
        }

        private static void OnMyCustomCallback()
        {
            // Notify HookRegistry
            HookRegistry.NotifyMyCustomEvent(new MyCustomArgs
            {
                // Set properties
            });
        }
    }
}
```

---

## Adding Event Arguments

### Argument Class Design

Edit [`MCPForUnity/Editor/Hooks/EventArgs/HookEventArgs.cs`](../../MCPForUnity/Editor/Hooks/EventArgs/HookEventArgs.cs):

```csharp
namespace MCPForUnity.Editor.Hooks.EventArgs
{
    /// <summary>
    /// Arguments for my custom event.
    /// </summary>
    public class MyCustomArgs : HookEventArgs
    {
        /// <summary>Description of the property</summary>
        public string MyProperty { get; set; }

        /// <summary>Optional: make property nullable if not always available</summary>
        public int? OptionalValue { get; set; }
    }
}
```

### Guidelines

| Guideline | Description |
|-----------|-------------|
| **Inherit from HookEventArgs** | Automatically gets Timestamp |
| **Use nullable for optional** | `int?`, `string?`, etc. for optional data |
| **XML Documentation** | Describe each property |
| **Immutability** | Consider making properties `init` only if appropriate |

---

## Code Standards

### Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Event | `On[EventName]` | `OnScriptCompiled` |
| Event (Detailed) | `On[EventName]Detailed` | `OnScriptCompiledDetailed` |
| Args Class | `[EventName]Args` | `ScriptCompilationArgs` |
| Notify Method | `Notify[EventName]` | `NotifyScriptCompiled` |

### Event Naming Patterns

| Pattern | Example |
|---------|---------|
| Past Tense (completed) | `OnScriptCompiled` |
| Past Tense (failed) | `OnScriptCompilationFailed` |
| Noun (state change) | `OnPlayModeChanged` |
| Noun (selection) | `OnSelectionChanged` |
| Verb (action) | `OnSceneSaved` |

### Required Using Statements

```csharp
// For HookRegistry subscriptions
using MCPForUnity.Editor.Hooks;

// For detailed event args
using MCPForUnity.Editor.Hooks.EventArgs;
```

### Partial Method Guidelines

When adding partial methods to `UnityEventHooks`:

| Guideline | Description | Example |
|-----------|-------------|---------|
| **Use `static partial void`** | Required for static classes | `static partial void InitializeTracking();` |
| **Declare in base file** | Add declaration in `UnityEventHooks.cs` | For extension points |
| **Implement in Advanced** | Add implementation in `.Advanced.cs` | For advanced features |
| **Private or no access** | Partial methods are implicitly private | No access modifier needed |

```csharp
// UnityEventHooks.cs - Declaration
static partial void TrackCustomFeature();

// UnityEventHooks.Advanced.cs - Implementation
static partial void TrackCustomFeature()
{
    // Implementation here
    HookRegistry.NotifyCustomFeature();
}
```

---

## Testing

### Unit Test Template

```csharp
using NUnit.Framework;
using MCPForUnity.Editor.Hooks;
using MCPForUnity.Editor.Hooks.EventArgs;

public class MyCustomHookTests
{
    private bool _eventReceived;
    private MyCustomArgs _receivedArgs;

    [SetUp]
    public void SetUp()
    {
        _eventReceived = false;
        _receivedArgs = null;
    }

    [TearDown]
    public void TearDown()
    {
        // Always unsubscribe to prevent test pollution
        HookRegistry.OnMyCustomEvent -= OnEvent;
    }

    [Test]
    public void NotifyMyCustomEvent_WhenCalled_TriggersSubscribers()
    {
        // Arrange
        HookRegistry.OnMyCustomEvent += OnEvent;
        var expectedArgs = new MyCustomArgs { MyProperty = "test" };

        // Act
        HookRegistry.NotifyMyCustomEvent(expectedArgs);

        // Assert
        Assert.IsTrue(_eventReceived);
        Assert.AreEqual("test", _receivedArgs.MyProperty);
    }

    [Test]
    public void NotifyMyCustomEvent_WhenSubscriberThrows_ContinuesDispatch()
    {
        // Arrange
        HookRegistry.OnMyCustomEvent += (args) => throw new Exception("Test error");
        var secondHandlerCalled = false;
        HookRegistry.OnMyCustomEvent += (args) => secondHandlerCalled = true;

        // Act
        HookRegistry.NotifyMyCustomEvent(new MyCustomArgs());

        // Assert - second handler should still be called
        Assert.IsTrue(secondHandlerCalled, "Second handler should be called despite first throwing");
    }

    private void OnEvent(MyCustomArgs args)
    {
        _eventReceived = true;
        _receivedArgs = args;
    }
}
```

---

## Common Patterns

### Simple Event (No Args)

```csharp
// HookRegistry.cs
public static event Action OnSimpleEvent;
internal static void NotifySimpleEvent() { /* dispatch */ }

// Usage
HookRegistry.OnSimpleEvent += () => Debug.Log("Happened!");
```

### Event With Unity Object

```csharp
// HookRegistry.cs
public static event Action<GameObject> OnGameObjectSelected;

// Usage
HookRegistry.OnGameObjectSelected += (go) =>
{
    if (go != null) Debug.Log($"Selected: {go.name}");
};
```

### Simple + Detailed Pattern

```csharp
// HookRegistry.cs
public static event Action<bool> OnBuildCompleted;
public static event Action<BuildArgs> OnBuildCompletedDetailed;

// UnityEventHooks.cs - always notify both
HookRegistry.NotifyBuildCompleted(success);
HookRegistry.NotifyBuildCompletedDetailed(new BuildArgs { Success = success, ... });
```

### Cached Data for Destroyed Objects

```csharp
// For objects that will be destroyed, cache data BEFORE destruction
public class GameObjectDestroyedArgs : HookEventArgs
{
    public int InstanceId { get; set; }    // Cached ID
    public string Name { get; set; }       // Cached name
    public string GlobalId { get; set; }   // Cached GlobalID
}
```

---

## Commit Message Format

```
feat(Hooks): add undo/redo event hooks

Add OnUndoPerformed and OnRedoPerformed events to HookRegistry.
Includes UndoRedoArgs for detailed event tracking.

Subscribers can now track editor undo/redo operations
without directly subscribing to Undo.undoRedoPerformed.
```

---

## FAQ

**Q: Should I add business logic to HookRegistry?**

A: No. HookRegistry is for event dispatch only. Add business logic to your system.

**Q: Can I trigger Hook events manually?**

A: No. `Notify*` methods are `internal`. Only Unity event detectors should call them.

**Q: When should I add a Detailed event?**

A: When you need to pass additional context beyond the simple event parameters.

**Q: Do I need to update ActionTrace when adding Hooks?**

A: No. Hooks are independent infrastructure. ActionTrace is just one consumer.

**Q: Why do partial methods need `static` modifier in UnityEventHooks?**

A: `UnityEventHooks` is a `static partial class`. In C#, all members of a static class must be static, including partial methods. Use `static partial void MethodName()` syntax.

**Q: How do I add a new partial method to UnityEventHooks?**

A: Declare it with `static partial void` in `UnityEventHooks.cs`, then implement it with `static partial void` in `UnityEventHooks.Advanced.cs` (or another partial file).

---

## References

- **Design Doc**: [DESIGN.md](DESIGN.md)
- **ActionTrace Guide**: [../ActionTrace/CONTRIBUTING.md](../ActionTrace/CONTRIBUTING.md)
- **HookRegistry Code**: [../../MCPForUnity/Editor/Hooks/HookRegistry.cs](../../MCPForUnity/Editor/Hooks/HookRegistry.cs)

---

Thanks for contributing to the Hooks system! 🎉
