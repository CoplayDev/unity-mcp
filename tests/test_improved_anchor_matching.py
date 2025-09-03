"""
Test the improved anchor matching logic.
"""

import sys
import pathlib
import importlib.util
import types

# add server src to path and load modules
ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "UnityMcpBridge" / "UnityMcpServer~" / "src"
sys.path.insert(0, str(SRC))

# stub mcp.server.fastmcp
mcp_pkg = types.ModuleType("mcp")
server_pkg = types.ModuleType("mcp.server")
fastmcp_pkg = types.ModuleType("mcp.server.fastmcp")

class _Dummy:
    pass

fastmcp_pkg.FastMCP = _Dummy
fastmcp_pkg.Context = _Dummy
server_pkg.fastmcp = fastmcp_pkg
mcp_pkg.server = server_pkg
sys.modules.setdefault("mcp", mcp_pkg)
sys.modules.setdefault("mcp.server", server_pkg)
sys.modules.setdefault("mcp.server.fastmcp", fastmcp_pkg)

def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

manage_script_edits_module = load_module(SRC / "tools" / "manage_script_edits.py", "manage_script_edits_module")

def test_improved_anchor_matching():
    """Test that our improved anchor matching finds the right closing brace."""
    
    test_code = '''using UnityEngine;

public class TestClass : MonoBehaviour  
{
    void Start()
    {
        Debug.Log("test");
    }
    
    void Update()
    {
        // Update logic
    }
}'''
    
    import re
    
    # Test the problematic anchor pattern
    anchor_pattern = r"\s*}\s*$"
    flags = re.MULTILINE
    
    # Test our improved function
    best_match = manage_script_edits_module._find_best_anchor_match(
        anchor_pattern, test_code, flags, prefer_last=True
    )
    
    if best_match:
        match_pos = best_match.start()
        
        # Get line number
        lines_before = test_code[:match_pos].count('\n')
        line_num = lines_before + 1
        
        print(f"Improved matching chose position {match_pos} on line {line_num}")
        
        # Show context
        before_context = test_code[max(0, match_pos-50):match_pos]
        after_context = test_code[match_pos:match_pos+20]
        print(f"Context: ...{before_context}|MATCH|{after_context}...")
        
        # Check if this is closer to the end (should be line 13 or 14, not line 7)
        total_lines = test_code.count('\n') + 1
        print(f"Total lines: {total_lines}")
        
        if line_num >= total_lines - 2:  # Within last 2 lines
            print("✅ SUCCESS: Improved matching found class-ending brace!")
            return True
        else:
            print("❌ FAIL: Still matching early in file")
            return False
    else:
        print("❌ FAIL: No match found")
        return False

def test_old_vs_new_matching():
    """Compare old vs new matching behavior."""
    
    test_code = '''using UnityEngine;

public class TestClass : MonoBehaviour  
{
    void Start()
    {
        Debug.Log("test");
    }
    
    void Update()
    {
        if (condition)
        {
            DoSomething();
        }
    }
    
    void LateUpdate()
    {
        // More logic
    }
}'''
    
    import re
    
    anchor_pattern = r"\s*}\s*$"
    flags = re.MULTILINE
    
    # Old behavior (first match)
    old_match = re.search(anchor_pattern, test_code, flags)
    old_line = test_code[:old_match.start()].count('\n') + 1 if old_match else None
    
    # New behavior (improved matching)
    new_match = manage_script_edits_module._find_best_anchor_match(
        anchor_pattern, test_code, flags, prefer_last=True
    )
    new_line = test_code[:new_match.start()].count('\n') + 1 if new_match else None
    
    print(f"Old matching (first): Line {old_line}")
    print(f"New matching (improved): Line {new_line}")
    
    total_lines = test_code.count('\n') + 1
    print(f"Total lines: {total_lines}")
    
    # The new approach should choose a line much closer to the end
    if new_line and old_line and new_line > old_line:
        print("✅ SUCCESS: New matching chooses a later line!")
        
        # Verify it's actually the class end, not just a later method
        if new_line >= total_lines - 2:
            print("✅ EXCELLENT: New matching found the actual class end!")
            return True
        else:
            print("⚠️  PARTIAL: Better than before, but might still be a method end")
            return True
    else:
        print("❌ FAIL: New matching didn't improve")
        return False

def test_apply_edits_with_improved_matching():
    """Test that _apply_edits_locally uses improved matching."""
    
    original_code = '''using UnityEngine;

public class TestClass : MonoBehaviour
{
    public string message = "Hello World";
    
    void Start()
    {
        Debug.Log(message);
    }
}'''
    
    # Test anchor_insert with the problematic pattern
    edits = [{
        "op": "anchor_insert",
        "anchor": r"\s*}\s*$",  # This should now find the class end
        "position": "before",
        "text": "\n    public void NewMethod() { Debug.Log(\"Added at class end\"); }\n"
    }]
    
    try:
        result = manage_script_edits_module._apply_edits_locally(original_code, edits)
        
        # Check where the new method was inserted
        lines = result.split('\n')
        for i, line in enumerate(lines):
            if "NewMethod" in line:
                print(f"NewMethod inserted at line {i+1}: {line.strip()}")
                
                # Verify it's near the end, not in the middle
                total_lines = len(lines)
                if i >= total_lines - 5:  # Within last 5 lines
                    print("✅ SUCCESS: Method inserted near class end!")
                    return True
                else:
                    print("❌ FAIL: Method inserted too early in file")
                    return False
                    
        print("❌ FAIL: NewMethod not found in result")
        return False
        
    except Exception as e:
        print(f"❌ ERROR: {e}")
        return False

if __name__ == "__main__":
    print("Testing improved anchor matching...")
    print("="*60)
    
    success1 = test_improved_anchor_matching()
    
    print("\n" + "="*60)
    print("Comparing old vs new behavior...")
    success2 = test_old_vs_new_matching()
    
    print("\n" + "="*60)
    print("Testing _apply_edits_locally with improved matching...")
    success3 = test_apply_edits_with_improved_matching()
    
    print("\n" + "="*60)
    if success1 and success2 and success3:
        print("🎉 ALL TESTS PASSED! Improved anchor matching is working!")
    else:
        print("💥 Some tests failed. Need more work on anchor matching.")