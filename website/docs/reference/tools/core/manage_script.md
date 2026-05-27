---
title: manage_script
sidebar_label: manage_script
description: "Compatibility router for legacy script operations."
---

# `manage_script`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_script`

## Description

Compatibility router for legacy script operations. Prefer apply_text_edits (ranges) or script_apply_edits (structured) for edits. Read-only action: read. Modifying actions: create, delete.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['create', 'read', 'delete']` | yes | Perform CRUD operations on C# scripts. |
| `name` | `str` | yes | Script name (no .cs extension) |
| `path` | `str` | yes | Asset path (default: 'Assets/') |
| `contents` | `str \| None` | — | Contents of the script to create |
| `script_type` | `str \| None` | — | Script type (e.g., 'C#') |
| `namespace` | `str \| None` | — | Namespace for the script |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
:::note Use the right tool for the job
`manage_script` only handles whole-file lifecycle: **create**, **read**, **delete**. For editing existing scripts, reach for:
- **[`script_apply_edits`](./script_apply_edits)** — structured edits (replace method, insert method, anchor-based insert/replace) with balanced-brace guards. Use this for most code changes.
- **[`apply_text_edits`](./apply_text_edits)** — raw line/column text edits with optional SHA precondition. Use for surgical text patches.
- **[`validate_script`](./validate_script)** — Roslyn-based validation (structural or full semantic).
- **[`read_console`](./read_console)** — fetch compile diagnostics after any change.
:::

### Create a new script

> Create `Assets/Scripts/PlayerController.cs` with a starter MonoBehaviour.

```json
{
  "action": "create",
  "name": "PlayerController",
  "path": "Scripts/",
  "namespace": "MyGame",
  "contents": "using UnityEngine;\n\nnamespace MyGame {\n    public class PlayerController : MonoBehaviour {\n        void Update() { }\n    }\n}\n"
}
```

`path` is relative to `Assets/` and must end in `/`. Omit `contents` to write a minimal stub.

### Read a script's full contents

> Show me `Assets/Scripts/PlayerController.cs`.

```json
{
  "action": "read",
  "name": "PlayerController",
  "path": "Scripts/"
}
```

Returns the full file. For just a SHA (to detect drift between reads and writes), use [`get_sha`](./get_sha) instead — it's cheaper.

### Delete a script

> Remove `Assets/Scripts/PlayerController.cs`.

```json
{
  "action": "delete",
  "name": "PlayerController",
  "path": "Scripts/"
}
```

### After every create / delete

Unity needs a domain reload to compile the new file (or notice the old one is gone). Poll the `editor_state` resource's `isCompiling` field until it flips back to `false`, then run [`read_console`](./read_console) to catch any compile errors before relying on the new types.
<!-- examples:end -->

