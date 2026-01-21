# ActionTrace Contributing Guide

> Welcome to contribute to the Unity-mcp ActionTrace system!

---

## Table of Contents

- [Quick Start](#quick-start)
- [Development Setup](#development-setup)
- [Contribution Types](#contribution-types)
- [Code Standards](#code-standards)
- [Testing Guide](#testing-guide)
- [Submitting Pull Requests](#submitting-pull-requests)
- [FAQ](#faq)

---

## Quick Start

```bash
# Fork the project, clone and create a feature branch
git clone https://github.com/your-username/unity-mcp.git
cd unity-mcp && git checkout -b feature/my-action-trace-feature
```

Refer to [Development Setup](#development-setup) below for configuration.

---

## Development Setup

### Required Tools

| Tool | Version | Purpose |
|------|---------|---------|
| Unity | 2022.3+ | Run and test |
| Python | 3.11+ | MCP Server |
| Git | Latest | Version control |

---

## Contribution Types

### 1. Add New Event Type

Add constant and metadata configuration in [EventTypes.cs](../../MCPForUnity/Editor/ActionTrace/Core/EventTypes.cs):

```csharp
// 1. Add constant
public const string MyCustomEvent = "my_custom_event";

// 2. Configure metadata
[MyCustomEvent] = new EventMetadata(
    baseScore: 0.5f,
    category: EventCategory.Operation,
    defaultSummary: "Custom event occurred"
);
```

### 2. Add New Event Capture Point

Inherit from `EventCapturePointBase`, register with `[EventCapturePoint]` attribute:

```csharp
[EventCapturePoint(Name = "My Capture Point")]
public class MyCustomCapturePoint : EventCapturePointBase
{
    public override void Initialize() { /* Subscribe to Unity events */ }
    public override void Shutdown() { /* Cleanup subscriptions */ }
}
```

### 3. Add Custom Scorer

Implement `IEventScorer` interface:

```csharp
public class MyCustomScorer : IEventScorer
{
    public float Score(in EditorEvent e) => 0.5f;  // Custom scoring
}
```

Register in `ActionTraceSettings`: `ActionTraceSettings.ins.CustomScorer = new MyCustomScorer();`

### 4. Add Custom Sampling Strategy

Inherit from `SamplingStrategy`, override `ShouldSample()` method:

```csharp
public class CustomSamplingStrategy : SamplingStrategy
{
    protected override bool ShouldSample(in EditorEvent e)
    {
        // Custom sampling logic
        return base.ShouldSample(in e);
    }
}
```

### 5. Extend Query Functionality

Extend `ActionTraceQuery` partial class:

```csharp
public static partial class ActionTraceQuery
{
    public static ActionTraceQuery ByAgent(this ActionTraceQuery query, string agentId)
    {
        return query.AddFilter(e => GetContextId(e) == agentId);
    }
}
```

### 6. Add MCP Tool Integration

Create tool class with `[McpForUnityTool]` attribute:

```csharp
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

[McpForUnityTool("query_action_trace", Description = "Query ActionTrace events")]
public static class QueryActionTraceTool
{
    public class Parameters
    {
        [ToolParameter("Time range start (Unix ms)")]
        public long StartTime { get; set; }

        [ToolParameter("Time range end (Unix ms)")]
        public long EndTime { get; set; }
    }

    public static object HandleCommand(JObject @params)
    {
        var parameters = @params.ToObject<Parameters>();
        // Query events...
        return new { success = true };
    }
}
```

### 7. Bug Fixes

Create branch: `git checkout -b fix/issue-xxx`

Fix, test, then commit with conventional format:
```
fix(ActionTrace): resolve event memory leak in sampler
```

### 8. Documentation Improvement

Modify documentation in `docs/ActionTrace/` or `docs/Hooks/`, ensure clarity and accuracy.

---

## Code Standards

### Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Class | PascalCase | `EventStore` |
| Interface | I + PascalCase | `IEventScorer` |
| Method | PascalCase | `RecordEvent()` |
| Private Field | _camelCase | `_events` |
| Constant | PascalCase | `MaxEventCount` |
| Event | PascalCase | `OnScriptCompiled` |

### Code Style

- Use `var` when type is obvious
- Use `in` modifier for large readonly structures
- Use string interpolation `$""`
- Avoid nesting more than 3 levels
- Avoid methods longer than 50 lines

### Testing Standards

- Test class: `[ClassName]Tests.cs`
- Test method: `[MethodName]_ExpectedBehavior()`
- Use AAA pattern (Arrange-Act-Assert)

```csharp
[Test]
public void Record_WhenCalled_IncreasesEventCount()
{
    // Arrange
    var evt = CreateTestEvent();

    // Act
    _store.Record(in evt);

    // Assert
    Assert.AreEqual(1, _store.EventCount);
}
```

---

## Testing Guide

### Run Tests

```bash
# Unity tests via MCP
call_tool("run_tests", {"mode": "EditMode"})

# Python tests
cd Server && uv run pytest tests/ -v

# With coverage
uv run pytest tests/ --cov --cov-report=html
```

Or in Unity Editor: `Window > General > Test Runner > EditMode > Run All`

---

## Submitting Pull Requests

### Checklist

- [ ] All tests pass
- [ ] Follow code standards
- [ ] Add necessary documentation
- [ ] Clear commit messages

### Commit Message Format

```
<type>: <subject>

<body>

<footer>
```

**Types:** `feat` | `fix` | `docs` | `refactor` | `test` | `chore`

**Examples:**
```
feat(ActionTrace): add custom event capture point for undo/redo

Implement capture point that monitors Undo/UndoPerformed events
to track editor state changes.

Closes #123
```

```
fix(Hooks): prevent subscriber errors from breaking event dispatch

Wrap each subscriber invocation in try-catch to ensure one
misbehaving subscriber doesn't prevent others from receiving
notifications.
```

---

## FAQ

**Q: Events not being recorded?**

A: Check `ActionTraceSettings.ins.EnableRecording` and `MinImportance` threshold.

**Q: How to debug?**

A: Set `MinImportance = 0.0f` to record all events, use `Debug.Log()` for output.

**Q: Performance issues?**

A: Enable event merging, adjust sampling intervals, raise `MinImportance` threshold.

**Q: How to add a new Hook event?**

A: See [Hooks CONTRIBUTING.md](../Hooks/CONTRIBUTING.md) for detailed guide.

---

## References

- **Design Doc**: [DESIGN.md](DESIGN.md)
- **Hook System**: [../Hooks/DESIGN.md](../Hooks/DESIGN.md)
- **GitHub Issues**: Report bugs or feature requests

---

Thanks for contributing to ActionTrace! 🎉
