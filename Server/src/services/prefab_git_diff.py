"""Read the committed and saved versions of a local Unity Prefab."""

import os
import subprocess
from pathlib import Path, PurePosixPath
from typing import Any

from services.prefab_diff import PrefabDiffError, compare_prefabs


MAX_PREFAB_BYTES = 2 * 1024 * 1024
GIT_TIMEOUT_SECONDS = 5
_GIT_REPOSITORY_ENV = {
    "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_COMMON_DIR", "GIT_DIR",
    "GIT_IMPLICIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_NAMESPACE",
    "GIT_OBJECT_DIRECTORY", "GIT_PREFIX", "GIT_SHALLOW_FILE", "GIT_WORK_TREE",
}


def _git(project_root: Path, *args: str) -> subprocess.CompletedProcess[bytes]:
    # The selected Unity project is authoritative, even inside a Git hook environment.
    env = {key: value for key, value in os.environ.items() if key not in _GIT_REPOSITORY_ENV}
    env["GIT_NO_LAZY_FETCH"] = "1"
    try:
        return subprocess.run(
            ["git", "--no-replace-objects", *args],
            cwd=project_root,
            env=env,
            capture_output=True,
            timeout=GIT_TIMEOUT_SECONDS,
            check=False,
        )
    except FileNotFoundError as exc:
        raise PrefabDiffError("Git is required on the local MCP server.") from exc


def diff_prefab_from_git(
    project_root: Path, asset_path: str, *, max_changes: int = 200,
) -> dict[str, Any]:
    """Compare one text Prefab under Assets against its blob in HEAD."""
    path = PurePosixPath(asset_path.replace("\\", "/"))
    if (
        len(path.parts) < 2 or path.parts[0] != "Assets"
        or ".." in path.parts or ":" in asset_path or "\x00" in asset_path
        or path.suffix.lower() != ".prefab"
    ):
        raise PrefabDiffError("Use a Unity project-relative .prefab path under Assets/.")

    try:
        project_root = project_root.resolve(strict=True)
        candidate = project_root.joinpath(*path.parts)
        resolved = candidate.resolve(strict=True)
        if resolved != candidate or not resolved.is_file():
            raise PrefabDiffError("The worktree Prefab must be a regular file without symbolic links.")
        with resolved.open("rb") as handle:
            worktree = handle.read(MAX_PREFAB_BYTES + 1)
    except (OSError, RuntimeError) as exc:
        raise PrefabDiffError("Could not read the saved Prefab in the Unity project.") from exc
    if len(worktree) > MAX_PREFAB_BYTES:
        raise PrefabDiffError("The worktree Prefab exceeds the 2 MiB comparison limit.")

    # The ./ prefix resolves relative to the Unity project even in a nested Git worktree.
    # Pin the blob so the size check and read refer to the same content if HEAD moves.
    revision = _git(project_root, "rev-parse", "--verify", f"HEAD:./{path.as_posix()}")
    if revision.returncode:
        raise PrefabDiffError("The Prefab is unavailable at Git HEAD. Check that the project is in Git and the Prefab is committed.")
    blob_id = revision.stdout.decode("ascii").strip()
    size = _git(project_root, "cat-file", "-s", blob_id)
    if size.returncode:
        raise PrefabDiffError("Could not read the Prefab blob size at Git HEAD.")
    if int(size.stdout) > MAX_PREFAB_BYTES:
        raise PrefabDiffError("The HEAD Prefab exceeds the 2 MiB comparison limit.")
    baseline = _git(project_root, "cat-file", "blob", blob_id)
    if baseline.returncode:
        raise PrefabDiffError("Could not read the Prefab blob at Git HEAD.")

    try:
        before = baseline.stdout.decode("utf-8-sig")
        after = worktree.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        raise PrefabDiffError("Both Prefab versions must use Unity text serialization (UTF-8).") from exc
    return {
        "assetPath": path.as_posix(),
        "base": "HEAD",
        "target": "workingTree",
        **compare_prefabs(before, after, max_changes=max_changes),
    }
