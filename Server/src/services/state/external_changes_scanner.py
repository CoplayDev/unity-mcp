from __future__ import annotations

import os
import json
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


def _now_unix_ms() -> int:
    return int(time.time() * 1000)


def _in_pytest() -> bool:
    # Keep scanner inert during the Python integration suite unless explicitly invoked.
    return bool(os.environ.get("PYTEST_CURRENT_TEST"))


@dataclass
class ExternalChangesState:
    project_root: str | None = None
    last_scan_unix_ms: int | None = None
    last_seen_mtime_ns: int | None = None
    dirty: bool = False
    dirty_since_unix_ms: int | None = None
    external_changes_last_seen_unix_ms: int | None = None
    last_cleared_unix_ms: int | None = None
    # Cached package roots referenced by Packages/manifest.json "file:" dependencies
    extra_roots: list[str] | None = None
    manifest_last_mtime_ns: int | None = None


class ExternalChangesScanner:
    """
    Lightweight external-changes detector using recursive max-mtime scan.

    This is intentionally conservative:
    - It only marks dirty when it sees a strictly newer mtime than the baseline.
    - It scans at most once per scan_interval_ms per instance to keep overhead bounded.
    """

    def __init__(self, *, scan_interval_ms: int = 1500, max_entries: int = 20000):
        self._states: dict[str, ExternalChangesState] = {}
        self._scan_interval_ms = int(scan_interval_ms)
        self._max_entries = int(max_entries)

    def _get_state(self, instance_id: str) -> ExternalChangesState:
        return self._states.setdefault(instance_id, ExternalChangesState())

    def set_project_root(self, instance_id: str, project_root: str | None) -> None:
        st = self._get_state(instance_id)
        if project_root:
            st.project_root = project_root

    def clear_dirty(self, instance_id: str) -> None:
        st = self._get_state(instance_id)
        st.dirty = False
        st.dirty_since_unix_ms = None
        st.last_cleared_unix_ms = _now_unix_ms()
        # Reset baseline to “now” on next scan.
        st.last_seen_mtime_ns = None

    def _scan_paths_max_mtime_ns(self, roots: Iterable[Path]) -> int | None:
        newest: int | None = None
        entries = 0

        for root in roots:
            if not root.exists():
                continue

            # Walk the tree; skip common massive/irrelevant dirs (Library/Temp/Logs).
            for dirpath, dirnames, filenames in os.walk(str(root)):
                entries += 1
                if entries > self._max_entries:
                    return newest

                dp = Path(dirpath)
                name = dp.name.lower()
                if name in {"library", "temp", "logs", "obj", ".git", "node_modules"}:
                    dirnames[:] = []
                    continue

                # Allow skipping hidden directories quickly
                dirnames[:] = [d for d in dirnames if not d.startswith(".")]

                for fn in filenames:
                    if fn.startswith("."):
                        continue
                    entries += 1
                    if entries > self._max_entries:
                        return newest
                    p = dp / fn
                    try:
                        stat = p.stat()
                    except OSError:
                        continue
                    m = getattr(stat, "st_mtime_ns", None)
                    if m is None:
                        # Fallback when st_mtime_ns is unavailable
                        m = int(stat.st_mtime * 1_000_000_000)
                    newest = m if newest is None else max(newest, int(m))

        return newest

    def _resolve_manifest_extra_roots(self, project_root: Path, st: ExternalChangesState) -> list[Path]:
        """
        Parse Packages/manifest.json for local file: dependencies and resolve them to absolute paths.
        Returns a list of Paths that exist and are directories.
        """
        manifest_path = project_root / "Packages" / "manifest.json"
        try:
            stat = manifest_path.stat()
        except OSError:
            st.extra_roots = []
            st.manifest_last_mtime_ns = None
            return []

        mtime_ns = getattr(stat, "st_mtime_ns", int(
            stat.st_mtime * 1_000_000_000))
        if st.extra_roots is not None and st.manifest_last_mtime_ns == mtime_ns:
            return [Path(p) for p in st.extra_roots if p]

        try:
            raw = manifest_path.read_text(encoding="utf-8")
            doc = json.loads(raw)
        except Exception:
            st.extra_roots = []
            st.manifest_last_mtime_ns = mtime_ns
            return []

        deps = doc.get("dependencies") if isinstance(doc, dict) else None
        if not isinstance(deps, dict):
            st.extra_roots = []
            st.manifest_last_mtime_ns = mtime_ns
            return []

        roots: list[str] = []
        base_dir = manifest_path.parent

        for _, ver in deps.items():
            if not isinstance(ver, str):
                continue
            v = ver.strip()
            if not v.startswith("file:"):
                continue
            suffix = v[len("file:"):].strip()
            # Handle file:///abs/path or file:/abs/path
            if suffix.startswith("///"):
                candidate = Path("/" + suffix.lstrip("/"))
            elif suffix.startswith("/"):
                candidate = Path(suffix)
            else:
                candidate = (base_dir / suffix).resolve()
            try:
                if candidate.exists() and candidate.is_dir():
                    roots.append(str(candidate))
            except OSError:
                continue

        # De-dupe, preserve order
        deduped: list[str] = []
        seen = set()
        for r in roots:
            if r not in seen:
                seen.add(r)
                deduped.append(r)

        st.extra_roots = deduped
        st.manifest_last_mtime_ns = mtime_ns
        return [Path(p) for p in deduped if p]

    def update_and_get(self, instance_id: str) -> dict[str, int | bool | None]:
        """
        Returns a small dict suitable for embedding in editor_state_v2.assets:
          - external_changes_dirty
          - external_changes_last_seen_unix_ms
          - dirty_since_unix_ms
          - last_cleared_unix_ms
        """
        st = self._get_state(instance_id)

        if _in_pytest():
            return {
                "external_changes_dirty": st.dirty,
                "external_changes_last_seen_unix_ms": st.external_changes_last_seen_unix_ms,
                "dirty_since_unix_ms": st.dirty_since_unix_ms,
                "last_cleared_unix_ms": st.last_cleared_unix_ms,
            }

        now = _now_unix_ms()
        if st.last_scan_unix_ms is not None and (now - st.last_scan_unix_ms) < self._scan_interval_ms:
            return {
                "external_changes_dirty": st.dirty,
                "external_changes_last_seen_unix_ms": st.external_changes_last_seen_unix_ms,
                "dirty_since_unix_ms": st.dirty_since_unix_ms,
                "last_cleared_unix_ms": st.last_cleared_unix_ms,
            }

        st.last_scan_unix_ms = now

        project_root = st.project_root
        if not project_root:
            return {
                "external_changes_dirty": st.dirty,
                "external_changes_last_seen_unix_ms": st.external_changes_last_seen_unix_ms,
                "dirty_since_unix_ms": st.dirty_since_unix_ms,
                "last_cleared_unix_ms": st.last_cleared_unix_ms,
            }

        root = Path(project_root)
        paths = [root / "Assets", root / "ProjectSettings", root / "Packages"]
        # Include any local package roots referenced by file: deps in Packages/manifest.json
        try:
            paths.extend(self._resolve_manifest_extra_roots(root, st))
        except Exception:
            pass
        newest = self._scan_paths_max_mtime_ns(paths)
        if newest is None:
            return {
                "external_changes_dirty": st.dirty,
                "external_changes_last_seen_unix_ms": st.external_changes_last_seen_unix_ms,
                "dirty_since_unix_ms": st.dirty_since_unix_ms,
                "last_cleared_unix_ms": st.last_cleared_unix_ms,
            }

        if st.last_seen_mtime_ns is None:
            st.last_seen_mtime_ns = newest
        elif newest > st.last_seen_mtime_ns:
            st.last_seen_mtime_ns = newest
            st.external_changes_last_seen_unix_ms = now
            if not st.dirty:
                st.dirty = True
                st.dirty_since_unix_ms = now

        return {
            "external_changes_dirty": st.dirty,
            "external_changes_last_seen_unix_ms": st.external_changes_last_seen_unix_ms,
            "dirty_since_unix_ms": st.dirty_since_unix_ms,
            "last_cleared_unix_ms": st.last_cleared_unix_ms,
        }


# Global singleton (simple, process-local)
external_changes_scanner = ExternalChangesScanner()
