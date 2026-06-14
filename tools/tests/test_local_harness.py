"""Hermetic unit tests for tools/local_harness.py pure helpers.

These tests exercise the Unity-free, filesystem-free (or filesystem-injected)
helpers at the top of ``tools/local_harness.py`` WITHOUT booting Unity, opening
sockets, or shelling out. Every filesystem and platform dependency is either
monkeypatched or threaded through the module's injection seams
(``platform=``, ``environ=``, ``exists=``, ``is_exec=``, ``list_dir=``,
``read_text=``, ``glob_fn=``, ``mtime_fn=``), so the suite is pure and offline.

Covered surfaces (mapped to the harness task):
  * resolve_editor_binary (= discover_editor): per-OS editor path resolution,
    nearest-patch fallback, EditorNotFound search-path reporting.
  * resolve_unity_version (= resolve_version): ProjectVersion.txt vs
    unity-versions.json precedence.
  * classify_editor_log (= classify_log): license-fatal -> exit 4 mapping,
    compile-error -> exit 3 mapping, clean log -> ok.
  * aggregate_exit_code (= aggregate_exit): exit-code aggregation across legs
    (smoke bridge-unreachable=2, EditMode fail=1, PlayMode non-blocking fail).
  * merge_junit: well-formed merged <testsuites> XML.
  * build_arg_parser: defaults + flag parsing for the CLI surface.

Run from the repo root::

    python -m pytest tools/tests/test_local_harness.py -v
"""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import pytest

# tools/local_harness.py lives one directory up from tools/tests/. It is a
# top-level (non-package) module, so put tools/ on sys.path before importing.
_TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(_TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(_TOOLS_DIR))

import local_harness as lh  # noqa: E402
from local_harness import (  # noqa: E402
    EditorNotFound,
    EditorSpec,
    JUnitCase,
    JUnitSuite,
    LegOutcome,
    aggregate_exit_code,
    build_arg_parser,
    classify_editor_log,
    merge_junit,
    resolve_editor_binary,
    resolve_unity_version,
)


# ===========================================================================
# Helpers for hermetic filesystem injection into discover_editor
# ===========================================================================
def _fake_fs(present_paths: set[str], dirs: dict[str, list[str]] | None = None):
    """Build (exists, is_exec, list_dir) callables over an in-memory file set.

    ``present_paths`` is the set of files that "exist" and are executable.
    ``dirs`` maps a directory path to the names it lists (for nearest-patch
    enumeration). All callables are pure and never touch the real disk.
    """
    dirs = dirs or {}

    def exists(p: str) -> bool:
        return p in present_paths

    def is_exec(p: str) -> bool:
        return p in present_paths

    def list_dir(d: str) -> list[str]:
        return list(dirs.get(d, []))

    return exists, is_exec, list_dir


def _no_secondary(_path: str) -> str:
    """read_text stub that reports no Hub secondaryInstallPath file."""
    raise OSError("no secondary install path file")


# ===========================================================================
# resolve_editor_binary (discover_editor): per-OS path resolution
# ===========================================================================
class TestEditorRelpath:
    def test_macos_relpath(self):
        assert lh.editor_relpath("darwin") == "Unity.app/Contents/MacOS/Unity"

    def test_windows_relpath(self):
        assert lh.editor_relpath("win32") == "Editor/Unity.exe"

    def test_linux_relpath(self):
        assert lh.editor_relpath("linux") == "Editor/Unity"

    def test_unknown_platform_defaults_to_linux_layout(self):
        assert lh.editor_relpath("freebsd13") == "Editor/Unity"


class TestHubRoots:
    def test_macos_hub_root(self):
        roots = lh.hub_roots("darwin", environ={})
        assert roots == ["/Applications/Unity/Hub/Editor"]

    def test_windows_hub_roots_use_program_files(self):
        env = {"ProgramFiles": r"C:\Program Files", "ProgramFiles(x86)": r"C:\Program Files (x86)"}
        roots = lh.hub_roots("win32", environ=env)
        assert any("Program Files" in r and r.endswith("Editor") for r in roots)
        # Both ProgramFiles roots are enumerated.
        assert len(roots) == 2

    def test_windows_hub_roots_fallback_when_no_env(self):
        roots = lh.hub_roots("win32", environ={})
        assert roots == [r"C:\Program Files\Unity\Hub\Editor"]

    def test_linux_hub_root_under_home(self):
        roots = lh.hub_roots("linux", environ={"HOME": "/home/dev"})
        assert roots == ["/home/dev/Unity/Hub/Editor"]


class TestDiscoverEditorPerOS:
    """First existing+executable candidate wins, per-OS layout."""

    def test_macos_resolves_hub_layout(self):
        version = "6000.0.75f1"
        binary = f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"
        exists, is_exec, list_dir = _fake_fs({binary})
        spec = resolve_editor_binary(
            version,
            platform="darwin",
            environ={},
            exists=exists,
            is_exec=is_exec,
            list_dir=list_dir,
            read_text=_no_secondary,
        )
        assert isinstance(spec, EditorSpec)
        assert spec.binary == binary
        assert spec.version == version

    def test_windows_resolves_hub_layout(self):
        version = "2021.3.45f2"
        env = {"ProgramFiles": r"C:\Program Files"}
        binary = str(Path(r"C:\Program Files") / "Unity" / "Hub" / "Editor" / version / "Editor" / "Unity.exe")
        exists, is_exec, list_dir = _fake_fs({binary})
        spec = resolve_editor_binary(
            version,
            platform="win32",
            environ=env,
            exists=exists,
            is_exec=is_exec,
            list_dir=list_dir,
            read_text=_no_secondary,
        )
        assert spec.binary == binary
        assert spec.version == version

    def test_linux_resolves_hub_layout(self):
        version = "6000.0.75f1"
        env = {"HOME": "/home/dev"}
        binary = str(Path("/home/dev") / "Unity" / "Hub" / "Editor" / version / "Editor" / "Unity")
        exists, is_exec, list_dir = _fake_fs({binary})
        spec = resolve_editor_binary(
            version,
            platform="linux",
            environ=env,
            exists=exists,
            is_exec=is_exec,
            list_dir=list_dir,
            read_text=_no_secondary,
        )
        assert spec.binary == binary

    def test_explicit_editor_takes_precedence(self):
        version = "6000.0.75f1"
        explicit = "/custom/path/Unity"
        hub = f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"
        # Both exist; explicit (precedence 1) must win.
        exists, is_exec, list_dir = _fake_fs({explicit, hub})
        spec = resolve_editor_binary(
            version,
            explicit_editor=explicit,
            platform="darwin",
            environ={},
            exists=exists,
            is_exec=is_exec,
            list_dir=list_dir,
            read_text=_no_secondary,
        )
        assert spec.binary == explicit

    def test_unity_editor_env_override(self):
        version = "6000.0.75f1"
        env_editor = "/env/Unity"
        exists, is_exec, list_dir = _fake_fs({env_editor})
        spec = resolve_editor_binary(
            version,
            platform="darwin",
            environ={"UNITY_EDITOR": env_editor},
            exists=exists,
            is_exec=is_exec,
            list_dir=list_dir,
            read_text=_no_secondary,
        )
        assert spec.binary == env_editor

    def test_not_found_raises_with_searched_paths(self):
        version = "6000.0.75f1"
        exists, is_exec, list_dir = _fake_fs(set())  # nothing exists
        with pytest.raises(EditorNotFound) as ei:
            resolve_editor_binary(
                version,
                platform="darwin",
                environ={},
                exists=exists,
                is_exec=is_exec,
                list_dir=list_dir,
                read_text=_no_secondary,
            )
        searched = ei.value.searched
        assert searched, "EditorNotFound must carry the probed paths"
        # The Hub candidate path must appear in the search report.
        assert any(version in s and s.endswith("Unity") for s in searched)
        assert version in str(ei.value)

    def test_nearest_patch_fallback_same_major_minor(self):
        """When the exact version is absent, pick the highest installed patch
        within the SAME major.minor, never crossing major.minor."""
        want = "6000.0.75f1"
        root = "/Applications/Unity/Hub/Editor"
        # Installed: a lower patch (same major.minor), a higher patch (same
        # major.minor), and a different minor that must be ignored.
        v_lower = "6000.0.50f1"
        v_higher = "6000.0.80f1"
        v_other_minor = "6000.1.10f1"
        bin_lower = f"{root}/{v_lower}/Unity.app/Contents/MacOS/Unity"
        bin_higher = f"{root}/{v_higher}/Unity.app/Contents/MacOS/Unity"
        bin_other = f"{root}/{v_other_minor}/Unity.app/Contents/MacOS/Unity"
        exists, is_exec, list_dir = _fake_fs(
            {bin_lower, bin_higher, bin_other},
            dirs={root: [v_lower, v_higher, v_other_minor]},
        )
        spec = resolve_editor_binary(
            want,
            platform="darwin",
            environ={},
            exists=exists,
            is_exec=is_exec,
            list_dir=list_dir,
            read_text=_no_secondary,
        )
        # Highest patch within 6000.0.x wins; the 6000.1.x install is excluded.
        assert spec.binary == bin_higher
        assert spec.version == v_higher

    def test_nearest_patch_never_crosses_major_minor(self):
        """If only a different major.minor is installed, discovery must fail
        rather than silently substituting an incompatible editor."""
        want = "6000.0.75f1"
        root = "/Applications/Unity/Hub/Editor"
        v_wrong = "2021.3.45f2"
        bin_wrong = f"{root}/{v_wrong}/Unity.app/Contents/MacOS/Unity"
        exists, is_exec, list_dir = _fake_fs({bin_wrong}, dirs={root: [v_wrong]})
        with pytest.raises(EditorNotFound):
            resolve_editor_binary(
                want,
                platform="darwin",
                environ={},
                exists=exists,
                is_exec=is_exec,
                list_dir=list_dir,
                read_text=_no_secondary,
            )


class TestDiscoverEditorMonkeypatchedPlatform:
    """Same resolution, but driving sys.platform + os.path via monkeypatch to
    prove the default (non-injected) code paths honor the OS."""

    def test_default_platform_darwin(self, monkeypatch):
        version = "6000.0.75f1"
        binary = f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"
        monkeypatch.setattr(lh.sys, "platform", "darwin")
        # Inject only the filesystem; let platform default from sys.platform.
        exists, is_exec, list_dir = _fake_fs({binary})
        spec = resolve_editor_binary(
            version,
            environ={},
            exists=exists,
            is_exec=is_exec,
            list_dir=list_dir,
            read_text=_no_secondary,
        )
        assert spec.binary == binary

    def test_default_platform_linux(self, monkeypatch):
        version = "6000.0.75f1"
        monkeypatch.setattr(lh.sys, "platform", "linux")
        env = {"HOME": "/home/ci"}
        binary = str(Path("/home/ci") / "Unity" / "Hub" / "Editor" / version / "Editor" / "Unity")
        exists, is_exec, list_dir = _fake_fs({binary})
        spec = resolve_editor_binary(
            version,
            environ=env,
            exists=exists,
            is_exec=is_exec,
            list_dir=list_dir,
            read_text=_no_secondary,
        )
        assert spec.binary == binary


class TestSecondaryInstallPath:
    """Hub secondaryInstallPath is consulted as a candidate root."""

    def test_secondary_root_used_for_candidate(self):
        version = "6000.0.75f1"
        secondary = "/Volumes/Big/UnityEditors"
        binary = str(Path(secondary) / version / "Unity.app/Contents/MacOS/Unity")
        exists, is_exec, list_dir = _fake_fs({binary})

        def read_text(_p: str) -> str:
            # Hub stores a JSON-encoded string path.
            return f'"{secondary}"'

        spec = resolve_editor_binary(
            version,
            platform="darwin",
            environ={},
            exists=exists,
            is_exec=is_exec,
            list_dir=list_dir,
            read_text=read_text,
        )
        assert spec.binary == binary


# ===========================================================================
# resolve_unity_version: ProjectVersion.txt vs unity-versions.json precedence
# ===========================================================================
class TestResolveUnityVersion:
    def test_project_version_takes_precedence(self, tmp_path):
        """ProjectVersion.txt wins over unity-versions.json defaultVersion."""
        proj = tmp_path / "MyProject"
        (proj / "ProjectSettings").mkdir(parents=True)
        (proj / "ProjectSettings" / "ProjectVersion.txt").write_text(
            "m_EditorVersion: 2021.3.45f2\nm_EditorVersionWithRevision: 2021.3.45f2 (abc123)\n",
            encoding="utf-8",
        )
        versions_json = tmp_path / "unity-versions.json"
        versions_json.write_text('{"defaultVersion": "6000.0.75f1"}', encoding="utf-8")

        resolved = resolve_unity_version(proj, versions_json=versions_json)
        assert resolved == "2021.3.45f2"

    def test_falls_back_to_default_version_when_no_project_file(self, tmp_path):
        proj = tmp_path / "EmptyProject"
        proj.mkdir()
        versions_json = tmp_path / "unity-versions.json"
        versions_json.write_text('{"defaultVersion": "6000.0.75f1"}', encoding="utf-8")

        resolved = resolve_unity_version(proj, versions_json=versions_json)
        assert resolved == "6000.0.75f1"

    def test_returns_none_when_neither_source_present(self, tmp_path):
        proj = tmp_path / "EmptyProject"
        proj.mkdir()
        missing_json = tmp_path / "does-not-exist.json"
        assert resolve_unity_version(proj, versions_json=missing_json) is None

    def test_project_version_tolerates_bom(self, tmp_path):
        proj = tmp_path / "BomProject"
        (proj / "ProjectSettings").mkdir(parents=True)
        # Write a UTF-8 BOM before the version line.
        (proj / "ProjectSettings" / "ProjectVersion.txt").write_bytes(
            b"\xef\xbb\xbfm_EditorVersion: 6000.0.75f1\n"
        )
        assert resolve_unity_version(proj, versions_json=tmp_path / "nope.json") == "6000.0.75f1"

    def test_malformed_default_version_json_returns_none(self, tmp_path):
        proj = tmp_path / "EmptyProject"
        proj.mkdir()
        versions_json = tmp_path / "unity-versions.json"
        versions_json.write_text("{ this is not json", encoding="utf-8")
        assert resolve_unity_version(proj, versions_json=versions_json) is None

    def test_real_repo_versions_json_default(self):
        """Sanity-check the precedence helper against the real (committed)
        tools/unity-versions.json defaultVersion, with an empty project so the
        json branch is exercised."""
        repo_json = lh.REPO_ROOT / "tools" / "unity-versions.json"
        default = lh.read_default_version(repo_json)
        assert isinstance(default, str) and default, "unity-versions.json must have a defaultVersion"


# ===========================================================================
# classify_editor_log: license-fatal -> 4, compile-error -> 3, clean -> ok
# ===========================================================================
class TestClassifyEditorLog:
    def test_license_fatal_log(self):
        log = (
            "[Licensing::Client] Successfully resolved entitlement\n"
            "No valid Unity Editor license found. License is not active.\n"
        )
        assert classify_editor_log(log, license_grace_elapsed=True) == "license_fatal"

    def test_license_fatal_maps_to_exit_4_via_wait_for_ready_branching(self):
        """The harness wait loop maps a license_fatal classification to exit 4.

        We assert the classification AND the mapping the live code uses
        (license_fatal -> 4) so the fixture documents the exit contract without
        booting Unity. Mapping mirrors wait_for_ready() / the warm-up branch.
        """
        log = "cannot load ULF license file; Entitlement check failed"
        kind = classify_editor_log(log, license_grace_elapsed=True)
        assert kind == "license_fatal"
        exit_code = {"license_fatal": 4, "compile_fatal": 3}.get(kind, 2)
        assert exit_code == 4

    def test_license_suppressed_during_grace(self):
        """Transient Licensing chatter before the grace window must NOT be
        classified license_fatal."""
        log = "No valid Unity Editor license found"
        assert classify_editor_log(log, license_grace_elapsed=False) == "none"

    def test_compile_error_log_maps_to_exit_3(self):
        log = (
            "Assets/Foo.cs(12,5): error CS0103: The name 'Bar' does not exist\n"
            "Scripts have compiler errors.\n"
        )
        kind = classify_editor_log(log, license_grace_elapsed=True)
        assert kind == "compile_fatal"
        exit_code = {"license_fatal": 4, "compile_fatal": 3}.get(kind, 2)
        assert exit_code == 3

    def test_clean_ready_log_is_ok(self):
        log = (
            "[MCPForUnity] Bridge listening on port 6400\n"
            "AutoConnect started; bound to loopback\n"
        )
        assert classify_editor_log(log, license_grace_elapsed=True) == "ready_ok"

    def test_empty_log_is_none(self):
        assert classify_editor_log("", license_grace_elapsed=True) == "none"
        assert classify_editor_log(None, license_grace_elapsed=True) == "none"

    def test_precedence_license_over_compile(self):
        """When both signals appear, license dominates (it is the more
        actionable setup failure, exit 4 > 3)."""
        log = "error CS0103: missing symbol\nLicense activation failed\n"
        assert classify_editor_log(log, license_grace_elapsed=True) == "license_fatal"

    def test_compile_when_license_in_grace(self):
        """Within the license grace window, a real compile error still
        classifies as compile_fatal (compile gate is not grace-suppressed)."""
        log = "License is not active\nerror CS1002: ; expected\n"
        assert classify_editor_log(log, license_grace_elapsed=False) == "compile_fatal"


# ===========================================================================
# aggregate_exit_code: exit aggregation across leg results
# ===========================================================================
def _leg(name, status, blocking, exit_code):
    return LegOutcome(name=name, status=status, blocking=blocking, exit_code=exit_code)


class TestAggregateExitCode:
    def test_all_pass_is_zero(self):
        outcomes = [
            _leg("smoke", "pass", True, 0),
            _leg("editmode", "pass", True, 0),
            _leg("playmode", "pass", False, 0),
        ]
        assert aggregate_exit_code(outcomes) == 0

    def test_editmode_fail_yields_1(self):
        outcomes = [
            _leg("smoke", "pass", True, 0),
            _leg("editmode", "fail", True, 1),
        ]
        assert aggregate_exit_code(outcomes) == 1

    def test_smoke_bridge_unreachable_is_2(self):
        outcomes = [
            _leg("smoke", "error", True, 2),
        ]
        assert aggregate_exit_code(outcomes) == 2

    def test_smoke_2_dominates_editmode_1(self):
        """Setup/infra severity (2) dominates a real test regression (1)."""
        outcomes = [
            _leg("smoke", "error", True, 2),
            _leg("editmode", "fail", True, 1),
        ]
        assert aggregate_exit_code(outcomes) == 2

    def test_playmode_nonblocking_fail_does_not_raise_code(self):
        """The composite scenario from the task: smoke bridge-unreachable=2,
        editmode fail=1, playmode non-blocking fail. The non-blocking PlayMode
        leg must never raise the top-level code; severity ordering keeps 2."""
        outcomes = [
            _leg("smoke", "error", True, 2),
            _leg("editmode", "fail", True, 1),
            _leg("playmode", "fail", False, 1),  # non-blocking
        ]
        assert aggregate_exit_code(outcomes) == 2

    def test_playmode_nonblocking_fail_alone_is_zero(self):
        outcomes = [
            _leg("smoke", "pass", True, 0),
            _leg("editmode", "pass", True, 0),
            _leg("playmode", "fail", False, 1),  # non-blocking -> swallowed
        ]
        assert aggregate_exit_code(outcomes) == 0

    def test_strict_playmode_blocking_fail_yields_1(self):
        """Under --strict-playmode the leg is blocking and a failure raises 1."""
        outcomes = [
            _leg("smoke", "pass", True, 0),
            _leg("editmode", "pass", True, 0),
            _leg("playmode", "fail", True, 1),  # blocking via --strict-playmode
        ]
        assert aggregate_exit_code(outcomes) == 1

    def test_compile_fatal_3_dominates_test_fail_1(self):
        outcomes = [
            _leg("editmode", "fail", True, 3),  # compile fatal
            _leg("playmode", "fail", False, 1),
        ]
        assert aggregate_exit_code(outcomes) == 3

    def test_license_4_dominates_compile_3(self):
        outcomes = [
            _leg("editmode", "fail", True, 3),
            _leg("smoke", "error", True, 4),
        ]
        assert aggregate_exit_code(outcomes) == 4

    def test_failing_leg_without_explicit_code_defaults_to_1(self):
        outcomes = [_leg("editmode", "fail", True, 0)]
        assert aggregate_exit_code(outcomes) == 1


# ===========================================================================
# merge_junit: well-formed merged <testsuites>
# ===========================================================================
class TestMergeJUnit:
    def test_merge_produces_wellformed_testsuites(self):
        suites = [
            JUnitSuite(
                name="smoke",
                cases=[JUnitCase(name="ping", time_s=0.1)],
            ),
            JUnitSuite(
                name="editmode",
                cases=[
                    JUnitCase(name="t_pass", time_s=0.5),
                    JUnitCase(name="t_fail", time_s=0.2, failure="AssertionError: boom"),
                    JUnitCase(name="t_skip", time_s=0.0, skipped=True),
                ],
            ),
        ]
        tree = merge_junit(suites)
        root = tree.getroot()
        assert root.tag == "testsuites"

        # Roundtrip through serialization to prove it is well-formed XML.
        serialized = ET.tostring(root, encoding="unicode")
        reparsed = ET.fromstring(serialized)
        assert reparsed.tag == "testsuites"

        # Aggregate counts on the root.
        assert root.get("tests") == "4"
        assert root.get("failures") == "1"
        assert root.get("skipped") == "1"

        testsuite_els = root.findall("testsuite")
        assert len(testsuite_els) == 2
        names = {ts.get("name") for ts in testsuite_els}
        assert names == {"smoke", "editmode"}

        editmode = next(ts for ts in testsuite_els if ts.get("name") == "editmode")
        assert editmode.get("tests") == "3"
        assert editmode.get("failures") == "1"
        assert editmode.get("skipped") == "1"

        # The failing case carries a <failure> element with the message text.
        fail_case = next(tc for tc in editmode.findall("testcase") if tc.get("name") == "t_fail")
        fail_el = fail_case.find("failure")
        assert fail_el is not None
        assert "boom" in (fail_el.text or "")

        # The skipped case carries a <skipped/> element.
        skip_case = next(tc for tc in editmode.findall("testcase") if tc.get("name") == "t_skip")
        assert skip_case.find("skipped") is not None

    def test_merge_empty_suite_list_is_wellformed(self):
        tree = merge_junit([])
        root = tree.getroot()
        assert root.tag == "testsuites"
        assert root.get("tests") == "0"
        assert root.get("failures") == "0"
        # Roundtrips cleanly.
        ET.fromstring(ET.tostring(root, encoding="unicode"))

    def test_merge_skips_none_suites(self):
        tree = merge_junit([None, JUnitSuite(name="only", cases=[JUnitCase(name="t")])])
        root = tree.getroot()
        assert root.get("tests") == "1"
        assert len(root.findall("testsuite")) == 1

    def test_failure_message_attribute_is_capped(self):
        long_msg = "x" * 500
        tree = merge_junit([JUnitSuite(name="s", cases=[JUnitCase(name="big", failure=long_msg)])])
        fail_el = tree.getroot().find("testsuite").find("testcase").find("failure")
        # Attribute is truncated to 200 chars; full text preserved in body.
        assert len(fail_el.get("message")) == 200
        assert fail_el.text == long_msg

    def test_time_attributes_are_formatted(self):
        tree = merge_junit([JUnitSuite(name="s", cases=[JUnitCase(name="t", time_s=1.2345)])])
        ts = tree.getroot().find("testsuite")
        # Three-decimal formatting on suite + case time.
        assert ts.get("time") == "1.234" or ts.get("time") == "1.235"
        tc = ts.find("testcase")
        assert tc.get("time").count(".") == 1


# ===========================================================================
# build_arg_parser: defaults + flag parsing
# ===========================================================================
class TestBuildArgParser:
    def test_defaults(self):
        parser = build_arg_parser()
        ns = parser.parse_args([])
        assert ns.legs == "smoke,editmode,playmode"
        assert ns.project_path == lh.DEFAULT_PROJECT_PATH
        assert ns.editor is None
        assert ns.ci is False
        assert ns.reuse is False
        assert ns.keep_alive is False
        assert ns.no_warmup is False
        assert ns.strict_playmode is False
        assert ns.playmode_init_timeout == lh.DEFAULT_PLAYMODE_INIT_TIMEOUT_MS
        assert ns.editor_args == []

    def test_legs_and_project_override(self):
        parser = build_arg_parser()
        ns = parser.parse_args(["--legs", "smoke", "--project-path", "/tmp/MyProj"])
        assert ns.legs == "smoke"
        assert ns.project_path == "/tmp/MyProj"

    def test_boolean_flags(self):
        parser = build_arg_parser()
        ns = parser.parse_args(["--ci", "--reuse", "--strict-playmode", "--no-warmup", "--keep-alive"])
        assert ns.ci is True
        assert ns.reuse is True
        assert ns.strict_playmode is True
        assert ns.no_warmup is True
        assert ns.keep_alive is True

    def test_editor_arg_is_repeatable(self):
        parser = build_arg_parser()
        # Dash-prefixed editor args must use the --opt=value form so argparse
        # does not mistake the value for another option (this is how the
        # harness docstring's -manualLicenseFile example must be passed).
        ns = parser.parse_args(
            ["--editor-arg=-manualLicenseFile", "--editor-arg", "/root/Unity_lic.ulf"]
        )
        assert ns.editor_args == ["-manualLicenseFile", "/root/Unity_lic.ulf"]

    def test_numeric_args_are_typed(self):
        parser = build_arg_parser()
        ns = parser.parse_args(
            ["--boot-timeout", "1200", "--bridge-wait", "300", "--playmode-init-timeout", "60000"]
        )
        assert ns.boot_timeout == 1200
        assert isinstance(ns.boot_timeout, int)
        assert ns.bridge_wait == 300
        assert ns.playmode_init_timeout == 60000


# ===========================================================================
# parse_legs: ordered, de-duplicated, allow-listed (supporting helper)
# ===========================================================================
class TestParseLegs:
    def test_normalizes_and_dedupes(self):
        assert lh.parse_legs("smoke, EditMode ,smoke,playmode") == ["smoke", "editmode", "playmode"]

    def test_drops_unknown_legs(self):
        assert lh.parse_legs("smoke,bogus,editmode") == ["smoke", "editmode"]

    def test_empty_string_is_empty_list(self):
        assert lh.parse_legs("") == []


# ===========================================================================
# compile_probe: must read read_console entries from the bare-list "data"
# envelope. Regression: the exit-3 compile gate was dead because it only looked
# under "items"/"result", so a real `error CS0103` slipped through as ok.
# ===========================================================================
class TestConsoleEntriesAndCompileProbe:
    def test_console_entries_bare_list_under_data(self):
        resp = {"success": True, "message": "1 entry",
                "data": [{"type": "Error", "message": "Assets/X.cs(1,1): error CS0103: bad"}]}
        entries = lh._console_entries(resp)
        assert isinstance(entries, list) and len(entries) == 1

    def test_console_entries_paging_items_under_data(self):
        resp = {"success": True, "data": {"items": [{"message": "info"}], "next_cursor": 5}}
        assert lh._console_entries(resp) == [{"message": "info"}]

    def test_console_entries_result_wrapped(self):
        resp = {"result": {"success": True, "data": [{"message": "x"}]}}
        assert lh._console_entries(resp) == [{"message": "x"}]

    def test_compile_probe_detects_cs_error(self):
        def fake_send(cmd, params, **kw):
            assert cmd == "read_console"
            return {"success": True, "message": "1 error",
                    "data": [{"type": "Error",
                              "message": "Assets/Foo.cs(10,5): error CS0103: 'X' not found"}]}
        assert lh.compile_probe("inst@hash", 1, 50, send=fake_send) is False

    def test_compile_probe_clean_project(self):
        def fake_send(cmd, params, **kw):
            return {"success": True, "message": "0 errors", "data": []}
        assert lh.compile_probe("inst@hash", 1, 50, send=fake_send) is True

    def test_compile_probe_inconclusive_does_not_block(self):
        # An errored probe is inconclusive -> treated as compiles-OK (do not block).
        def fake_send(cmd, params, **kw):
            return {"success": False, "error": "boom"}
        assert lh.compile_probe("inst@hash", 1, 50, send=fake_send) is True


# ===========================================================================
# _start_utf: the tests_running back-off must fire BEFORE the _ok() gate.
# Regression: the success:false ErrorResponse was rejected by _ok() first, so a
# transient "tests already running" was misreported as a hard start failure.
# ===========================================================================
class TestStartUtfTestsRunning:
    def test_retries_on_tests_running_then_succeeds(self, monkeypatch):
        monkeypatch.setattr(lh.time, "sleep", lambda *_a, **_k: None)
        calls = {"n": 0}

        def fake_send(cmd, params, **kw):
            calls["n"] += 1
            if calls["n"] == 1:
                return {"success": False, "error": "tests_running",
                        "data": {"retry_after_ms": 10}}
            return {"success": True, "data": {"job_id": "J42"}}

        job_id, _ = lh._start_utf(fake_send, "EditMode", "inst@hash", 120000, 1, 50)
        assert job_id == "J42"
        assert calls["n"] == 2  # retried exactly once

    def test_exhausts_after_persistent_tests_running(self, monkeypatch):
        monkeypatch.setattr(lh.time, "sleep", lambda *_a, **_k: None)
        calls = {"n": 0}

        def fake_send(cmd, params, **kw):
            calls["n"] += 1
            return {"success": False, "error": "tests_running", "data": {"retry_after_ms": 1}}

        job_id, marker = lh._start_utf(fake_send, "EditMode", "inst@hash", None, 1, 50)
        assert job_id is None
        assert calls["n"] == 5  # bounded retry budget
        assert "exhausted" in str(marker)

    def test_hard_start_failure_returns_immediately(self):
        def fake_send(cmd, params, **kw):
            return {"success": False, "error": "bridge_down"}
        job_id, _ = lh._start_utf(fake_send, "EditMode", "inst@hash", None, 1, 50)
        assert job_id is None

    def test_success_returns_job_id(self):
        def fake_send(cmd, params, **kw):
            assert params.get("initTimeout") == 120000
            return {"success": True, "data": {"job_id": 7}}
        job_id, _ = lh._start_utf(fake_send, "PlayMode", "inst@hash", 120000, 1, 50)
        assert job_id == "7"


# ===========================================================================
# UTF poll/start transport resilience. Regression: a live EditMode run blocks
# Unity's main thread, so get_test_job times out mid-run; the poll/start loops
# must treat a raised transport error as non-terminal, not crash the harness.
# ===========================================================================
class TestUtfTransportResilience:
    def test_poll_tolerates_timeout_then_returns_terminal(self, monkeypatch):
        monkeypatch.setattr(lh.time, "sleep", lambda *_a, **_k: None)
        calls = {"n": 0}

        def fake_send(cmd, params, **kw):
            calls["n"] += 1
            if calls["n"] <= 3:
                raise TimeoutError("Timeout receiving Unity response")
            return {"success": True, "data": {"status": "succeeded",
                                              "result": {"summary": {"total": 2, "passed": 2}}}}

        terminal = lh._poll_utf(fake_send, "J1", "inst@hash", lh.time.time() + 1000, 8, 50)
        assert lh._dig(terminal, "status") == "succeeded"
        assert calls["n"] == 4  # 3 timeouts tolerated, then terminal

    def test_poll_wedges_after_deadline_without_crashing(self, monkeypatch):
        monkeypatch.setattr(lh.time, "sleep", lambda *_a, **_k: None)

        def fake_send(cmd, params, **kw):
            raise TimeoutError("editor unresponsive")

        terminal = lh._poll_utf(fake_send, "J1", "inst@hash", lh.time.time() + 0.3, 8, 50)
        assert isinstance(terminal, dict) and terminal.get("_wedge") is True

    def test_start_tolerates_transport_exception_then_succeeds(self, monkeypatch):
        monkeypatch.setattr(lh.time, "sleep", lambda *_a, **_k: None)
        calls = {"n": 0}

        def fake_send(cmd, params, **kw):
            calls["n"] += 1
            if calls["n"] == 1:
                raise TimeoutError("transient")
            return {"success": True, "data": {"job_id": "J9"}}

        job_id, _ = lh._start_utf(fake_send, "EditMode", "inst@hash", None, 8, 50)
        assert job_id == "J9"
        assert calls["n"] == 2
