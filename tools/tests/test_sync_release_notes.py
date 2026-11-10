"""`gh api --paginate` prints one JSON array per page; joining them must not touch release bodies."""
import json
import sys
from pathlib import Path
from types import SimpleNamespace

_TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(_TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(_TOOLS_DIR))

import sync_release_notes  # noqa: E402


def test_pages_are_joined_without_rewriting_release_bodies(monkeypatch):
    first = [{"tag_name": "v2", "body": "See [the guide][1] and [notes][2]."}]
    second = [{"tag_name": "v1", "body": "plain"}]
    monkeypatch.setattr(sync_release_notes.shutil, "which", lambda _: "gh")
    monkeypatch.setattr(
        sync_release_notes.subprocess, "run",
        lambda *a, **k: SimpleNamespace(stdout=json.dumps(first) + json.dumps(second)),
    )

    assert sync_release_notes._fetch_via_gh("repos/o/r/releases") == first + second
