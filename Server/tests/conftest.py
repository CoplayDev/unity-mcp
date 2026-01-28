"""Pytest configuration for unity-mcp tests."""
import logging
import sys
from pathlib import Path
import pytest

logger = logging.getLogger(__name__)

# Add src directory to Python path so tests can import cli, transport, etc.
src_path = Path(__file__).parent.parent / "src"
if str(src_path) not in sys.path:
    sys.path.insert(0, str(src_path))


def _safe_reset_telemetry() -> None:
    """Safely reset telemetry, distinguishing import errors from reset failures."""
    try:
        from core.telemetry import reset_telemetry
    except ImportError:
        # Telemetry module not available - this is normal if telemetry not used
        return
    try:
        reset_telemetry()
    except Exception as exc:
        logger.debug("Telemetry reset failed (may indicate cleanup needed)", exc_info=exc)


@pytest.fixture(scope="module", autouse=True)
def cleanup_telemetry():
    """Clean up telemetry singleton after each test module to prevent state pollution."""
    yield
    _safe_reset_telemetry()


@pytest.fixture(scope="class")
def fresh_telemetry():
    """Reset telemetry before test class runs (for tests that need clean state)."""
    _safe_reset_telemetry()
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
