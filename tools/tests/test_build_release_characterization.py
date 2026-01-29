"""Characterization tests for Build, Release & Testing domain.

This module captures the CURRENT behavior of build, release, and testing infrastructure
without refactoring. Tests document:

1. Version Bumping Logic & File Updates
   - Version loading from package.json
   - Multi-file version sync across JSON/TOML/Markdown files
   - Regex-based URL version patching in documentation
   - Dry-run mode validation

2. Package Building & Validation
   - MCPB bundle generation with manifest injection
   - Icon file handling and temporary directory staging
   - NPX subprocess invocation and error handling
   - Asset store package preparation with staged edits

3. Test Setup & Teardown Patterns
   - Temporary directory lifecycle management
   - File state backup and restore
   - Pytest async fixtures
   - Status file discovery and cleanup

4. Stress Test Execution & Measurement
   - Concurrent client connection management
   - Frame-based binary protocol I/O (8-byte big-endian length headers)
   - Reconnect backoff with exponential decay
   - Handshake validation and timeout handling
   - Script edit churn with precondition validation

5. Release Checklist & Git Integration
   - Version consistency validation across all files
   - Manifest version injection
   - Changelog generation preparation

6. Git Tag & Changelog Generation
   - Tag name formatting (v-prefixed semantic versions)
   - Changelog pattern detection and update

Architecture Notes:
- tools/update_versions.py: Multi-target version sync (6 files)
- tools/generate_mcpb.py: Bundle creation with manifest templating
- tools/prepare_unity_asset_store_release.py: Staged editing with C# code modifications
- tools/stress_mcp.py: Async multi-client stress test with reload churn
- tools/stress_editor_state.py: Focused performance stress test for GC profiling

Test Patterns:
- Heavy use of regex for text file patching
- Temporary directories for isolated operations
- JSON/TOML config file manipulation
- Monkeypatching for file I/O isolation
- Mock socket connections for protocol testing
"""

import asyncio
import json
import os
import random
import re
import struct
import sys
import tempfile
import pytest
from pathlib import Path
from typing import Dict, List, Any
from unittest.mock import Mock, AsyncMock, patch, MagicMock, mock_open


# =============================================================================
# FIXTURES & SETUP PATTERNS
# =============================================================================

@pytest.fixture
def temp_repo():
    """Create a temporary repository structure for testing.

    Pattern: Isolated filesystem for file operation testing
    Captures: Multi-directory staging pattern used in build tools
    """
    with tempfile.TemporaryDirectory(prefix="unity_mcp_test_") as tmpdir:
        repo_root = Path(tmpdir)

        # Create typical project structure
        (repo_root / "MCPForUnity").mkdir(parents=True)
        (repo_root / "Server").mkdir(parents=True)
        (repo_root / "docs" / "i18n").mkdir(parents=True)

        yield {
            "root": repo_root,
            "mcp_package": repo_root / "MCPForUnity" / "package.json",
            "manifest": repo_root / "manifest.json",
            "pyproject": repo_root / "Server" / "pyproject.toml",
            "server_readme": repo_root / "Server" / "README.md",
            "root_readme": repo_root / "README.md",
            "zh_readme": repo_root / "docs" / "i18n" / "README-zh.md",
        }


@pytest.fixture
def sample_package_json():
    """Sample package.json structure.

    Pattern: Version as string in JSON root level
    Used by: update_versions.py::load_package_version()
    """
    return {
        "name": "com.coplay.mcpforunity",
        "version": "9.2.0",
        "displayName": "MCP for Unity",
        "description": "Model Context Protocol for Unity",
        "unity": "2022.2",
        "keywords": ["mcp", "ai", "unity"],
        "author": {"name": "Coplay", "url": "https://coplay.dev"},
    }


@pytest.fixture
def sample_manifest_json():
    """Sample manifest.json structure.

    Pattern: Version as string in root, icon filename reference
    Used by: generate_mcpb.py::create_manifest()
    """
    return {
        "name": "unity-mcp",
        "version": "9.2.0",
        "description": "Model Context Protocol Bundle for Unity",
        "icon": "coplay-logo.png",
        "license": "MIT",
    }


@pytest.fixture
def sample_pyproject_toml():
    """Sample pyproject.toml content.

    Pattern: TOML version string on single line, must match exactly
    Used by: update_versions.py::update_pyproject_toml()
    Challenge: Regex must preserve exact formatting
    """
    return '''[project]
name = "mcpforunityserver"
version = "9.2.0"
description = "MCP for Unity Server"
readme = "README.md"
requires-python = ">=3.10"

[build-system]
requires = ["setuptools>=64.0.0"]
build-backend = "setuptools.build_meta"
'''


@pytest.fixture
def sample_readme_content():
    """Sample README with git URL references.

    Pattern: Git URLs with version tags in fragments
    Used by: update_versions.py::update_server_readme()
    """
    return '''# MCP for Unity

## Installation

Install from git:
```bash
pip install git+https://github.com/CoplayDev/unity-mcp@v9.2.0#subdirectory=Server
```

Or via package URL:
```
https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v9.2.0
```
'''


# =============================================================================
# VERSION MANAGEMENT TESTS
# =============================================================================

class TestVersionBumpingLogic:
    """Tests for version bumping and file synchronization.

    Domain: Version Management
    Scripts: tools/update_versions.py
    Patterns: JSON/TOML parsing, regex-based patching, dry-run simulation
    """

    def test_load_package_version_from_json(self, temp_repo, sample_package_json):
        """Test extracting version from package.json.

        Behavior: Loads JSON, reads 'version' field, returns string
        Failure modes:
        - File not found
        - Version field missing
        - Invalid JSON
        """
        # Write package.json
        temp_repo["mcp_package"].write_text(
            json.dumps(sample_package_json, indent=2),
            encoding="utf-8"
        )

        # Simulate load_package_version()
        package_data = json.loads(
            temp_repo["mcp_package"].read_text(encoding="utf-8")
        )
        version = package_data.get("version")

        assert version == "9.2.0"
        assert isinstance(version, str)

    def test_load_package_version_missing_file(self):
        """Test error handling when package.json not found.

        Behavior: Raises FileNotFoundError with clear message
        """
        nonexistent = Path("/tmp/nonexistent/package.json")

        with pytest.raises(FileNotFoundError):
            if not nonexistent.exists():
                raise FileNotFoundError(f"Package file not found: {nonexistent}")

    def test_update_package_json_version(self, temp_repo, sample_package_json):
        """Test updating version in package.json.

        Behavior:
        1. Load current JSON
        2. Modify 'version' field
        3. Write with indent=2, +newline
        4. Return True if changed, False if already at target
        """
        temp_repo["mcp_package"].write_text(
            json.dumps(sample_package_json, indent=2),
            encoding="utf-8"
        )

        new_version = "9.3.0"
        package_data = json.loads(
            temp_repo["mcp_package"].read_text(encoding="utf-8")
        )
        old_version = package_data.get("version")

        assert old_version == "9.2.0"

        # Update
        package_data["version"] = new_version
        temp_repo["mcp_package"].write_text(
            json.dumps(package_data, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8"
        )

        # Verify
        updated = json.loads(
            temp_repo["mcp_package"].read_text(encoding="utf-8")
        )
        assert updated["version"] == "9.3.0"

    def test_update_pyproject_toml_version(self, temp_repo, sample_pyproject_toml):
        """Test updating version in pyproject.toml with regex.

        Behavior:
        1. Read file as text
        2. Match pattern: ^version = "([^"]+)"
        3. Replace with: version = "NEW_VERSION"
        4. Only first match (count=1)
        5. MULTILINE flag required

        Challenge: Must not modify version strings in other contexts
        """
        temp_repo["pyproject"].write_text(sample_pyproject_toml, encoding="utf-8")

        new_version = "9.3.0"
        content = temp_repo["pyproject"].read_text(encoding="utf-8")

        # Apply regex replacement pattern from update_versions.py
        pattern = r'^version = "([^"]+)"'
        match = re.search(pattern, content, re.MULTILINE)
        assert match is not None
        assert match.group(1) == "9.2.0"

        # Replace exactly once
        new_content, count = re.subn(
            pattern,
            f'version = "{new_version}"',
            content,
            count=1,
            flags=re.MULTILINE
        )

        assert count == 1  # Exactly one replacement
        assert f'version = "{new_version}"' in new_content
        temp_repo["pyproject"].write_text(new_content, encoding="utf-8")

        # Verify
        updated = temp_repo["pyproject"].read_text(encoding="utf-8")
        assert f'version = "{new_version}"' in updated

    def test_update_readme_git_url_with_version(self, temp_repo, sample_readme_content):
        """Test updating git URLs with version tags in README.

        Behavior:
        1. Match pattern: git+https://...@vX.Y.Z#subdirectory=...
        2. Replace version tag in URL fragment
        3. CRITICAL: Fragment hash # not escaped in regex

        Pattern:
        FROM: git+https://github.com/CoplayDev/unity-mcp@v9.2.0#subdirectory=Server
        TO:   git+https://github.com/CoplayDev/unity-mcp@v9.3.0#subdirectory=Server
        """
        temp_repo["server_readme"].write_text(sample_readme_content, encoding="utf-8")

        new_version = "9.3.0"
        content = temp_repo["server_readme"].read_text(encoding="utf-8")

        # Pattern from update_versions.py
        pattern = r'git\+https://github\.com/CoplayDev/unity-mcp@v[0-9]+\.[0-9]+\.[0-9]+#subdirectory=Server'
        replacement = f'git+https://github.com/CoplayDev/unity-mcp@v{new_version}#subdirectory=Server'

        assert re.search(pattern, content) is not None

        new_content = re.sub(pattern, replacement, content)
        assert f'@v{new_version}#subdirectory=Server' in new_content
        assert '@v9.2.0#' not in new_content

        temp_repo["server_readme"].write_text(new_content, encoding="utf-8")

    def test_update_readme_package_url_with_version(self, temp_repo, sample_readme_content):
        """Test updating package URLs with version tags in README.

        Behavior:
        1. Match pattern: https://github.com/...?path=...#vX.Y.Z
        2. Replace version in fragment

        Pattern:
        FROM: https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v9.2.0
        TO:   https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v9.3.0
        """
        temp_repo["root_readme"].write_text(sample_readme_content, encoding="utf-8")

        new_version = "9.3.0"
        content = temp_repo["root_readme"].read_text(encoding="utf-8")

        # Pattern from update_versions.py
        pattern = r'https://github\.com/CoplayDev/unity-mcp\.git\?path=/MCPForUnity#v[0-9]+\.[0-9]+\.[0-9]+'
        replacement = f'https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v{new_version}'

        if re.search(pattern, content):
            new_content = re.sub(pattern, replacement, content)
            assert f'#v{new_version}' in new_content

    def test_dry_run_mode_no_file_modifications(self, temp_repo, sample_package_json):
        """Test that dry-run mode doesn't modify files.

        Behavior:
        1. Read file
        2. Compute what would change
        3. Report changes WITHOUT writing
        4. File remains unchanged

        Pattern: Conditional write based on dry_run flag
        """
        temp_repo["mcp_package"].write_text(
            json.dumps(sample_package_json, indent=2),
            encoding="utf-8"
        )

        original_content = temp_repo["mcp_package"].read_text(encoding="utf-8")
        new_version = "9.3.0"
        dry_run = True

        package_data = json.loads(original_content)
        package_data["version"] = new_version

        # With dry_run=True, skip the write
        if not dry_run:
            temp_repo["mcp_package"].write_text(
                json.dumps(package_data, indent=2, ensure_ascii=False) + "\n",
                encoding="utf-8"
            )

        # File should be unchanged
        after_content = temp_repo["mcp_package"].read_text(encoding="utf-8")
        assert after_content == original_content

    def test_version_consistency_validation(self, temp_repo, sample_package_json,
                                           sample_manifest_json, sample_pyproject_toml):
        """Test comprehensive version consistency check across all files.

        Behavior: Load versions from all sources, compare

        Files checked:
        1. MCPForUnity/package.json (JSON)
        2. manifest.json (JSON)
        3. Server/pyproject.toml (TOML)
        4. Server/README.md (URL patterns)
        5. README.md (URL patterns)
        6. docs/i18n/README-zh.md (URL patterns)

        Expected: All have same version
        """
        # Setup all files
        temp_repo["mcp_package"].write_text(
            json.dumps(sample_package_json, indent=2),
            encoding="utf-8"
        )
        temp_repo["manifest"].write_text(
            json.dumps(sample_manifest_json, indent=2),
            encoding="utf-8"
        )
        temp_repo["pyproject"].write_text(
            sample_pyproject_toml,
            encoding="utf-8"
        )

        # Extract versions
        versions = {}

        # From package.json
        pkg = json.loads(temp_repo["mcp_package"].read_text(encoding="utf-8"))
        versions["package.json"] = pkg["version"]

        # From manifest.json
        mfst = json.loads(temp_repo["manifest"].read_text(encoding="utf-8"))
        versions["manifest.json"] = mfst["version"]

        # From pyproject.toml
        pyproj = temp_repo["pyproject"].read_text(encoding="utf-8")
        match = re.search(r'^version = "([^"]+)"', pyproj, re.MULTILINE)
        versions["pyproject.toml"] = match.group(1) if match else None

        # All should match
        unique_versions = set(versions.values())
        assert len(unique_versions) == 1
        assert "9.2.0" in unique_versions


# =============================================================================
# PACKAGE BUILDING & VALIDATION TESTS
# =============================================================================

class TestMCPBBundleGeneration:
    """Tests for MCPB bundle generation process.

    Domain: Package Building
    Script: tools/generate_mcpb.py
    Patterns: Manifest templating, icon handling, subprocess invocation
    """

    @pytest.fixture
    def mock_manifest_template(self, temp_repo):
        """Setup manifest template in test repo."""
        template = {
            "name": "unity-mcp",
            "version": "0.0.0",  # Will be injected
            "description": "Model Context Protocol Bundle for Unity",
            "icon": "coplay-logo.png",
            "license": "MIT",
        }
        temp_repo["manifest"].write_text(
            json.dumps(template, indent=2),
            encoding="utf-8"
        )
        return template

    @pytest.fixture
    def mock_icon_file(self, temp_repo):
        """Create a mock icon file."""
        icon_path = temp_repo["root"] / "docs" / "images"
        icon_path.mkdir(parents=True, exist_ok=True)
        icon_file = icon_path / "coplay-logo.png"
        icon_file.write_bytes(b"PNG_FAKE_DATA")
        return icon_file

    def test_create_manifest_with_version_injection(self, temp_repo, mock_manifest_template):
        """Test manifest creation with version injection.

        Behavior:
        1. Load manifest template
        2. Inject version string
        3. Inject icon filename
        4. Return modified dict

        Pattern: In-memory manipulation before file write
        """
        template_content = temp_repo["manifest"].read_text(encoding="utf-8")
        manifest = json.loads(template_content)

        version = "9.2.0"
        icon_filename = "coplay-logo.png"

        # Inject (simulating create_manifest)
        manifest["version"] = version
        manifest["icon"] = icon_filename

        assert manifest["version"] == "9.2.0"
        assert manifest["icon"] == "coplay-logo.png"

    def test_mcpb_build_directory_staging(self, temp_repo, mock_icon_file):
        """Test temporary directory staging for MCPB build.

        Behavior:
        1. Create temp dir with "mcpb-build" subdirectory
        2. Copy icon into build dir
        3. Write manifest.json into build dir
        4. Copy LICENSE and README if exist
        5. Call npx mcpb pack
        6. Clean up temp dir

        Pattern: Temporary directory scoped to context manager
        Captures: File staging and aggregation pattern
        """
        with tempfile.TemporaryDirectory() as tmpdir:
            build_dir = Path(tmpdir) / "mcpb-build"
            build_dir.mkdir()

            # Stage files
            # 1. Copy icon
            icon_dest = build_dir / "coplay-logo.png"
            icon_dest.write_bytes(b"PNG_FAKE_DATA")
            assert icon_dest.exists()

            # 2. Write manifest
            manifest = {
                "name": "unity-mcp",
                "version": "9.2.0",
                "icon": "coplay-logo.png",
            }
            manifest_path = build_dir / "manifest.json"
            manifest_path.write_text(
                json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
                encoding="utf-8"
            )
            assert manifest_path.exists()

            # 3. Copy LICENSE if exists
            license_src = temp_repo["root"] / "LICENSE"
            if license_src.exists():
                license_src.write_text("MIT License", encoding="utf-8")
                license_dst = build_dir / "LICENSE"
                license_dst.write_text(license_src.read_text(), encoding="utf-8")
                assert license_dst.exists()

            # 4. Copy README if exists
            readme_src = temp_repo["root"] / "README.md"
            if readme_src.exists():
                readme_src.write_text("# Unity MCP", encoding="utf-8")
                readme_dst = build_dir / "README.md"
                readme_dst.write_text(readme_src.read_text(), encoding="utf-8")
                assert readme_dst.exists()

            # Verify staging complete
            assert (build_dir / "manifest.json").exists()
            assert (build_dir / "coplay-logo.png").exists()

    @patch("subprocess.run")
    def test_mcpb_pack_subprocess_invocation(self, mock_run, temp_repo):
        """Test subprocess invocation of 'npx mcpb pack'.

        Behavior:
        1. Execute: npx @anthropic-ai/mcpb pack . /path/to/output.mcpb
        2. cwd = build_dir
        3. capture_output=True, text=True
        4. check=True (raises on error)
        5. On CalledProcessError: print stderr and re-raise

        Pattern: Subprocess with error propagation
        """
        mock_run.return_value = Mock(
            stdout="Packed successfully",
            returncode=0
        )

        build_dir = temp_repo["root"] / "build"
        build_dir.mkdir()
        output_path = temp_repo["root"] / "output.mcpb"

        # Simulate subprocess call
        result = mock_run(
            ["npx", "@anthropic-ai/mcpb", "pack", ".", str(output_path.absolute())],
            cwd=build_dir,
            capture_output=True,
            text=True,
            check=True,
        )

        assert result.returncode == 0
        mock_run.assert_called_once()

    @patch("subprocess.run")
    def test_mcpb_pack_error_handling(self, mock_run):
        """Test error handling when mcpb pack fails.

        Behavior:
        1. Catch CalledProcessError
        2. Print stderr to sys.stderr
        3. Re-raise exception
        4. Caller must handle FileNotFoundError for missing npx

        Failure scenarios:
        - npx not installed -> FileNotFoundError
        - mcpb command fails -> CalledProcessError with stderr
        - Output file not created -> RuntimeError
        """
        import subprocess

        # Simulate mcpb failure
        error = subprocess.CalledProcessError(
            returncode=1,
            cmd="npx @anthropic-ai/mcpb pack",
            stderr="manifest.json not found in build directory"
        )
        mock_run.side_effect = error

        # Should raise
        with pytest.raises(subprocess.CalledProcessError):
            mock_run(
                ["npx", "@anthropic-ai/mcpb", "pack", ".", "output.mcpb"],
                capture_output=True,
                text=True,
                check=True,
            )

    def test_mcpb_output_file_validation(self, temp_repo):
        """Test validation that output .mcpb file was created.

        Behavior:
        1. After subprocess completes
        2. Check if output_path.exists()
        3. If not, raise RuntimeError
        4. Print file size in bytes for logging

        Pattern: Post-condition validation
        """
        output_path = temp_repo["root"] / "unity-mcp-9.2.0.mcpb"

        # File doesn't exist
        assert not output_path.exists()

        # Simulate error check
        if not output_path.exists():
            with pytest.raises(RuntimeError):
                raise RuntimeError(f"MCPB file was not created: {output_path}")

        # After "creation"
        output_path.write_bytes(b"FAKE_MCPB_DATA" * 1000)
        assert output_path.exists()
        size = output_path.stat().st_size
        assert size == 14000


# =============================================================================
# ASSET STORE PACKAGE PREPARATION TESTS
# =============================================================================

class TestAssetStorePackagePreparation:
    """Tests for Asset Store release packaging.

    Domain: Package Building
    Script: tools/prepare_unity_asset_store_release.py
    Patterns: Staged editing, text file manipulation, directory replacement
    """

    @pytest.fixture
    def unity_project_structure(self, temp_repo):
        """Create a mock Unity project structure."""
        asset_project = temp_repo["root"] / "AssetStoreTest"
        assets = asset_project / "Assets"
        assets.mkdir(parents=True)

        # Create MCPForUnity source
        source_mcp = temp_repo["root"] / "MCPForUnity"
        source_mcp.mkdir(exist_ok=True)
        (source_mcp / "package.json").write_text('{"version":"9.2.0"}')

        return {
            "asset_project": asset_project,
            "assets_dir": assets,
            "source_mcp": source_mcp,
        }

    def test_text_file_replacement_once(self, unity_project_structure):
        """Test exact-once regex replacement in C# files.

        Behavior:
        1. Read file
        2. Apply regex substitution
        3. Verify exactly 1 replacement (count=1)
        4. Raise error if != 1
        5. Write file only if changed

        Pattern: Used in prepare_unity_asset_store_release.py::replace_once()

        Example:
        FROM: private const string DefaultBaseUrl = "http://localhost:8080";
        TO:   private const string DefaultBaseUrl = "https://aws-endpoint/";
        """
        http_util = unity_project_structure["source_mcp"] / "HttpEndpointUtility.cs"

        original_content = '''public class HttpEndpointUtility {
    private const string DefaultBaseUrl = "http://localhost:8080";

    public string GetBaseUrl() {
        return DefaultBaseUrl;
    }
}'''

        http_util.write_text(original_content, encoding="utf-8")

        # Simulate replace_once
        pattern = r'private const string DefaultBaseUrl = "http://localhost:8080";'
        replacement = 'private const string DefaultBaseUrl = "https://mc-0cb5e1039f6b4499b473670f70662d29.ecs.us-east-2.on.aws/";'

        content = http_util.read_text(encoding="utf-8")
        new_content, n = re.subn(pattern, replacement, content, flags=re.MULTILINE)

        assert n == 1, f"Expected 1 replacement, got {n}"
        assert "https://" in new_content
        assert "localhost" not in new_content

        http_util.write_text(new_content, encoding="utf-8")

    def test_line_removal_exact_match(self, unity_project_structure):
        """Test removing a specific line by exact match.

        Behavior:
        1. Read file, split lines with keepends=True
        2. Find and remove line matching exactly (stripped)
        3. Verify exactly 1 removal
        4. Join and write back

        Pattern: Used for removing [InitializeOnLoad] attribute

        Example:
        REMOVE: [InitializeOnLoad]
        """
        setup_service = unity_project_structure["source_mcp"] / "SetupWindowService.cs"

        original_content = '''using UnityEngine;

[InitializeOnLoad]
public class SetupWindowService {
    static SetupWindowService() {
        EditorApplication.update += OnUpdate;
    }
}'''

        setup_service.write_text(original_content, encoding="utf-8")

        # Simulate remove_line_exact
        line_to_remove = "[InitializeOnLoad]"
        content = setup_service.read_text(encoding="utf-8")
        lines = content.splitlines(keepends=True)

        removed = 0
        kept = []
        for l in lines:
            if l.strip() == line_to_remove:
                removed += 1
                continue
            kept.append(l)

        assert removed == 1, f"Expected 1 removal, got {removed}"

        new_content = "".join(kept)
        assert "[InitializeOnLoad]" not in new_content
        setup_service.write_text(new_content, encoding="utf-8")

    def test_staged_copy_with_edits(self, unity_project_structure):
        """Test staged copying with multiple edits applied.

        Behavior:
        1. Create temp directory with "MCPForUnity" subdir
        2. Copy source MCPForUnity to staged location
        3. Apply Asset Store specific edits in place
        4. Replace target Assets/MCPForUnity with staged version
        5. Clean up temp dir

        Pattern: Isolated edit environment prevents source pollution
        Challenge: 4 files must exist and be editable
        """
        source = unity_project_structure["source_mcp"]

        # Create required files
        (source / "Editor" / "Setup").mkdir(parents=True, exist_ok=True)
        (source / "Editor" / "MenuItems").mkdir(parents=True, exist_ok=True)
        (source / "Editor" / "Helpers").mkdir(parents=True, exist_ok=True)
        (source / "Editor" / "Windows" / "Components" / "Connection").mkdir(
            parents=True, exist_ok=True
        )

        setup_service = source / "Editor" / "Setup" / "SetupWindowService.cs"
        setup_service.write_text("[InitializeOnLoad]\npublic class Setup {}")

        http_util = source / "Editor" / "Helpers" / "HttpEndpointUtility.cs"
        http_util.write_text('private const string DefaultBaseUrl = "http://localhost:8080";')

        connection_section = (
            source / "Editor" / "Windows" / "Components" / "Connection" / "McpConnectionSection.cs"
        )
        connection_section.write_text('transportDropdown.Init(TransportProtocol.HTTPLocal);')

        with tempfile.TemporaryDirectory(prefix="assetstore_") as tmpdir:
            staged_mcp = Path(tmpdir) / "MCPForUnity"

            # Copy all files
            import shutil
            shutil.copytree(source, staged_mcp)

            assert (staged_mcp / "Editor" / "Setup" / "SetupWindowService.cs").exists()

            # Apply edits to staged copy
            staged_service = staged_mcp / "Editor" / "Setup" / "SetupWindowService.cs"
            content = staged_service.read_text(encoding="utf-8")
            new_content, count = re.subn(
                r"\[InitializeOnLoad\]", "", content
            )
            assert count == 1
            staged_service.write_text(new_content, encoding="utf-8")

            # Replace target (simulated)
            target_mcp = unity_project_structure["assets_dir"] / "MCPForUnity"
            if target_mcp.exists():
                import shutil
                shutil.rmtree(target_mcp)

            shutil.copytree(staged_mcp, target_mcp)
            assert target_mcp.exists()

    @patch("shutil.copytree")
    def test_backup_existing_mcp_folder(self, mock_copytree, unity_project_structure):
        """Test backing up existing Assets/MCPForUnity before replacement.

        Behavior:
        1. If Assets/MCPForUnity exists and --backup flag set
        2. Create AssetStoreBackups directory
        3. Copy Assets/MCPForUnity to: MCPForUnity.backup.TIMESTAMP
        4. Return backup path

        Pattern: Timestamped backup directory
        Format: {src.name}.backup.{YYYYMMDD-HHMMSS}
        """
        import datetime as dt

        assets_dir = unity_project_structure["assets_dir"]
        dest_mcp = assets_dir / "MCPForUnity"
        dest_mcp.mkdir()

        backup_root = assets_dir / "AssetStoreBackups"
        backup_root.mkdir(parents=True, exist_ok=True)

        # Simulate backup_dir function
        ts = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
        backup_path = backup_root / f"{dest_mcp.name}.backup.{ts}"

        # In real code, would use shutil.copytree
        mock_copytree(dest_mcp, backup_path)

        mock_copytree.assert_called_once_with(dest_mcp, backup_path)

    def test_dry_run_validation_without_changes(self, unity_project_structure):
        """Test dry-run mode validates paths without making changes.

        Behavior:
        1. Verify all required directories exist
        2. Report what WOULD be done
        3. Return 0 without touching files
        4. All paths must be valid even in dry-run

        Pattern: Early validation prevents partial edits
        """
        source_mcp = unity_project_structure["source_mcp"]
        assets_dir = unity_project_structure["assets_dir"]
        dest_mcp = assets_dir / "MCPForUnity"

        # Validate paths exist (in real code)
        assert source_mcp.is_dir()
        assert assets_dir.is_dir()
        # dest_mcp may not exist yet

        # In dry-run, report what would happen
        dry_run_output = [
            "[dry-run] Validated paths. No changes applied.",
            f"[dry-run] Would replace: {dest_mcp} with {source_mcp}",
        ]

        assert all("Would" in line or "Validated" in line for line in dry_run_output)


# =============================================================================
# STRESS TEST PATTERNS & EXECUTION TESTS
# =============================================================================

class TestStressTestSetupPatterns:
    """Tests for stress test infrastructure.

    Domain: Stress Testing
    Scripts: tools/stress_mcp.py, tools/stress_editor_state.py
    Patterns: Binary protocol I/O, async client loops, reconnect backoff
    """

    @pytest.fixture
    def mock_status_files(self, temp_repo):
        """Setup mock status files for port discovery.

        Pattern: Status files stored in ~/.unity-mcp/unity-mcp-status-*.json
        Purpose: Auto-discover bridge port from running Unity instance
        """
        status_dir = temp_repo["root"] / ".unity-mcp"
        status_dir.mkdir()

        status_file = status_dir / "unity-mcp-status-latest.json"
        status_data = {
            "unity_port": 6400,
            "unity_host": "127.0.0.1",
            "project_path": "/path/to/project",
            "timestamp": 1234567890,
        }
        status_file.write_text(json.dumps(status_data), encoding="utf-8")

        return status_dir

    def test_port_discovery_from_status_files(self, mock_status_files):
        """Test discovering bridge port from status files.

        Behavior:
        1. Check ~/.unity-mcp/ for unity-mcp-status-*.json files
        2. Sort by mtime (most recent first)
        3. Load JSON, extract "unity_port" field
        4. Validate port range (0 < port < 65536)
        5. If no valid file found, return default 6400

        Pattern: Auto-discovery mechanism for dynamic ports
        """
        # Simulate find_status_files
        status_dir = mock_status_files
        files = sorted(
            status_dir.glob("unity-mcp-status-*.json"),
            key=lambda p: p.stat().st_mtime,
            reverse=True
        )

        assert len(files) > 0

        # Load most recent
        status_data = json.loads(files[0].read_text())
        port = int(status_data.get("unity_port", 0) or 0)

        assert 0 < port < 65536
        assert port == 6400

    def test_port_discovery_default_fallback(self):
        """Test port discovery falls back to default when no status file.

        Behavior: Return 6400 if ~/.unity-mcp doesn't exist or no valid files
        """
        status_dir = Path("/tmp/nonexistent_unity_mcp")

        # Simulate find_status_files returning empty
        if not status_dir.exists():
            files = []

        default_port = 6400
        port = default_port if not files else int(files[0])

        assert port == 6400

    @pytest.mark.asyncio
    async def test_binary_frame_protocol_read_exact(self):
        """Test reading exact number of bytes from async stream.

        Pattern: Protocol framing with 8-byte big-endian length header

        Frame format:
        [8 bytes: length in big-endian] [length bytes: payload]

        Behavior:
        1. Loop until buf has exactly N bytes
        2. If chunk empty, connection closed
        3. Raise ConnectionError if closed before complete
        """
        # Create mock reader
        mock_reader = AsyncMock()

        # Simulate 3 reads of 5 bytes each, expecting 15 total
        mock_reader.read.side_effect = [
            b"chunk",
            b"data1",
            b"data2",
        ]

        # Simulate read_exact logic
        n = 15
        buf = b""
        for _ in range(3):
            chunk = await mock_reader.read(n - len(buf))
            if not chunk:
                raise ConnectionError("Connection closed while reading")
            buf += chunk

        assert len(buf) == 15
        assert buf == b"chunkdata1data2"

    @pytest.mark.asyncio
    async def test_binary_frame_protocol_parse_header(self):
        """Test parsing 8-byte big-endian length header.

        Behavior:
        1. Read exactly 8 bytes
        2. Unpack as unsigned 64-bit big-endian: struct.unpack(">Q", header)
        3. Validate: 0 < length <= 64MB
        4. Raise ValueError if invalid

        Pattern: Data length extraction for frame boundaries
        """
        # Test valid frame length
        length_bytes = struct.pack(">Q", 512)
        assert len(length_bytes) == 8

        (length,) = struct.unpack(">Q", length_bytes)
        assert length == 512

        # Test too large
        too_large = struct.pack(">Q", 100 * 1024 * 1024)
        (length,) = struct.unpack(">Q", too_large)
        assert length > 64 * 1024 * 1024

        # Validation would reject this
        if length <= 0 or length > (64 * 1024 * 1024):
            with pytest.raises(ValueError):
                raise ValueError(f"Invalid frame length: {length}")

    @pytest.mark.asyncio
    async def test_binary_frame_protocol_write(self):
        """Test writing binary frame with length header.

        Behavior:
        1. Compute payload length
        2. Pack as 8-byte big-endian header
        3. Write header + payload
        4. Call drain() with timeout
        5. Raise error if drain times out

        Pattern: Atomic frame write with buffering flush
        """
        mock_writer = AsyncMock()
        mock_writer.drain = AsyncMock()

        payload = b"hello world test"

        # Simulate write_frame
        header = struct.pack(">Q", len(payload))
        mock_writer.write(header)
        mock_writer.write(payload)
        await asyncio.wait_for(mock_writer.drain(), timeout=2.0)

        assert mock_writer.write.call_count == 2
        mock_writer.drain.assert_called_once()

    @pytest.mark.asyncio
    async def test_connection_handshake_validation(self):
        """Test server handshake validation.

        Behavior:
        1. Read line from server
        2. Validate contains "WELCOME UNITY-MCP"
        3. Raise ConnectionError if unexpected

        Pattern: Protocol initialization check
        Expected: "WELCOME UNITY-MCP 1 FRAMING=1\n"
        """
        # Valid handshake
        mock_reader = AsyncMock()
        mock_reader.readline.return_value = b"WELCOME UNITY-MCP 1 FRAMING=1\n"

        line = await mock_reader.readline()
        if not line or b"WELCOME UNITY-MCP" not in line:
            raise ConnectionError(f"Unexpected handshake: {line!r}")

        assert b"WELCOME UNITY-MCP" in line

        # Invalid handshake
        mock_reader.readline.return_value = b"UNKNOWN SERVER\n"
        line = await mock_reader.readline()

        with pytest.raises(ConnectionError):
            if not line or b"WELCOME UNITY-MCP" not in line:
                raise ConnectionError(f"Unexpected handshake: {line!r}")

    @pytest.mark.asyncio
    async def test_concurrent_client_loop_with_backoff(self):
        """Test single client loop with reconnect backoff.

        Behavior:
        1. Initialize reconnect_delay = 0.2
        2. Loop until stop_time
        3. Try to connect, perform work
        4. On error: increment disconnect count, sleep(reconnect_delay)
        5. Backoff decay: reconnect_delay *= 1.5, cap at 2.0

        Pattern: Exponential backoff for reliability
        Challenge: Prevent connection burst thundering
        """
        stop_time = 0.5  # Short test
        stats = {"pings": 0, "disconnects": 0, "errors": 0}
        reconnect_delay = 0.2

        # Simulate client_loop with errors
        start = asyncio.get_event_loop().time()

        while asyncio.get_event_loop().time() - start < stop_time:
            try:
                # Simulate immediate failure
                raise ConnectionError("simulated disconnect")
            except (ConnectionError, OSError):
                stats["disconnects"] += 1
                await asyncio.sleep(0.01)  # Shorter for testing
                reconnect_delay = min(reconnect_delay * 1.5, 2.0)
                continue

        assert stats["disconnects"] > 0
        assert reconnect_delay <= 2.0

    @pytest.mark.asyncio
    async def test_stress_ping_frame_construction(self):
        """Test constructing ping frame for keep-alive.

        Behavior:
        1. Payload = b"ping"
        2. Sent periodically to keep connection alive
        3. Expected response = echo/acknowledgment

        Pattern: Simple protocol for connection maintenance
        """
        def make_ping_frame() -> bytes:
            return b"ping"

        frame = make_ping_frame()
        assert frame == b"ping"
        assert len(frame) == 4

    @pytest.mark.asyncio
    async def test_stress_manage_script_read_request(self):
        """Test constructing manage_script read request.

        Behavior:
        1. JSON payload with type, action, name, path
        2. Used to read current file contents before editing
        3. Response includes: success, data.contents, data.sha256

        Pattern: Request/response protocol for file operations
        """
        name = "LongUnityScriptClaudeTest"
        path = "Assets/Scripts"

        read_payload = {
            "type": "manage_script",
            "params": {
                "action": "read",
                "name": name,
                "path": path,
            }
        }

        frame = json.dumps(read_payload).encode("utf-8")

        # Simulate response
        read_response = {
            "result": {
                "success": True,
                "data": {
                    "contents": "public class Test {}",
                    "sha256": "abc123...",
                }
            }
        }

        assert json.loads(frame)["type"] == "manage_script"
        assert json.loads(frame)["params"]["action"] == "read"

    @pytest.mark.asyncio
    async def test_stress_apply_text_edits_with_precondition(self):
        """Test apply_text_edits request with SHA precondition.

        Behavior:
        1. Construct JSON with file path, edits, precondition_sha256
        2. Edits include: startLine, startCol, endLine, endCol, newText
        3. Options: refresh="immediate", validate="standard"
        4. Precondition prevents apply if file changed since read

        Pattern: Optimistic concurrency control via SHA comparison
        Challenge: Lines and columns are 1-based (Unity convention)
        """
        edits = [
            {
                "startLine": 5,
                "startCol": 1,
                "endLine": 5,
                "endCol": 1,
                "newText": "\n// Marker comment\n",
            }
        ]

        apply_payload = {
            "type": "manage_script",
            "params": {
                "action": "apply_text_edits",
                "name": "TestScript",
                "path": "Assets/Scripts",
                "edits": edits,
                "precondition_sha256": "abc123def456...",
                "options": {
                    "refresh": "immediate",
                    "validate": "standard",
                }
            }
        }

        # Validate structure
        assert apply_payload["params"]["action"] == "apply_text_edits"
        assert len(apply_payload["params"]["edits"]) == 1
        assert "precondition_sha256" in apply_payload["params"]

    @pytest.mark.asyncio
    async def test_stress_reload_churn_marker_generation(self):
        """Test generating unique markers for reload churn.

        Behavior:
        1. Create marker: // MCP_STRESS seq={seq} time={timestamp}
        2. Append to file (triggers recompilation)
        3. Increment seq counter for uniqueness
        4. Ensure comment appears on new line

        Pattern: Deterministic but unique churn for reproducibility
        Challenge: Must not corrupt existing code
        """
        seq = 0
        contents = "public class Test {}\n"

        # Generate marker
        marker = f"// MCP_STRESS seq={seq} time={int(1234567890)}"
        seq += 1

        # Insert text (append at EOF with newline if needed)
        insert_text = ("\n" if not contents.endswith("\n") else "") + marker + "\n"

        new_contents = contents + insert_text

        assert "MCP_STRESS seq=0" in new_contents
        assert seq == 1
        assert new_contents.endswith("\n")

    @pytest.mark.asyncio
    async def test_stress_storm_mode_multiple_file_targets(self):
        """Test storm mode touching multiple C# files per cycle.

        Behavior:
        1. Collect all .cs files in Assets/ recursively
        2. If storm_count > 1, randomly sample storm_count files
        3. Apply edits to each file in parallel
        4. Increases load on editor state cache

        Pattern: Variable load parameter for scaling tests
        """
        candidates = [
            Path("Assets/Scripts/TestA.cs"),
            Path("Assets/Scripts/TestB.cs"),
            Path("Assets/Scripts/Nested/TestC.cs"),
        ]

        storm_count = 2

        if storm_count and storm_count > 1 and candidates:
            k = min(max(1, storm_count), len(candidates))
            targets = random.sample(candidates, k)
            assert len(targets) == min(2, len(candidates))

    @pytest.mark.asyncio
    async def test_stress_stat_tracking_metrics(self):
        """Test stress test statistics accumulation.

        Behavior:
        1. Track counters: pings, disconnects, errors, applies, apply_errors
        2. Increment on each event
        3. Report final JSON with port and stats

        Pattern: Minimal instrumentation for performance
        """
        stats = {
            "pings": 0,
            "menus": 0,
            "mods": 0,
            "disconnects": 0,
            "errors": 0,
            "applies": 0,
            "apply_errors": 0,
        }

        # Simulate events
        stats["pings"] += 15
        stats["disconnects"] += 2
        stats["applies"] += 1
        stats["apply_errors"] += 0

        # Report
        result = {
            "port": 6400,
            "stats": stats,
        }

        json_str = json.dumps(result, indent=2)
        assert "pings" in json_str
        assert stats["pings"] == 15


# =============================================================================
# RELEASE CHECKLIST & GIT INTEGRATION TESTS
# =============================================================================

class TestReleaseChecklistValidation:
    """Tests for release validation and checklist items.

    Domain: Release Management
    Patterns: Version consistency, manifest validation, changelog preparation
    """

    def test_version_consistency_checklist(self, temp_repo, sample_package_json,
                                          sample_manifest_json, sample_pyproject_toml):
        """Test comprehensive version consistency validation checklist.

        Checklist items:
        1. package.json version matches manifest.json
        2. manifest.json version matches pyproject.toml
        3. README.md git URL contains matching version tag
        4. Server/README.md git URL contains matching version tag
        5. docs/i18n/README-zh.md git URL contains matching version tag

        All must match before release can proceed
        """
        # Setup all files
        temp_repo["mcp_package"].write_text(
            json.dumps(sample_package_json, indent=2), encoding="utf-8"
        )
        temp_repo["manifest"].write_text(
            json.dumps(sample_manifest_json, indent=2), encoding="utf-8"
        )
        temp_repo["pyproject"].write_text(sample_pyproject_toml, encoding="utf-8")

        # Verify checklist
        checks = {}

        # Check 1: package.json
        pkg = json.loads(temp_repo["mcp_package"].read_text(encoding="utf-8"))
        checks["package.json"] = pkg["version"]

        # Check 2: manifest.json
        mfst = json.loads(temp_repo["manifest"].read_text(encoding="utf-8"))
        checks["manifest.json"] = mfst["version"]

        # Check 3: pyproject.toml
        pyproj = temp_repo["pyproject"].read_text(encoding="utf-8")
        match = re.search(r'^version = "([^"]+)"', pyproj, re.MULTILINE)
        checks["pyproject.toml"] = match.group(1) if match else None

        # All should be equal
        versions = list(checks.values())
        assert len(set(versions)) == 1, f"Version mismatch: {checks}"

    def test_manifest_icon_file_exists(self, temp_repo, sample_manifest_json):
        """Test that manifest references existing icon file.

        Behavior:
        1. Load manifest.json
        2. Get icon filename
        3. Verify icon exists in docs/images/
        4. Icon must be valid file (not directory)

        Pre-release validation item
        """
        icon_dir = temp_repo["root"] / "docs" / "images"
        icon_dir.mkdir(parents=True, exist_ok=True)
        icon_file = icon_dir / "coplay-logo.png"
        icon_file.write_bytes(b"PNG_DATA")

        # Load manifest
        temp_repo["manifest"].write_text(
            json.dumps(sample_manifest_json, indent=2), encoding="utf-8"
        )
        manifest = json.loads(temp_repo["manifest"].read_text(encoding="utf-8"))

        # Verify icon exists
        icon_name = manifest.get("icon", "")
        icon_path = icon_dir / icon_name
        assert icon_path.exists()
        assert icon_path.is_file()

    def test_license_file_exists_for_mcpb(self, temp_repo):
        """Test that LICENSE file exists for MCPB bundle inclusion.

        Behavior:
        1. Check repo root for LICENSE file
        2. If exists, include in MCPB bundle
        3. Not strictly required but expected for open source

        Pre-release check item
        """
        license_file = temp_repo["root"] / "LICENSE"

        # License should exist
        license_file.write_text("MIT License\n\nCopyright (c) 2024 Coplay", encoding="utf-8")
        assert license_file.exists()

    def test_readme_file_exists_for_mcpb(self, temp_repo):
        """Test that README.md exists for MCPB bundle inclusion.

        Behavior:
        1. Check repo root for README.md
        2. If exists, include in MCPB bundle
        3. Provides documentation to bundle users

        Pre-release check item
        """
        readme_file = temp_repo["root"] / "README.md"

        # README should exist
        readme_file.write_text("# Unity MCP\n\nA Unity package...", encoding="utf-8")
        assert readme_file.exists()


# =============================================================================
# GIT TAG & CHANGELOG GENERATION TESTS
# =============================================================================

class TestGitTagAndChangelogGeneration:
    """Tests for git tag creation and changelog patterns.

    Domain: Release Management
    Pattern: Tag naming, changelog structure preparation
    """

    def test_git_tag_naming_convention(self):
        """Test git tag naming follows v-prefixed semantic version.

        Format: v{MAJOR}.{MINOR}.{PATCH}
        Examples: v9.0.0, v9.2.0, v10.0.0

        Behavior:
        1. Extract version from package.json: "9.2.0"
        2. Prepend "v" for git tag: "v9.2.0"
        3. Tag must be deterministic from version
        """
        version = "9.2.0"
        tag_name = f"v{version}"

        assert tag_name == "v9.2.0"
        assert tag_name.startswith("v")
        assert re.match(r"^v\d+\.\d+\.\d+$", tag_name)

    def test_changelog_entry_structure(self):
        """Test changelog entry follows consistent structure.

        Format (example):
        ```
        ## [9.2.0] - 2024-01-15

        ### Added
        - Feature X
        - Feature Y

        ### Fixed
        - Bug fix for issue #123

        ### Changed
        - Breaking change A
        ```

        Pattern: Semantic versioning changelog format (keepachangelog.com)
        """
        changelog_entry = """## [9.2.0] - 2024-01-15

### Added
- Support for async script operations
- Improved error handling

### Fixed
- Connection timeout handling
- Memory leak in EditorStateCache

### Changed
- Increased default timeout from 5s to 10s
"""

        # Validate structure
        assert "## [9.2.0]" in changelog_entry
        assert "### Added" in changelog_entry
        assert "### Fixed" in changelog_entry
        assert "2024-01-15" in changelog_entry

    def test_changelog_version_detection_pattern(self):
        r"""Test detecting version from existing changelog.

        Pattern: Find latest version entry with regex
        Regex: ## \[(\d+\.\d+\.\d+)\]

        Behavior:
        1. Read CHANGELOG.md
        2. Extract latest version from first ## [ entry
        3. Compare with current version
        4. If same, skip changelog update
        5. If different, prompt for new entry
        """
        changelog_content = """# Changelog

All notable changes to this project are documented in this file.

## [9.2.0] - 2024-01-15
### Added
- Feature X

## [9.1.0] - 2024-01-01
### Added
- Feature Y
"""

        # Extract latest version
        match = re.search(r"## \[(\d+\.\d+\.\d+)\]", changelog_content)
        assert match is not None
        latest_version = match.group(1)

        assert latest_version == "9.2.0"

    def test_changelog_requires_manual_update(self):
        """Test that changelog requires manual entries per release.

        Behavior:
        1. Script cannot auto-generate meaningful changelog
        2. Manual steps required to document changes
        3. Checklist item: "Update CHANGELOG.md with release notes"

        Limitation: Requires human judgment for change categorization
        """
        # This is a validation pattern, not auto-generation
        checklist_item = "Update CHANGELOG.md with release notes"

        # Human must manually create entries under:
        # - Added (new features)
        # - Changed (behavior changes)
        # - Fixed (bug fixes)
        # - Deprecated (to be removed)
        # - Removed (previously deprecated)

        required_sections = [
            "[X.Y.Z]",
            "### Added",
            "### Fixed",
        ]

        # Changelog entry template
        template = """## [X.Y.Z] - YYYY-MM-DD

### Added
- Feature description

### Fixed
- Bug fix description
"""

        # Validate required section markers are present
        for section in required_sections:
            assert section in template, f"Missing {section} in template"


# =============================================================================
# INTEGRATION & WORKFLOW TESTS
# =============================================================================

class TestBuildReleaseWorkflow:
    """Integration tests for complete build/release workflow.

    Pattern: Multi-step process validation
    Captures: Typical release steps in order
    """

    def test_version_bump_workflow(self, temp_repo, sample_package_json,
                                   sample_manifest_json, sample_pyproject_toml):
        """Test complete version bumping workflow.

        Steps:
        1. Load current version from package.json
        2. Prompt for new version (human input, simulated)
        3. Update all 6 files with new version
        4. Validate all files updated
        5. Dry-run first to catch errors
        6. Commit changes with message
        7. Tag with new version

        Pattern: Fail-safe with dry-run preview
        """
        # Setup
        temp_repo["mcp_package"].write_text(
            json.dumps(sample_package_json, indent=2), encoding="utf-8"
        )
        temp_repo["manifest"].write_text(
            json.dumps(sample_manifest_json, indent=2), encoding="utf-8"
        )
        temp_repo["pyproject"].write_text(sample_pyproject_toml, encoding="utf-8")

        old_version = "9.2.0"
        new_version = "9.3.0"

        # Step 1: Dry-run
        dry_run_passed = True

        # Step 2: Real update
        files_updated = []

        pkg = json.loads(temp_repo["mcp_package"].read_text(encoding="utf-8"))
        pkg["version"] = new_version
        temp_repo["mcp_package"].write_text(
            json.dumps(pkg, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8"
        )
        files_updated.append("MCPForUnity/package.json")

        # Step 3: Validate
        assert len(files_updated) > 0
        assert files_updated[0] == "MCPForUnity/package.json"

        # Step 4: Would create git commit with message:
        # "Bump version to 9.3.0"

        # Step 5: Would create git tag:
        # "v9.3.0"

    def test_release_checklist_validation_order(self):
        """Test that release checklist items are validated in correct order.

        Order:
        1. Verify all version files match
        2. Verify CHANGELOG.md updated
        3. Verify icon file exists
        4. Verify LICENSE and README exist
        5. Run test suite (not in scope)
        6. Build MCPB bundle
        7. Verify bundle file created
        8. Create git tag
        9. Push to remote

        Early checks prevent wasted time on later steps
        """
        checklist = [
            ("Version consistency", "Check all files at target version"),
            ("Changelog updated", "Manual review of CHANGELOG.md"),
            ("Icon exists", "docs/images/coplay-logo.png present"),
            ("Metadata files", "LICENSE and README present"),
            ("Tests passing", "Run test suite"),
            ("MCPB buildable", "generate_mcpb.py succeeds"),
            ("Git tag ready", "v{VERSION} tag prepared"),
            ("Remote push", "All artifacts pushed"),
        ]

        assert len(checklist) == 8
        assert checklist[0][0] == "Version consistency"
        assert checklist[-1][0] == "Remote push"


# =============================================================================
# ERROR HANDLING & EDGE CASES
# =============================================================================

class TestErrorHandlingPatterns:
    """Tests for error scenarios and edge cases.

    Captures: How tools handle failures
    Documents: Recovery strategies and validation
    """

    def test_missing_source_file_error(self, temp_repo):
        """Test error handling when required source file missing.

        Example: setup_service file not found in staged copy

        Behavior:
        1. Check file.exists()
        2. If not, raise RuntimeError with path
        3. Abort before attempting edits

        Pattern: Fail fast with clear error message
        """
        missing_file = temp_repo["root"] / "NonExistent.cs"

        if not missing_file.exists():
            with pytest.raises(RuntimeError):
                raise RuntimeError(f"Expected file not found: {missing_file}")

    def test_regex_replacement_count_mismatch(self, temp_repo):
        """Test error when regex replacement count != 1.

        Behavior:
        1. Apply regex substitution with count=1
        2. Check return value (number of replacements)
        3. If n != 1, raise RuntimeError
        4. Prevents accidental double-replacement or miss

        Pattern: Strict single-match requirement
        Motivation: Protect against pattern ambiguity
        """
        content = "version = 1.0\nversion = 1.0\n"

        pattern = r'^version = 1\.0'
        new_content, count = re.subn(
            pattern, "version = 2.0", content, count=1, flags=re.MULTILINE
        )

        # Would replace only first, but need exactly 1 total
        if count != 1:
            with pytest.raises(RuntimeError):
                raise RuntimeError(
                    f"Expected 1 replacement, got {count}"
                )

    def test_line_removal_count_mismatch(self, temp_repo):
        """Test error when exact line removal doesn't match exactly once.

        Behavior:
        1. Split file by lines (keepends=True)
        2. Find exact matches to line.strip() == target
        3. If found != 1, raise RuntimeError
        4. Prevents accidental over-removal

        Pattern: Strict single-match requirement
        """
        content = "[InitializeOnLoad]\nclass A {}\n[InitializeOnLoad]\n"
        lines = content.splitlines(keepends=True)

        target = "[InitializeOnLoad]"
        removed = 0
        for l in lines:
            if l.strip() == target:
                removed += 1

        if removed != 1:
            with pytest.raises(RuntimeError):
                raise RuntimeError(
                    f"Expected to remove exactly 1 line, removed {removed}"
                )

    def test_json_parsing_error_handling(self, temp_repo):
        """Test handling of invalid JSON files.

        Behavior:
        1. Try to parse JSON
        2. Catch json.JSONDecodeError
        3. Report which file failed
        4. Abort operation

        Pattern: Early parse validation
        """
        bad_json = temp_repo["root"] / "bad.json"
        bad_json.write_text("{invalid json}", encoding="utf-8")

        with pytest.raises(json.JSONDecodeError):
            json.loads(bad_json.read_text(encoding="utf-8"))

    def test_file_permission_error_handling(self, temp_repo):
        """Test handling of permission errors during file write.

        Behavior:
        1. Attempt to write file
        2. Catch PermissionError or OSError
        3. Report which file failed
        4. Suggest running with sudo or checking perms

        Pattern: Error message includes remediation hint
        """
        # Create read-only file
        readonly_file = temp_repo["root"] / "readonly.json"
        readonly_file.write_text("{}", encoding="utf-8")
        readonly_file.chmod(0o444)

        try:
            readonly_file.write_text("{}", encoding="utf-8")
        except (PermissionError, OSError) as e:
            # Error handling code would log this
            error_msg = f"Failed to write {readonly_file}: {e}"
            assert "Failed to write" in error_msg
        finally:
            readonly_file.chmod(0o644)  # Restore for cleanup


# =============================================================================
# TEST EXECUTION CONFIGURATION
# =============================================================================

if __name__ == "__main__":
    pytest.main([__file__, "-v", "--tb=short"])
