---
id: roslyn
slug: /guides/roslyn
title: Roslyn Script Validation (Advanced)
sidebar_label: Roslyn Validation
description: Enable strict C# validation that catches undefined namespaces, types, and methods before the script reaches the Unity compiler.
---

# Roslyn Script Validation

By default, MCP for Unity uses a fast structural validator for scripts the LLM generates. For **strict** validation that catches undefined namespaces, types, and methods at write time — without a full Unity compile — install the optional Roslyn DLLs.

Most users don't need this. Enable it when:

- You're letting the LLM write a lot of unsupervised C# and want stricter feedback loops
- You're seeing recurring compile errors that survive the structural validator
- You're building custom tools that depend on accurate symbol resolution

## One-click installer (recommended)

1. Open **Window → MCP for Unity**.
2. Scroll to the **Runtime Code Execution (Roslyn)** section in the Scripts / Validation tab.
3. Click **Install Roslyn DLLs**.

The installer downloads the required NuGet packages, places the DLLs in `Assets/Plugins/Roslyn/`, and adds `USE_ROSLYN` to Scripting Define Symbols.

You can also trigger it from the menu: **Window → MCP for Unity → Install Roslyn DLLs**.

## Manual install (if the installer isn't available)

1. Install [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).
2. Open **Window → NuGet Package Manager**.
3. Install:
   - `Microsoft.CodeAnalysis` v5.0
   - `SQLitePCLRaw.core` v3.0.2
   - `SQLitePCLRaw.bundle_e_sqlite3` v3.0.2
4. Add `USE_ROSLYN` to **Player Settings → Scripting Define Symbols**.
5. Restart Unity.

## Manual DLL install (no NuGetForUnity)

1. Download `Microsoft.CodeAnalysis.CSharp.dll` and its dependencies from [NuGet.org](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/).
2. Place DLLs in `Assets/Plugins/Roslyn/`.
3. Ensure .NET compatibility settings are correct for your Unity version.
4. Add `USE_ROSLYN` to Scripting Define Symbols.
5. Restart Unity.

## Verifying it's active

After restart, the MCP for Unity status panel shows **Roslyn: enabled** under the Scripts section. The `validate_script` tool now performs full semantic analysis rather than the structural pass.

## Disabling

Remove `USE_ROSLYN` from Scripting Define Symbols. The plugin falls back to structural validation; the DLLs can stay in `Assets/Plugins/Roslyn/` or be removed.
