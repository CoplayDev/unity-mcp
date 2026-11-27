"""Regression coverage for read-only Unity Prefab diffs against Git HEAD."""

import json
import shutil
import subprocess
from pathlib import Path
from unittest.mock import AsyncMock, Mock

import pytest
from fastmcp import Context

import services.tools.diff_prefab as diff_prefab_tool
from core.config import config
from services.prefab_diff import PrefabDiffError, compare_prefabs
from services.prefab_git_diff import diff_prefab_from_git
from services.resources.project_info import ProjectInfoData, ProjectInfoResponse


HEADER = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"


def _game_object(file_id: int, name: str, component_ids: tuple[int, ...]) -> str:
    components = "".join(f"  - component: {{fileID: {item}}}\n" for item in component_ids)
    return (
        f"--- !u!1 &{file_id}\nGameObject:\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        "  m_Component:\n"
        f"{components}"
        "  m_Layer: 0\n"
        f"  m_Name: {name}\n"
        "  m_IsActive: 1\n"
    )


def _transform(file_id: int, game_object: int, parent: int, children: tuple[int, ...] = ()) -> str:
    child_list = " []\n" if not children else "\n" + "".join(
        f"  - {{fileID: {item}}}\n" for item in children
    )
    return (
        f"--- !u!4 &{file_id}\nTransform:\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_GameObject: {{fileID: {game_object}}}\n"
        "  m_LocalPosition: {x: 0, y: 0, z: 0}\n"
        f"  m_Children:{child_list}"
        f"  m_Father: {{fileID: {parent}}}\n"
    )


def _collider(file_id: int, game_object: int, trigger: int = 0, enabled: int = 1) -> str:
    return (
        f"--- !u!65 &{file_id}\nBoxCollider:\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_GameObject: {{fileID: {game_object}}}\n"
        f"  m_IsTrigger: {trigger}\n"
        f"  m_Enabled: {enabled}\n"
        "  m_Size: {x: 1, y: 1, z: 1}\n"
    )


def _prefab(*, trigger: int = 0, enabled: int = 1, reverse_documents: bool = False) -> str:
    documents = [
        _game_object(100, "Player", (101,)),
        _transform(101, 100, 0, (201, 301)),
        _game_object(200, "Hitbox", (201, 202, 203)),
        _transform(201, 200, 101),
        _collider(202, 200),
        _collider(203, 200, trigger=trigger, enabled=enabled),
        _game_object(300, "Hitbox", (301, 302)),
        _transform(301, 300, 101),
        _collider(302, 300),
    ]
    if reverse_documents:
        documents.reverse()
    return HEADER + "".join(documents)


def test_matches_siblings_and_same_type_components_by_file_id() -> None:
    result = compare_prefabs(_prefab(), _prefab(trigger=1))

    assert result == {
        "changes": [{
            "object": "Player/Hitbox",
            "gameObjectFileId": "200",
            "component": "BoxCollider",
            "componentFileId": "203",
            "field": "m_IsTrigger",
            "oldValue": 0,
            "newValue": 1,
            "kind": "modified",
        }],
        "totalChanges": 1,
        "truncated": False,
    }


def test_document_and_mapping_order_and_line_endings_do_not_change_the_result() -> None:
    formatted = _prefab(reverse_documents=True).replace(
        "{x: 1, y: 1, z: 1}", "{ z: 1, x: 1, y: 1 }"
    ).replace("\n", "\r\n")

    assert compare_prefabs(_prefab(), formatted) == {
        "changes": [], "totalChanges": 0, "truncated": False,
    }


def test_reports_removed_and_added_fields_without_conflating_missing_with_null() -> None:
    old_doc = _collider(203, 200).replace("  m_Enabled: 1\n", "  oldField: null\n")
    new_doc = old_doc.replace("  oldField: null\n", "  newField: null\n")
    original = _prefab()
    result = compare_prefabs(
        original.replace(_collider(203, 200), old_doc),
        original.replace(_collider(203, 200), new_doc),
    )

    assert {(row["field"], row["kind"]) for row in result["changes"]} == {
        ("oldField", "removed"), ("newField", "added"),
    }
    assert all(row["oldValue"] is None and row["newValue"] is None for row in result["changes"])


def test_truncation_reports_the_complete_change_count() -> None:
    before = _prefab()
    after = _prefab(trigger=1, enabled=0)
    full = compare_prefabs(before, after)
    limited = compare_prefabs(before, after, max_changes=1)

    assert full["totalChanges"] == limited["totalChanges"] == 2
    assert full["truncated"] is False
    assert limited["truncated"] is True
    assert limited["changes"] == full["changes"][:1]
    json.dumps(limited, allow_nan=False)


def test_truncation_also_caps_large_field_values() -> None:
    before = _prefab()
    collider = _collider(203, 200)
    value = "x" * 300_000
    after = before.replace(collider, f'{collider}  note: "{value}"\n')

    assert compare_prefabs(before, after) == {"changes": [], "totalChanges": 1, "truncated": True}


def test_reference_values_preserve_large_file_ids() -> None:
    component = _collider(203, 200)
    old_id = "9012456863420194011"
    new_id = "9012456863420194012"
    before = _prefab().replace(component, component + f"  m_Material: {{fileID: {old_id}}}\n")
    after = before.replace(old_id, new_id)

    result = compare_prefabs(before, after)

    assert result["totalChanges"] == 1
    change, = result["changes"]
    assert change["field"] == "m_Material"
    assert change["oldValue"] == {"fileID": old_id}
    assert change["newValue"] == {"fileID": new_id}


def test_unity_object_names_are_strings_even_when_they_look_like_yaml_types() -> None:
    before = _prefab().replace("  m_Name: Player\n", "  m_Name: Off\n")
    after = before.replace("  m_Name: Off\n", "  m_Name: 123\n")

    result = compare_prefabs(before, after)

    assert result["totalChanges"] == 1
    change, = result["changes"]
    assert change["component"] == "GameObject"
    assert change["field"] == "m_Name"
    assert change["oldValue"] == "Off"
    assert change["newValue"] == "123"


@pytest.mark.parametrize("content", [
    "ordinary: yaml\n",
    "UnityFS\x00\x00\x03",
    _prefab().replace("  m_Size: {x: 1, y: 1, z: 1}", "  m_Size: {broken"),
    _prefab().replace("--- !u!1 &100", "--- !u!1 &" + "1" * 5000, 1),
    _prefab() + _collider(203, 200),
    _prefab() + "--- !u!1001 &999\nPrefabInstance:\n  m_SourcePrefab: {fileID: 100100000}\n",
    _prefab() + "--- !u!4 &999 stripped\nTransform:\n  m_Father: {fileID: 0}\n",
])
def test_cannot_report_an_empty_diff_for_unsupported_or_invalid_input(content: str) -> None:
    with pytest.raises(PrefabDiffError):
        compare_prefabs(_prefab(), content)


def _git(repo: Path, *args: str) -> str:
    return subprocess.run(
        ["git", "-C", str(repo), *args], check=True, capture_output=True, text=True, timeout=10,
    ).stdout


@pytest.fixture
def git_prefab(tmp_path: Path) -> tuple[Path, Path, Path]:
    if shutil.which("git") is None:
        pytest.skip("Git is required for the working tree regression test")
    repo = tmp_path / "repo"
    project = repo / "UnityProject"
    asset = project / "Assets" / "Player.prefab"
    asset.parent.mkdir(parents=True)
    settings = project / "ProjectSettings"
    settings.mkdir()
    (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.0f1\n", encoding="utf-8")
    asset.write_text(_prefab(), encoding="utf-8")
    _git(repo, "init", "--quiet")
    _git(repo, "add", "--", "UnityProject")
    _git(repo, "-c", "user.name=Prefab Test", "-c", "user.email=test@example.invalid",
         "commit", "--no-gpg-sign", "--quiet", "-m", "Add Prefab baseline")
    return repo, project, asset


def test_git_compares_head_to_saved_working_tree_and_preserves_dirty_files(
    git_prefab: tuple[Path, Path, Path],
) -> None:
    repo, project, asset = git_prefab
    asset.write_text(_prefab(trigger=1), encoding="utf-8")
    git_path = asset.relative_to(repo).as_posix()
    _git(repo, "add", "--", git_path)
    asset.write_text(_prefab(enabled=0), encoding="utf-8")
    staged_before = _git(repo, "show", f":{git_path}")
    worktree_before = asset.read_bytes()
    head_before = _git(repo, "rev-parse", "HEAD")

    result = diff_prefab_from_git(project, "Assets/Player.prefab")

    assert result["totalChanges"] == 1
    assert [(row["field"], row["oldValue"], row["newValue"]) for row in result["changes"]] == [
        ("m_Enabled", 1, 0),
    ]
    assert asset.read_bytes() == worktree_before
    assert _git(repo, "show", f":{git_path}") == staged_before
    assert _git(repo, "rev-parse", "HEAD") == head_before


def test_git_rejects_paths_outside_the_unity_project(git_prefab: tuple[Path, Path, Path]) -> None:
    repo, project, _ = git_prefab
    outside = repo / "External.prefab"
    outside.write_text(_prefab(), encoding="utf-8")

    with pytest.raises(ValueError):
        diff_prefab_from_git(project, "../External.prefab")


def test_git_reports_an_untracked_prefab_without_a_head_blob(git_prefab: tuple[Path, Path, Path]) -> None:
    _, project, _ = git_prefab
    (project / "Assets" / "Untracked.prefab").write_text(_prefab(), encoding="utf-8")

    with pytest.raises(PrefabDiffError, match="unavailable at Git HEAD"):
        diff_prefab_from_git(project, "Assets/Untracked.prefab")


def test_git_ignores_inherited_repository_location(
    git_prefab: tuple[Path, Path, Path], monkeypatch: pytest.MonkeyPatch,
) -> None:
    repo, project, asset = git_prefab
    asset.write_text(_prefab(trigger=1), encoding="utf-8")
    monkeypatch.setenv("GIT_DIR", str(repo / "unrelated" / ".git"))
    monkeypatch.setenv("GIT_WORK_TREE", str(repo / "unrelated"))

    assert diff_prefab_from_git(project, "Assets/Player.prefab")["totalChanges"] == 1


def test_git_reports_a_repository_without_a_head(tmp_path: Path) -> None:
    if shutil.which("git") is None:
        pytest.skip("Git is required for this regression test")
    asset = tmp_path / "Assets" / "Player.prefab"
    asset.parent.mkdir()
    asset.write_text(_prefab(), encoding="utf-8")
    _git(tmp_path, "init", "--quiet")

    with pytest.raises(PrefabDiffError, match="unavailable at Git HEAD"):
        diff_prefab_from_git(tmp_path, "Assets/Player.prefab")


def test_git_rejects_a_worktree_prefab_symlink(git_prefab: tuple[Path, Path, Path]) -> None:
    repo, project, tracked_asset = git_prefab
    outside = repo / "External.prefab"
    outside.write_text(_prefab(enabled=0), encoding="utf-8")
    tracked_asset.unlink()
    try:
        tracked_asset.symlink_to(outside)
    except (NotImplementedError, OSError) as exc:
        pytest.skip(f"Could not make a symlink for this regression test: {exc}")

    with pytest.raises(PrefabDiffError, match="without symbolic links"):
        diff_prefab_from_git(project, "Assets/Player.prefab")


@pytest.mark.asyncio
async def test_diff_prefab_uses_the_selected_project_without_logging_values(
    git_prefab: tuple[Path, Path, Path], monkeypatch: pytest.MonkeyPatch,
) -> None:
    _, project, asset = git_prefab
    asset.write_text(_prefab(trigger=1), encoding="utf-8")
    monkeypatch.setattr(config, "transport_mode", "stdio")
    monkeypatch.setattr(config, "http_remote_hosted", False)
    lookup = AsyncMock(return_value=ProjectInfoResponse(
        success=True, data=ProjectInfoData(projectRoot=str(project)),
    ))
    monkeypatch.setattr(diff_prefab_tool, "get_project_info", lookup)
    ctx = Mock(spec=Context)

    result = await diff_prefab_tool.diff_prefab(ctx, "Assets/Player.prefab")

    lookup.assert_awaited_once_with(ctx)
    data = result.model_dump()["data"]
    change, = data["changes"]
    assert result.success and change["field"] == "m_IsTrigger"
    assert "m_IsTrigger" not in repr(result)


@pytest.mark.asyncio
@pytest.mark.parametrize(("host", "remote_hosted"), [("0.0.0.0", False), ("127.0.0.1", True)])
async def test_diff_prefab_skips_non_local_servers(
    host: str, remote_hosted: bool, monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(config, "transport_mode", "http")
    monkeypatch.setattr(config, "http_remote_hosted", remote_hosted)
    monkeypatch.setenv("UNITY_MCP_HTTP_HOST", host)
    lookup = AsyncMock()
    monkeypatch.setattr(diff_prefab_tool, "get_project_info", lookup)

    result = await diff_prefab_tool.diff_prefab(Mock(spec=Context), "Assets/Player.prefab")

    assert result.success is False
    lookup.assert_not_awaited()
