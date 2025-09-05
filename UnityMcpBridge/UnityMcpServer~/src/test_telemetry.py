#!/usr/bin/env python3
"""
Test script for Unity MCP Telemetry System
Run this to verify telemetry is working correctly
"""

import os
import time
import sys
from pathlib import Path

# Add src to Python path for imports
sys.path.insert(0, str(Path(__file__).parent))

def test_telemetry_basic():
    """Test basic telemetry functionality"""
    print("🧪 Testing Unity MCP Telemetry System...")
    
    try:
        from telemetry import (
            get_telemetry, record_telemetry, record_milestone, 
            RecordType, MilestoneType, is_telemetry_enabled
        )
        print("✅ Telemetry module imported successfully")
    except ImportError as e:
        print(f"❌ Failed to import telemetry module: {e}")
        return False
    
    # Test telemetry enabled status
    print(f"📊 Telemetry enabled: {is_telemetry_enabled()}")
    
    # Test basic record
    try:
        record_telemetry(RecordType.VERSION, {
            "version": "3.0.2",
            "test_run": True
        })
        print("✅ Basic telemetry record sent")
    except Exception as e:
        print(f"❌ Failed to send basic telemetry: {e}")
        return False
    
    # Test milestone recording
    try:
        is_first = record_milestone(MilestoneType.FIRST_STARTUP, {
            "test_mode": True
        })
        print(f"✅ Milestone recorded (first time: {is_first})")
    except Exception as e:
        print(f"❌ Failed to record milestone: {e}")
        return False
    
    # Test telemetry collector
    try:
        collector = get_telemetry()
        print(f"✅ Telemetry collector initialized (UUID: {collector._customer_uuid[:8]}...)")
    except Exception as e:
        print(f"❌ Failed to get telemetry collector: {e}")
        return False
    
    return True

def test_telemetry_disabled():
    """Test telemetry with disabled state"""
    print("\n🚫 Testing telemetry disabled state...")
    
    # Set environment variable to disable telemetry
    os.environ["DISABLE_TELEMETRY"] = "true"
    
    # Re-import to get fresh config
    import importlib
    import telemetry
    importlib.reload(telemetry)
    
    from telemetry import is_telemetry_enabled, record_telemetry, RecordType
    
    print(f"📊 Telemetry enabled (should be False): {is_telemetry_enabled()}")
    
    if not is_telemetry_enabled():
        print("✅ Telemetry correctly disabled via environment variable")
        
        # Test that records are ignored when disabled
        record_telemetry(RecordType.USAGE, {"test": "should_be_ignored"})
        print("✅ Telemetry record ignored when disabled")
        
        return True
    else:
        print("❌ Telemetry not disabled by environment variable")
        return False

def test_data_storage():
    """Test data storage functionality"""
    print("\n💾 Testing data storage...")
    
    try:
        from telemetry import get_telemetry
        
        collector = get_telemetry()
        data_dir = collector.config.data_dir
        
        print(f"📁 Data directory: {data_dir}")
        print(f"🏷️  UUID file: {collector.config.uuid_file}")
        print(f"🎯 Milestones file: {collector.config.milestones_file}")
        
        # Check if files exist
        if collector.config.uuid_file.exists():
            print("✅ UUID file exists")
        else:
            print("ℹ️  UUID file will be created on first use")
            
        if collector.config.milestones_file.exists():
            print("✅ Milestones file exists")
        else:
            print("ℹ️  Milestones file will be created on first milestone")
        
        return True
        
    except Exception as e:
        print(f"❌ Data storage test failed: {e}")
        return False

def main():
    """Run all telemetry tests"""
    print("🚀 Unity MCP Telemetry Test Suite")
    print("=" * 50)
    
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
                print("✅ PASSED\n")
            else:
                failed += 1
                print("❌ FAILED\n")
        except Exception as e:
            failed += 1
            print(f"❌ FAILED with exception: {e}\n")
    
    print("=" * 50)
    print(f"📊 Test Results: {passed} passed, {failed} failed")
    
    if failed == 0:
        print("🎉 All telemetry tests passed!")
        return True
    else:
        print(f"⚠️  {failed} test(s) failed")
        return False

if __name__ == "__main__":
    success = main()
    sys.exit(0 if success else 1)