import asyncio
import re
from html.parser import HTMLParser
from typing import Annotated, Any, Optional
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool

ALL_ACTIONS = ["get_doc", "get_manual", "get_package_doc", "lookup"]


# ---------------------------------------------------------------------------
# Version extraction
# ---------------------------------------------------------------------------

def _extract_version(version_str: str | None) -> str | None:
    """Extract major.minor from a full Unity version string.

    Examples:
        "6000.0.38f1" -> "6000.0"
        "2022.3.45f1" -> "2022.3"
        "6000.1.0b2"  -> "6000.1"
        None           -> None
        ""             -> None
    """
    if not version_str:
        return None
    parts = version_str.split(".")
    if len(parts) < 2:
        return version_str
    second = re.sub(r"[a-zA-Z].*$", "", parts[1])
    return f"{parts[0]}.{second}"


# ---------------------------------------------------------------------------
# URL construction
# ---------------------------------------------------------------------------

def _build_doc_url(
    class_name: str,
    member_name: str | None,
    version: str | None,
) -> str:
    """Build the ScriptReference URL using dot separator for members."""
    if member_name:
        page = f"{class_name}.{member_name}.html"
    else:
        page = f"{class_name}.html"

    if version:
        return f"https://docs.unity3d.com/{version}/Documentation/ScriptReference/{page}"
    return f"https://docs.unity3d.com/ScriptReference/{page}"


def _build_property_url(
    class_name: str,
    member_name: str,
    version: str | None,
) -> str:
    """Build the ScriptReference URL using dash separator (property style)."""
    page = f"{class_name}-{member_name}.html"
    if version:
        return f"https://docs.unity3d.com/{version}/Documentation/ScriptReference/{page}"
    return f"https://docs.unity3d.com/ScriptReference/{page}"


# ---------------------------------------------------------------------------
# HTTP fetch
# ---------------------------------------------------------------------------

async def _fetch_url(url: str) -> tuple[int, str]:
    """Fetch a URL and return (status_code, body_text).

    Runs urllib in an executor to avoid blocking the event loop.
    """
    status, body, _final = await _fetch_url_full(url)
    return (status, body)


async def _fetch_url_full(url: str) -> tuple[int, str, str]:
    """Fetch a URL and return (status_code, body_text, final_url).

    Like _fetch_url but also returns the final URL after any redirects.
    """
    loop = asyncio.get_running_loop()

    def _do_fetch() -> tuple[int, str, str]:
        req = Request(url, headers={"User-Agent": "MCPForUnity/1.0"})
        try:
            with urlopen(req, timeout=10) as resp:
                body = resp.read().decode("utf-8", errors="replace")
                return (resp.status, body, resp.url)
        except HTTPError as e:
            return (e.code, "", url)
        except URLError as e:
            raise ConnectionError(f"Cannot reach {url}: {e}") from e

    return await loop.run_in_executor(None, _do_fetch)


# ---------------------------------------------------------------------------
# HTML parser
# ---------------------------------------------------------------------------

class _UnityDocParser(HTMLParser):
    """Extracts structured data from Unity ScriptReference HTML pages."""

    def __init__(self) -> None:
        super().__init__()
        # Tracking state
        self._in_subsection = False
        self._subsection_depth = 0
        self._subsection_title: str | None = None
        self._in_signature = False
        self._in_pre = False
        self._signature_from_pre = False
        self._in_code_example = False
        self._in_param_table = False
        self._in_td = False
        self._td_class: str | None = None
        self._in_h2 = False
        self._in_p = False
        self._current_param: dict[str, str] = {}
        self._current_text: list[str] = []

        # Collected results
        self.description = ""
        self.signatures: list[str] = []
        self.parameters: list[dict[str, str]] = []
        self.returns = ""
        self.examples: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        attr_dict = dict(attrs)
        classes = (attr_dict.get("class") or "").split()

        if tag == "div" and "subsection" in classes:
            self._in_subsection = True
            self._subsection_depth = 1
            self._subsection_title = None
        elif tag == "div" and self._in_subsection:
            self._subsection_depth += 1

        if tag == "div" and ("signature" in classes or "signature-CS" in classes):
            self._in_signature = True

        # Unity docs use h3 (not h2) for subsection titles
        if tag in ("h2", "h3") and self._in_subsection:
            self._in_h2 = True
            self._current_text = []

        # Signatures: capture text inside signature-CS div (no <pre> in modern docs)
        if tag == "div" and "signature-CS" in classes:
            self._in_signature = True
            self._current_text = []

        if tag == "pre":
            if "codeExampleCS" in classes:
                self._in_code_example = True
                self._current_text = []
            elif self._in_signature:
                self._in_pre = True
                self._current_text = []

        if tag == "p" and self._in_subsection:
            self._in_p = True
            self._current_text = []

        if tag == "table" and self._in_subsection:
            self._in_param_table = True

        if tag == "td" and self._in_param_table:
            self._in_td = True
            self._td_class = attr_dict.get("class", "")
            self._current_text = []

        if tag == "tr" and self._in_param_table:
            self._current_param = {}

    def handle_endtag(self, tag: str) -> None:
        if tag in ("h2", "h3") and self._in_h2:
            self._in_h2 = False
            self._subsection_title = "".join(self._current_text).strip()

        if tag == "pre":
            if self._in_code_example:
                self._in_code_example = False
                self.examples.append("".join(self._current_text).strip())
            elif self._in_pre:
                self._in_pre = False
                self.signatures.append("".join(self._current_text).strip())
                self._signature_from_pre = True  # Mark that pre already captured this

        if tag == "div" and self._in_signature:
            if not self._signature_from_pre:
                # Capture inline signature text (modern Unity docs don't use <pre>)
                text = " ".join("".join(self._current_text).split()).strip()
                # Remove "Declaration" prefix that appears inside the sig block
                if text.startswith("Declaration"):
                    text = text[len("Declaration"):].strip()
                if text:
                    self.signatures.append(text)
            self._in_signature = False
            self._signature_from_pre = False

        if tag == "p" and self._in_p:
            self._in_p = False
            text = "".join(self._current_text).strip()
            if text and self._subsection_title == "Description" and not self.description:
                self.description = text
            elif text and self._subsection_title == "Returns" and not self.returns:
                self.returns = text

        if tag == "td" and self._in_td:
            self._in_td = False
            text = "".join(self._current_text).strip()
            # Support both old ("name-collumn"/"desc-collumn") and new ("name lbl"/"desc") class names
            if self._td_class and ("name-collumn" in self._td_class or "name" in self._td_class.split()):
                self._current_param["name"] = text
            elif self._td_class and ("desc-collumn" in self._td_class or "desc" in self._td_class.split()):
                self._current_param["description"] = text

        if tag == "tr" and self._in_param_table:
            if self._current_param.get("name"):
                self.parameters.append(dict(self._current_param))
            self._current_param = {}

        if tag == "table" and self._in_param_table:
            self._in_param_table = False

        if tag == "div" and self._in_subsection:
            self._subsection_depth -= 1
            if self._subsection_depth <= 0:
                self._in_subsection = False

    def handle_data(self, data: str) -> None:
        if self._in_h2 or self._in_pre or self._in_code_example or self._in_p or self._in_td or self._in_signature:
            self._current_text.append(data)


def _parse_unity_doc_html(html: str) -> dict[str, Any]:
    """Parse Unity ScriptReference HTML into structured data."""
    parser = _UnityDocParser()
    parser.feed(html)
    return {
        "description": parser.description,
        "signatures": parser.signatures,
        "parameters": parser.parameters,
        "returns": parser.returns,
        "examples": parser.examples,
        "see_also": [],
    }


# ---------------------------------------------------------------------------
# Manual / package doc HTML parser
# ---------------------------------------------------------------------------

class _ManualPageParser(HTMLParser):
    """Extracts content from Unity Manual / package doc HTML pages.

    These are article-style pages with h1 title, h2/h3 section headings,
    p paragraphs, and pre code blocks — simpler than ScriptReference.
    """

    def __init__(self) -> None:
        super().__init__()
        self._in_h1 = False
        self._in_heading = False
        self._in_p = False
        self._in_pre = False
        self._current_text: list[str] = []
        self._current_heading: str | None = None
        self._content_parts: list[str] = []

        # Collected results
        self.title = ""
        self.sections: list[dict[str, str]] = []
        self.code_examples: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag == "h1" and not self.title:
            self._in_h1 = True
            self._current_text = []
        elif tag in ("h2", "h3"):
            # Flush previous section before starting a new heading
            self._flush_section()
            self._in_heading = True
            self._current_text = []
        elif tag == "p":
            self._in_p = True
            self._current_text = []
        elif tag == "pre":
            self._in_pre = True
            self._current_text = []

    def handle_endtag(self, tag: str) -> None:
        if tag == "h1" and self._in_h1:
            self._in_h1 = False
            self.title = "".join(self._current_text).strip()

        elif tag in ("h2", "h3") and self._in_heading:
            self._in_heading = False
            self._current_heading = "".join(self._current_text).strip()
            self._content_parts = []

        elif tag == "p" and self._in_p:
            self._in_p = False
            text = "".join(self._current_text).strip()
            if text:
                self._content_parts.append(text)

        elif tag == "pre" and self._in_pre:
            self._in_pre = False
            code = "".join(self._current_text).strip()
            if code:
                self.code_examples.append(code)

    def handle_data(self, data: str) -> None:
        if self._in_h1 or self._in_heading or self._in_p or self._in_pre:
            self._current_text.append(data)

    def _flush_section(self) -> None:
        """Flush the current heading + accumulated content as a section."""
        if not self._content_parts and self._current_heading is None:
            return
        heading = self._current_heading or "Introduction"
        content = "\n".join(self._content_parts)
        self.sections.append({"heading": heading, "content": content})
        self._current_heading = None
        self._content_parts = []

    def close(self) -> None:
        self._flush_section()
        super().close()


def _parse_manual_html(html: str) -> dict[str, Any]:
    """Parse Unity Manual or package doc HTML into structured data."""
    parser = _ManualPageParser()
    parser.feed(html)
    parser.close()
    return {
        "title": parser.title,
        "sections": parser.sections,
        "code_examples": parser.code_examples,
    }


# ---------------------------------------------------------------------------
# get_manual / get_package_doc helpers
# ---------------------------------------------------------------------------

async def _get_manual(slug: str, version: str | None) -> dict[str, Any]:
    """Fetch a Unity Manual page by slug."""
    extracted_version = _extract_version(version)

    if extracted_version:
        url = f"https://docs.unity3d.com/{extracted_version}/Documentation/Manual/{slug}.html"
    else:
        url = f"https://docs.unity3d.com/Manual/{slug}.html"

    try:
        status, body = await _fetch_url(url)

        # Version fallback: try unversioned if versioned 404s
        if status == 404 and extracted_version:
            fallback_url = f"https://docs.unity3d.com/Manual/{slug}.html"
            status, body = await _fetch_url(fallback_url)
            if status == 200:
                url = fallback_url

        if status == 404:
            return {
                "success": True,
                "data": {
                    "found": False,
                    "slug": slug,
                    "suggestion": (
                        "Check the slug matches the Manual page URL path. "
                        "Common slugs: 'execution-order', 'urp/urp-introduction', "
                        "'UIE-USS-Properties-Reference'."
                    ),
                },
            }

        parsed = _parse_manual_html(body)
        return {
            "success": True,
            "data": {
                "found": True,
                "url": url,
                "title": parsed["title"],
                "sections": parsed["sections"],
                "code_examples": parsed["code_examples"],
            },
        }

    except ConnectionError as e:
        return {
            "success": False,
            "message": f"Could not reach docs.unity3d.com: {e}",
        }


async def _get_package_doc(
    package: str,
    page: str,
    pkg_version: str,
) -> dict[str, Any]:
    """Fetch a Unity package documentation page."""
    url = f"https://docs.unity3d.com/Packages/{package}@{pkg_version}/manual/{page}.html"

    try:
        status, body, final_url = await _fetch_url_full(url)

        if status == 404:
            return {
                "success": True,
                "data": {
                    "found": False,
                    "package": package,
                    "page": page,
                    "suggestion": (
                        "Check that the package name, version, and page slug are correct. "
                        "Common pages: 'index', 'installation', 'whats-new'."
                    ),
                },
            }

        parsed = _parse_manual_html(body)
        return {
            "success": True,
            "data": {
                "found": True,
                "url": final_url,
                "package": package,
                "page": page,
                "title": parsed["title"],
                "sections": parsed["sections"],
                "code_examples": parsed["code_examples"],
            },
        }

    except ConnectionError as e:
        return {
            "success": False,
            "message": f"Could not reach docs.unity3d.com: {e}",
        }


# ---------------------------------------------------------------------------
# lookup — parallel search across all doc sources
# ---------------------------------------------------------------------------

# Asset-related keywords that trigger manage_asset search in lookup
_ASSET_KEYWORDS = {
    "shader", "shaders", "material", "materials", "mat",
    "texture", "textures", "tex", "sprite", "sprites",
    "prefab", "prefabs", "mesh", "model", "font", "fonts",
    "lit", "unlit", "urp", "hdrp", "2d", "3d",
}


# Words to skip when building asset search patterns
_ASSET_STOPWORDS = {
    "in", "the", "a", "an", "to", "for", "of", "on", "with", "how", "can", "do",
    "i", "my", "is", "it", "this", "that", "unity", "objects", "object", "using",
    "receive", "make", "apply", "get", "set", "use", "create",
}

# Map keywords to Unity asset filter types
_KEYWORD_TO_FILTER_TYPE = {
    "shader": "Shader", "shaders": "Shader", "lit": "Shader", "unlit": "Shader",
    "material": "Material", "materials": "Material", "mat": "Material",
    "texture": "Texture2D", "textures": "Texture2D", "tex": "Texture2D",
    "sprite": "Sprite", "sprites": "Sprite",
    "prefab": "Prefab", "prefabs": "Prefab",
    "mesh": "Mesh", "model": "Mesh",
    "font": "Font", "fonts": "Font",
}


def _build_asset_search_terms(query: str) -> list[dict[str, str]]:
    """Extract meaningful search terms and infer asset filter types from query."""
    words = query.lower().replace("-", " ").replace("_", " ").split()
    terms = [w for w in words if w not in _ASSET_STOPWORDS and len(w) > 1]

    # Infer filter_type from keywords
    filter_type = None
    for w in words:
        if w in _KEYWORD_TO_FILTER_TYPE:
            filter_type = _KEYWORD_TO_FILTER_TYPE[w]
            break

    # Build search patterns: each non-stopword term as a separate search
    searches = []
    for term in terms:
        if term in _ASSET_KEYWORDS:
            continue  # Skip generic keywords like "shader" — they're too broad
        params: dict[str, str] = {"search_pattern": f"*{term}*"}
        if filter_type:
            params["filter_type"] = filter_type
        searches.append(params)

    # If only keywords remain (e.g., "2D shader"), search by filter type alone
    if not searches and filter_type:
        searches.append({"filter_type": filter_type})

    return searches


async def _search_assets(ctx: Any, query: str) -> dict[str, Any] | None:
    """Search Unity assets if ctx has a Unity connection. Returns None if unavailable."""
    try:
        from services.tools import get_unity_instance_from_context
        from transport.unity_transport import send_with_unity_instance
        from transport.legacy.unity_connection import async_send_command_with_retry

        unity_instance = await get_unity_instance_from_context(ctx)

        search_terms = _build_asset_search_terms(query)
        if not search_terms:
            return None

        # Run all search terms in parallel
        all_assets = []
        seen_paths: set[str] = set()

        async def _do_search(params: dict) -> list[dict]:
            search_params: dict[str, Any] = {"action": "search", "path": "Assets", "pageSize": 10}
            search_params.update(params)
            result = await send_with_unity_instance(
                async_send_command_with_retry, unity_instance, "manage_asset", search_params,
            )
            if isinstance(result, dict) and result.get("success"):
                return result.get("data", {}).get("assets", [])
            return []

        results = await asyncio.gather(*[_do_search(p) for p in search_terms], return_exceptions=True)

        for result in results:
            if isinstance(result, list):
                for a in result:
                    path = a.get("path", "")
                    if path and path not in seen_paths:
                        seen_paths.add(path)
                        all_assets.append(
                            {"name": a.get("name", ""), "path": path, "type": a.get("assetType", "")}
                        )

        if all_assets:
            return {
                "source": "project_assets",
                "found": True,
                "assets": all_assets[:15],  # Cap to avoid huge payloads
            }
    except ImportError:
        pass  # Unity transport not available — skip asset search
    except Exception as e:
        try:
            if hasattr(ctx, 'warning'):
                await ctx.warning(f"Asset search failed: {e}")
        except Exception:
            pass  # ctx might not be usable
    return None


def _should_search_assets(query: str) -> bool:
    """Check if the query likely refers to an asset (shader, material, texture, etc.)."""
    words = set(query.lower().replace("-", " ").replace("_", " ").split())
    return bool(words & _ASSET_KEYWORDS)


async def _lookup_single(
    query: str,
    version: str | None,
    package: str | None,
    pkg_version: str | None,
    ctx: Any = None,
) -> dict[str, Any]:
    """Search all doc sources for a single query."""
    # Split "Physics.Raycast" into class_name + member_name
    class_name = query
    member_name = None
    if "." in query and not query.startswith("com."):
        parts = query.rsplit(".", 1)
        class_name, member_name = parts[0], parts[1]

    # Build tasks
    tasks: list[tuple[str, Any]] = []

    # ScriptReference
    tasks.append(("script_ref", _get_doc(class_name, member_name, version)))

    # Manual — try original case first (e.g., UIE-USS-Properties-Reference),
    # then lowercase fallback if different
    original_slug = query.replace(" ", "-").replace("_", "-")
    tasks.append(("manual", _get_manual(original_slug, version)))
    lowercase_slug = original_slug.lower()
    if lowercase_slug != original_slug:
        tasks.append(("manual_lc", _get_manual(lowercase_slug, version)))

    # Package docs (if package info provided)
    if package and pkg_version:
        tasks.append(("package", _get_package_doc(package, original_slug, pkg_version)))
        if lowercase_slug != original_slug:
            tasks.append(("package_lc", _get_package_doc(package, lowercase_slug, pkg_version)))

    # Run doc tasks in parallel
    labels = [t[0] for t in tasks]
    results = await asyncio.gather(*(t[1] for t in tasks), return_exceptions=True)

    # Collect successful hits and errors
    hits = []
    errors = []
    for label, result in zip(labels, results):
        if isinstance(result, Exception):
            errors.append({"source": label, "error": str(result)})
            continue
        if not isinstance(result, dict):
            continue
        if not result.get("success"):
            errors.append({"source": label, "error": result.get("message", "unknown error")})
            continue
        if result.get("data", {}).get("found"):
            hits.append({"source": label, **result["data"]})

    # Auto-search project assets for asset-related queries
    if ctx and _should_search_assets(query):
        asset_result = await _search_assets(ctx, query)
        if asset_result:
            hits.append(asset_result)
            labels.append("project_assets")

    result: dict[str, Any] = {"query": query, "hits": hits, "sources_checked": labels}
    if errors and not hits:
        result["errors"] = errors
    return result


async def _lookup(
    queries: list[str],
    version: str | None,
    package: str | None,
    pkg_version: str | None,
    ctx: Any = None,
) -> dict[str, Any]:
    """Search ScriptReference, Manual, and package docs in parallel.

    Supports multiple queries — all run concurrently via asyncio.gather.
    For asset-related queries (shader, material, etc.), also searches project assets.
    """
    # Run all queries in parallel
    tasks = [_lookup_single(q, version, package, pkg_version, ctx) for q in queries]
    results = await asyncio.gather(*tasks, return_exceptions=True)

    query_results = []
    for result in results:
        if isinstance(result, Exception):
            continue
        if isinstance(result, dict):
            query_results.append(result)

    all_found = [r for r in query_results if r.get("hits")]
    all_missed = [r for r in query_results if not r.get("hits")]

    return {
        "success": True,
        "data": {
            "found": len(all_found) > 0,
            "queries": [q for q in queries],
            "results": query_results,
            "summary": {
                "total": len(queries),
                "found": len(all_found),
                "missed": len(all_missed),
            },
            "suggestion": (
                "For missed queries, try:\n"
                "- get_doc with exact class name\n"
                "- get_manual with the correct page slug\n"
                "- manage_asset(action='search') for shaders, materials, prefabs"
            ) if all_missed else None,
        },
    }


# ---------------------------------------------------------------------------
# MCP tool
# ---------------------------------------------------------------------------

@mcp_for_unity_tool(
    unity_target="unity_reflect",
    group="docs",
    description=(
        "Fetch official Unity documentation from docs.unity3d.com. "
        "Returns descriptions, parameter details, code examples, and caveats. "
        "Use after unity_reflect confirms a type exists, to get usage patterns, "
        "gotchas, and code examples before writing implementation code.\n\n"
        "Actions:\n"
        "- get_doc: Fetch ScriptReference docs for a class or member. Requires class_name. "
        "Optional member_name, version.\n"
        "- get_manual: Fetch a Unity Manual page. Requires slug (e.g., 'execution-order', "
        "'urp/urp-introduction'). Optional version.\n"
        "- get_package_doc: Fetch package documentation. Requires package, page, pkg_version "
        "(e.g., package='com.unity.render-pipelines.universal', page='2d-index', pkg_version='17.0').\n"
        "- lookup: Search all doc sources in parallel (ScriptReference + Manual + package docs). "
        "Requires query or queries (comma-separated). Supports batch: queries='Physics.Raycast,NavMeshAgent,Light2D' "
        "searches all in one call. Optional package + pkg_version to also search package docs."
    ),
    annotations=ToolAnnotations(
        title="Unity Docs",
        readOnlyHint=True,
        destructiveHint=False,
    ),
)
async def unity_docs(
    ctx: Context,
    action: Annotated[str, "The documentation action to perform."],
    class_name: Annotated[Optional[str], "Unity class name (e.g. 'Physics', 'Transform')."] = None,
    member_name: Annotated[Optional[str], "Method or property name to look up."] = None,
    version: Annotated[Optional[str], "Unity version (e.g. '6000.0.38f1'). Auto-extracted."] = None,
    slug: Annotated[Optional[str], "Manual page slug (e.g., 'execution-order')."] = None,
    package: Annotated[Optional[str], "Package name (e.g., 'com.unity.render-pipelines.universal')."] = None,
    page: Annotated[Optional[str], "Package doc page (e.g., 'index', '2d-index')."] = None,
    pkg_version: Annotated[Optional[str], "Package version major.minor (e.g., '17.0')."] = None,
    query: Annotated[Optional[str], "Single search query for lookup (class name, topic, or slug)."] = None,
    queries: Annotated[Optional[str], "Comma-separated search queries for batch lookup (e.g., 'Physics.Raycast,NavMeshAgent,Light2D')."] = None,
) -> dict[str, Any]:
    action_lower = action.lower()
    if action_lower not in ALL_ACTIONS:
        return {
            "success": False,
            "message": f"Unknown action '{action}'. Valid actions: {', '.join(ALL_ACTIONS)}",
        }

    if action_lower == "get_doc":
        if not class_name:
            return {
                "success": False,
                "message": "get_doc requires class_name.",
            }
        return await _get_doc(class_name, member_name, version)

    if action_lower == "get_manual":
        if not slug:
            return {"success": False, "message": "get_manual requires slug."}
        return await _get_manual(slug, version)

    if action_lower == "get_package_doc":
        if not package or not page or not pkg_version:
            return {
                "success": False,
                "message": "get_package_doc requires package, page, and pkg_version.",
            }
        return await _get_package_doc(package, page, pkg_version)

    if action_lower == "lookup":
        # Accept single query or comma-separated queries string
        if queries:
            query_list = [q.strip() for q in queries.split(",") if q.strip()]
        elif query:
            query_list = [query]
        else:
            return {"success": False, "message": "lookup requires query or queries."}
        return await _lookup(query_list, version, package, pkg_version, ctx)

    return {"success": False, "message": "Unreachable"}


async def _get_doc(
    class_name: str,
    member_name: str | None,
    version: str | None,
) -> dict[str, Any]:
    extracted_version = _extract_version(version)

    url = _build_doc_url(class_name, member_name, extracted_version)

    try:
        status, body = await _fetch_url(url)

        # Member fallback: try property (dash) URL if method (dot) URL 404s
        if status == 404 and member_name:
            prop_url = _build_property_url(class_name, member_name, extracted_version)
            status, body = await _fetch_url(prop_url)
            if status == 200:
                url = prop_url

        # Version fallback: try versionless URL if versioned 404s
        if status == 404 and extracted_version:
            fallback_url = _build_doc_url(class_name, member_name, None)
            status, body = await _fetch_url(fallback_url)
            if status == 200:
                url = fallback_url
            elif member_name:
                # Also try property fallback without version
                prop_fallback = _build_property_url(class_name, member_name, None)
                status, body = await _fetch_url(prop_fallback)
                if status == 200:
                    url = prop_fallback

        if status == 404:
            return {
                "success": True,
                "data": {
                    "found": False,
                    "suggestion": (
                        "Try unity_reflect search action to verify the type name, "
                        "then retry with the correct class_name."
                    ),
                },
            }

        parsed = _parse_unity_doc_html(body)
        return {
            "success": True,
            "data": {
                "found": True,
                "url": url,
                "class": class_name,
                "member": member_name,
                "description": parsed["description"],
                "signatures": parsed["signatures"],
                "parameters": parsed["parameters"],
                "returns": parsed["returns"],
                "examples": parsed["examples"],
                "see_also": parsed["see_also"],
            },
        }

    except ConnectionError as e:
        return {
            "success": False,
            "message": f"Could not reach docs.unity3d.com: {e}",
        }
