#!/usr/bin/env python3
"""
Sync release notes from GitHub Releases into:
  - website/docs/releases.md         (full history, grouped by minor)
  - README.md "Recent Updates"       (latest N releases between sentinel markers)

Why a sync script: the previous releases.md was hand-maintained and went stale
(it claimed v9.6.3 was latest when v9.7.0 had shipped). GitHub Releases is the
single source of truth; this script makes it the only source.

Usage:
  python tools/sync_release_notes.py                # write + verify
  python tools/sync_release_notes.py --check        # CI: fail if drift
  GITHUB_TOKEN=xxx python tools/sync_release_notes.py   # higher rate limit

Local runs without a token use anonymous GitHub API (60 req/hr — plenty for one
sync since we only paginate the /releases endpoint).
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import ssl
import subprocess
import sys
import textwrap
import urllib.error
import urllib.request
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
RELEASES_MD = REPO_ROOT / "website" / "docs" / "releases.md"
README_MD = REPO_ROOT / "README.md"

OWNER = "CoplayDev"
REPO = "unity-mcp"
API = f"https://api.github.com/repos/{OWNER}/{REPO}/releases"

README_RECENT_COUNT = 5

README_MARKER_OPEN = "<!-- recent-updates:start -->"
README_MARKER_CLOSE = "<!-- recent-updates:end -->"

RELEASES_HEADER = textwrap.dedent(
    """\
    ---
    id: releases
    slug: /releases
    title: Release Notes
    sidebar_label: Releases
    description: Full version-by-version change history for MCP for Unity.
    ---

    # Release Notes

    Latest releases land in [`beta`](https://github.com/CoplayDev/unity-mcp/tree/beta) before promotion to [`main`](https://github.com/CoplayDev/unity-mcp/tree/main). Major breaking changes get a dedicated migration guide under [Migrations](/migrations/v5).

    For the canonical changelog with PR links, see [GitHub Releases](https://github.com/CoplayDev/unity-mcp/releases).

    > Auto-generated from the GitHub Releases API by `tools/sync_release_notes.py`. Do not hand-edit — changes will be overwritten on the next sync.

    """
)


# ---------------------------------------------------------------------------
# Fetch
# ---------------------------------------------------------------------------


def _fetch_via_gh(path: str) -> list[dict] | None:
    """Prefer `gh api` when available — handles auth + SSL cleanly across
    macOS Python distributions that miss the system trust store."""
    if not shutil.which("gh"):
        return None
    try:
        result = subprocess.run(
            ["gh", "api", path, "--paginate"],
            check=True,
            capture_output=True,
            text=True,
            timeout=60,
        )
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as e:
        print(f"gh api failed ({e}); falling back to urllib.", file=sys.stderr)
        return None
    # `gh api --paginate` concatenates JSON arrays as `][`. Split + parse.
    text = result.stdout.strip()
    if not text:
        return []
    if "][" in text:
        text = "[" + text.replace("][", ",") + "]"
        # Now we may have [[..],[..]] — flatten.
        nested = json.loads(text)
        flat: list[dict] = []
        for chunk in nested:
            flat.extend(chunk if isinstance(chunk, list) else [chunk])
        return flat
    return json.loads(text)


def _fetch_via_urllib(url: str) -> list[dict]:
    headers = {
        "User-Agent": "mcp-for-unity-docs-sync",
        "Accept": "application/vnd.github+json",
    }
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"

    ctx = ssl.create_default_context()
    try:
        import certifi  # type: ignore
        ctx = ssl.create_default_context(cafile=certifi.where())
    except ImportError:
        pass

    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, timeout=30, context=ctx) as resp:
        return json.loads(resp.read().decode("utf-8"))


def fetch_all_releases() -> list[dict]:
    # Try `gh api` first.
    via_gh = _fetch_via_gh(f"repos/{OWNER}/{REPO}/releases?per_page=100")
    if via_gh is not None:
        return [r for r in via_gh if not r.get("draft")]

    # Fallback: paginate urllib.
    all_releases: list[dict] = []
    page = 1
    while True:
        batch = _fetch_via_urllib(f"{API}?per_page=100&page={page}")
        if not batch:
            break
        all_releases.extend(batch)
        if len(batch) < 100:
            break
        page += 1
    return [r for r in all_releases if not r.get("draft")]


# ---------------------------------------------------------------------------
# Render
# ---------------------------------------------------------------------------


_VERSION_RE = re.compile(r"^v?(\d+)\.(\d+)\.(\d+)")


def _parse_version(tag: str) -> tuple[int, int, int] | None:
    m = _VERSION_RE.match(tag.strip())
    if not m:
        return None
    return tuple(int(x) for x in m.groups())  # type: ignore[return-value]


def _minor_key(tag: str) -> str:
    v = _parse_version(tag)
    if not v:
        return tag
    return f"v{v[0]}.{v[1]} series"


def _normalize_body(body: str | None) -> str:
    if not body:
        return "_No release notes._"
    body = body.replace("\r\n", "\n").replace("\r", "\n")
    # Trim trailing whitespace; collapse 3+ blank lines.
    body = body.strip()
    body = re.sub(r"\n{3,}", "\n\n", body)
    return body


def _digest(body: str | None, max_chars: int = 280) -> str:
    """Pull a one-line digest for the README block. Prefers the first non-empty
    line that isn't a heading or list-of-PRs link."""
    if not body:
        return ""
    for raw in body.splitlines():
        line = raw.strip()
        if not line:
            continue
        # Skip headings and bullet-of-PR lines.
        if line.startswith(("#", ">", "**Full Changelog**")):
            continue
        if line.startswith(("*", "-")) and "github.com" in line and "pull/" in line:
            continue
        # Strip bullet prefixes for the digest.
        line = re.sub(r"^[*-]\s+", "", line)
        # Strip leading markdown emphasis.
        line = re.sub(r"^\*\*[^*]+\*\*[:.\s—-]*", "", line)
        if len(line) > max_chars:
            line = line[: max_chars - 1].rstrip() + "…"
        return line
    return ""


def render_releases_md(releases: list[dict]) -> str:
    out = [RELEASES_HEADER]

    grouped: dict[str, list[dict]] = defaultdict(list)
    order: list[str] = []
    for r in releases:
        key = _minor_key(r["tag_name"])
        if key not in grouped:
            order.append(key)
        grouped[key].append(r)

    for key in order:
        out.append(f"## {key}\n")
        for r in grouped[key]:
            tag = r["tag_name"]
            name = (r.get("name") or tag).strip()
            date = (r.get("published_at") or "").split("T")[0]
            prerelease = " (beta)" if r.get("prerelease") else ""
            url = r.get("html_url") or f"https://github.com/{OWNER}/{REPO}/releases/tag/{tag}"
            body = _normalize_body(r.get("body"))

            summary = name if name and name != tag else tag
            out.append(f"### [{summary}{prerelease}]({url}) — {date}\n")
            out.append(f"<details>\n<summary>Show release notes</summary>\n\n{body}\n\n</details>\n")
        out.append("")  # blank line between groups

    # Migration footer
    out.append("## Migration guides\n")
    out.append(
        "Breaking changes from prior major versions live under [Migrations](/migrations/v5):\n"
    )
    out.append("- [v5 — UnityMcpBridge → MCPForUnity](/migrations/v5)")
    out.append("- [v6 — New Editor Window (UI Toolkit + service architecture)](/migrations/v6)")
    out.append("- [v8 — HTTP and Stdio support](/migrations/v8)")
    out.append("- [v10 — Asset Generation and Docs Refresh](/migrations/v10)\n")
    return "\n".join(out)


def render_readme_recent(releases: list[dict], n: int = README_RECENT_COUNT) -> str:
    lines = [README_MARKER_OPEN]
    lines.append("<details>")
    lines.append("<summary><strong>Recent Updates</strong></summary>")
    lines.append("")
    for r in releases[:n]:
        tag = r["tag_name"]
        date = (r.get("published_at") or "").split("T")[0]
        url = r.get("html_url") or f"https://github.com/{OWNER}/{REPO}/releases/tag/{tag}"
        digest = _digest(r.get("body"))
        suffix = f" — {digest}" if digest else ""
        prerelease = " *(beta)*" if r.get("prerelease") else ""
        lines.append(f"* **[{tag}{prerelease}]({url})** ({date}){suffix}")
    lines.append("")
    lines.append(
        "Full history: [Release Notes](https://coplaydev.github.io/unity-mcp/releases)."
    )
    lines.append("")
    lines.append("</details>")
    lines.append(README_MARKER_CLOSE)
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Apply
# ---------------------------------------------------------------------------


def replace_marked_block(text: str, replacement: str) -> str:
    pattern = re.compile(
        re.escape(README_MARKER_OPEN) + r".*?" + re.escape(README_MARKER_CLOSE),
        re.DOTALL,
    )
    if pattern.search(text):
        return pattern.sub(replacement, text)
    # First-time insert: try to replace the legacy <details><summary>Recent Updates</summary>
    legacy = re.compile(
        r"<details>\s*<summary>\s*<strong>Recent Updates</strong>\s*</summary>.*?</details>",
        re.DOTALL,
    )
    if legacy.search(text):
        return legacy.sub(replacement, text)
    # Otherwise append before the "## Community" header if present.
    anchor = "## Community"
    if anchor in text:
        return text.replace(anchor, f"{replacement}\n\n{anchor}", 1)
    return text + "\n\n" + replacement + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                        help="Exit non-zero if files would change. Used by CI to detect drift.")
    args = parser.parse_args(argv)

    try:
        releases = fetch_all_releases()
    except urllib.error.HTTPError as e:
        print(f"GitHub API error: {e.code} {e.reason}", file=sys.stderr)
        if e.code == 403:
            print("Hint: set GITHUB_TOKEN to lift the anonymous rate limit.", file=sys.stderr)
        return 2

    if not releases:
        print("No releases returned by GitHub API. Aborting to avoid blanking files.", file=sys.stderr)
        return 2

    new_releases = render_releases_md(releases)
    new_readme_block = render_readme_recent(releases)

    existing_releases = RELEASES_MD.read_text(encoding="utf-8") if RELEASES_MD.exists() else ""
    existing_readme = README_MD.read_text(encoding="utf-8") if README_MD.exists() else ""
    new_readme = replace_marked_block(existing_readme, new_readme_block)

    releases_drift = existing_releases != new_releases
    readme_drift = existing_readme != new_readme

    if args.check:
        if releases_drift or readme_drift:
            print("Release notes are stale. Run:")
            print("  python tools/sync_release_notes.py")
            print("then commit the changes.")
            if releases_drift:
                print(f"  - drift in {RELEASES_MD.relative_to(REPO_ROOT)}")
            if readme_drift:
                print(f"  - drift in {README_MD.relative_to(REPO_ROOT)}")
            return 1
        print(f"Release notes are up-to-date ({len(releases)} releases).")
        return 0

    if releases_drift:
        RELEASES_MD.write_text(new_releases, encoding="utf-8")
    if readme_drift:
        README_MD.write_text(new_readme, encoding="utf-8")

    print(f"Synced {len(releases)} releases.")
    print(f"  - {RELEASES_MD.relative_to(REPO_ROOT)}: {'updated' if releases_drift else 'unchanged'}")
    print(f"  - {README_MD.relative_to(REPO_ROOT)}:    {'updated' if readme_drift else 'unchanged'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
