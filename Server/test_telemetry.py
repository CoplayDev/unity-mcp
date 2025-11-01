#!/usr/bin/env python3
"""
Test script for MCP for Unity Telemetry System
Run this to verify telemetry is working correctly
"""

import os
from pathlib import Path
import sys

# Add src to Python path for imports
sys.path.insert(0, str(Path(__file__).parent))


def test_telemetry_basic():
    """Test basic telemetry functionality"""
    # Avoid stdout noise in tests

    try:
        from telemetry import (
            get_telemetry, record_telemetry, record_milestone,
            RecordType, MilestoneType, is_telemetry_enabled
        )
        pass
    except ImportError as e:
        # Silent failure path for tests
        return False

    # Test telemetry enabled status
    _ = is_telemetry_enabled()

    # Test basic record
    try:
        record_telemetry(RecordType.VERSION, {
            "version": "3.0.2",
            "test_run": True
        })
        pass
    except Exception as e:
        # Silent failure path for tests
        return False

    # Test milestone recording
    try:
        is_first = record_milestone(MilestoneType.FIRST_STARTUP, {
            "test_mode": True
        })
        _ = is_first
    except Exception as e:
        # Silent failure path for tests
        return False

    # Test telemetry collector
    try:
        collector = get_telemetry()
        _ = collector
    except Exception as e:
        # Silent failure path for tests
        return False

    return True


def test_telemetry_disabled():
    """Test telemetry with disabled state"""
    # Silent for tests

    # Set environment variable to disable telemetry
    os.environ["DISABLE_TELEMETRY"] = "true"

    # Re-import to get fresh config
    import importlib
    import telemetry
    importlib.reload(telemetry)

    from telemetry import is_telemetry_enabled, record_telemetry, RecordType

    _ = is_telemetry_enabled()

    if not is_telemetry_enabled():
        pass

        # Test that records are ignored when disabled
        record_telemetry(RecordType.USAGE, {"test": "should_be_ignored"})
        pass

        return True
    else:
        pass
        return False


def test_data_storage():
    """Test data storage functionality"""
    # Silent for tests

    try:
        from telemetry import get_telemetry

        collector = get_telemetry()
        data_dir = collector.config.data_dir

        _ = (data_dir, collector.config.uuid_file,
             collector.config.milestones_file)

        # Check if files exist
        if collector.config.uuid_file.exists():
            pass
        else:
            pass

        if collector.config.milestones_file.exists():
            pass
        else:
            pass

        return True

    except Exception as e:
        # Silent failure path for tests
        return False


def main():
    """Run all telemetry tests"""
    # Silent runner for CI

    tests = [
        test_telemetry_basic,
        test_data_storage,
        test_telemetry_disabled,
    ]

    passed = 0
    failed = 0

    for test in tests:
        try:
            if test():
                passed += 1
                pass
            else:
                failed += 1
                pass
        except Exception as e:
            failed += 1
            pass

    _ = (passed, failed)

    if failed == 0:
        pass
        return True
    else:
        pass
        return False


if __name__ == "__main__":
    success = main()
    sys.exit(0 if success else 1)
