from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from services.tools.unity_docs import (
    unity_docs,
    ALL_ACTIONS,
    _extract_version,
    _build_doc_url,
    _build_property_url,
    _parse_unity_doc_html,
    _parse_manual_html,
)


# ---------------------------------------------------------------------------
# Sample HTML for parser tests
# ---------------------------------------------------------------------------

SAMPLE_DOC_HTML = """\
<div class="subsection">
  <div class="signature">
    <pre>public static bool <strong>Raycast</strong>(Vector3 origin, Vector3 direction)</pre>
  </div>
</div>
<div class="subsection">
  <h2>Description</h2>
  <p>Casts a ray against all colliders in the Scene.</p>
</div>
<div class="subsection">
  <h2>Parameters</h2>
  <table>
    <tr>
      <td class="name-collumn"><strong>origin</strong></td>
      <td class="desc-collumn">The starting point of the ray in world coordinates.</td>
    </tr>
    <tr>
      <td class="name-collumn"><strong>direction</strong></td>
      <td class="desc-collumn">The direction of the ray.</td>
    </tr>
  </table>
</div>
<div class="subsection">
  <h2>Returns</h2>
  <p><strong>bool</strong> True when the ray intersects any collider.</p>
</div>
<div class="subsection">
  <h2>Examples</h2>
  <pre class="codeExampleCS">void Update() {
    if (Physics.Raycast(transform.position, transform.forward, 100))
        Debug.Log("Hit something");
}</pre>
</div>
"""

# Modern Unity docs use h3, "signature-CS", "name lbl", "desc" classes
SAMPLE_DOC_HTML_MODERN = """\
<div class="section">
  <div class="subsection">
    <div class="signature">
      <div class="signature-CS sig-block">
        <h2>Declaration</h2>public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance = Mathf.Infinity);
      </div>
    </div>
  </div>
  <div class="subsection">
    <h3>Parameters</h3>
    <table class="list">
      <tr>
        <td class="name lbl">origin</td>
        <td class="desc">The starting point of the ray in world coordinates.</td>
      </tr>
      <tr>
        <td class="name lbl">direction</td>
        <td class="desc">The direction of the ray.</td>
      </tr>
    </table>
  </div>
  <div class="subsection">
    <h3>Returns</h3>
    <p><strong>bool</strong> Returns true if the ray intersects with a Collider.</p>
  </div>
  <div class="subsection">
    <h3>Description</h3>
    <p>Casts a ray against all colliders in the Scene.</p>
  </div>
  <pre class="codeExampleCS">void Update() {
    Physics.Raycast(transform.position, Vector3.forward, 10f);
}</pre>
</div>
"""


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def ctx():
    return SimpleNamespace(info=AsyncMock(), warning=AsyncMock())


# ---------------------------------------------------------------------------
# Version extraction (pure)
# ---------------------------------------------------------------------------

def test_extract_version_full():
    assert _extract_version("6000.0.38f1") == "6000.0"


def test_extract_version_lts():
    assert _extract_version("2022.3.45f1") == "2022.3"


def test_extract_version_beta():
    assert _extract_version("6000.1.0b2") == "6000.1"


def test_extract_version_none():
    assert _extract_version(None) is None


def test_extract_version_empty():
    assert _extract_version("") is None


def test_extract_version_already_short():
    assert _extract_version("6000.0") == "6000.0"


# ---------------------------------------------------------------------------
# URL construction (pure)
# ---------------------------------------------------------------------------

def test_build_url_class_only():
    url = _build_doc_url("Physics", None, "6000.0")
    assert url == "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.html"


def test_build_url_with_member():
    url = _build_doc_url("Physics", "Raycast", "6000.0")
    assert url == "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.Raycast.html"


def test_build_url_versionless():
    url = _build_doc_url("Physics", None, None)
    assert url == "https://docs.unity3d.com/ScriptReference/Physics.html"


def test_build_property_url():
    url = _build_property_url("Transform", "position", "6000.0")
    assert url == "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform-position.html"


def test_build_property_url_versionless():
    url = _build_property_url("Transform", "position", None)
    assert url == "https://docs.unity3d.com/ScriptReference/Transform-position.html"


# ---------------------------------------------------------------------------
# HTML parsing (pure)
# ---------------------------------------------------------------------------

def test_parse_html_description():
    result = _parse_unity_doc_html(SAMPLE_DOC_HTML)
    assert "Casts a ray" in result["description"]


def test_parse_html_signatures():
    result = _parse_unity_doc_html(SAMPLE_DOC_HTML)
    assert len(result["signatures"]) >= 1
    assert "Raycast" in result["signatures"][0]


def test_parse_html_parameters():
    result = _parse_unity_doc_html(SAMPLE_DOC_HTML)
    assert len(result["parameters"]) == 2
    assert result["parameters"][0]["name"] == "origin"
    assert "starting point" in result["parameters"][0]["description"]
    assert result["parameters"][1]["name"] == "direction"


def test_parse_html_returns():
    result = _parse_unity_doc_html(SAMPLE_DOC_HTML)
    assert "True when" in result["returns"]


def test_parse_html_examples():
    result = _parse_unity_doc_html(SAMPLE_DOC_HTML)
    assert len(result["examples"]) >= 1
    assert "Physics.Raycast" in result["examples"][0]


def test_parse_empty_html():
    result = _parse_unity_doc_html("")
    assert result["description"] == ""
    assert result["signatures"] == []
    assert result["parameters"] == []
    assert result["returns"] == ""
    assert result["examples"] == []
    assert result["see_also"] == []


# ---------------------------------------------------------------------------
# Modern HTML format (h3 headings, "name lbl", "desc", "signature-CS")
# ---------------------------------------------------------------------------

def test_parse_modern_description():
    result = _parse_unity_doc_html(SAMPLE_DOC_HTML_MODERN)
    assert "Casts a ray" in result["description"]


def test_parse_modern_signatures():
    result = _parse_unity_doc_html(SAMPLE_DOC_HTML_MODERN)
    assert len(result["signatures"]) >= 1
    assert "Raycast" in result["signatures"][0]


def test_parse_modern_parameters():
    result = _parse_unity_doc_html(SAMPLE_DOC_HTML_MODERN)
    assert len(result["parameters"]) == 2
    assert result["parameters"][0]["name"] == "origin"
    assert "starting point" in result["parameters"][0]["description"]


def test_parse_modern_returns():
    result = _parse_unity_doc_html(SAMPLE_DOC_HTML_MODERN)
    assert "bool" in result["returns"]


def test_parse_modern_examples():
    result = _parse_unity_doc_html(SAMPLE_DOC_HTML_MODERN)
    assert len(result["examples"]) >= 1
    assert "Raycast" in result["examples"][0]


# ---------------------------------------------------------------------------
# Tool action tests (mock _fetch_url)
# ---------------------------------------------------------------------------

def test_unknown_action_returns_error():
    result = asyncio.run(unity_docs(SimpleNamespace(), action="bad_action"))
    assert result["success"] is False
    assert "Unknown action" in result["message"]


def test_get_doc_requires_class_name():
    result = asyncio.run(unity_docs(SimpleNamespace(), action="get_doc"))
    assert result["success"] is False
    assert "class_name" in result["message"]


def test_get_doc_success():
    async def mock_fetch(url):
        return (200, SAMPLE_DOC_HTML)

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch):
        result = asyncio.run(
            unity_docs(SimpleNamespace(), action="get_doc", class_name="Physics")
        )
    assert result["success"] is True
    assert result["data"]["found"] is True
    assert result["data"]["class"] == "Physics"
    assert "Casts a ray" in result["data"]["description"]
    assert len(result["data"]["signatures"]) >= 1
    assert len(result["data"]["parameters"]) == 2
    assert len(result["data"]["examples"]) >= 1


def test_get_doc_404():
    async def mock_fetch(url):
        return (404, "")

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch):
        result = asyncio.run(
            unity_docs(SimpleNamespace(), action="get_doc", class_name="FakeClass")
        )
    assert result["success"] is True
    assert result["data"]["found"] is False
    assert "suggestion" in result["data"]


def test_get_doc_property_fallback():
    """First fetch (dot URL) 404s, second fetch (dash URL) succeeds."""
    call_count = 0

    async def mock_fetch(url):
        nonlocal call_count
        call_count += 1
        if "-position" in url:
            return (200, SAMPLE_DOC_HTML)
        return (404, "")

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch):
        result = asyncio.run(
            unity_docs(
                SimpleNamespace(),
                action="get_doc",
                class_name="Transform",
                member_name="position",
            )
        )
    assert result["success"] is True
    assert result["data"]["found"] is True
    assert call_count == 2


def test_get_doc_network_error():
    async def mock_fetch(url):
        raise ConnectionError("Network unreachable")

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch):
        result = asyncio.run(
            unity_docs(SimpleNamespace(), action="get_doc", class_name="Physics")
        )
    assert result["success"] is False
    assert "Could not reach" in result["message"]


def test_get_doc_version_fallback():
    """Versioned URL 404s, versionless succeeds."""
    async def mock_fetch(url):
        if "/6000.0/" in url:
            return (404, "")
        return (200, SAMPLE_DOC_HTML)

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch):
        result = asyncio.run(
            unity_docs(
                SimpleNamespace(),
                action="get_doc",
                class_name="Physics",
                version="6000.0.38f1",
            )
        )
    assert result["success"] is True
    assert result["data"]["found"] is True
    assert "/6000.0/" not in result["data"]["url"]


def test_get_doc_with_member_and_version():
    async def mock_fetch(url):
        return (200, SAMPLE_DOC_HTML)

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch):
        result = asyncio.run(
            unity_docs(
                SimpleNamespace(),
                action="get_doc",
                class_name="Physics",
                member_name="Raycast",
                version="6000.0.38f1",
            )
        )
    assert result["success"] is True
    assert result["data"]["found"] is True
    assert result["data"]["member"] == "Raycast"
    assert "Physics.Raycast" in result["data"]["url"]


def test_get_doc_class_only_no_member_in_response():
    async def mock_fetch(url):
        return (200, SAMPLE_DOC_HTML)

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch):
        result = asyncio.run(
            unity_docs(SimpleNamespace(), action="get_doc", class_name="Physics")
        )
    assert result["data"]["member"] is None


def test_all_actions_list():
    assert ALL_ACTIONS == ["get_doc", "get_manual", "get_package_doc", "lookup"]


def test_no_duplicate_actions():
    assert len(ALL_ACTIONS) == len(set(ALL_ACTIONS))


def test_all_actions_includes_new():
    assert "get_manual" in ALL_ACTIONS
    assert "get_package_doc" in ALL_ACTIONS
    assert "lookup" in ALL_ACTIONS
    assert len(ALL_ACTIONS) == 4


# ---------------------------------------------------------------------------
# Manual page HTML samples
# ---------------------------------------------------------------------------

SAMPLE_MANUAL_HTML = """\
<h1>Execution Order</h1>
<h2>Overview</h2>
<p>Unity calls event functions in a specific order.</p>
<h2>Initialization</h2>
<p>Awake is called first, then OnEnable, then Start.</p>
<pre>void Awake() {
    Debug.Log("Awake");
}</pre>
"""


# ---------------------------------------------------------------------------
# Manual HTML parsing (pure)
# ---------------------------------------------------------------------------

def test_parse_manual_title():
    result = _parse_manual_html(SAMPLE_MANUAL_HTML)
    assert result["title"] == "Execution Order"


def test_parse_manual_sections():
    result = _parse_manual_html(SAMPLE_MANUAL_HTML)
    assert len(result["sections"]) == 2
    assert result["sections"][0]["heading"] == "Overview"
    assert "event functions" in result["sections"][0]["content"]
    assert result["sections"][1]["heading"] == "Initialization"
    assert "Awake is called first" in result["sections"][1]["content"]


def test_parse_manual_code_examples():
    result = _parse_manual_html(SAMPLE_MANUAL_HTML)
    assert len(result["code_examples"]) == 1
    assert "Debug.Log" in result["code_examples"][0]


def test_parse_manual_empty():
    result = _parse_manual_html("")
    assert result["title"] == ""
    assert result["sections"] == []
    assert result["code_examples"] == []


# ---------------------------------------------------------------------------
# get_manual action tests (mock _fetch_url)
# ---------------------------------------------------------------------------

def test_get_manual_success():
    async def mock_fetch(url):
        return (200, SAMPLE_MANUAL_HTML)

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch):
        result = asyncio.run(
            unity_docs(SimpleNamespace(), action="get_manual", slug="execution-order")
        )
    assert result["success"] is True
    assert result["data"]["found"] is True
    assert result["data"]["title"] == "Execution Order"
    assert "Manual/execution-order" in result["data"]["url"]
    assert len(result["data"]["sections"]) == 2
    assert len(result["data"]["code_examples"]) == 1


def test_get_manual_requires_slug():
    result = asyncio.run(unity_docs(SimpleNamespace(), action="get_manual"))
    assert result["success"] is False
    assert "slug" in result["message"]


def test_get_manual_404():
    async def mock_fetch(url):
        return (404, "")

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch):
        result = asyncio.run(
            unity_docs(SimpleNamespace(), action="get_manual", slug="nonexistent-page")
        )
    assert result["success"] is True
    assert result["data"]["found"] is False


def test_get_manual_version_fallback():
    """Versioned URL 404s, unversioned succeeds."""
    async def mock_fetch(url):
        if "/6000.0/" in url:
            return (404, "")
        return (200, SAMPLE_MANUAL_HTML)

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch):
        result = asyncio.run(
            unity_docs(
                SimpleNamespace(),
                action="get_manual",
                slug="execution-order",
                version="6000.0.38f1",
            )
        )
    assert result["success"] is True
    assert result["data"]["found"] is True
    assert "/6000.0/" not in result["data"]["url"]


# ---------------------------------------------------------------------------
# get_package_doc action tests (mock _fetch_url_full)
# ---------------------------------------------------------------------------

def test_get_package_doc_success():
    async def mock_fetch_full(url):
        final = "https://docs.unity3d.com/6000.0/Documentation/Manual/urp/2d-index.html"
        return (200, SAMPLE_MANUAL_HTML, final)

    with patch("services.tools.unity_docs._fetch_url_full", side_effect=mock_fetch_full):
        result = asyncio.run(
            unity_docs(
                SimpleNamespace(),
                action="get_package_doc",
                package="com.unity.render-pipelines.universal",
                page="2d-index",
                pkg_version="17.0",
            )
        )
    assert result["success"] is True
    assert result["data"]["found"] is True
    assert result["data"]["package"] == "com.unity.render-pipelines.universal"
    assert result["data"]["page"] == "2d-index"
    assert result["data"]["title"] == "Execution Order"
    assert len(result["data"]["sections"]) == 2
    assert len(result["data"]["code_examples"]) == 1
    # Should use the final (redirected) URL
    assert "Manual/urp/2d-index" in result["data"]["url"]


def test_get_package_doc_requires_all_params():
    # Missing package
    result = asyncio.run(
        unity_docs(
            SimpleNamespace(),
            action="get_package_doc",
            page="index",
            pkg_version="17.0",
        )
    )
    assert result["success"] is False
    assert "package" in result["message"]

    # Missing page
    result = asyncio.run(
        unity_docs(
            SimpleNamespace(),
            action="get_package_doc",
            package="com.unity.render-pipelines.universal",
            pkg_version="17.0",
        )
    )
    assert result["success"] is False
    assert "page" in result["message"]

    # Missing pkg_version
    result = asyncio.run(
        unity_docs(
            SimpleNamespace(),
            action="get_package_doc",
            package="com.unity.render-pipelines.universal",
            page="index",
        )
    )
    assert result["success"] is False
    assert "pkg_version" in result["message"]


def test_get_package_doc_404():
    async def mock_fetch_full(url):
        return (404, "", url)

    with patch("services.tools.unity_docs._fetch_url_full", side_effect=mock_fetch_full):
        result = asyncio.run(
            unity_docs(
                SimpleNamespace(),
                action="get_package_doc",
                package="com.unity.fake-package",
                page="index",
                pkg_version="1.0",
            )
        )
    assert result["success"] is True
    assert result["data"]["found"] is False


# ---------------------------------------------------------------------------
# lookup action tests
# ---------------------------------------------------------------------------

def test_lookup_requires_query():
    result = asyncio.run(unity_docs(SimpleNamespace(), action="lookup"))
    assert result["success"] is False
    assert "query" in result["message"] or "queries" in result["message"]


def test_lookup_single_query():
    """lookup with a single query finds it via ScriptReference."""
    async def mock_fetch(url):
        if "ScriptReference/Physics" in url:
            return (200, SAMPLE_DOC_HTML)
        return (404, "")

    async def mock_fetch_full(url):
        return (404, "", url)

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch), \
         patch("services.tools.unity_docs._fetch_url_full", side_effect=mock_fetch_full):
        result = asyncio.run(
            unity_docs(SimpleNamespace(), action="lookup", query="Physics")
        )
    assert result["success"] is True
    assert result["data"]["found"] is True
    assert result["data"]["summary"]["found"] == 1


def test_lookup_batch_queries():
    """lookup with multiple queries searches all in parallel."""
    async def mock_fetch(url):
        if "ScriptReference/Physics" in url or "ScriptReference/Camera" in url:
            return (200, SAMPLE_DOC_HTML)
        return (404, "")

    async def mock_fetch_full(url):
        return (404, "", url)

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch), \
         patch("services.tools.unity_docs._fetch_url_full", side_effect=mock_fetch_full):
        result = asyncio.run(
            unity_docs(SimpleNamespace(), action="lookup",
                       queries="Physics,Camera,zzz-nonexistent")
        )
    assert result["success"] is True
    assert result["data"]["summary"]["total"] == 3
    assert result["data"]["summary"]["found"] == 2
    assert result["data"]["summary"]["missed"] == 1


def test_lookup_no_results():
    """lookup with garbage returns found=False with suggestions."""
    async def mock_fetch(url):
        return (404, "")

    async def mock_fetch_full(url):
        return (404, "", url)

    with patch("services.tools.unity_docs._fetch_url", side_effect=mock_fetch), \
         patch("services.tools.unity_docs._fetch_url_full", side_effect=mock_fetch_full):
        result = asyncio.run(
            unity_docs(SimpleNamespace(), action="lookup", query="zzz-nonexistent-xyz")
        )
    assert result["success"] is True
    assert result["data"]["found"] is False
    assert result["data"]["summary"]["missed"] == 1


def test_asset_keyword_detection():
    """Queries with asset keywords trigger project asset search."""
    from services.tools.unity_docs import _should_search_assets
    assert _should_search_assets("Mesh2D shader") is True
    assert _should_search_assets("Lit material") is True
    assert _should_search_assets("URP 2D lighting") is True
    assert _should_search_assets("default sprite") is True
    assert _should_search_assets("Physics.Raycast") is False
    assert _should_search_assets("NavMeshAgent") is False
    assert _should_search_assets("execution-order") is False


def test_build_asset_search_terms():
    """Extract meaningful search terms from query, infer filter types."""
    from services.tools.unity_docs import _build_asset_search_terms
    # "Mesh2D shader" → search for *mesh2d* with filter_type=Shader
    terms = _build_asset_search_terms("Mesh2D shader")
    assert len(terms) >= 1
    assert any("mesh2d" in t.get("search_pattern", "") for t in terms)
    assert any(t.get("filter_type") == "Shader" for t in terms)

    # "MeshRenderer 2D lights" → search for *meshrenderer*, *lights* (2d triggers keyword)
    terms = _build_asset_search_terms("MeshRenderer 2D lights")
    assert len(terms) >= 1
    assert any("meshrenderer" in t.get("search_pattern", "") for t in terms)

    # "Physics.Raycast" → no asset search terms (no asset keywords)
    # (This won't be called since _should_search_assets returns False, but test the function)
    terms = _build_asset_search_terms("Physics.Raycast")
    assert len(terms) >= 1  # Still extracts terms, just won't be triggered
