"""Characterization tests for unity-mcp build and release infrastructure.

This package contains pytest-based characterization tests that document
the CURRENT behavior of build, release, and testing tools without refactoring.

Test Structure:
- test_build_release_characterization.py: Main characterization suite

Domains Covered:
1. Version Management (update_versions.py)
2. MCPB Bundle Generation (generate_mcpb.py)
3. Asset Store Preparation (prepare_unity_asset_store_release.py)
4. Stress Testing (stress_mcp.py, stress_editor_state.py)
5. Release Workflows and Checklists
6. Git Integration Patterns

Test Style:
- Characterization tests (capture current behavior)
- No refactoring performed
- Heavy use of mocking and fixtures
- Async tests using pytest-asyncio
- Comprehensive docstrings explaining patterns

Running Tests:
    cd /Users/davidsarno/unity-mcp
    python -m pytest tools/tests/ -v
    python -m pytest tools/tests/test_build_release_characterization.py -v --tb=short
"""
