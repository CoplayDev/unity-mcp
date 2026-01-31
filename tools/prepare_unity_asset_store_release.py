#!/usr/bin/env python3
"""Prepare MCPForUnity for Asset Store upload.

Usage:
  python tools/prepare_unity_asset_store_release.py \
    --remote-url https://your.remote.endpoint/ \
    --asset-project /path/to/AssetStoreUploads \
    --backup
"""
from __future__ import annotations

import argparse
import datetime as dt
import re
import shutil
import tempfile
from pathlib import Path


REPO_ROOT_DEFAULT = Path(__file__).resolve(
).parents[1]  # adjust if you place elsewhere


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def write_text(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8")


def replace_once(path: Path, pattern: str, repl: str) -> None:
    """
    Regex replace exactly once, else raise.
    """
    original = read_text(path)
    new, n = re.subn(pattern, repl, original, flags=re.MULTILINE)
    if n != 1:
        raise RuntimeError(
            f"{path}: expected 1 replacement for pattern, got {n}")
    if new != original:
        write_text(path, new)


def remove_line_exact(path: Path, line: str) -> None:
    original = read_text(path)
    lines = original.splitlines(keepends=True)

    removed = 0
    kept: list[str] = []
    for l in lines:
        if l.strip() == line:
            removed += 1
            continue
        kept.append(l)

    if removed != 1:
        raise RuntimeError(
            f"{path}: expected to remove exactly 1 line '{line}', removed {removed}")

    write_text(path, "".join(kept))


def backup_dir(src: Path, backup_root: Path) -> Path:
    ts = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_path = backup_root / f"{src.name}.backup.{ts}"
    shutil.copytree(src, backup_path)
    return backup_path


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Prepare MCPForUnity for Asset Store upload.")
    parser.add_argument(
        "--repo-root",
        default=str(REPO_ROOT_DEFAULT),
        help="Path to unity-mcp repo root (default: inferred from script location).",
    )
    parser.add_argument(
        "--asset-project",
        default=None,
        help="Path to the Unity project used for Asset Store uploads.",
    )
    parser.add_argument(
        "--remote-url",
        required=True,
        help="Remote MCP HTTP base URL to set as default for Asset Store builds.",
    )
    parser.add_argument(
        "--backup",
        action="store_true",
        help="Backup existing Assets/MCPForUnity before replacing.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Only validate that operations would succeed; do not write/copy/delete.",
    )
    args = parser.parse_args()

    repo_root = Path(args.repo_root).expanduser().resolve()
    asset_project = Path(args.asset_project).expanduser().resolve(
    ) if args.asset_project else (repo_root / "TestProjects" / "AssetStoreUploads")
    remote_url = args.remote_url.strip()
    if not remote_url:
        raise RuntimeError("--remote-url must be a non-empty URL")

    source_mcp = repo_root / "MCPForUnity"
    if not source_mcp.is_dir():
        raise RuntimeError(
            f"Source MCPForUnity folder not found: {source_mcp}")

    assets_dir = asset_project / "Assets"
    if not assets_dir.is_dir():
        raise RuntimeError(f"Assets folder not found: {assets_dir}")

    dest_mcp = assets_dir / "MCPForUnity"

    if args.dry_run:
        print("[dry-run] Validated paths. No changes applied.")
        print("[dry-run] Would stage a temporary copy of MCPForUnity and apply Asset Store edits there.")
        print(
            f"[dry-run] Would replace:\n- {dest_mcp}\n  with\n- {source_mcp}")
        return 0

    # 1) Stage a temporary copy of MCPForUnity and apply Asset Store-specific edits there.
    with tempfile.TemporaryDirectory(prefix="mcpforunity_assetstore_") as tmpdir:
        staged_mcp = Path(tmpdir) / "MCPForUnity"
        shutil.copytree(source_mcp, staged_mcp)

        setup_service = staged_mcp / "Editor" / "Setup" / "SetupWindowService.cs"
        menu_file = staged_mcp / "Editor" / "MenuItems" / "MCPForUnityMenu.cs"
        http_util = staged_mcp / "Editor" / "Helpers" / "HttpEndpointUtility.cs"
        connection_section = staged_mcp / "Editor" / "Windows" / \
            "Components" / "Connection" / "McpConnectionSection.cs"

        for f in (setup_service, menu_file, http_util, connection_section):
            if not f.is_file():
                raise RuntimeError(f"Expected file not found: {f}")

        # Remove auto-popup setup window for Asset Store packaging
        remove_line_exact(setup_service, "[InitializeOnLoad]")

        # Set default remote base URL to the hosted endpoint
        replace_once(
            http_util,
            r'private const string DefaultRemoteBaseUrl = "";',
            f'private const string DefaultRemoteBaseUrl = "{remote_url}";',
        )

        # Default transport to HTTP Remote and persist inferred scope when missing
        replace_once(
            connection_section,
            r'transportDropdown\.Init\(TransportProtocol\.HTTPLocal\);',
            'transportDropdown.Init(TransportProtocol.HTTPRemote);',
        )
        replace_once(
            connection_section,
            r'scope = MCPServiceLocator\.Server\.IsLocalUrl\(\) \? "local" : "remote";',
            'scope = "remote";',
        )

        # 2) Replace Assets/MCPForUnity in the target project
        if dest_mcp.exists():
            if args.backup:
                backup_root = asset_project / "AssetStoreBackups"
                backup_root.mkdir(parents=True, exist_ok=True)
                backup_path = backup_dir(dest_mcp, backup_root)
                print(f"Backed up existing folder to: {backup_path}")

            shutil.rmtree(dest_mcp)

        shutil.copytree(staged_mcp, dest_mcp)

    print("Done.")
    print(f"- Source (unchanged): {source_mcp}")
    print(f"- Updated Asset Store project folder: {dest_mcp}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
