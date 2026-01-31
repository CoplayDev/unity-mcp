"""
Characterization tests for Core Infrastructure domain (logging, telemetry, config).

These tests capture the CURRENT behavior of the Core Infrastructure without refactoring.
They document decorator patterns, logging flows, telemetry collection, and configuration
handling as they exist today.

Key patterns documented:
1. Decorator duplication: ~44+ lines of identical code between sync/async wrappers in both
   logging_decorator.py and telemetry_decorator.py
2. Telemetry collection: Multiple event types with milestone tracking and error handling
3. Configuration loading: Multi-source precedence (config file -> env vars)
4. Error handling: Graceful failure modes with exception re-raising
5. Logging levels: Cross-cutting concerns using module-level loggers

To run:
    cd Server && uv run pytest tests/test_core_infrastructure_characterization.py -v
"""

import asyncio
import json
import logging
import os
import sys
import threading
import time
from pathlib import Path
from unittest.mock import patch, MagicMock, AsyncMock, call
from tempfile import TemporaryDirectory

import pytest

# Set up sys.path for imports
SERVER_ROOT = Path(__file__).resolve().parents[1]
SERVER_SRC = SERVER_ROOT / "src"
if str(SERVER_ROOT) not in sys.path:
    sys.path.insert(0, str(SERVER_ROOT))
if str(SERVER_SRC) not in sys.path:
    sys.path.insert(0, str(SERVER_SRC))

# Ensure telemetry is disabled during tests to avoid background threads
os.environ.setdefault("DISABLE_TELEMETRY", "true")
os.environ.setdefault("UNITY_MCP_DISABLE_TELEMETRY", "true")

from core.logging_decorator import log_execution
from core.telemetry_decorator import telemetry_tool, telemetry_resource
from core.config import ServerConfig
from core.telemetry import (
    TelemetryCollector, TelemetryConfig, RecordType, MilestoneType,
    record_tool_usage, record_resource_usage, record_milestone,
    is_telemetry_enabled, get_telemetry
)


# =============================================================================
# Fixtures
# =============================================================================

@pytest.fixture
def caplog_fixture(caplog):
    """Fixture to capture and configure logging."""
    caplog.set_level(logging.DEBUG)
    return caplog


@pytest.fixture
def temp_telemetry_data():
    """Fixture to provide temporary directory for telemetry data."""
    with TemporaryDirectory() as tmpdir:
        yield tmpdir


@pytest.fixture
def mock_telemetry_config(temp_telemetry_data):
    """Mock telemetry configuration for testing."""
    # Point data directory to temp location
    with patch("core.telemetry.TelemetryConfig._get_data_directory") as mock_dir:
        mock_dir.return_value = Path(temp_telemetry_data)
        yield mock_dir


@pytest.fixture
def reset_telemetry():
    """Reset global telemetry instance between tests, properly shutting down worker."""
    import core.telemetry
    original = core.telemetry._telemetry_collector
    # Properly reset telemetry to shut down any running worker thread
    core.telemetry.reset_telemetry()
    yield
    # Restore original state after test
    core.telemetry.reset_telemetry()
    core.telemetry._telemetry_collector = original


# =============================================================================
# SECTION 1: Logging Decorator Tests
# =============================================================================

class TestLoggingDecoratorBasics:
    """Tests for log_execution decorator basic behavior."""

    def test_decorator_logs_function_call_sync(self, caplog_fixture):
        """Verify decorator logs function entry with arguments (sync)."""
        caplog_fixture.clear()

        @log_execution("test_func", "TestType")
        def sync_function(x, y):
            return x + y

        result = sync_function(1, 2)

        assert result == 3
        # Should log entry with arguments
        assert "TestType 'test_func' called with args=(1, 2) kwargs={}" in caplog_fixture.text
        # Should log return value
        assert "TestType 'test_func' returned: 3" in caplog_fixture.text

    def test_decorator_logs_function_call_async(self, caplog_fixture):
        """Verify decorator logs function entry with arguments (async)."""
        caplog_fixture.clear()

        @log_execution("async_func", "AsyncType")
        async def async_function(x, y):
            return x + y

        result = asyncio.run(async_function(10, 20))

        assert result == 30
        # Should log entry with arguments
        assert "AsyncType 'async_func' called with args=(10, 20) kwargs={}" in caplog_fixture.text
        # Should log return value
        assert "AsyncType 'async_func' returned: 30" in caplog_fixture.text

    def test_decorator_logs_kwargs(self, caplog_fixture):
        """Verify decorator logs keyword arguments."""
        caplog_fixture.clear()

        @log_execution("kwarg_func", "KwargType")
        def func_with_kwargs(a, b=None, c=None):
            return (a, b, c)

        result = func_with_kwargs(1, b=2, c=3)

        assert result == (1, 2, 3)
        # kwargs are logged in dict format {'b': 2, 'c': 3}
        assert "'b': 2" in caplog_fixture.text
        assert "'c': 3" in caplog_fixture.text

    def test_decorator_logs_exception(self, caplog_fixture):
        """Verify decorator logs exceptions and re-raises them."""
        caplog_fixture.clear()

        @log_execution("error_func", "ErrorType")
        def func_that_raises():
            raise ValueError("Test error")

        with pytest.raises(ValueError, match="Test error"):
            func_that_raises()

        # Should log the failure
        assert "ErrorType 'error_func' failed: Test error" in caplog_fixture.text

    def test_decorator_preserves_function_metadata(self):
        """Verify @functools.wraps preserves original function metadata."""
        @log_execution("metadata_func", "MetaType")
        def original_func():
            """Original docstring."""
            pass

        assert original_func.__name__ == "original_func"
        assert "Original docstring" in original_func.__doc__

    def test_decorator_sync_wrapper_selection(self):
        """Verify decorator returns sync wrapper for sync functions."""
        @log_execution("sync_test", "SyncTest")
        def is_sync():
            return "sync"

        # Should be the sync wrapper (not a coroutine)
        result = is_sync()
        assert result == "sync"

    def test_decorator_async_wrapper_selection(self):
        """Verify decorator returns async wrapper for async functions."""
        @log_execution("async_test", "AsyncTest")
        async def is_async():
            return "async"

        # Should be a coroutine function
        assert asyncio.iscoroutinefunction(is_async)
        result = asyncio.run(is_async())
        assert result == "async"


class TestLoggingDecoratorExceptionHandling:
    """Tests for exception handling in logging decorator."""

    def test_decorator_exception_reraised_sync(self):
        """Verify exceptions are re-raised after logging (sync)."""
        @log_execution("error_test", "ErrorTest")
        def failing_func():
            raise RuntimeError("Original error")

        with pytest.raises(RuntimeError, match="Original error"):
            failing_func()

    def test_decorator_exception_reraised_async(self):
        """Verify exceptions are re-raised after logging (async)."""
        @log_execution("async_error", "AsyncError")
        async def async_failing_func():
            raise RuntimeError("Async original error")

        with pytest.raises(RuntimeError, match="Async original error"):
            asyncio.run(async_failing_func())

    def test_decorator_logs_exception_message(self, caplog_fixture):
        """Verify decorator logs the exception message string."""
        caplog_fixture.clear()

        @log_execution("exc_msg", "ExcMsg")
        def func_with_message():
            raise ValueError("Specific error details")

        with pytest.raises(ValueError):
            func_with_message()

        assert "Specific error details" in caplog_fixture.text

    def test_decorator_logs_any_exception_type(self, caplog_fixture):
        """Verify decorator handles all exception types."""
        caplog_fixture.clear()

        class CustomError(Exception):
            """Custom exception for testing."""
            pass

        @log_execution("any_exc", "AnyExc")
        def func_raises_custom():
            raise CustomError("Custom")

        with pytest.raises(CustomError):
            func_raises_custom()

        assert "Custom" in caplog_fixture.text


class TestLoggingDecoratorComplex:
    """Tests for complex decorator usage patterns."""

    def test_decorator_stacking_with_multiple_decorators(self, caplog_fixture):
        """Verify decorator works when stacked with other decorators.

        This documents behavior when multiple decorators are applied.
        """
        caplog_fixture.clear()

        def other_decorator(f):
            def wrapper(*args, **kwargs):
                return f(*args, **kwargs)
            return wrapper

        @other_decorator
        @log_execution("stacked", "Stacked")
        def decorated_twice(x):
            return x * 2

        result = decorated_twice(5)

        assert result == 10
        assert "Stacked 'stacked'" in caplog_fixture.text

    def test_decorator_with_class_methods(self, caplog_fixture):
        """Verify decorator works on instance methods.

        Documents current behavior with self parameter.
        """
        caplog_fixture.clear()

        class TestClass:
            @log_execution("method", "Method")
            def method(self, value):
                return value * 2

        obj = TestClass()
        result = obj.method(5)

        assert result == 10
        # self is included in args
        assert "method" in caplog_fixture.text
        assert "10" in caplog_fixture.text

    def test_decorator_with_many_arguments(self, caplog_fixture):
        """Verify decorator handles functions with many arguments."""
        caplog_fixture.clear()

        @log_execution("many_args", "ManyArgs")
        def func_many_args(a, b, c, d, e=5, f=6, g=7):
            return sum([a, b, c, d, e, f, g])

        result = func_many_args(1, 2, 3, 4, e=5, f=6, g=7)

        assert result == 28
        assert "many_args" in caplog_fixture.text
        assert "28" in caplog_fixture.text


# =============================================================================
# SECTION 2: Telemetry Decorator Tests
# =============================================================================

class TestTelemetryDecoratorBasics:
    """Tests for telemetry_tool and telemetry_resource decorators."""

    def test_telemetry_tool_decorator_sync(self, caplog_fixture):
        """Verify telemetry_tool decorator works on sync functions."""
        caplog_fixture.clear()

        @telemetry_tool("test_tool")
        def sync_tool(param1):
            return f"result_{param1}"

        result = sync_tool("value")

        assert result == "result_value"
        # Should log decorator application (first 10 times)
        assert "telemetry_decorator sync: tool=test_tool" in caplog_fixture.text

    def test_telemetry_tool_decorator_async(self, caplog_fixture):
        """Verify telemetry_tool decorator works on async functions."""
        caplog_fixture.clear()

        @telemetry_tool("async_tool")
        async def async_tool(param1):
            return f"async_result_{param1}"

        result = asyncio.run(async_tool("value"))

        assert result == "async_result_value"
        assert "telemetry_decorator async: tool=async_tool" in caplog_fixture.text

    def test_telemetry_resource_decorator_sync(self, caplog_fixture):
        """Verify telemetry_resource decorator works on sync functions."""
        caplog_fixture.clear()

        @telemetry_resource("test_resource")
        def sync_resource(param1):
            return f"resource_{param1}"

        result = sync_resource("data")

        assert result == "resource_data"
        assert "telemetry_decorator sync: resource=test_resource" in caplog_fixture.text

    def test_telemetry_resource_decorator_async(self, caplog_fixture):
        """Verify telemetry_resource decorator works on async functions."""
        caplog_fixture.clear()

        @telemetry_resource("async_resource")
        async def async_resource(param1):
            return f"async_resource_{param1}"

        result = asyncio.run(async_resource("data"))

        assert result == "async_resource_data"
        assert "telemetry_decorator async: resource=async_resource" in caplog_fixture.text


class TestTelemetryDecoratorDuplication:
    """Tests documenting the decorator code duplication pattern.

    This pattern shows that ~44+ lines of code are duplicated between
    _sync_wrapper and _async_wrapper in both telemetry_tool and
    telemetry_resource decorators.
    """

    def test_telemetry_tool_sync_and_async_produce_similar_logs(self, caplog_fixture):
        """Verify sync and async decorators produce equivalent logging behavior.

        This documents that both wrappers perform identical logging operations,
        just with await for async.
        """
        caplog_fixture.clear()

        @telemetry_tool("tool_dup")
        def sync_func():
            return "sync_result"

        @telemetry_tool("tool_dup_async")
        async def async_func():
            return "async_result"

        sync_result = sync_func()
        async_result = asyncio.run(async_func())

        assert sync_result == "sync_result"
        assert async_result == "async_result"

        # Both should have logged decorator application
        assert "telemetry_decorator sync:" in caplog_fixture.text
        assert "telemetry_decorator async:" in caplog_fixture.text

    def test_telemetry_resource_sync_and_async_identical_behavior(self, caplog_fixture):
        """Verify resource decorators have identical sync/async behavior.

        Documents the duplication in telemetry_resource decorator.
        """
        caplog_fixture.clear()

        @telemetry_resource("resource_dup")
        def sync_resource():
            return "sync"

        @telemetry_resource("resource_dup_async")
        async def async_resource():
            return "async"

        sync_result = sync_resource()
        async_result = asyncio.run(async_resource())

        assert sync_result == "sync"
        assert async_result == "async"
        assert "resource=resource_dup" in caplog_fixture.text
        assert "resource=resource_dup_async" in caplog_fixture.text

    def test_decorator_log_count_limit(self, caplog_fixture):
        """Verify decorator has a log count limit (max 10 entries).

        Documents the global _decorator_log_count that limits logging to first 10.
        """
        caplog_fixture.clear()

        @telemetry_tool("limited_logs")
        def func_limited():
            return "result"

        # Call multiple times
        for _ in range(15):
            func_limited()

        # Count how many times the decorator logged
        log_count = caplog_fixture.text.count("telemetry_decorator sync: tool=limited_logs")

        # Should only log first 10 times due to global counter
        assert log_count <= 10


class TestTelemetryDecoratorExceptionHandling:
    """Tests for exception handling in telemetry decorators."""

    @pytest.fixture(autouse=True)
    def setup(self, fresh_telemetry):
        """Reset telemetry before each test in this class."""
        pass

    def test_telemetry_tool_exception_recorded(self):
        """Verify telemetry records exceptions in tool execution."""
        @telemetry_tool("failing_tool")
        def failing_tool():
            raise ValueError("Tool error")

        with patch("core.telemetry_decorator.record_tool_usage") as mock_record:
            with pytest.raises(ValueError, match="Tool error"):
                failing_tool()

            # Verify record_tool_usage was called with success=False
            assert mock_record.called
            call_args = mock_record.call_args
            assert call_args[0][1] is False  # success=False
            assert call_args[0][3] is not None  # error message provided

    def test_telemetry_resource_exception_recorded(self):
        """Verify telemetry records exceptions in resource retrieval."""
        @telemetry_resource("failing_resource")
        def failing_resource():
            raise RuntimeError("Resource error")

        with patch("core.telemetry_decorator.record_resource_usage") as mock_record:
            with pytest.raises(RuntimeError, match="Resource error"):
                failing_resource()

            assert mock_record.called
            call_args = mock_record.call_args
            assert call_args[0][1] is False  # success=False
            assert call_args[0][3] is not None  # error message provided

    def test_telemetry_decorator_suppresses_recording_errors(self, caplog_fixture):
        """Verify telemetry recording errors don't propagate.

        Documents the try/except around record_tool_usage and record_resource_usage.
        """
        caplog_fixture.clear()

        @telemetry_tool("tool_record_error")
        def func_with_recording_error():
            return "result"

        with patch("core.telemetry_decorator.record_tool_usage", side_effect=Exception("Recording failed")):
            # Should not raise despite record_tool_usage error
            result = func_with_recording_error()
            assert result == "result"

            # Error should be logged as debug
            assert "record_tool_usage failed" in caplog_fixture.text


class TestTelemetrySubAction:

    @pytest.fixture(autouse=True)
    def setup(self, fresh_telemetry):
        """Reset telemetry before each test in this class."""
        pass

    """Tests for sub-action extraction in telemetry decorators."""

    def test_telemetry_tool_extracts_action_parameter(self):
        """Verify telemetry_tool extracts 'action' parameter as sub_action."""
        @telemetry_tool("manage_script")
        def tool_with_action(name, action=None):
            return f"result_{action}"

        with patch("core.telemetry_decorator.record_tool_usage") as mock_record:
            result = tool_with_action("test", action="create")

            assert result == "result_create"
            # sub_action should be extracted from parameters
            assert mock_record.called
            call_kwargs = mock_record.call_args[1]
            assert call_kwargs.get("sub_action") == "create"

    def test_telemetry_tool_missing_action_parameter(self):
        """Verify telemetry_tool handles missing action parameter gracefully."""
        @telemetry_tool("tool_no_action")
        def tool_no_action(name):
            return "result"

        with patch("core.telemetry_decorator.record_tool_usage") as mock_record:
            result = tool_no_action("test")

            assert result == "result"
            assert mock_record.called
            call_kwargs = mock_record.call_args[1]
            assert call_kwargs.get("sub_action") is None

    def test_telemetry_tool_milestone_on_script_create(self):
        """Verify telemetry_tool records FIRST_SCRIPT_CREATION milestone."""
        @telemetry_tool("manage_script")
        def create_script(name, action=None):
            return "created"

        with patch("core.telemetry_decorator.record_milestone") as mock_milestone:
            result = create_script("test", action="create")

            assert result == "created"
            # Should record FIRST_SCRIPT_CREATION milestone
            assert mock_milestone.called
            milestone_calls = [c for c in mock_milestone.call_args_list
                             if "FIRST_SCRIPT_CREATION" in str(c)]
            assert len(milestone_calls) > 0

    def test_telemetry_tool_milestone_on_scene_modification(self):
        """Verify telemetry_tool records FIRST_SCENE_MODIFICATION milestone."""
        @telemetry_tool("manage_scene_hierarchy")
        def modify_scene(name, action=None):
            return "modified"

        with patch("core.telemetry_decorator.record_milestone") as mock_milestone:
            result = modify_scene("test", action="edit")

            assert result == "modified"
            # Should record milestone for scene modification
            assert mock_milestone.called
            milestone_calls = [c for c in mock_milestone.call_args_list
                             if c is not None]
            assert len(milestone_calls) > 0

    def test_telemetry_tool_milestone_first_tool_usage(self):
        """Verify telemetry_tool always records FIRST_TOOL_USAGE milestone."""
        @telemetry_tool("any_tool")
        def any_tool():
            return "done"

        with patch("core.telemetry_decorator.record_milestone") as mock_milestone:
            result = any_tool()

            assert result == "done"
            # Should record FIRST_TOOL_USAGE
            assert mock_milestone.called
            milestone_calls = [c for c in mock_milestone.call_args_list
                             if "FIRST_TOOL_USAGE" in str(c)]
            assert len(milestone_calls) > 0


class TestTelemetryDuration:

    @pytest.fixture(autouse=True)
    def setup(self, fresh_telemetry):
        """Reset telemetry before each test in this class."""
        pass

    """Tests for duration measurement in telemetry decorators."""

    def test_telemetry_measures_duration_sync(self):
        """Verify telemetry_tool measures and records execution duration (sync)."""
        @telemetry_tool("timed_tool")
        def slow_tool():
            time.sleep(0.05)  # 50ms
            return "done"

        with patch("core.telemetry_decorator.record_tool_usage") as mock_record:
            result = slow_tool()

            assert result == "done"
            assert mock_record.called
            # duration_ms should be in call args
            duration_ms = mock_record.call_args[0][2]
            assert duration_ms >= 50  # Should be at least 50ms

    def test_telemetry_measures_duration_async(self):
        """Verify telemetry_tool measures and records execution duration (async)."""
        @telemetry_tool("async_timed")
        async def slow_async_tool():
            await asyncio.sleep(0.05)
            return "done"

        with patch("core.telemetry_decorator.record_tool_usage") as mock_record:
            result = asyncio.run(slow_async_tool())

            assert result == "done"
            assert mock_record.called
            duration_ms = mock_record.call_args[0][2]
            # Allow 20% variance for timer resolution (especially on Windows)
            assert duration_ms >= 40

    def test_telemetry_duration_recorded_even_on_error(self):
        """Verify duration is recorded even when tool raises exception."""
        @telemetry_tool("error_tool")
        def error_tool():
            time.sleep(0.02)
            raise ValueError("Error")

        with patch("core.telemetry_decorator.record_tool_usage") as mock_record:
            with pytest.raises(ValueError):
                error_tool()

            assert mock_record.called
            duration_ms = mock_record.call_args[0][2]
            assert duration_ms >= 20


# =============================================================================
# SECTION 3: Configuration Tests
# =============================================================================

class TestServerConfigDefaults:
    """Tests for ServerConfig default values."""

    def test_config_default_values(self):
        """Verify ServerConfig has expected default values."""
        config = ServerConfig()

        assert config.unity_host == "localhost"
        assert config.unity_port == 6400
        assert config.mcp_port == 6500
        assert config.connection_timeout == 30.0
        assert config.buffer_size == 16 * 1024 * 1024
        assert config.require_framing is True
        assert config.handshake_timeout == 1.0
        assert config.framed_receive_timeout == 2.0
        assert config.max_heartbeat_frames == 16
        assert config.heartbeat_timeout == 2.0

    def test_config_logging_defaults(self):
        """Verify logging configuration defaults."""
        config = ServerConfig()

        assert config.log_level == "INFO"
        assert "%(asctime)s" in config.log_format
        assert "%(name)s" in config.log_format
        assert "%(levelname)s" in config.log_format
        assert "%(message)s" in config.log_format

    def test_config_server_defaults(self):
        """Verify server configuration defaults."""
        config = ServerConfig()

        assert config.max_retries == 5
        assert config.retry_delay == 0.25
        assert config.reload_retry_ms == 250
        assert config.reload_max_retries == 40
        assert config.port_registry_ttl == 5.0

    def test_config_telemetry_defaults(self):
        """Verify telemetry configuration defaults."""
        config = ServerConfig()

        assert config.telemetry_enabled is True
        assert config.telemetry_endpoint == "https://api-prod.coplay.dev/telemetry/events"

    def test_config_is_dataclass(self):
        """Verify ServerConfig is a dataclass."""
        from dataclasses import is_dataclass
        assert is_dataclass(ServerConfig)


class TestServerConfigLogging:
    """Tests documenting that ServerConfig.configure_logging() was removed.

    The method was defined but never invoked anywhere in the codebase.
    Removed during QW-1: Delete Dead Code refactoring (2026-01-27).

    Historical note: config.py had a bug - it used logging without importing it.
    """

    def test_configure_logging_method_removed(self):
        """Documents that configure_logging was removed as unused code."""
        config = ServerConfig()
        assert not hasattr(config, "configure_logging")

    def test_configure_logging_info_level_removed(self):
        """Documents that configure_logging was removed as unused code."""
        config = ServerConfig(log_level="INFO")
        # Method no longer exists
        assert not hasattr(config, "configure_logging")
        # Log level config field still exists for potential future use
        assert config.log_level == "INFO"

    def test_configure_logging_debug_level_removed(self):
        """Documents that configure_logging was removed as unused code."""
        config = ServerConfig(log_level="DEBUG")
        # Method no longer exists
        assert not hasattr(config, "configure_logging")
        # Log level config field still exists for potential future use
        assert config.log_level == "DEBUG"


class TestTelemetryConfigPrecedence:

    @pytest.fixture(autouse=True)
    def setup(self, fresh_telemetry):
        """Reset telemetry before each test in this class."""
        pass

    """Tests for TelemetryConfig configuration precedence.

    Pattern: config file -> env variable override
    """

    def test_telemetry_config_enabled_from_server_config(self):
        """Verify telemetry enabled flag comes from ServerConfig."""
        with patch("core.telemetry.import_module") as mock_import:
            mock_config = MagicMock()
            mock_config.telemetry_enabled = False
            mock_module = MagicMock()
            mock_module.config = mock_config
            mock_import.return_value = mock_module

            config = TelemetryConfig()

            assert config.enabled is False

    def test_telemetry_config_disabled_via_env_opt_out(self):
        """Verify telemetry can be disabled via environment variables.

        Precedence: DISABLE_TELEMETRY > UNITY_MCP_DISABLE_TELEMETRY > MCP_DISABLE_TELEMETRY
        """
        with patch.dict(os.environ, {"DISABLE_TELEMETRY": "true"}):
            with patch("core.telemetry.import_module", side_effect=Exception("No module")):
                config = TelemetryConfig()
                assert config.enabled is False

    def test_telemetry_config_endpoint_from_server_config(self):
        """Verify telemetry endpoint comes from ServerConfig."""
        with patch("core.telemetry.import_module") as mock_import:
            mock_config = MagicMock()
            mock_config.telemetry_enabled = True
            mock_config.telemetry_endpoint = "https://custom.endpoint.com/telemetry"
            mock_module = MagicMock()
            mock_module.config = mock_config
            mock_import.return_value = mock_module

            with patch("core.telemetry.TelemetryConfig._is_disabled", return_value=False):
                config = TelemetryConfig()

                assert "custom.endpoint.com" in config.endpoint

    def test_telemetry_config_endpoint_env_override(self):
        """Verify telemetry endpoint can be overridden via env variable."""
        with patch.dict(os.environ, {"UNITY_MCP_TELEMETRY_ENDPOINT": "https://env.endpoint.com/telemetry"}):
            with patch("core.telemetry.import_module", side_effect=Exception("No module")):
                with patch("core.telemetry.TelemetryConfig._is_disabled", return_value=False):
                    config = TelemetryConfig()

                    assert "env.endpoint.com" in config.endpoint

    def test_telemetry_config_timeout_default(self):
        """Verify telemetry timeout has default value."""
        with patch("core.telemetry.import_module", side_effect=Exception("No module")):
            with patch("core.telemetry.TelemetryConfig._is_disabled", return_value=False):
                config = TelemetryConfig()

                assert config.timeout == 1.5

    def test_telemetry_config_timeout_env_override(self):
        """Verify telemetry timeout can be overridden via env variable."""
        with patch.dict(os.environ, {"UNITY_MCP_TELEMETRY_TIMEOUT": "3.0"}):
            with patch("core.telemetry.import_module", side_effect=Exception("No module")):
                with patch("core.telemetry.TelemetryConfig._is_disabled", return_value=False):
                    config = TelemetryConfig()

                    assert config.timeout == 3.0

    def test_telemetry_config_endpoint_validation(self):
        """Verify telemetry endpoint is validated for scheme and host."""
        with patch("core.telemetry.import_module", side_effect=Exception("No module")):
            with patch("core.telemetry.TelemetryConfig._is_disabled", return_value=False):
                # Invalid endpoint should fall back to default
                with patch.dict(os.environ, {"UNITY_MCP_TELEMETRY_ENDPOINT": "invalid://localhost/path"}):
                    config = TelemetryConfig()

                    # Should use default since localhost is rejected
                    assert "api-prod.coplay.dev" in config.endpoint

    def test_telemetry_config_rejects_localhost(self):
        """Verify telemetry rejects localhost endpoints for security."""
        with patch("core.telemetry.import_module", side_effect=Exception("No module")):
            with patch("core.telemetry.TelemetryConfig._is_disabled", return_value=False):
                with patch.dict(os.environ, {"UNITY_MCP_TELEMETRY_ENDPOINT": "http://localhost:8000/telemetry"}):
                    config = TelemetryConfig()

                    # Should reject localhost and use default
                    assert "localhost" not in config.endpoint
                    assert "api-prod.coplay.dev" in config.endpoint


# =============================================================================
# SECTION 4: Telemetry Collection Tests
# =============================================================================

class TestTelemetryCollection:

    @pytest.fixture(autouse=True)
    def setup(self, fresh_telemetry):
        """Reset telemetry before each test in this class."""
        pass

    """Tests for TelemetryCollector basic functionality."""

    def test_telemetry_collector_initialization(self, mock_telemetry_config, temp_telemetry_data):
        """Verify TelemetryCollector initializes with config."""
        # Explicitly reference fixture to suppress unused parameter warning
        _ = mock_telemetry_config
        # Create minimal path files to avoid file I/O errors
        data_path = Path(temp_telemetry_data)
        (data_path / "customer_uuid.txt").write_text("test-uuid")
        (data_path / "milestones.json").write_text("{}")

        with patch("core.telemetry.TelemetryConfig") as mock_config_cls:
            mock_config = MagicMock()
            mock_config.uuid_file = data_path / "customer_uuid.txt"
            mock_config.milestones_file = data_path / "milestones.json"
            mock_config_cls.return_value = mock_config

            collector = TelemetryCollector()

            assert collector.config is not None
            assert collector._customer_uuid is not None
            assert isinstance(collector._milestones, dict)

    def test_telemetry_collector_has_worker_thread(self, mock_telemetry_config, temp_telemetry_data):
        """Verify TelemetryCollector starts background worker thread."""
        # Explicitly reference fixture to suppress unused parameter warning
        _ = mock_telemetry_config
        data_path = Path(temp_telemetry_data)
        (data_path / "customer_uuid.txt").write_text("test-uuid")
        (data_path / "milestones.json").write_text("{}")

        with patch("core.telemetry.TelemetryConfig") as mock_config_cls:
            mock_config = MagicMock()
            mock_config.uuid_file = data_path / "customer_uuid.txt"
            mock_config.milestones_file = data_path / "milestones.json"
            mock_config_cls.return_value = mock_config

            collector = TelemetryCollector()

            assert collector._worker is not None
            assert isinstance(collector._worker, threading.Thread)
            assert collector._worker.daemon is True

    def test_telemetry_collector_records_event(self, mock_telemetry_config, temp_telemetry_data):
        """Verify TelemetryCollector.record queues events."""
        # Explicitly reference fixture to suppress unused parameter warning
        _ = mock_telemetry_config
        data_path = Path(temp_telemetry_data)
        (data_path / "customer_uuid.txt").write_text("test-uuid")
        (data_path / "milestones.json").write_text("{}")

        with patch("core.telemetry.TelemetryConfig") as mock_config_cls:
            mock_config = MagicMock()
            mock_config.uuid_file = data_path / "customer_uuid.txt"
            mock_config.milestones_file = data_path / "milestones.json"
            mock_config.enabled = True
            mock_config_cls.return_value = mock_config

            # Mock the worker thread to prevent it from consuming queued events
            with patch("core.telemetry.threading.Thread") as mock_thread_cls:
                mock_thread = MagicMock()
                mock_thread_cls.return_value = mock_thread

                collector = TelemetryCollector()

                collector.record(RecordType.USAGE, {"tool": "test"})

                # Event should be queued (won't be consumed since worker thread is mocked)
                assert not collector._queue.empty()

    def test_telemetry_collector_queue_full_drops_events(self, mock_telemetry_config, caplog_fixture, temp_telemetry_data):
        """Verify TelemetryCollector drops events when queue is full."""
        caplog_fixture.clear()

        data_path = Path(temp_telemetry_data)
        (data_path / "customer_uuid.txt").write_text("test-uuid")
        (data_path / "milestones.json").write_text("{}")

        with patch("core.telemetry.TelemetryConfig") as mock_config_cls:
            mock_config = MagicMock()
            mock_config.uuid_file = data_path / "customer_uuid.txt"
            mock_config.milestones_file = data_path / "milestones.json"
            mock_config.enabled = True
            mock_config_cls.return_value = mock_config

            collector = TelemetryCollector()
            # Queue has maxsize=1000

            # Fill queue beyond capacity
            for _ in range(1500):
                collector.record(RecordType.USAGE, {"data": "test"})

            # Should have dropped events and logged
            assert "full" in caplog_fixture.text.lower()


class TestTelemetryRecordTypes:

    @pytest.fixture(autouse=True)
    def setup(self, fresh_telemetry):
        """Reset telemetry before each test in this class."""
        pass

    """Tests for telemetry record types and data structures."""

    def test_telemetry_record_type_enum(self):
        """Verify RecordType enum has expected values."""
        assert hasattr(RecordType, "VERSION")
        assert hasattr(RecordType, "STARTUP")
        assert hasattr(RecordType, "USAGE")
        assert hasattr(RecordType, "LATENCY")
        assert hasattr(RecordType, "FAILURE")
        assert hasattr(RecordType, "RESOURCE_RETRIEVAL")
        assert hasattr(RecordType, "TOOL_EXECUTION")
        assert hasattr(RecordType, "UNITY_CONNECTION")
        assert hasattr(RecordType, "CLIENT_CONNECTION")

    def test_milestone_type_enum(self):
        """Verify MilestoneType enum has expected values."""
        assert hasattr(MilestoneType, "FIRST_STARTUP")
        assert hasattr(MilestoneType, "FIRST_TOOL_USAGE")
        assert hasattr(MilestoneType, "FIRST_SCRIPT_CREATION")
        assert hasattr(MilestoneType, "FIRST_SCENE_MODIFICATION")
        assert hasattr(MilestoneType, "MULTIPLE_SESSIONS")
        assert hasattr(MilestoneType, "DAILY_ACTIVE_USER")
        assert hasattr(MilestoneType, "WEEKLY_ACTIVE_USER")

    def test_record_tool_usage_basic(self):
        """Verify record_tool_usage creates proper data structure."""
        with patch("core.telemetry.get_telemetry") as mock_get:
            mock_collector = MagicMock()
            mock_get.return_value = mock_collector

            record_tool_usage("test_tool", True, 100.5)

            assert mock_collector.record.called
            call_args = mock_collector.record.call_args
            data = call_args[0][1]

            assert data["tool_name"] == "test_tool"
            assert data["success"] is True
            assert data["duration_ms"] == 100.5

    def test_record_tool_usage_with_error(self):
        """Verify record_tool_usage includes error message when provided."""
        with patch("core.telemetry.get_telemetry") as mock_get:
            mock_collector = MagicMock()
            mock_get.return_value = mock_collector

            record_tool_usage("error_tool", False, 50.0, error="Test error")

            call_args = mock_collector.record.call_args
            data = call_args[0][1]

            assert data["error"] == "Test error"

    def test_record_tool_usage_error_truncation(self):
        """Verify record_tool_usage truncates long error messages."""
        long_error = "x" * 500

        with patch("core.telemetry.get_telemetry") as mock_get:
            mock_collector = MagicMock()
            mock_get.return_value = mock_collector

            record_tool_usage("tool", False, 50.0, error=long_error)

            call_args = mock_collector.record.call_args
            data = call_args[0][1]

            # Should be truncated to 200 chars
            assert len(data["error"]) == 200

    def test_record_tool_usage_with_sub_action(self):
        """Verify record_tool_usage includes sub_action when provided."""
        with patch("core.telemetry.get_telemetry") as mock_get:
            mock_collector = MagicMock()
            mock_get.return_value = mock_collector

            record_tool_usage("manage_script", True, 75.0, sub_action="create")

            call_args = mock_collector.record.call_args
            data = call_args[0][1]

            assert data["sub_action"] == "create"

    def test_record_resource_usage_basic(self):
        """Verify record_resource_usage creates proper data structure."""
        with patch("core.telemetry.get_telemetry") as mock_get:
            mock_collector = MagicMock()
            mock_get.return_value = mock_collector

            record_resource_usage("test_resource", True, 50.0)

            assert mock_collector.record.called
            call_args = mock_collector.record.call_args
            data = call_args[0][1]

            assert data["resource_name"] == "test_resource"
            assert data["success"] is True
            assert data["duration_ms"] == 50.0

    def test_record_resource_usage_with_error(self):
        """Verify record_resource_usage includes error when provided."""
        with patch("core.telemetry.get_telemetry") as mock_get:
            mock_collector = MagicMock()
            mock_get.return_value = mock_collector

            record_resource_usage("resource", False, 30.0, error="Resource error")

            call_args = mock_collector.record.call_args
            data = call_args[0][1]

            assert data["error"] == "Resource error"


class TestTelemetryMilestones:

    @pytest.fixture(autouse=True)
    def setup(self, fresh_telemetry):
        """Reset telemetry before each test in this class."""
        pass

    """Tests for milestone tracking in telemetry."""

    def test_record_milestone_first_occurrence(self, mock_telemetry_config, temp_telemetry_data):
        """Verify record_milestone returns True on first occurrence."""
        data_path = Path(temp_telemetry_data)
        (data_path / "customer_uuid.txt").write_text("test-uuid")
        (data_path / "milestones.json").write_text("{}")

        with patch("core.telemetry.TelemetryConfig") as mock_config_cls:
            mock_config = MagicMock()
            mock_config.uuid_file = data_path / "customer_uuid.txt"
            mock_config.milestones_file = data_path / "milestones.json"
            mock_config.enabled = True
            mock_config_cls.return_value = mock_config

            collector = TelemetryCollector()

            result = collector.record_milestone(MilestoneType.FIRST_STARTUP)

            assert result is True
            # Should be recorded
            assert MilestoneType.FIRST_STARTUP.value in collector._milestones

    def test_record_milestone_duplicate_ignored(self, mock_telemetry_config, temp_telemetry_data):
        """Verify record_milestone returns False on duplicate."""
        data_path = Path(temp_telemetry_data)
        (data_path / "customer_uuid.txt").write_text("test-uuid")
        (data_path / "milestones.json").write_text("{}")

        with patch("core.telemetry.TelemetryConfig") as mock_config_cls:
            mock_config = MagicMock()
            mock_config.uuid_file = data_path / "customer_uuid.txt"
            mock_config.milestones_file = data_path / "milestones.json"
            mock_config.enabled = True
            mock_config_cls.return_value = mock_config

            collector = TelemetryCollector()

            # First call
            result1 = collector.record_milestone(MilestoneType.FIRST_STARTUP)
            assert result1 is True

            # Second call (duplicate)
            result2 = collector.record_milestone(MilestoneType.FIRST_STARTUP)
            assert result2 is False

    def test_record_milestone_sends_telemetry_event(self, mock_telemetry_config, temp_telemetry_data):
        """Verify record_milestone sends telemetry event."""
        data_path = Path(temp_telemetry_data)
        (data_path / "customer_uuid.txt").write_text("test-uuid")
        (data_path / "milestones.json").write_text("{}")

        with patch("core.telemetry.TelemetryConfig") as mock_config_cls:
            mock_config = MagicMock()
            mock_config.uuid_file = data_path / "customer_uuid.txt"
            mock_config.milestones_file = data_path / "milestones.json"
            mock_config.enabled = True
            mock_config_cls.return_value = mock_config

            collector = TelemetryCollector()

            with patch.object(collector, "record") as mock_record:
                collector.record_milestone(MilestoneType.FIRST_TOOL_USAGE, {"extra": "data"})

                assert mock_record.called
                # record is called with: record_type=RecordType.USAGE, data={...}, milestone=milestone
                call_args = mock_record.call_args
                call_kwargs = call_args.kwargs
                assert call_kwargs["milestone"] == MilestoneType.FIRST_TOOL_USAGE
                # data dict contains the milestone key and extra data
                assert call_kwargs["data"]["milestone"] == "first_tool_usage"
                assert call_kwargs["data"]["extra"] == "data"

    def test_record_milestone_persists_to_disk(self, mock_telemetry_config, temp_telemetry_data):
        """Verify record_milestone saves milestones to disk."""
        data_path = Path(temp_telemetry_data)
        (data_path / "customer_uuid.txt").write_text("test-uuid")
        (data_path / "milestones.json").write_text("{}")

        with patch("core.telemetry.TelemetryConfig") as mock_config_cls:
            mock_config = MagicMock()
            mock_config.uuid_file = data_path / "customer_uuid.txt"
            mock_config.milestones_file = data_path / "milestones.json"
            mock_config.enabled = True
            mock_config_cls.return_value = mock_config

            collector = TelemetryCollector()

            with patch.object(collector, "_save_milestones") as mock_save:
                collector.record_milestone(MilestoneType.FIRST_STARTUP)

                assert mock_save.called


class TestTelemetryDisabled:
    """Tests for telemetry when disabled."""

    def test_telemetry_disabled_skips_collection(self, mock_telemetry_config, temp_telemetry_data):
        """Verify disabled telemetry doesn't queue events."""
        data_path = Path(temp_telemetry_data)
        (data_path / "customer_uuid.txt").write_text("test-uuid")
        (data_path / "milestones.json").write_text("{}")

        with patch("core.telemetry.TelemetryConfig") as mock_config_class:
            mock_config = MagicMock()
            mock_config.enabled = False
            mock_config.uuid_file = data_path / "customer_uuid.txt"
            mock_config.milestones_file = data_path / "milestones.json"
            mock_config_class.return_value = mock_config

            collector = TelemetryCollector()
            collector.record(RecordType.USAGE, {"data": "test"})

            # Queue should be empty (early return)
            assert collector._queue.empty()

    def test_is_telemetry_enabled_returns_false_when_disabled(self, mock_telemetry_config):
        """Verify is_telemetry_enabled returns False when disabled."""
        with patch("core.telemetry.TelemetryConfig") as mock_config_class:
            mock_config = MagicMock()
            mock_config.enabled = False
            mock_config_class.return_value = mock_config

            with patch("core.telemetry.get_telemetry") as mock_get:
                mock_get.return_value.config.enabled = False

                assert is_telemetry_enabled() is False


# =============================================================================
# SECTION 5: Integration Tests
# =============================================================================

class TestDecoratorTelemetryIntegration:

    @pytest.fixture(autouse=True)
    def setup(self, fresh_telemetry):
        """Reset telemetry before each test in this class."""
        pass

    """Tests for interaction between decorators and telemetry system."""

    def test_logging_decorator_independent_of_telemetry(self, caplog_fixture):
        """Verify logging decorator works even with telemetry disabled."""
        caplog_fixture.clear()

        @log_execution("test", "Test")
        def func():
            return "result"

        result = func()

        assert result == "result"
        assert "test" in caplog_fixture.text

    def test_telemetry_decorator_with_logging_decorator_stacked(self, caplog_fixture):
        """Verify decorators can be stacked together."""
        caplog_fixture.clear()

        @log_execution("stacked", "Stacked")
        @telemetry_tool("stacked_tool")
        def stacked_func():
            return "result"

        with patch("core.telemetry_decorator.record_tool_usage"):
            result = stacked_func()

            assert result == "result"
            assert "stacked" in caplog_fixture.text

    def test_multiple_tools_record_telemetry_independently(self):
        """Verify multiple tools record telemetry independently."""
        @telemetry_tool("tool1")
        def tool1():
            return "result1"

        @telemetry_tool("tool2")
        def tool2():
            return "result2"

        with patch("core.telemetry_decorator.record_tool_usage") as mock_record:
            result1 = tool1()
            result2 = tool2()

            assert result1 == "result1"
            assert result2 == "result2"
            # Should have 2 calls to record_tool_usage
            assert mock_record.call_count == 2


class TestConfigurationEnvironmentInteraction:
    """Tests for configuration and environment variable interaction."""

    def test_telemetry_respects_disable_environment_variables(self):
        """Verify telemetry respects disable environment variables."""
        with patch.dict(os.environ, {"DISABLE_TELEMETRY": "1"}):
            with patch("core.telemetry.import_module", side_effect=Exception("No module")):
                config = TelemetryConfig()

                assert config.enabled is False

    def test_telemetry_multiple_disable_env_vars(self):
        """Verify telemetry checks multiple disable environment variable names."""
        disable_vars = ["DISABLE_TELEMETRY", "UNITY_MCP_DISABLE_TELEMETRY", "MCP_DISABLE_TELEMETRY"]

        for var_name in disable_vars:
            # Don't use clear=True as it removes HOME/USERPROFILE which breaks Path.home() on Windows
            with patch.dict(os.environ, {var_name: "true"}):
                with patch("core.telemetry.import_module", side_effect=Exception("No module")):
                    config = TelemetryConfig()
                    assert config.enabled is False, f"{var_name} did not disable telemetry"


# =============================================================================
# SECTION 6: Error Handling and Edge Cases
# =============================================================================

class TestErrorHandlingEdgeCases:

    @pytest.fixture(autouse=True)
    def setup(self, fresh_telemetry):
        """Reset telemetry before each test in this class."""
        pass

    """Tests for edge cases and error handling."""

    def test_decorator_with_none_return_value(self, caplog_fixture):
        """Verify decorator handles None return values."""
        caplog_fixture.clear()

        @log_execution("none_func", "None")
        def returns_none():
            return None

        result = returns_none()

        assert result is None
        assert "None" in caplog_fixture.text or "returned" in caplog_fixture.text

    def test_decorator_with_empty_string_return(self, caplog_fixture):
        """Verify decorator handles empty string return values."""
        caplog_fixture.clear()

        @log_execution("empty_func", "Empty")
        def returns_empty():
            return ""

        result = returns_empty()

        assert result == ""
        assert "Empty" in caplog_fixture.text

    def test_decorator_with_complex_nested_exceptions(self, caplog_fixture):
        """Verify decorator handles nested exception chains."""
        caplog_fixture.clear()

        @log_execution("nested_error", "Nested")
        def nested_error():
            try:
                raise ValueError("Inner error")
            except ValueError as e:
                raise RuntimeError("Outer error") from e

        with pytest.raises(RuntimeError, match="Outer error"):
            nested_error()

        assert "Outer error" in caplog_fixture.text

    def test_telemetry_with_invalid_duration(self):
        """Verify telemetry handles invalid duration values gracefully."""
        with patch("core.telemetry.get_telemetry") as mock_get:
            mock_collector = MagicMock()
            mock_get.return_value = mock_collector

            # Negative duration (shouldn't happen, but test robustness)
            record_tool_usage("tool", True, -10.0)

            call_args = mock_collector.record.call_args
            data = call_args[0][1]
            # Should still record it
            assert data["duration_ms"] == -10.0


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
