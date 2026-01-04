from services.registry import get_registered_tools


def test_refresh_unity_tool_is_registered():
    """
    Red test: we expect an explicit refresh tool to exist so callers can force an import/refresh/compile cycle.
    """
    # Import the specific module to ensure it registers its decorator without disturbing global registry state.
    import services.tools.refresh_unity  # noqa: F401

    names = {t.get("name") for t in get_registered_tools()}
    assert "refresh_unity" in names


