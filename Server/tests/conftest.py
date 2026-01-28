"""Pytest configuration for unity-mcp tests."""
import sys
from pathlib import Path
import pytest

# Add src directory to Python path so tests can import cli, transport, etc.
src_path = Path(__file__).parent.parent / "src"
if str(src_path) not in sys.path:
    sys.path.insert(0, str(src_path))


@pytest.fixture(scope="module", autouse=True)
def cleanup_telemetry():
    """Clean up telemetry singleton after each test module to prevent state pollution."""
    yield
    # Import here to avoid circular import issues
    try:
        from core.telemetry import reset_telemetry
        reset_telemetry()
    except Exception:
        pass  # Ignore if telemetry not used in this module


@pytest.fixture(scope="class")
def fresh_telemetry():
    """Reset telemetry before test class runs (for tests that need clean state)."""
    try:
        from core.telemetry import reset_telemetry
        reset_telemetry()
    except Exception:
        pass
    yield


def pytest_collection_modifyitems(session, config, items):
    """Reorder tests so characterization tests run before integration tests.

    This prevents integration tests from initializing the telemetry singleton
    before characterization tests can mock it.
    """
    # Separate integration tests from other tests
    integration_tests = []
    other_tests = []

    for item in items:
        # Check if test is in integration/ directory
        if "integration" in str(item.fspath):
            integration_tests.append(item)
        else:
            other_tests.append(item)

    # Reorder: characterization/unit tests first, then integration tests
    items[:] = other_tests + integration_tests
