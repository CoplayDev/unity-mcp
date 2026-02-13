"""Tests for script_apply_edits.py local helper functions.

Focuses on _apply_edits_locally, _find_best_closing_brace_match,
and _is_in_string_context — especially around C# string variants
(verbatim, interpolated, raw) that can fool brace/anchor matching.
"""
import re
import pytest

from services.tools.script_apply_edits import (
    _apply_edits_locally,
    _find_best_closing_brace_match,
    _find_best_anchor_match,
    _is_in_string_context,
)


# ── _is_in_string_context ────────────────────────────────────────────

class TestIsInStringContext:
    def test_plain_code_not_in_string(self):
        text = 'int x = 42;'
        assert not _is_in_string_context(text, 4)

    def test_inside_regular_string(self):
        text = 'string s = "hello world";'
        # Position inside "hello world"
        pos = text.index("hello")
        assert _is_in_string_context(text, pos)

    def test_inside_verbatim_string(self):
        text = 'string s = @"C:\\Users\\file";'
        pos = text.index("C:")
        assert _is_in_string_context(text, pos)

    def test_inside_interpolated_string(self):
        text = 'string s = $"Value: {x}";'
        # The "Value" part is inside the string
        pos = text.index("Value")
        assert _is_in_string_context(text, pos)

    def test_interpolation_hole_is_not_string(self):
        text = 'string s = $"Value: {x}";'
        # The x inside {x} is in an interpolation hole — it's code, not string
        brace_pos = text.index("{x}") + 1  # the 'x'
        assert not _is_in_string_context(text, brace_pos)

    def test_inside_single_line_comment(self):
        text = 'int x = 1; // this is a comment'
        pos = text.index("this")
        assert _is_in_string_context(text, pos)

    def test_inside_multi_line_comment(self):
        text = 'int x = 1; /* block { } */ int y = 2;'
        pos = text.index("block")
        assert _is_in_string_context(text, pos)

    def test_after_comment_is_code(self):
        text = '// comment\nint x = 1;'
        pos = text.index("int")
        assert not _is_in_string_context(text, pos)

    def test_verbatim_string_doubled_quotes(self):
        text = 'string s = @"He said ""hello""";'
        # The whole thing is one string ending at the final ";
        pos = text.index("hello")
        assert _is_in_string_context(text, pos)

    def test_interpolated_verbatim_combined(self):
        text = 'string s = $@"Path: {dir}\\file";'
        # "Path" is inside the string
        pos = text.index("Path")
        assert _is_in_string_context(text, pos)

    def test_raw_string_literal(self):
        text = 'string s = """\n{ }\n""";'
        pos = text.index("{ }")
        assert _is_in_string_context(text, pos)

    def test_interpolated_raw_string_content(self):
        text = 'string s = $"""\n    Hello {name}\n    """;'
        # "Hello" is string content (non-code)
        pos = text.index("Hello")
        assert _is_in_string_context(text, pos)

    def test_interpolated_raw_string_hole_is_code(self):
        text = 'string s = $"""\n    Hello {name}\n    """;'
        # "name" inside {name} is in an interpolation hole — code
        pos = text.index("name")
        assert not _is_in_string_context(text, pos)

    def test_multi_dollar_raw_string_content(self):
        text = 'string s = $$"""\n    {literal} {{interp}}\n    """;'
        # {literal} has only 1 brace — it's literal string content
        pos = text.index("literal")
        assert _is_in_string_context(text, pos)

    def test_multi_dollar_raw_string_hole_is_code(self):
        text = 'string s = $$"""\n    {literal} {{interp}}\n    """;'
        # {{interp}} has 2 braces matching $$ — it's an interpolation hole
        pos = text.index("interp")
        assert not _is_in_string_context(text, pos)

    def test_interpolated_raw_string_closing(self):
        text = 'string s = $"""\n    body\n    """; int x = 1;'
        # "x" after the closing """ is code
        pos = text.index("x = 1")
        assert not _is_in_string_context(text, pos)


# ── _find_best_closing_brace_match ───────────────────────────────────

class TestFindBestClosingBraceMatch:
    def test_skips_braces_in_interpolated_strings(self):
        """Braces inside $"...{x}..." should not be scored as class-end."""
        code = (
            'public class Foo {\n'
            '    void M() {\n'
            '        string s = $"Score: {score}";\n'
            '    }\n'
            '}\n'
        )
        pattern = r'^\s*}\s*$'
        matches = list(re.finditer(pattern, code, re.MULTILINE))
        # There should be matches for the method close and class close
        assert len(matches) >= 1
        best = _find_best_closing_brace_match(matches, code)
        # The best match should be the class-closing brace, not one inside a string
        assert best is not None
        line_num = code[:best.start()].count('\n')
        # Class close is the last "}" line
        assert line_num == 4  # 0-indexed, line 5 is "}"

    def test_skips_braces_in_verbatim_strings(self):
        """@"{ }" should not confuse the scorer."""
        code = (
            'public class Foo {\n'
            '    string s = @"{ }";\n'
            '}\n'
        )
        pattern = r'^\s*}\s*$'
        matches = list(re.finditer(pattern, code, re.MULTILINE))
        best = _find_best_closing_brace_match(matches, code)
        assert best is not None

    def test_prefers_class_brace_over_method_brace(self):
        """Should pick class-closing } (depth 1) over method-closing } (depth 2)."""
        code = (
            'public class Foo : MonoBehaviour\n'
            '{\n'
            '    private int score = 42;\n'
            '\n'
            '    void Start()\n'
            '    {\n'
            '        Debug.Log($"Score: {score}");\n'
            '    }\n'
            '\n'
            '    void OnGUI()\n'
            '    {\n'
            '        GUI.Label(new Rect(10, 10, 200, 20), $"Score: {score}");\n'
            '    }\n'
            '}\n'
        )
        pattern = r'^\s*}\s*$'
        matches = list(re.finditer(pattern, code, re.MULTILINE))
        # Should have 3 matches: Start close, OnGUI close, class close
        assert len(matches) == 3
        best = _find_best_closing_brace_match(matches, code)
        assert best is not None
        best_line = code[:best.start()].count('\n')
        # Class close is the last "}" — line 13 (0-indexed)
        assert best_line == 13

    def test_skips_braces_in_interpolated_raw_strings(self):
        """$\"\"\"{x}\"\"\" braces should not confuse the scorer."""
        code = (
            'public class Foo {\n'
            '    string s = $"""\n'
            '        { literal }\n'
            '        {interp}\n'
            '        """;\n'
            '}\n'
        )
        pattern = r'^\s*}\s*$'
        matches = list(re.finditer(pattern, code, re.MULTILINE))
        best = _find_best_closing_brace_match(matches, code)
        assert best is not None
        best_line = code[:best.start()].count('\n')
        assert best_line == 5  # class-closing brace

    def test_closing_brace_scorer_with_interpolated_code(self):
        """Realistic C# with multiple $"" strings should still find class-end."""
        code = (
            'using UnityEngine;\n'
            'public class HUD : MonoBehaviour {\n'
            '    void OnGUI() {\n'
            '        Debug.Log($"Score: {score}");\n'
            '        Debug.Log($@"Path: {path}\\save");\n'
            '    }\n'
            '}\n'
        )
        pattern = r'^\s*}\s*$'
        matches = list(re.finditer(pattern, code, re.MULTILINE))
        best = _find_best_closing_brace_match(matches, code)
        assert best is not None
        # Should pick the class-closing brace (last one)
        best_line = code[:best.start()].count('\n')
        assert best_line == 6  # 0-indexed


# ── _apply_edits_locally regression guards ───────────────────────────

class TestApplyEditsLocally:
    @pytest.mark.asyncio
    async def test_replace_range_basic(self):
        original = "line1\nline2\nline3\n"
        edits = [{
            "op": "replace_range",
            "startLine": 2,
            "startCol": 1,
            "endLine": 2,
            "endCol": 6,
            "text": "REPLACED",
        }]
        result = await _apply_edits_locally(original, edits)
        assert "REPLACED" in result
        assert "line1" in result
        assert "line3" in result

    @pytest.mark.asyncio
    async def test_prepend_and_append(self):
        original = "middle\n"
        edits = [
            {"op": "prepend", "text": "top\n"},
            {"op": "append", "text": "bottom\n"},
        ]
        result = await _apply_edits_locally(original, edits)
        assert result.startswith("top\n")
        assert "bottom" in result

    @pytest.mark.asyncio
    async def test_regex_replace_near_interpolated_strings(self):
        """regex_replace should work even when interpolated strings are in the code."""
        original = (
            'void M() {\n'
            '    Debug.Log($"x={x}");\n'
            '    int OLD = 1;\n'
            '}\n'
        )
        edits = [{
            "op": "regex_replace",
            "pattern": r"OLD",
            "replacement": "NEW",
            "text": "NEW",
        }]
        result = await _apply_edits_locally(original, edits)
        assert "NEW" in result
        assert "OLD" not in result


# ── _find_best_anchor_match with string-aware filtering ──────────────

class TestAnchorMatchFiltering:
    def test_anchor_skips_braces_in_interpolated_strings(self):
        """$"...{x}..." brace should not be picked as anchor match."""
        code = (
            'class Foo {\n'
            '    string s = $"val: {x}";\n'
            '    void M() { }\n'
            '}\n'
        )
        # Pattern looking for closing brace at end of line
        pattern = r'^\s*}\s*$'
        flags = re.MULTILINE
        match = _find_best_anchor_match(pattern, code, flags, prefer_last=True)
        assert match is not None
        # Should match the class-closing brace, not anything inside the string
        best_line = code[:match.start()].count('\n')
        assert best_line == 3  # 0-indexed, class close

    def test_anchor_skips_braces_in_verbatim_strings(self):
        """@"{ }" should not confuse anchor matching."""
        code = (
            'class Foo {\n'
            '    string s = @"{ }";\n'
            '}\n'
        )
        pattern = r'^\s*}\s*$'
        flags = re.MULTILINE
        match = _find_best_anchor_match(pattern, code, flags, prefer_last=True)
        assert match is not None
