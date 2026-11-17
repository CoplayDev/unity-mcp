"""Contracts for shared batch image output and optional audience annotations."""

import copy
import json

import pytest
from mcp.types import ImageContent, TextContent

from services.tools.utils import extract_screenshot_images


@pytest.mark.parametrize("audience", [None, [], ["assistant"], ["user", "assistant"]])
def test_batch_images_preserve_audience_metadata_and_source(audience):
    """Batch images keep their bytes and display hints without duplicating pixels in text."""
    response = {"success": True, "message": "captured", "data": {
        "sceneCenter": [1, 2, 3], "sceneRadius": 5,
        "screenshots": [
            {"angle": 0, "path": "front.png", "imageBase64": "ZnJvbnQ="},
            {"angle": 90, "path": "side.png", "imageBase64": "c2lkZQ=="},
            {"angle": 180, "path": "file-only.png"},
        ],
    }}
    original = copy.deepcopy(response)

    if audience is None:
        result = extract_screenshot_images(response)
    else:
        result = extract_screenshot_images(response, image_audience=audience)

    assert len(result.content) == 5
    assert isinstance(result.content[0], TextContent)
    metadata = json.loads(result.content[0].text)
    assert metadata == {"success": True, "message": "captured", "data": {
        "sceneCenter": [1, 2, 3], "sceneRadius": 5,
        "screenshots": [
            {"angle": 0, "path": "front.png"},
            {"angle": 90, "path": "side.png"},
            {"angle": 180, "path": "file-only.png"},
        ],
    }}
    assert result.content[1].text == "[Angle: 0]"
    assert result.content[3].text == "[Angle: 90]"
    images = [block for block in result.content if isinstance(block, ImageContent)]
    assert [image.data for image in images] == ["ZnJvbnQ=", "c2lkZQ=="]
    for image in images:
        assert image.mimeType == "image/png"
        if audience is None:
            assert getattr(image, "annotations", None) is None
        else:
            annotations = image.annotations
            actual = annotations["audience"] if isinstance(annotations, dict) else annotations.audience
            assert actual == audience
    for block in result.content:
        if isinstance(block, TextContent):
            assert "imageBase64" not in block.text
            assert "ZnJvbnQ=" not in block.text
            assert "c2lkZQ==" not in block.text
    assert response == original
