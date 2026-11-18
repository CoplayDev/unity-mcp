---
title: execute_code
sidebar_label: execute_code
description: "Execute arbitrary C# code inside the Unity Editor."
---

# `execute_code`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `scripting_ext` &nbsp;·&nbsp; **Module:** `services.tools.execute_code`

## Description

Execute arbitrary C# code inside the Unity Editor. The code runs as a method body with access to UnityEngine and UnityEditor namespaces. Use 'return' to send data back. Compiled in-memory — no script files created. Actions: execute (run code), get_history (list past executions), replay (re-run a history entry), clear_history. NOTE: safety_checks blocks known dangerous patterns but is not a full sandbox. File.Delete/Directory.Delete are allowed only for a single string-literal path under the configured delete roots (defaults: Builds, Temp, Logs, obj, Library/Bee, Library/Artifacts); Assets/ and paths outside the project are always blocked. Compiler options: 'auto' (Roslyn if available, else CodeDom), 'roslyn' (C# 12+, requires Microsoft.CodeAnalysis), 'codedom' (C# 6 only).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['execute', 'get_history', 'replay', 'clear_history']` | yes | Action to perform. |
| `code` | `str \| None` | — | C# code to execute (for 'execute' action). Must be a valid method body. Access UnityEngine and UnityEditor namespaces. Use 'return' to send data back. |
| `safety_checks` | `bool` | — | Enable basic blocked-pattern checks (Process.Start, infinite loops, etc) and the File.Delete/Directory.Delete path allowlist. Not a full sandbox — advanced bypass is possible. Default: true. |
| `index` | `int \| None` | — | History entry index to replay (for 'replay' action). |
| `limit` | `int` | — | Number of history entries to return (for 'get_history' action, 1-50). Default: 10. |
| `compiler` | `Literal['auto', 'roslyn', 'codedom']` | — | Compiler backend for 'execute' action. 'auto' uses Roslyn if Microsoft.CodeAnalysis is installed, else falls back to CodeDom. 'roslyn' forces Roslyn (C# 12+). 'codedom' forces legacy CSharpCodeProvider (C# 6). Default: auto. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

