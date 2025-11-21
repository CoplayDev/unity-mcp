"""
Test the improved anchor matching logic.
"""

import re

import pytest

import services.tools.script_apply_edits as script_apply_edits_module


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

    # Test the problematic anchor pattern
    anchor_pattern = r"\s*}\s*$"
    flags = re.MULTILINE

    # Test our improved function
    best_match = script_apply_edits_module._find_best_anchor_match(
        anchor_pattern, test_code, flags, prefer_last=True
    )

    assert best_match is not None, "anchor pattern not found"
    match_pos = best_match.start()
    line_num = test_code[:match_pos].count('\n') + 1
    total_lines = test_code.count('\n') + 1
    assert line_num >= total_lines - \
        2, f"expected match near end (>= {total_lines-2}), got line {line_num}"


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
    old_line = test_code[:old_match.start()].count(
        '\n') + 1 if old_match else None

    # New behavior (improved matching)
    new_match = script_apply_edits_module._find_best_anchor_match(
        anchor_pattern, test_code, flags, prefer_last=True
    )
    new_line = test_code[:new_match.start()].count(
        '\n') + 1 if new_match else None

    assert old_line is not None and new_line is not None, "failed to locate anchors"
    assert new_line > old_line, f"improved matcher should choose a later line (old={old_line}, new={new_line})"
    total_lines = test_code.count('\n') + 1
    assert new_line >= total_lines - \
        2, f"expected class-end match near end (>= {total_lines-2}), got {new_line}"


@pytest.mark.asyncio
async def test_apply_edits_with_improved_matching():
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

    result = await script_apply_edits_module._apply_edits_locally(
        original_code, edits)
    lines = result.split('\n')
    try:
        idx = next(i for i, line in enumerate(lines) if "NewMethod" in line)
    except StopIteration:
        assert False, "NewMethod not found in result"
    total_lines = len(lines)
    assert idx >= total_lines - \
        5, f"method inserted too early (idx={idx}, total_lines={total_lines})"


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
