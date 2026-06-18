---
id: unity-compat
slug: /architecture/unity-compat
title: Unity API Compatibility Shims
sidebar_label: Unity Compat Shims
description: How MCP for Unity supports Unity 2021 LTS through 6.x and the CoreCLR 6.8 path without sprinkling version gates across every call site.
---

# Unity API Compatibility Shims

MCP for Unity targets a wide Unity version range — **2021.3 LTS → Unity 6.x → CoreCLR 6.8**. Unity has renamed, deprecated, and threatened to remove a handful of APIs across that window. Rather than sprinkle `#if UNITY_*_OR_NEWER` at every call site, MCP for Unity routes the friction through a small set of **shims** under `MCPForUnity/Runtime/Helpers/`.

## The catalog

The canonical list lives in `MCPForUnity/Runtime/Helpers/UnityCompatShims.cs` (an intentionally empty marker class — its XML doc is the source of truth and ships inside the UPM package, so end-users can `F12` into it).

| Shim | Wraps | Deprecated / Removed |
|---|---|---|
| `UnityFindObjectsCompat` | `Object.FindObjectsOfType` → `FindObjectsByType` | 2023.1 |
| `UnityObjectIdCompat` | `InstanceID` ↔ `EntityId` | 6000.3 → 6000.6 (CS0619) |
| `UnityPhysicsCompat` | `Physics{,2D}.autoSyncTransforms`, `autoSimulation` → `simulationMode` | 6000.0 / 2022.2 |
| `UnityAssembliesCompat` | `AppDomain.GetAssemblies` → `UnityEngine.Assemblies.CurrentAssemblies` | Unity 6.8 CoreCLR |

## When to add a new shim

One of these must be true:

1. The API is marked `[Obsolete]` **and** the call site can't simply be deleted, **or**
2. Three or more call sites need version gating for the same API, **or**
3. A future Unity version has publicly announced rename or removal of the API.

If only one or two call sites are affected and the rename isn't on the roadmap, a localized `#if UNITY_*_OR_NEWER` is fine. Don't pre-shim speculatively.

## What does **not** belong in a shim

- Hot-path engine APIs (`Transform.position`, `Vector3.*`, `GetComponent<T>`) — version gating these is noise, and they don't move
- APIs Unity has not threatened to break (`Mathf`, `Quaternion`, most of `AssetDatabase`) — adding a shim implies maintenance forever
- Editor-internal undocumented APIs — those *should* break loudly so the package maintainers notice immediately

## The pattern

Two implementation styles, picked by what the SDK exposes:

- **Static dispatch** (`#if UNITY_*_OR_NEWER`): use when the new API exists in the SDK you compile against. The shim picks the right call site at compile time, with no runtime cost.
- **Reflection with a cached `MethodInfo` / `PropertyInfo`**: use when the new API is in a version you don't yet target, or when the old API may eventually be removed (CS0619). One reflection lookup at static-init time, then plain delegate invocation forever after.

In both cases, **fail-soft**: callers should treat a missing API as a no-op, never throw. This keeps the package compiling and behaving sensibly on every supported Unity version, including ones the maintainer hasn't tested yet.

## Compile-checking across versions locally

`tools/check-unity-versions.sh` runs a compile-only check across the same Unity versions CI runs. The matrix is in `tools/unity-versions.json`.

```bash
tools/check-unity-versions.sh           # compile-only across installed Unity Hub editors
tools/check-unity-versions.sh --full    # full EditMode test run
```

The pre-push hook (installed via `tools/install-hooks.sh`) runs this automatically when your push touches `MCPForUnity/`, `TestProjects/`, or the version matrix.

## Source pointers

- Catalog + policy: `MCPForUnity/Runtime/Helpers/UnityCompatShims.cs`
- Individual shim files: same directory, named `Unity*Compat.cs`
- Unity 6.x deprecation list: Unity upgrade guides
- CoreCLR 6.8 path: [Unity discussion thread](https://discussions.unity.com/t/path-to-coreclr-2026-upgrade-guide/1714279)
