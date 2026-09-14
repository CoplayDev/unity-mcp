"""Tests for tools/update_versions.py.

The release bump used to leave Server/uv.lock behind: pyproject.toml moved to 10.2.0
while the lock still recorded 10.1.0, which `uv sync --locked` rejects. These tests pin
the lock updater to the lock format uv actually writes for this repo, so a format change
surfaces here rather than in a release run.
"""
import re
import shutil
import sys
from pathlib import Path

import pytest

_TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(_TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(_TOOLS_DIR))

import update_versions  # noqa: E402

REAL_LOCK = _TOOLS_DIR.parent / "Server" / "uv.lock"
REAL_PYPROJECT = _TOOLS_DIR.parent / "Server" / "pyproject.toml"

SAMPLE_LOCK = (
    'version = 1\n'
    'revision = 3\n'
    'requires-python = ">=3.10"\n'
    '\n'
    '[[package]]\n'
    'name = "click"\n'
    'version = "8.3.1"\n'
    'source = { registry = "https://pypi.org/simple" }\n'
    '\n'
    '[[package]]\n'
    'name = "mcpforunityserver"\n'
    'version = "10.1.0"\n'
    'source = { editable = "." }\n'
    'dependencies = [\n'
    '    { name = "click" },\n'
    ']\n'
    '\n'
    '[[package]]\n'
    'name = "mcp"\n'
    'version = "1.26.0"\n'
    'source = { registry = "https://pypi.org/simple" }\n'
)


@pytest.fixture
def lock_file(tmp_path, monkeypatch):
    path = tmp_path / "uv.lock"
    path.write_bytes(SAMPLE_LOCK.encode("utf-8"))
    monkeypatch.setattr(update_versions, "UV_LOCK", path)
    return path


def test_update_uv_lock_rewrites_only_the_project_entry(lock_file):
    assert update_versions.update_uv_lock("10.2.0") is True
    updated = lock_file.read_bytes().decode("utf-8")
    assert 'name = "mcpforunityserver"\nversion = "10.2.0"' in updated
    assert 'name = "click"\nversion = "8.3.1"' in updated
    assert 'name = "mcp"\nversion = "1.26.0"' in updated
    assert updated.count('version = "10.2.0"') == 1


def test_update_uv_lock_is_a_noop_when_already_current(lock_file):
    update_versions.update_uv_lock("10.2.0")
    before = lock_file.read_bytes()
    assert update_versions.update_uv_lock("10.2.0") is False
    assert lock_file.read_bytes() == before


def test_update_uv_lock_dry_run_does_not_write(lock_file):
    assert update_versions.update_uv_lock("10.2.0", dry_run=True) is True
    assert 'version = "10.1.0"' in lock_file.read_bytes().decode("utf-8")


def test_update_uv_lock_preserves_crlf_line_endings(tmp_path, monkeypatch):
    """A core.autocrlf checkout must not be rewritten to LF (or vice versa) by a version bump."""
    path = tmp_path / "uv.lock"
    path.write_bytes(SAMPLE_LOCK.replace("\n", "\r\n").encode("utf-8"))
    monkeypatch.setattr(update_versions, "UV_LOCK", path)
    assert update_versions.update_uv_lock("10.2.0") is True
    raw = path.read_bytes()
    assert b"\r\n" in raw
    assert b"\n" not in raw.replace(b"\r\n", b"")
    assert b'name = "mcpforunityserver"\r\nversion = "10.2.0"' in raw


def test_update_uv_lock_missing_entry_is_reported_not_raised(tmp_path, monkeypatch):
    path = tmp_path / "uv.lock"
    path.write_bytes(b'version = 1\n\n[[package]]\nname = "click"\nversion = "8.3.1"\n')
    monkeypatch.setattr(update_versions, "UV_LOCK", path)
    assert update_versions.update_uv_lock("10.2.0") is False
    assert b'version = "8.3.1"' in path.read_bytes()


def test_update_uv_lock_missing_file_is_reported_not_raised(tmp_path, monkeypatch):
    monkeypatch.setattr(update_versions, "UV_LOCK", tmp_path / "absent.lock")
    assert update_versions.update_uv_lock("10.2.0") is False


def test_update_uv_lock_matches_the_checked_in_lock_format(tmp_path, monkeypatch):
    """The regex has to keep matching whatever uv writes for this repo."""
    path = tmp_path / "uv.lock"
    shutil.copy(REAL_LOCK, path)
    monkeypatch.setattr(update_versions, "UV_LOCK", path)
    assert update_versions.update_uv_lock("0.0.0.dev0") is True
    assert re.search(
        r'^\[\[package\]\]\s*\nname = "mcpforunityserver"\s*\nversion = "0\.0\.0\.dev0"',
        path.read_bytes().decode("utf-8"),
        re.MULTILINE,
    )


def test_checked_in_lock_agrees_with_pyproject_version():
    """Guards the drift that `uv sync --locked` now rejects in CI."""
    pyproject_version = re.search(
        r'^version = "([^"]+)"', REAL_PYPROJECT.read_text(encoding="utf-8"), re.MULTILINE
    ).group(1)
    lock_version = update_versions._UV_LOCK_SELF_VERSION.search(
        REAL_LOCK.read_bytes().decode("utf-8")
    ).group(2)
    assert lock_version == pyproject_version
