"""Phase 0 scaffold: the asset_gen tool group exists and is off by default."""
from services.registry.tool_registry import TOOL_GROUPS, DEFAULT_ENABLED_GROUPS


def test_asset_gen_group_registered():
    assert "asset_gen" in TOOL_GROUPS
    assert TOOL_GROUPS["asset_gen"]  # has a non-empty human description


def test_asset_gen_group_disabled_by_default():
    # Parity with vfx/animation: non-core groups start hidden until enabled at runtime.
    assert "asset_gen" not in DEFAULT_ENABLED_GROUPS
