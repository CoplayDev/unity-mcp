import sys
import pathlib
import importlib.util
import types
import os

ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "MCPForUnity" / "UnityMcpServer~" / "src"
sys.path.insert(0, str(SRC))


def _load_module(path: pathlib.Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Cannot load module {name} from {path}")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# Stub fastmcp to avoid real MCP deps
fastmcp_pkg = types.ModuleType("fastmcp")


class _Dummy:
    pass


fastmcp_pkg.FastMCP = _Dummy
fastmcp_pkg.Context = _Dummy
sys.modules.setdefault("fastmcp", fastmcp_pkg)


from tests.test_helpers import DummyContext


def test_manage_gameobject_boolean_and_tag_mapping(monkeypatch):
    # Import with SRC as CWD to satisfy telemetry import side effects
    _prev = os.getcwd()
    os.chdir(str(SRC))
    try:
        manage_go_mod = _load_module(SRC / "tools" / "manage_gameobject.py", "manage_go_mod")
    finally:
        os.chdir(_prev)

    captured = {}

    def fake_send(cmd, params):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(manage_go_mod, "send_command_with_retry", fake_send)

    # find by tag: allow tag to map to searchTerm
    resp = manage_go_mod.manage_gameobject(
        ctx=DummyContext(),
        action="find",
        search_method="by_tag",
        tag="Player",
        find_all="true",
        search_inactive="0",
    )
    # Loosen equality: wrapper may include a diagnostic message
    assert resp.get("success") is True
    assert "data" in resp
    # ensure tag mapped to searchTerm and booleans passed through; C# side coerces true/false already
    assert captured["params"]["searchTerm"] == "Player"
    assert captured["params"]["findAll"] == "true" or captured["params"]["findAll"] is True
    assert captured["params"]["searchInactive"] in ("0", False, 0)


