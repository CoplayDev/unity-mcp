import sys
import pathlib
import importlib.util
import types
import asyncio
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


def test_manage_asset_pagination_coercion(monkeypatch):
    # Import with SRC as CWD to satisfy telemetry import side effects
    _prev = os.getcwd()
    os.chdir(str(SRC))
    try:
        manage_asset_mod = _load_module(SRC / "tools" / "manage_asset.py", "manage_asset_mod")
    finally:
        os.chdir(_prev)

    captured = {}

    async def fake_async_send(cmd, params, loop=None):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(manage_asset_mod, "async_send_command_with_retry", fake_async_send)

    result = asyncio.run(
        manage_asset_mod.manage_asset(
            ctx=DummyContext(),
            action="search",
            path="Assets",
            page_size="50",
            page_number="2",
        )
    )

    assert result == {"success": True, "data": {}}
    assert captured["params"]["pageSize"] == 50
    assert captured["params"]["pageNumber"] == 2






