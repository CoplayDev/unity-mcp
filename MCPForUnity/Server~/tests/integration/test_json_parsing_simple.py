"""
Simple tests for JSON string parameter parsing logic.
Tests the core JSON parsing functionality without MCP server dependencies.
"""
import json
import pytest


def parse_properties_json(properties):
    """
    Test the JSON parsing logic that would be used in manage_asset.
    This simulates the core parsing functionality.
    """
    if isinstance(properties, str):
        try:
            parsed = json.loads(properties)
            return parsed, "success"
        except json.JSONDecodeError as e:
            return properties, f"failed to parse: {e}"
    return properties, "no_parsing_needed"


class TestJsonParsingLogic:
    """Test the core JSON parsing logic."""

    def test_valid_json_string_parsing(self):
        """Test that valid JSON strings are correctly parsed."""
        json_string = '{"shader": "Universal Render Pipeline/Lit", "color": [0, 0, 1, 1]}'

        result, status = parse_properties_json(json_string)

        assert status == "success"
        assert isinstance(result, dict)
        assert result["shader"] == "Universal Render Pipeline/Lit"
        assert result["color"] == [0, 0, 1, 1]

    def test_invalid_json_string_handling(self):
        """Test that invalid JSON strings are handled gracefully."""
        invalid_json = '{"invalid": json, "missing": quotes}'

        result, status = parse_properties_json(invalid_json)

        assert "failed to parse" in status
        assert result == invalid_json  # Original string returned

    def test_dict_input_unchanged(self):
        """Test that dict inputs are passed through unchanged."""
        original_dict = {
            "shader": "Universal Render Pipeline/Lit", "color": [0, 0, 1, 1]}

        result, status = parse_properties_json(original_dict)

        assert status == "no_parsing_needed"
        assert result == original_dict

    def test_none_input_handled(self):
        """Test that None input is handled correctly."""
        result, status = parse_properties_json(None)

        assert status == "no_parsing_needed"
        assert result is None

    def test_complex_json_parsing(self):
        """Test parsing of complex JSON with nested objects and arrays."""
        complex_json = '''
        {
            "shader": "Universal Render Pipeline/Lit",
            "color": [1, 0, 0, 1],
            "float": {"name": "_Metallic", "value": 0.5},
            "texture": {"name": "_MainTex", "path": "Assets/Textures/Test.png"}
        }
        '''

        result, status = parse_properties_json(complex_json)

        assert status == "success"
        assert isinstance(result, dict)
        assert result["shader"] == "Universal Render Pipeline/Lit"
        assert result["color"] == [1, 0, 0, 1]
        assert result["float"]["name"] == "_Metallic"
        assert result["float"]["value"] == 0.5
        assert result["texture"]["name"] == "_MainTex"
        assert result["texture"]["path"] == "Assets/Textures/Test.png"

    def test_empty_json_string(self):
        """Test handling of empty JSON string."""
        empty_json = "{}"

        result, status = parse_properties_json(empty_json)

        assert status == "success"
        assert isinstance(result, dict)
        assert len(result) == 0

    def test_malformed_json_edge_cases(self):
        """Test various malformed JSON edge cases."""
        test_cases = [
            '{"incomplete": }',
            '{"missing": "quote}',
            '{"trailing": "comma",}',
            '{"unclosed": [1, 2, 3}',
            'not json at all',
            '{"nested": {"broken": }'
        ]

        for malformed_json in test_cases:
            result, status = parse_properties_json(malformed_json)
            assert "failed to parse" in status
            assert result == malformed_json


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
