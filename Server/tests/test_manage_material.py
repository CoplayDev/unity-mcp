"""Tests for the color that the manage_material tool sends to Unity."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_material import manage_material


@pytest.fixture
def mock_unity(monkeypatch):
    captured = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["params"] = params
        return {"success": True}

    monkeypatch.setattr(
        "services.tools.manage_material.get_unity_instance_from_context",
        AsyncMock(return_value=None),
    )
    monkeypatch.setattr("services.tools.manage_material.send_with_unity_instance", fake_send)
    return captured


def sent_color(mock_unity, color):
    asyncio.run(manage_material(
        SimpleNamespace(),
        action="set_material_color",
        material_path="Assets/Materials/Mat.mat",
        color=color,
    ))
    return mock_unity["params"]["color"]


@pytest.mark.parametrize("color", [
    [255, 128, 0],
    {"r": 255, "g": 128, "b": 0},
    "[255, 128, 0]",
    "255, 128, 0",
])
def test_rgb_in_0_255_range_without_alpha_is_opaque(mock_unity, color):
    assert sent_color(mock_unity, color) == pytest.approx([1.0, 128 / 255, 0.0, 1.0])


@pytest.mark.parametrize("color, expected", [
    ([1.0, 0.5, 0.0], [1.0, 0.5, 0.0, 1.0]),
    ([255, 128, 0, 64], [1.0, 128 / 255, 0.0, 64 / 255]),
    ("#ff8000", [1.0, 128 / 255, 0.0, 1.0]),
])
def test_normalized_rgb_explicit_alpha_and_hex_are_unchanged(mock_unity, color, expected):
    assert sent_color(mock_unity, color) == pytest.approx(expected)
