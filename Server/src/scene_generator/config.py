"""Centralized configuration for the scene generator pipeline.

Loads settings from a .env file (if present) next to this module, then
falls back to environment variables, then to hardcoded defaults.

Usage in other modules:
    from scene_generator.config import cfg

    api_key = cfg.openai_api_key
    model   = cfg.brainstorm_model
"""
from __future__ import annotations

import os
from pathlib import Path

# ---------------------------------------------------------------------------
# .env loader (no dependency on python-dotenv)
# ---------------------------------------------------------------------------

_ENV_DIR = Path(__file__).resolve().parent


def _load_dotenv(directory: Path = _ENV_DIR) -> None:
    """Parse a .env file and inject values into os.environ.

    Only sets a variable if it is NOT already present in the environment,
    so real env vars always win.
    """
    env_file = directory / ".env"
    if not env_file.is_file():
        return
    for line in env_file.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            continue
        key, _, value = line.partition("=")
        key = key.strip()
        value = value.strip().strip("'\"")
        if key and key not in os.environ:
            os.environ[key] = value


_load_dotenv()


# ---------------------------------------------------------------------------
# Config class
# ---------------------------------------------------------------------------

_DEFAULT_MODEL = "gpt-5.2"


class _Config:
    """Read-only configuration object. All values resolve at access time so
    they pick up any later changes to os.environ."""

    # ── API key ──────────────────────────────────────────────────────

    @property
    def openai_api_key(self) -> str | None:
        """Resolve OpenAI API key (first match wins)."""
        for var in (
            "OPENAI_API_KEY",
            "SCENE_BUILDER_DEFAULT_OPENAI_API_KEY",
            "SCENE_BUILDER_DEFAULT_API_KEY",
        ):
            val = os.environ.get(var)
            if val:
                return val
        return None

    # ── Model names ──────────────────────────────────────────────────

    @property
    def brainstorm_model(self) -> str:
        return os.environ.get("BRAINSTORM_MODEL", _DEFAULT_MODEL)

    @property
    def script_architect_model(self) -> str:
        return os.environ.get("SCRIPT_ARCHITECT_MODEL", _DEFAULT_MODEL)

    @property
    def merge_model(self) -> str:
        return os.environ.get("MERGE_MODEL", _DEFAULT_MODEL)

    @property
    def codegen_model(self) -> str:
        return os.environ.get("CODEGEN_MODEL", _DEFAULT_MODEL)

    # ── Output limits ────────────────────────────────────────────────

    @property
    def max_output_tokens(self) -> int:
        """Maximum output tokens per LLM call (prevents runaway generation)."""
        val = os.environ.get("MAX_OUTPUT_TOKENS", "16000")
        try:
            return int(val)
        except ValueError:
            return 16000

    # ── Streamlit UI model defaults ──────────────────────────────────

    @property
    def openai_model(self) -> str:
        return os.environ.get("OPENAI_MODEL", _DEFAULT_MODEL)

    @property
    def anthropic_model(self) -> str:
        return os.environ.get("ANTHROPIC_MODEL", "claude-sonnet-4-5-20250929")


cfg = _Config()
