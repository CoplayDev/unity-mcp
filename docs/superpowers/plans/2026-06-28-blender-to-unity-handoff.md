# BlenderMCP → UnityMCP Model Handoff Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a model that already exists on disk (e.g. exported from Blender) be imported into Unity through the existing model-import pipeline and placed in the scene, via one new DCC-agnostic UnityMCP tool plus a `blender-to-unity` orchestration skill.

**Architecture:** The two MCP servers never talk to each other; the integration seam is the local filesystem. Blender exports a file; a new UnityMCP tool `import_model_file` copies it under `Assets/` and runs the existing `ModelImportPipeline` (glTFast/FBX/OBJ/zip handling, scale-normalize, material settings), returning `{ asset_path, asset_guid }`. A client-side skill sequences Blender export → import → scene placement → screenshot verify.

**Tech Stack:** C# Unity Editor (`[McpForUnityTool]`, `ToolParams`, `ModelImportPipeline`), Python FastMCP (`@mcp_for_unity_tool`), Click CLI, NUnit EditMode tests, pytest.

## Global Constraints

- **Unity floor:** 2021.3+ — use only APIs available there (no `AssetDatabase` 6.x-only members).
- **No new package dependencies** — glTFast stays optional; FBX/OBJ use the built-in `ModelImporter`.
- **Key-free / byte-free over the bridge** — `import_model_file` carries no API keys and no file bytes; the file is already local to the editor. The C# side reads from disk.
- **Tool group:** `asset_gen`, `AutoRegister = false` (matches `generate_model` / `import_model`).
- **New-tool surfaces (CLAUDE.md):** Python MCP tool + Python CLI + C# handler + Python tests + C# EditMode tests.
- **Param naming:** Python snake_case maps to camelCase on the bridge — `source_path→sourcePath`, `output_folder→outputFolder`, `target_size→targetSize`, `name→name`.
- **C# responses:** `MCPForUnity.Editor.Helpers.SuccessResponse(message, data)` / `ErrorResponse(messageOrCode, data)`.
- **Commits:** This branch (`feat/3d-asset-generation`) may be shared with another agent. Commit ONLY this feature's files using explicit pathspecs — never `git add -A` / `git commit -a`.

---

### Task 1: C# `ImportModelFile` handler + EditMode tests

The reusable primitive. Independently useful for any DCC export, not just Blender.

**Files:**
- Create: `MCPForUnity/Editor/Tools/AssetGen/ImportModelFile.cs`
- Test: `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/ImportModelFileHandlerTests.cs`

**Interfaces:**
- Consumes: `ModelImportPipeline.ImportInto(AssetGenJob job, string localFilePath)` → mutates and returns the job (`AssetPath`, `AssetGuid`, `State`, `Error`); `AssetGenJob` public fields `TargetSize` (float, default 1f), `AssetPath`, `AssetGuid`, `State` (`AssetGenJobState`), `Error`; `AssetGenPrefs.OutputRoot` (default `"Assets/Generated"`); `ToolParams.Get(key)`, `ToolParams.GetFloat(key, default)`.
- Produces: command `import_model_file`, params `{ sourcePath (required), name?, outputFolder?, targetSize? }` → success `{ asset_path, asset_guid }`, else `{ success:false, error }`.

- [ ] **Step 1: Write the failing EditMode tests**

Create `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/ImportModelFileHandlerTests.cs`:

```csharp
using System;
using System.IO;
using MCPForUnity.Editor.Tools.AssetGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Drives the import_model_file handler with on-disk fixture files (no network, no provider).
    /// Covers missing source, unsupported extension, and a real OBJ import that yields an asset GUID.
    /// </summary>
    public class ImportModelFileHandlerTests
    {
        private string _tempDir;
        private const string TestFolder = "Assets/__import_model_file_test";

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mcp_imf_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.DeleteAsset(TestFolder);
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { /* ignore */ }
        }

        private static JObject Call(JObject p)
            => JObject.Parse(JsonConvert.SerializeObject(ImportModelFile.HandleCommand(p)));

        private string WriteCubeObj()
        {
            string path = Path.Combine(_tempDir, "cube.obj");
            File.WriteAllText(path,
                "o Cube\n" +
                "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 0 0 1\nv 1 0 1\nv 1 1 1\nv 0 1 1\n" +
                "f 1 2 3 4\nf 5 6 7 8\nf 1 2 6 5\nf 2 3 7 6\nf 3 4 8 7\nf 4 1 5 8\n");
            return path;
        }

        [Test]
        public void MissingSource_ReturnsError()
        {
            JObject resp = Call(new JObject { ["sourcePath"] = Path.Combine(_tempDir, "nope.obj") });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("not found", ((string)resp["error"]).ToLowerInvariant());
        }

        [Test]
        public void UnsupportedExtension_ReturnsError()
        {
            string txt = Path.Combine(_tempDir, "readme.txt");
            File.WriteAllText(txt, "hi");
            JObject resp = Call(new JObject { ["sourcePath"] = txt });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("unsupported", ((string)resp["error"]).ToLowerInvariant());
        }

        [Test]
        public void ImportsObj_ReturnsAssetPathAndGuid()
        {
            string obj = WriteCubeObj();
            JObject resp = Call(new JObject
            {
                ["sourcePath"] = obj,
                ["name"] = "TestCube",
                ["outputFolder"] = TestFolder,
            });
            Assert.AreEqual(true, (bool)resp["success"], resp.ToString());
            string assetPath = (string)resp["data"]["asset_path"];
            StringAssert.StartsWith(TestFolder, assetPath);
            Assert.IsFalse(string.IsNullOrEmpty((string)resp["data"]["asset_guid"]));
            Assert.IsTrue(File.Exists(assetPath), "imported file should exist under Assets");
        }
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

Run (boots a headless editor against the test project):
```bash
python3 tools/local_harness.py --legs editmode --no-warmup --bridge-wait 300 --boot-timeout 300 --junit reports/junit-editmode.xml
```
Expected: FAIL — `ImportModelFile` does not exist (compile error in the EditMode assembly).

- [ ] **Step 3: Implement `ImportModelFile.cs`**

Create `MCPForUnity/Editor/Tools/AssetGen/ImportModelFile.cs`:

```csharp
using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Import;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.AssetGen
{
    /// <summary>
    /// Import a local 3D model file (already on disk — e.g. exported from Blender/Maya) into the
    /// Unity project. DCC-agnostic and key-free: the file is copied under Assets/ and run through
    /// the shared ModelImportPipeline (glTFast/FBX/OBJ/zip handling, scale-normalize, material
    /// settings). Placement into the scene is the caller's job (kept single-purpose).
    /// </summary>
    [McpForUnityTool("import_model_file", AutoRegister = false, Group = "asset_gen")]
    public static class ImportModelFile
    {
        private static readonly string[] SupportedExt = { ".fbx", ".obj", ".glb", ".gltf", ".zip" };

        public static object HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            try
            {
                string source = p.Get("sourcePath");
                if (string.IsNullOrWhiteSpace(source))
                    return new ErrorResponse("'source_path' is required.");

                string srcAbs = ResolveSource(source);
                if (!File.Exists(srcAbs))
                    return new ErrorResponse($"Source file not found: {source}");

                string ext = Path.GetExtension(srcAbs).ToLowerInvariant();
                if (Array.IndexOf(SupportedExt, ext) < 0)
                    return new ErrorResponse(
                        $"Unsupported model extension '{ext}'. Supported: .fbx, .obj, .glb, .gltf, .zip.");

                string baseName = p.Get("name");
                if (string.IsNullOrWhiteSpace(baseName))
                    baseName = Path.GetFileNameWithoutExtension(srcAbs);

                string destRel = StageUnderAssets(srcAbs, baseName, ext, p.Get("outputFolder"));
                AssetDatabase.Refresh();

                var job = new AssetGenJob { TargetSize = p.GetFloat("targetSize", 1f) ?? 1f };
                AssetGenJob result = ModelImportPipeline.ImportInto(job, destRel);

                if (result == null || result.State == AssetGenJobState.Failed)
                    return new ErrorResponse(result?.Error ?? "Import failed.");

                return new SuccessResponse(
                    $"Imported model: {result.AssetPath}",
                    new { asset_path = result.AssetPath, asset_guid = result.AssetGuid });
            }
            catch (Exception e)
            {
                return new ErrorResponse(SecretRedactor.Scrub(e.Message));
            }
        }

        private static string ResolveSource(string source)
        {
            string s = source.Replace('\\', '/');
            if (s == "Assets" || s.StartsWith("Assets/")) return ToAbsolute(s);
            return s; // absolute path on disk
        }

        private static string StageUnderAssets(string srcAbs, string baseName, string ext, string outputFolder)
        {
            string root = !string.IsNullOrWhiteSpace(outputFolder)
                ? outputFolder
                : AssetGenPrefs.OutputRoot + "/Imported";
            if (!root.Replace('\\', '/').StartsWith("Assets"))
                root = AssetGenPrefs.OutputRoot + "/Imported";

            string absRoot = ToAbsolute(root);
            Directory.CreateDirectory(absRoot);

            string safe = SanitizeName(baseName);
            string fileName = safe + ext;
            string abs = Path.Combine(absRoot, fileName);
            int n = 1;
            while (File.Exists(abs)) { fileName = safe + "_" + n++ + ext; abs = Path.Combine(absRoot, fileName); }

            File.Copy(srcAbs, abs);
            return (root.TrimEnd('/') + "/" + fileName).Replace('\\', '/');
        }

        private static string ToAbsolute(string projectRelative)
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            string projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);
            return Path.Combine(projectRoot, projectRelative).Replace('\\', '/');
        }

        private static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "model";
            foreach (char c in Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
            return raw.Trim();
        }
    }
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run:
```bash
python3 tools/local_harness.py --legs editmode --no-warmup --bridge-wait 300 --boot-timeout 300 --junit reports/junit-editmode.xml
```
Expected: PASS — `reports/junit-editmode.xml` shows no failures for `ImportModelFileHandlerTests` (and no new failures elsewhere). Let Unity generate the `.cs.meta` files for the two new scripts.

- [ ] **Step 5: Commit**

```bash
git add MCPForUnity/Editor/Tools/AssetGen/ImportModelFile.cs \
        MCPForUnity/Editor/Tools/AssetGen/ImportModelFile.cs.meta \
        TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/ImportModelFileHandlerTests.cs \
        TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/ImportModelFileHandlerTests.cs.meta
git commit -m "feat(asset-gen): import_model_file C# handler (local model import)"
```

---

### Task 2: Python MCP tool `import_model_file` + pass-through tests

**Files:**
- Create: `Server/src/services/tools/import_model_file.py`
- Test: `Server/tests/test_asset_gen_import_file.py`

**Interfaces:**
- Consumes: `services.registry.mcp_for_unity_tool`, `services.tools.get_unity_instance_from_context`, `transport.unity_transport.send_with_unity_instance`, `transport.legacy.unity_connection.async_send_command_with_retry`.
- Produces: MCP tool `import_model_file(ctx, source_path, name?, output_folder?, target_size?)` sending command `import_model_file` with camelCase params; returns the dict response (or `{success:False, message}` if non-dict).

- [ ] **Step 1: Write the failing pass-through tests**

Create `Server/tests/test_asset_gen_import_file.py`:

```python
"""Tests for the import_model_file asset-gen tool and CLI command (local model import).

Pass-through tool: NO API keys, NO file bytes. Unity transport fully mocked.
"""

import asyncio
import pytest
from unittest.mock import patch, MagicMock, AsyncMock
from click.testing import CliRunner

from cli.commands.asset_gen import asset_gen
from cli.utils.config import CLIConfig
from services.registry import get_registered_tools

from services.tools import import_model_file as mod
from services.tools.import_model_file import import_model_file


COMMAND = "import_model_file"
ALLOWED_KEYS = {"sourcePath", "name", "outputFolder", "targetSize"}


def _call_tool(**kwargs):
    ctx = MagicMock()
    with patch.object(mod, "get_unity_instance_from_context",
                      new=AsyncMock(return_value="unity-1")):
        with patch.object(mod, "send_with_unity_instance",
                          new=AsyncMock(return_value={"success": True, "data": {}})) as mock_send:
            result = asyncio.run(import_model_file(ctx, **kwargs))
    return result, mock_send.call_args.args


def _sent_command(sent_args):
    return sent_args[2]


def _sent_params(sent_args):
    return sent_args[3]


@pytest.fixture
def runner():
    return CliRunner()


@pytest.fixture
def mock_config():
    return CLIConfig(host="127.0.0.1", port=8080, timeout=30, format="text", unity_instance=None)


@pytest.fixture
def cli_runner(runner, mock_config):
    def _invoke(args):
        with patch("cli.commands.asset_gen.get_config", return_value=mock_config):
            with patch("cli.commands.asset_gen.run_command",
                       return_value={"success": True, "message": "OK", "data": {}}) as mock_run:
                result = runner.invoke(asset_gen, args)
                return result, mock_run
    return _invoke


class TestImportModelFileRegistration:
    def test_tool_registered_under_asset_gen_group(self):
        tools = get_registered_tools()
        tool = next((t for t in tools if t["name"] == "import_model_file"), None)
        assert tool is not None
        assert tool["group"] == "asset_gen"


class TestImportModelFileRouting:
    def test_routes_to_command_with_param_mapping(self):
        _, sent = _call_tool(
            source_path="/tmp/cube.fbx", name="Cube",
            output_folder="Assets/Generated/Imported", target_size=2.0,
        )
        assert _sent_command(sent) == COMMAND
        params = _sent_params(sent)
        assert params["sourcePath"] == "/tmp/cube.fbx"
        assert params["name"] == "Cube"
        assert params["outputFolder"] == "Assets/Generated/Imported"
        assert params["targetSize"] == 2.0
        assert "source_path" not in params and "output_folder" not in params and "target_size" not in params

    def test_none_values_stripped(self):
        _, sent = _call_tool(source_path="/tmp/x.obj")
        assert _sent_params(sent) == {"sourcePath": "/tmp/x.obj"}

    def test_no_secret_keys_in_payload(self):
        _, sent = _call_tool(
            source_path="/tmp/a.glb", name="N",
            output_folder="Assets/Generated/Imported", target_size=1.0,
        )
        params = _sent_params(sent)
        assert set(params.keys()).issubset(ALLOWED_KEYS)
        joined = " ".join(params.keys()).lower()
        for forbidden in ("key", "secret", "token", "apikey", "password"):
            assert forbidden not in joined

    def test_non_dict_response_guarded(self):
        ctx = MagicMock()
        with patch.object(mod, "get_unity_instance_from_context",
                          new=AsyncMock(return_value="u")):
            with patch.object(mod, "send_with_unity_instance",
                              new=AsyncMock(return_value=None)):
                result = asyncio.run(import_model_file(ctx, source_path="/tmp/x.obj"))
        assert result["success"] is False


class TestImportModelFileCLI:
    def test_import_model_file_cli(self, cli_runner):
        result, mock_run = cli_runner([
            "import-model-file", "--source-path", "/tmp/cube.fbx",
            "--name", "Cube", "--output-folder", "Assets/Props", "--target-size", "1.5",
        ])
        assert result.exit_code == 0
        command = mock_run.call_args.args[0]
        params = mock_run.call_args.args[1]
        assert command == COMMAND
        assert params["sourcePath"] == "/tmp/cube.fbx"
        assert params["name"] == "Cube"
        assert params["outputFolder"] == "Assets/Props"
        assert params["targetSize"] == 1.5
        assert set(params.keys()).issubset(ALLOWED_KEYS)
```

- [ ] **Step 2: Run to confirm failure**

Run: `cd Server && uv run pytest tests/test_asset_gen_import_file.py -v`
Expected: FAIL — `ModuleNotFoundError: services.tools.import_model_file` (and the CLI test errors on the missing `import-model-file` command).

- [ ] **Step 3: Implement the MCP tool**

Create `Server/src/services/tools/import_model_file.py`:

```python
"""
Defines the import_model_file tool: import a local 3D model file (already on disk,
e.g. exported from Blender) into the Unity project.

Thin pass-through: NO API keys and NO file bytes cross the bridge. The C# side copies
the file under Assets/ and runs the shared model-import pipeline.
"""
from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    group="asset_gen",
    description=(
        "Import a local 3D model file that already exists on disk (e.g. an FBX/OBJ/glTF "
        "exported from Blender or another DCC tool) into the Unity project. The file is copied "
        "under Assets/ and run through Unity's model-import pipeline (scale-normalize, material "
        "settings; glTF requires glTFast). Carries no API keys and no file bytes over the bridge.\n\n"
        "Params: source_path (absolute or Assets-relative path to a .fbx/.obj/.glb/.gltf/.zip), "
        "name, output_folder (under Assets/), target_size. Returns { asset_path, asset_guid }."
    ),
    annotations=ToolAnnotations(
        title="Import Model File",
        destructiveHint=False,
    ),
)
async def import_model_file(
    ctx: Context,
    source_path: Annotated[str, "Path to the model file on disk (.fbx/.obj/.glb/.gltf/.zip)."],
    name: Annotated[str, "Base name for the imported asset."] | None = None,
    output_folder: Annotated[str, "Destination folder under Assets/ for the import."] | None = None,
    target_size: Annotated[float, "Normalize the largest dimension to this size (meters)."] | None = None,
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)

    params_dict = {
        "sourcePath": source_path,
        "name": name,
        "outputFolder": output_folder,
        "targetSize": target_size,
    }
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "import_model_file",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
```

- [ ] **Step 4: Run the routing tests (CLI test still fails)**

Run: `cd Server && uv run pytest tests/test_asset_gen_import_file.py -k "not CLI" -v`
Expected: PASS for routing/registration tests. (The CLI test is implemented in Task 3.)

- [ ] **Step 5: Commit**

```bash
git add Server/src/services/tools/import_model_file.py Server/tests/test_asset_gen_import_file.py
git commit -m "feat(asset-gen): import_model_file MCP tool + pass-through tests"
```

---

### Task 3: Python CLI `import-model-file` command

**Files:**
- Modify: `Server/src/cli/commands/asset_gen.py` (add a command after `import-model`, around line 117)

**Interfaces:**
- Consumes: `run_command("import_model_file", params, config)` (already imported in this module); `get_config`; `@handle_unity_errors`.
- Produces: CLI command `asset-gen import-model-file` sending command `import_model_file` with camelCase params.

- [ ] **Step 1: Confirm the CLI test currently fails**

Run: `cd Server && uv run pytest tests/test_asset_gen_import_file.py::TestImportModelFileCLI -v`
Expected: FAIL — `Error: No such command 'import-model-file'`.

- [ ] **Step 2: Add the CLI command**

In `Server/src/cli/commands/asset_gen.py`, add immediately after the existing `import-model` command (it ends with its `run_command("import_model", ...)` block near line 117):

```python
@asset_gen.command("import-model-file")
@click.option("--source-path", "source_path", required=True,
              help="Path to a local model file (.fbx/.obj/.glb/.gltf/.zip).")
@click.option("--name", default=None, help="Base name for the imported asset.")
@click.option("--output-folder", default=None, help="Destination folder under Assets/.")
@click.option("--target-size", default=None, type=float, help="Normalize largest dimension (meters).")
@handle_unity_errors
def import_model_file(source_path, name, output_folder, target_size):
    """Import a local 3D model file (e.g. a Blender export) into the Unity project."""
    config = get_config()
    params = {
        "sourcePath": source_path,
        "name": name,
        "outputFolder": output_folder,
        "targetSize": target_size,
    }
    params = {k: v for k, v in params.items() if v is not None}
    result = run_command("import_model_file", params, config)
    return result
```

> Note: match the exact return/echo convention of the neighbouring `import_model` CLI command — if it formats output via a helper instead of `return result`, do the same here.

- [ ] **Step 3: Run the full file's tests to confirm pass**

Run: `cd Server && uv run pytest tests/test_asset_gen_import_file.py -v`
Expected: PASS — all routing, registration, and CLI tests green.

- [ ] **Step 4: Run the asset-gen test suite for no regressions**

Run: `cd Server && uv run pytest tests/test_asset_gen_import.py tests/test_asset_gen_import_file.py -v`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Server/src/cli/commands/asset_gen.py
git commit -m "feat(asset-gen): import-model-file CLI command"
```

---

### Task 4: `blender-to-unity` skill + manual-verify checklist + docs note

**Files:**
- Create: `.claude/skills/blender-to-unity/SKILL.md`
- Create: `.claude/skills/blender-to-unity/manual-verify.md`
- Modify: the asset-gen README/docs (the file the Phase 8 commit added the asset-gen section to — locate with `grep -rl "Asset Generation" MCPForUnity *.md docs 2>/dev/null`) to add a short "Blender handoff" note.

**Interfaces:**
- Consumes (client-side orchestration only): `mcp__blender__get_scene_info`, `mcp__blender__get_object_info`, `mcp__blender__execute_blender_code`; UnityMCP `import_model_file`, `manage_gameobject`, `manage_scene`, `manage_camera`; resource `mcpforunity://editor/state`.
- Produces: a documented, repeatable Blender→Unity handoff workflow. No automated test (manual-verify checklist).

- [ ] **Step 1: Author the skill**

Create `.claude/skills/blender-to-unity/SKILL.md`:

```markdown
---
name: blender-to-unity
description: Hand off a model from Blender (via BlenderMCP) into Unity (via MCP for Unity) — export the current Blender model, import it through import_model_file, and place it in the open scene. Use when the user has BlenderMCP and MCP for Unity both connected and wants to bring a Blender model into Unity. Does NOT drive Blender's own generators; BlenderMCP owns how the model got into Blender.
---

# Blender → Unity Model Handoff

Bring whatever model is currently in Blender into the open Unity scene. The seam is the
local filesystem: Blender exports a file, Unity imports it. The two servers never talk
directly.

## Preconditions
- Both `mcp__blender__*` tools and MCP for Unity tools are connected.
- A model exists in the Blender scene (confirm with `mcp__blender__get_scene_info` /
  `get_object_info`). If empty, stop and tell the user — this skill does not generate models.

## Steps
1. **Resolve the Unity project path.** Read `mcpforunity://editor/state` for the project
   root (the editor dataPath's parent). Decide the export format: **FBX by default**
   (built-in importer, zero extra dependencies). Use glTF/.glb only if glTFast is installed
   and PBR fidelity matters.
2. **Export from Blender to a temp path** via `mcp__blender__execute_blender_code`:
   ```python
   import bpy, os, tempfile
   out = os.path.join(tempfile.gettempdir(), "blender_to_unity.fbx")
   # Export the selection if any, else the whole scene:
   bpy.ops.export_scene.fbx(filepath=out, use_selection=bool(bpy.context.selected_objects),
                            apply_unit_scale=True, bake_space_transform=True)
   print(out)
   ```
   (glTF branch: `bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB')`.)
3. **Import into Unity** with `import_model_file`:
   `import_model_file(source_path=<temp path>, name=<asset name>, target_size=<meters, optional>)`.
   It returns `{ asset_path, asset_guid }`.
4. **Place it in the scene.** Ensure the scene has a camera + directional light
   (`manage_scene` / `manage_gameobject`). Instantiate the imported model into the open scene
   at the chosen position via `manage_gameobject(action="create", prefab_path=<asset_path>, ...)`.
5. **Verify** with `manage_camera(action="screenshot", include_image=true)` and report the
   asset path + a screenshot.

## Notes
- FBX is the default because glTFast is optional in MCP for Unity. If the import errors with
  "GLB import requires glTFast", re-export as FBX (or install glTFast from the Dependencies tab).
- Keep one model per handoff; for batches, repeat the loop with distinct names.
- This skill never sends API keys or file bytes over the MCP bridge — Unity reads the file from disk.
```

- [ ] **Step 2: Author the manual-verify checklist**

Create `.claude/skills/blender-to-unity/manual-verify.md`:

```markdown
# Manual Verify — blender-to-unity

Run against a live Blender (BlenderMCP) + Unity (MCP for Unity) pair.

- [ ] A cube/model exists in Blender (`get_scene_info` shows it).
- [ ] FBX export writes a non-empty file to the temp path (printed path exists, size > 0).
- [ ] `import_model_file` returns `success: true` with `asset_path` under `Assets/` and a non-empty `asset_guid`.
- [ ] The imported model appears in the Project window at `asset_path`.
- [ ] The model is instantiated in the open scene and visible in a `manage_camera` screenshot.
- [ ] glTF path: with glTFast installed, a `.glb` export imports successfully; without it,
      the error names glTFast/the Dependencies tab (and FBX still works).
- [ ] No API keys or file bytes appear in any bridge payload (handoff is filesystem-only).
```

- [ ] **Step 3: Add a docs note**

Find the asset-gen docs section and add a short paragraph:
```bash
grep -rl "Asset Generation" MCPForUnity *.md docs 2>/dev/null
```
Add under it:
> **Blender handoff:** With BlenderMCP connected, use the `blender-to-unity` skill to export
> the current Blender model and import it via `import_model_file` (defaults to FBX). BlenderMCP
> handles modeling/generation; MCP for Unity handles import + scene placement.

- [ ] **Step 4: Verify the skill loads / front-matter is valid**

Confirm the `name`/`description` front-matter parses (no tabs, valid YAML) and the file lists
the exact tool names used. There is no automated test for a skill; the manual-verify checklist
is the acceptance gate.

- [ ] **Step 5: Commit**

```bash
git add .claude/skills/blender-to-unity/SKILL.md \
        .claude/skills/blender-to-unity/manual-verify.md
# Add the docs file you modified (exact path from the grep above):
git add <asset-gen-docs-file>
git commit -m "feat(asset-gen): blender-to-unity handoff skill + manual-verify + docs"
```

---

## Self-Review (completed)

- **Spec coverage:** Component A (`import_model_file`) → Tasks 1–3; Component B (`blender-to-unity` skill) → Task 4; FBX-default rule → Task 4 Step 1; testing strategy → Tasks 1–3 tests + Task 4 manual-verify; out-of-scope items respected (no socket bridge, no Blender-gen orchestration). ✅
- **Placeholder scan:** No TBD/TODO; all code blocks are complete. Two intentional "match the neighbouring convention" notes (CLI return shape; docs file path) are concrete instructions, not deferrals. ✅
- **Type consistency:** `sourcePath/name/outputFolder/targetSize` consistent across C# (`ToolParams.Get`/`GetFloat`), Python tool, CLI, and tests; command name `import_model_file` consistent; return `{ asset_path, asset_guid }` consistent. ✅
```
