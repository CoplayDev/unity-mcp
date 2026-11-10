"""The agent skill lives in two directories that must hold the same files.

`.claude/skills/unity-mcp-skill/` is the copy the Unity editor installs: its Install Skills
button mirrors that subtree from GitHub (SkillSyncService.SkillSubdir). `unity-mcp-skill/` at
the repo root is the copy users download by hand. Edits landed in one copy only, until the
installed skill linked two reference files it did not ship.
"""
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
PUBLIC = REPO_ROOT / "unity-mcp-skill"
INSTALLED = REPO_ROOT / ".claude" / "skills" / "unity-mcp-skill"


def _files(root: Path) -> dict[str, bytes]:
    # Line endings are normalised: core.autocrlf decides them per checkout, not per edit.
    return {
        path.relative_to(root).as_posix(): path.read_bytes().replace(b"\r\n", b"\n")
        for path in root.rglob("*")
        if path.is_file()
    }


def test_skill_copies_are_identical():
    public, installed = _files(PUBLIC), _files(INSTALLED)
    assert public, f"no skill files under {PUBLIC}"

    problems = [f"only in unity-mcp-skill/: {name}" for name in sorted(public.keys() - installed.keys())]
    problems += [f"only in .claude/skills/unity-mcp-skill/: {name}"
                 for name in sorted(installed.keys() - public.keys())]
    problems += [f"differs between the copies: {name}"
                 for name in sorted(public.keys() & installed.keys()) if public[name] != installed[name]]
    assert not problems, "\n".join(problems)
