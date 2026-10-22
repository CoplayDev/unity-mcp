"""Auto-selection of a Unity instance by the client's launch directory.

When more than one editor is connected, the server resolves the directories the
client is working in - via UNITY_MCP_PROJECT_DIR or the file:// MCP roots the
client advertises - and picks the instance whose Unity project shares a path
lineage with one of them. This keeps per-checkout and git-worktree setups
(identical project names, different paths) routing without an explicit
unity_instance, and it is client-agnostic (roots is the protocol-native signal).
"""
import asyncio
import sys
import types
from types import SimpleNamespace

import pytest

from .test_helpers import DummyContext
from core.config import config


def _load_middleware(monkeypatch, *, plugin_hub_configured):
    """Import the middleware against a stubbed PluginHub (mirrors sibling tests)."""
    plugin_hub = types.ModuleType("transport.plugin_hub")

    class PluginHub:
        @classmethod
        def is_configured(cls) -> bool:
            """Report whatever configured state the test requested."""
            return plugin_hub_configured

        @classmethod
        async def get_sessions(cls, *args, **kwargs):
            """Fail loudly unless a test overrides this stub."""
            raise AssertionError("get_sessions should be stubbed by the test")

    plugin_hub.PluginHub = PluginHub
    monkeypatch.setitem(sys.modules, "transport.plugin_hub", plugin_hub)
    monkeypatch.delitem(
        sys.modules, "transport.unity_instance_middleware", raising=False)

    import transport.unity_instance_middleware as mod
    return mod


class _FakeRootsContext:
    """Minimal context exposing client_id and the MCP roots capability."""

    def __init__(self, roots=None, fail=False, delay=0):
        """Create a context with a stable client id and optional roots."""
        self.client_id = "client-roots"
        self._roots = roots or []
        self._fail = fail
        self._delay = delay
        self.list_roots_calls = 0

    async def list_roots(self):
        """Return the configured roots, or raise to mimic a client without roots."""
        self.list_roots_calls += 1
        if self._delay:
            await asyncio.sleep(self._delay)
        if self._fail:
            raise RuntimeError("client does not support roots")
        return self._roots


def _root(uri):
    """Wrap a URI string as a minimal MCP root object."""
    return SimpleNamespace(uri=uri)


# --- _file_uri_to_path: file:// parsing (UNC / localhost / percent-encoding) ---

def test_file_uri_plain_posix(monkeypatch):
    """A plain POSIX file:// URI maps to its path."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    assert mod._file_uri_to_path("file:///work/repo-a") == "/work/repo-a"


def test_file_uri_localhost_host_dropped(monkeypatch):
    """A localhost host in a file:// URI is dropped."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    assert mod._file_uri_to_path("file://localhost/work/x") == "/work/x"


def test_file_uri_unc_keeps_host(monkeypatch):
    """A non-localhost host becomes a UNC path."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    assert mod._file_uri_to_path(
        "file://server/share/proj") == "//server/share/proj"


def test_file_uri_percent_decoded(monkeypatch):
    """Percent-encoded octets in the path are decoded."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    assert mod._file_uri_to_path("file:///work/a%20b") == "/work/a b"


def test_file_uri_non_file_scheme_is_none(monkeypatch):
    """A non-file:// URI yields None."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    assert mod._file_uri_to_path("mcpforunity://path/Assets/x") is None


# --- _select_instance_by_launch_dir: path-lineage matching over many dirs ---

def _select(mod, candidates, launch_dirs):
    """Invoke the static launch-dir matcher under test."""
    return mod.UnityInstanceMiddleware._select_instance_by_launch_dir(
        candidates, launch_dirs)


def test_select_picks_project_nested_under_launch_dir(monkeypatch):
    """A project nested under the launch dir is selected over an unrelated one."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    candidates = [
        ("RepoA@aaaa1111", "/work/repo-a/repo-a-unity/Assets"),
        ("RepoB@bbbb2222", "/work/repo-b/repo-b-unity/Assets"),
    ]
    assert _select(mod, candidates, ["/work/repo-a"]) == "RepoA@aaaa1111"


def test_select_normalizes_http_root_form(monkeypatch):
    """An HTTP-form project root (no /Assets) matches after normalization."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    assert _select(mod, [("RepoA@aaaa1111", "/work/repo-a/repo-a-unity")],
                   ["/work/repo-a"]) == "RepoA@aaaa1111"


def test_select_matches_when_launch_dir_is_inside_project(monkeypatch):
    """A launch dir inside the project (a subfolder) still resolves to it."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    candidates = [
        ("RepoA@aaaa1111", "/work/repo-a/repo-a-unity/Assets"),
        ("RepoB@bbbb2222", "/work/repo-b/repo-b-unity/Assets"),
    ]
    assert _select(mod, candidates,
                   ["/work/repo-a/repo-a-unity/Assets/Scripts"]) == "RepoA@aaaa1111"


def test_select_returns_none_when_multiple_projects_match(monkeypatch):
    """Two projects under one launch dir are ambiguous, so nothing is selected."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    candidates = [
        ("RepoA@aaaa1111", "/work/repo-a/repo-a-unity/Assets"),
        ("RepoADup@cccc3333", "/work/repo-a/secondary-unity/Assets"),
    ]
    assert _select(mod, candidates, ["/work/repo-a"]) is None


def test_select_returns_none_when_no_project_in_lineage(monkeypatch):
    """No project sharing the launch dir's lineage yields None."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    candidates = [
        ("RepoA@aaaa1111", "/work/repo-a/repo-a-unity/Assets"),
        ("RepoB@bbbb2222", "/work/repo-b/repo-b-unity/Assets"),
    ]
    assert _select(mod, candidates, ["/work/repo-z"]) is None


def test_select_returns_none_without_launch_dirs(monkeypatch):
    """With no launch dirs there is no signal, so nothing is selected."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    candidates = [("RepoA@aaaa1111", "/work/repo-a/repo-a-unity/Assets")]
    assert _select(mod, candidates, []) is None


def test_select_multi_root_unique_match(monkeypatch):
    """Across several advertised roots, the single matching editor is chosen."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    candidates = [
        ("RepoA@aaaa1111", "/work/repo-a/repo-a-unity/Assets"),
        ("RepoC@cccc3333", "/work/repo-c/repo-c-unity/Assets"),
    ]
    assert _select(mod, candidates,
                   ["/work/repo-a", "/work/repo-b"]) == "RepoA@aaaa1111"


def test_select_multi_root_matches_non_first_root(monkeypatch):
    """An editor under a non-first root still matches (not just the first root)."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    candidates = [("RepoB@bbbb2222", "/work/repo-b/repo-b-unity/Assets")]
    assert _select(mod, candidates,
                   ["/work/repo-a", "/work/repo-b"]) == "RepoB@bbbb2222"


def test_select_multi_root_ambiguous_returns_none(monkeypatch):
    """Two roots that each match a different editor are ambiguous -> None."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    candidates = [
        ("RepoA@aaaa1111", "/work/repo-a/repo-a-unity/Assets"),
        ("RepoB@bbbb2222", "/work/repo-b/repo-b-unity/Assets"),
    ]
    assert _select(mod, candidates, ["/work/repo-a", "/work/repo-b"]) is None


def test_select_returns_none_when_a_candidate_has_no_path(monkeypatch):
    """An unplaceable instance (no project_path) makes the whole set ambiguous."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    candidates = [
        ("RepoA@aaaa1111", "/work/repo-a/repo-a-unity/Assets"),
        ("Legacy@dddd4444", None),  # older plugin: no project_path reported
    ]
    assert _select(mod, candidates, ["/work/repo-a"]) is None


# --- _resolve_launch_dirs: env override, roots, caching ---

@pytest.mark.asyncio
async def test_resolve_prefers_explicit_env_over_roots(monkeypatch):
    """UNITY_MCP_PROJECT_DIR wins over roots and skips the client round-trip."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    monkeypatch.setenv("UNITY_MCP_PROJECT_DIR", "/work/explicit")
    middleware = mod.UnityInstanceMiddleware()
    ctx = _FakeRootsContext(roots=[_root("file:///work/from-roots")])
    assert await middleware._resolve_launch_dirs(ctx) == ["/work/explicit"]
    assert ctx.list_roots_calls == 0


@pytest.mark.asyncio
async def test_resolve_uses_all_mcp_roots_when_no_env(monkeypatch):
    """Without the env override, all advertised file:// roots are returned."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    monkeypatch.delenv("UNITY_MCP_PROJECT_DIR", raising=False)
    middleware = mod.UnityInstanceMiddleware()
    ctx = _FakeRootsContext(roots=[
        _root("file:///work/repo-a"), _root("file:///work/repo-b")])
    assert await middleware._resolve_launch_dirs(ctx) == [
        "/work/repo-a", "/work/repo-b"]


@pytest.mark.asyncio
async def test_resolve_caches_failed_roots_probe(monkeypatch):
    """A client without roots is probed at most once; the failure is cached."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    monkeypatch.delenv("UNITY_MCP_PROJECT_DIR", raising=False)
    middleware = mod.UnityInstanceMiddleware()
    ctx = _FakeRootsContext(fail=True)
    assert await middleware._resolve_launch_dirs(ctx) == []
    assert await middleware._resolve_launch_dirs(ctx) == []
    assert ctx.list_roots_calls == 1


@pytest.mark.asyncio
async def test_resolve_caches_roots_per_session(monkeypatch):
    """Resolved roots are cached per session, so only one probe is issued."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    monkeypatch.delenv("UNITY_MCP_PROJECT_DIR", raising=False)
    middleware = mod.UnityInstanceMiddleware()
    ctx = _FakeRootsContext(roots=[_root("file:///work/repo-a")])
    first = await middleware._resolve_launch_dirs(ctx)
    second = await middleware._resolve_launch_dirs(ctx)
    assert first == second == ["/work/repo-a"]
    assert ctx.list_roots_calls == 1


@pytest.mark.asyncio
async def test_resolve_roots_probed_once_under_concurrency(monkeypatch):
    """Concurrent first calls share one in-flight probe, not one each."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    monkeypatch.delenv("UNITY_MCP_PROJECT_DIR", raising=False)
    middleware = mod.UnityInstanceMiddleware()
    ctx = _FakeRootsContext(roots=[_root("file:///work/repo-a")], delay=0.02)
    first, second = await asyncio.gather(
        middleware._resolve_launch_dirs(ctx),
        middleware._resolve_launch_dirs(ctx),
    )
    assert first == second == ["/work/repo-a"]
    assert ctx.list_roots_calls == 1


# --- _maybe_autoselect_instance: end-to-end disambiguation ---

def _stub_pool(monkeypatch, instances):
    """Patch the stdio connection pool to return the given instances."""
    class PoolStub:
        def discover_all_instances(self, force_refresh=False):
            """Return the canned instances; force_refresh is asserted by the caller."""
            assert force_refresh is True
            return instances

    unity_connection = types.ModuleType("transport.legacy.unity_connection")
    unity_connection.get_unity_connection_pool = lambda: PoolStub()
    monkeypatch.setitem(
        sys.modules, "transport.legacy.unity_connection", unity_connection)


@pytest.mark.asyncio
async def test_autoselect_disambiguates_via_env(monkeypatch):
    """End-to-end stdio selection driven by UNITY_MCP_PROJECT_DIR."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    monkeypatch.setenv("UNITY_MCP_PROJECT_DIR", "/work/repo-a")
    monkeypatch.setattr(config, "transport_mode", "stdio")
    _stub_pool(monkeypatch, [
        SimpleNamespace(id="RepoA@aaaa1111", path="/work/repo-a/repo-a-unity/Assets"),
        SimpleNamespace(id="RepoB@bbbb2222", path="/work/repo-b/repo-b-unity/Assets"),
    ])

    middleware = mod.UnityInstanceMiddleware()
    ctx = DummyContext()
    ctx.client_id = "client-1"

    selected = await middleware._maybe_autoselect_instance(ctx)
    assert selected == "RepoA@aaaa1111"
    assert await middleware.get_active_instance(ctx) == "RepoA@aaaa1111"


@pytest.mark.asyncio
async def test_autoselect_disambiguates_via_mcp_roots_no_env(monkeypatch):
    """End-to-end stdio selection driven purely by MCP roots, with no env var."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=False)
    monkeypatch.delenv("UNITY_MCP_PROJECT_DIR", raising=False)
    monkeypatch.setattr(config, "transport_mode", "stdio")
    _stub_pool(monkeypatch, [
        SimpleNamespace(id="RepoA@aaaa1111", path="/work/repo-a/repo-a-unity/Assets"),
        SimpleNamespace(id="RepoB@bbbb2222", path="/work/repo-b/repo-b-unity/Assets"),
    ])

    middleware = mod.UnityInstanceMiddleware()
    ctx = DummyContext()
    ctx.client_id = "client-1"

    async def list_roots():
        """Advertise a single root pointing at repo-b's checkout."""
        return [_root("file:///work/repo-b")]
    ctx.list_roots = list_roots

    selected = await middleware._maybe_autoselect_instance(ctx)
    assert selected == "RepoB@bbbb2222"
    assert await middleware.get_active_instance(ctx) == "RepoB@bbbb2222"


@pytest.mark.asyncio
async def test_autoselect_disambiguates_via_pluginhub(monkeypatch):
    """End-to-end PluginHub selection, exercising HTTP project-root normalization."""
    mod = _load_middleware(monkeypatch, plugin_hub_configured=True)
    PluginHub = sys.modules["transport.plugin_hub"].PluginHub
    monkeypatch.setenv("UNITY_MCP_PROJECT_DIR", "/work/repo-b")
    monkeypatch.setattr(config, "transport_mode", "http")

    async def fake_get_sessions():
        """Return two PluginHub sessions reporting project roots (no /Assets)."""
        return SimpleNamespace(
            sessions={
                "s1": SimpleNamespace(
                    project="RepoA", hash="aaaa1111",
                    project_path="/work/repo-a/repo-a-unity"),
                "s2": SimpleNamespace(
                    project="RepoB", hash="bbbb2222",
                    project_path="/work/repo-b/repo-b-unity"),
            }
        )

    monkeypatch.setattr(PluginHub, "get_sessions", fake_get_sessions)

    middleware = mod.UnityInstanceMiddleware()
    ctx = DummyContext()
    ctx.client_id = "client-1"

    selected = await middleware._maybe_autoselect_instance(ctx)
    assert selected == "RepoB@bbbb2222"
    assert await middleware.get_active_instance(ctx) == "RepoB@bbbb2222"


def test_session_details_exposes_project_path(monkeypatch):
    """SessionDetails carries project_path and stays backward compatible."""
    _load_middleware(monkeypatch, plugin_hub_configured=False)
    from transport.models import SessionDetails

    details = SessionDetails(
        project="RepoA", hash="aaaa1111", unity_version="6000.0.0f1",
        connected_at="2026-01-01T00:00:00",
        project_path="/work/repo-a/repo-a-unity/Assets")
    assert details.project_path == "/work/repo-a/repo-a-unity/Assets"
    # Backward compatible: the field is optional and defaults to None.
    legacy = SessionDetails(
        project="RepoA", hash="aaaa1111", unity_version="6000.0.0f1",
        connected_at="2026-01-01T00:00:00")
    assert legacy.project_path is None
