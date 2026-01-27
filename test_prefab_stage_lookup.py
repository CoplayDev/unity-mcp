#!/usr/bin/env python3
"""
Test script to verify Prefab Stage GameObject lookup functionality.
This tests the fix for GameObject.Find() not working in Prefab Stage mode.

Usage:
    python test_prefab_stage_lookup.py

Prerequisites:
    - Unity Editor must be running with MCP for Unity installed
    - HTTP server must be running (Window > MCP for Unity > Start Local HTTP Server)
    - You need at least one prefab in your project (or create one first)
"""
import asyncio
import json
import sys
from typing import Any

try:
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client
except ImportError:
    print("Error: mcp package not found. Install with: pip install mcp")
    sys.exit(1)


async def test_prefab_stage_lookup():
    """Test prefab stage GameObject lookup functionality."""
    
    # For HTTP transport, we'd need to use httpx instead
    # For now, this is a guide on what to test
    
    print("=" * 60)
    print("Prefab Stage Lookup Test")
    print("=" * 60)
    print()
    
    print("TEST STEPS:")
    print("1. Check prefab stage status")
    print("2. Open a prefab in isolation mode")
    print("3. Test find_gameobjects with by_path search")
    print("4. Test get_hierarchy from manage_scene")
    print("5. Verify results work correctly")
    print()
    
    print("MCP Tool Calls to Make:")
    print()
    
    print("Step 1: Check current prefab stage")
    print("  Resource: mcpforunity://editor/prefab-stage")
    print()
    
    print("Step 2: Open a prefab stage (if none open)")
    print("  Tool: manage_prefabs")
    print("  Parameters:")
    print("    action: 'open_stage'")
    print("    prefab_path: 'Assets/Prefabs/YourPrefab.prefab'  # Adjust path")
    print("    mode: 'InIsolation'")
    print()
    
    print("Step 3: Test path-based GameObject search")
    print("  Tool: find_gameobjects")
    print("  Parameters:")
    print("    search_method: 'by_path'")
    print("    search_term: '<root-object-name>'  # Name of root prefab object")
    print("    include_inactive: true")
    print()
    
    print("Step 4: Test nested path search")
    print("  Tool: find_gameobjects")
    print("  Parameters:")
    print("    search_method: 'by_path'")
    print("    search_term: '<root>/<child>'  # Path to nested object")
    print("    include_inactive: true")
    print()
    
    print("Step 5: Test get_hierarchy in prefab stage")
    print("  Tool: manage_scene")
    print("  Parameters:")
    print("    action: 'get_hierarchy'")
    print()
    
    print("=" * 60)
    print("Expected Results:")
    print("=" * 60)
    print("✓ find_gameobjects should find objects by path in prefab stage")
    print("✓ get_hierarchy should return prefab stage hierarchy")
    print("✓ Both should work without errors (previously would fail)")
    print()
    
    print("To run these tests manually:")
    print("1. Use your MCP client (Cursor, Claude, etc.)")
    print("2. Make the tool calls listed above")
    print("3. Verify the results match expected behavior")
    print()


if __name__ == "__main__":
    asyncio.run(test_prefab_stage_lookup())
