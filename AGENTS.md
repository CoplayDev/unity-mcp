# Repository Guidelines

## Project Structure & Module Organization
This repo has two primary codebases:
- `Server/`: Python MCP server (`src/` runtime code, `tests/` Python tests, `pyproject.toml`).
- `MCPForUnity/`: Unity package (mainly `Editor/` tools/services and `Runtime/` code).

Supporting areas:
- `TestProjects/UnityMCPTests/`: Unity test project and C# test suites.
- `docs/`: user, migration, and development documentation.
- `tools/` and `scripts/`: release/dev utilities.
- `mcp_source.py`: switch Unity package source for local/upstream workflows.

## Build, Test, and Development Commands
- `cd Server && uv run src/main.py --transport stdio`: run local server over stdio.
- `cd Server && uv run src/main.py --transport http --http-url http://127.0.0.1:8080`: run local HTTP server.
- `cd Server && uv run pytest tests/ -v`: run Python server tests.
- `cd Server && uv run pytest tests/ --cov --cov-report=html`: run coverage and generate `htmlcov/`.
- `cd Server && uv run python -m cli.main editor tests --mode PlayMode`: run Unity tests via MCP bridge (Unity must be running).
- `python mcp_source.py`: point Unity project to upstream/main/beta/local package sources.

## Coding Style & Naming Conventions
- Python: 4-space indentation, snake_case modules/functions, keep async command handlers focused by domain under `Server/src/cli/commands/`.
- C#: PascalCase for types/methods, `ManageXxx` naming for tool classes, keep one tool responsibility per class.
- Prefer small, explicit code over one-off abstractions.
- Type checking uses `Server/pyrightconfig.json` (`typeCheckingMode: basic`).

## Testing Guidelines
- Add tests for all feature work in both touched layers when applicable (Python + Unity).
- Python test files follow `test_*.py` under `Server/tests/`.
- Validate behavior changes with targeted tests first, then broader suites before PR.

## Commit & Pull Request Guidelines
- Branch from `beta`; do not target `main` for feature development.
- Follow existing commit style: `feat(scope): ...`, `fix: ...`, `docs: ...`, `chore: ...`.
- Keep commits focused and reference issue/PR IDs when relevant (e.g., `(#859)`).
- PRs should include: clear summary, linked issue/discussion, test evidence, and screenshots/GIFs for Unity UI/tooling changes.

## Security & Configuration Tips
- Default local HTTP endpoint is `127.0.0.1`; avoid exposing `0.0.0.0` unless explicitly needed.
- For local server iteration in Unity, use **Server Source Override** to your `Server/` path and enable **Dev Mode**.
