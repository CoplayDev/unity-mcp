#!/usr/bin/env python3
"""Update version across all project files.

This script updates the version in all files that need it:
- MCPForUnity/package.json (Unity package version)
- manifest.json (MCP bundle manifest)
- Server/pyproject.toml (Python package version)
- Server/README.md (version references)
- README.md (fixed version examples)
- docs/i18n/README-zh.md (fixed version examples)

Usage:
    python3 tools/update_versions.py [--dry-run] [--version VERSION]

Options:
    --dry-run: Show what would be updated without making changes
    --version: Specify version to use (auto-detected from package.json if not provided)

Examples:
    # Update all files to match package.json version
    python3 tools/update_versions.py
    
    # Update all files to a specific version
    python3 tools/update_versions.py --version 9.2.0
    
    # Dry run to see what would be updated
    python3 tools/update_versions.py --dry-run
"""

import argparse
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
PACKAGE_JSON = REPO_ROOT / "MCPForUnity" / "package.json"
MANIFEST_JSON = REPO_ROOT / "manifest.json"
PYPROJECT_TOML = REPO_ROOT / "Server" / "pyproject.toml"
SERVER_README = REPO_ROOT / "Server" / "README.md"
ROOT_README = REPO_ROOT / "README.md"
ZH_README = REPO_ROOT / "docs" / "i18n" / "README-zh.md"


def load_package_version() -> str:
    """Load version from package.json."""
    if not PACKAGE_JSON.exists():
        raise FileNotFoundError(f"Package file not found: {PACKAGE_JSON}")

    package_data = json.loads(PACKAGE_JSON.read_text(encoding="utf-8"))
    version = package_data.get("version")

    if not version:
        raise ValueError("No version found in package.json")

    return version


def update_package_json(new_version: str, dry_run: bool = False) -> bool:
    """Update version in MCPForUnity/package.json."""
    if not PACKAGE_JSON.exists():
        print(f"Warning: {PACKAGE_JSON.relative_to(REPO_ROOT)} not found")
        return False

    package_data = json.loads(PACKAGE_JSON.read_text(encoding="utf-8"))
    current_version = package_data.get("version", "unknown")

    if current_version == new_version:
        print(f"✓ {PACKAGE_JSON.relative_to(REPO_ROOT)} already at v{new_version}")
        return False

    print(
        f"Updating {PACKAGE_JSON.relative_to(REPO_ROOT)}: {current_version} → {new_version}")

    if not dry_run:
        package_data["version"] = new_version
        PACKAGE_JSON.write_text(
            json.dumps(package_data, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )

    return True


def update_manifest_json(new_version: str, dry_run: bool = False) -> bool:
    """Update version in manifest.json."""
    if not MANIFEST_JSON.exists():
        print(f"Warning: {MANIFEST_JSON.relative_to(REPO_ROOT)} not found")
        return False

    manifest = json.loads(MANIFEST_JSON.read_text(encoding="utf-8"))
    current_version = manifest.get("version", "unknown")

    if current_version == new_version:
        print(f"✓ {MANIFEST_JSON.relative_to(REPO_ROOT)} already at v{new_version}")
        return False

    print(
        f"Updating {MANIFEST_JSON.relative_to(REPO_ROOT)}: {current_version} → {new_version}")

    if not dry_run:
        manifest["version"] = new_version
        MANIFEST_JSON.write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )

    return True


def update_pyproject_toml(new_version: str, dry_run: bool = False) -> bool:
    """Update version in Server/pyproject.toml."""
    if not PYPROJECT_TOML.exists():
        print(f"Warning: {PYPROJECT_TOML.relative_to(REPO_ROOT)} not found")
        return False

    content = PYPROJECT_TOML.read_text(encoding="utf-8")

    # Find current version
    version_match = re.search(r'^version = "([^"]+)"', content, re.MULTILINE)
    if not version_match:
        print(
            f"Warning: Could not find version in {PYPROJECT_TOML.relative_to(REPO_ROOT)}")
        return False

    current_version = version_match.group(1)

    if current_version == new_version:
        print(f"✓ {PYPROJECT_TOML.relative_to(REPO_ROOT)} already at v{new_version}")
        return False

    print(
        f"Updating {PYPROJECT_TOML.relative_to(REPO_ROOT)}: {current_version} → {new_version}")

    if not dry_run:
        # Replace only the first occurrence (the version field)
        content = re.sub(
            r'^version = ".*"', f'version = "{new_version}"', content, count=1, flags=re.MULTILINE)
        PYPROJECT_TOML.write_text(content, encoding="utf-8")

    return True


def update_server_readme(new_version: str, dry_run: bool = False) -> bool:
    """Update version references in Server/README.md."""
    if not SERVER_README.exists():
        print(f"Warning: {SERVER_README.relative_to(REPO_ROOT)} not found")
        return False

    content = SERVER_README.read_text(encoding="utf-8")

    # Pattern to match git+https URLs with version tags
    pattern = r'git\+https://github\.com/CoplayDev/unity-mcp@v[0-9]+\.[0-9]+\.[0-9]+#subdirectory=Server'
    replacement = f'git+https://github.com/CoplayDev/unity-mcp@v{new_version}#subdirectory=Server'

    if not re.search(pattern, content):
        print(
            f"✓ {SERVER_README.relative_to(REPO_ROOT)} has no version references to update")
        return False

    print(
        f"Updating version references in {SERVER_README.relative_to(REPO_ROOT)}")

    if not dry_run:
        content = re.sub(pattern, replacement, content)
        SERVER_README.write_text(content, encoding="utf-8")

    return True


def update_root_readme(new_version: str, dry_run: bool = False) -> bool:
    """Update fixed version examples in README.md."""
    if not ROOT_README.exists():
        print(f"Warning: {ROOT_README.relative_to(REPO_ROOT)} not found")
        return False

    content = ROOT_README.read_text(encoding="utf-8")

    # Pattern to match git URLs with fixed version tags
    pattern = r'https://github\.com/CoplayDev/unity-mcp\.git\?path=/MCPForUnity#v[0-9]+\.[0-9]+\.[0-9]+'
    replacement = f'https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v{new_version}'

    if not re.search(pattern, content):
        print(
            f"✓ {ROOT_README.relative_to(REPO_ROOT)} has no version references to update")
        return False

    print(
        f"Updating version references in {ROOT_README.relative_to(REPO_ROOT)}")

    if not dry_run:
        content = re.sub(pattern, replacement, content)
        ROOT_README.write_text(content, encoding="utf-8")

    return True


def update_zh_readme(new_version: str, dry_run: bool = False) -> bool:
    """Update fixed version examples in docs/i18n/README-zh.md."""
    if not ZH_README.exists():
        print(f"Warning: {ZH_README.relative_to(REPO_ROOT)} not found")
        return False

    content = ZH_README.read_text(encoding="utf-8")

    # Pattern to match git URLs with fixed version tags
    pattern = r'https://github\.com/CoplayDev/unity-mcp\.git\?path=/MCPForUnity#v[0-9]+\.[0-9]+\.[0-9]+'
    replacement = f'https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v{new_version}'

    if not re.search(pattern, content):
        print(
            f"✓ {ZH_README.relative_to(REPO_ROOT)} has no version references to update")
        return False

    print(f"Updating version references in {ZH_README.relative_to(REPO_ROOT)}")

    if not dry_run:
        content = re.sub(pattern, replacement, content)
        ZH_README.write_text(content, encoding="utf-8")

    return True


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Update version across all project files",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Show what would be updated without making changes",
    )
    parser.add_argument(
        "--version",
        help="Version to set (auto-detected from package.json if not provided)",
    )

    args = parser.parse_args()

    try:
        # Determine version
        if args.version:
            version = args.version
            print(f"Using specified version: {version}")
        else:
            version = load_package_version()
            print(f"Auto-detected version from package.json: {version}")

        # Update all files
        updates_made = []

        # Always update package.json if a version is specified
        if args.version:
            if update_package_json(version, args.dry_run):
                updates_made.append("MCPForUnity/package.json")

        if update_manifest_json(version, args.dry_run):
            updates_made.append("manifest.json")

        if update_pyproject_toml(version, args.dry_run):
            updates_made.append("Server/pyproject.toml")

        if update_server_readme(version, args.dry_run):
            updates_made.append("Server/README.md")


        # Summary
        if args.dry_run:
            print("\nDry run complete. No files were modified.")
        else:
            if updates_made:
                print(
                    f"\nUpdated {len(updates_made)} files: {', '.join(updates_made)}")
            else:
                print("\nAll files already at the correct version.")

        return 0

    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
