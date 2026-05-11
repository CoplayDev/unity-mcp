from types import SimpleNamespace
from starlette.routing import Route

from main import _attach_bound_mcp_route, _build_bound_mcp_path


def test_build_bound_mcp_path_appends_instance_segment():
    assert _build_bound_mcp_path("/mcp") == "/mcp/instance/{instance:path}"
    assert _build_bound_mcp_path("mcp") == "/mcp/instance/{instance:path}"
    assert _build_bound_mcp_path("/nested/mcp/") == "/nested/mcp/instance/{instance:path}"


def test_attach_bound_mcp_route_registers_alias():
    async def _endpoint(_request):
        return None

    base_route = Route("/mcp", endpoint=_endpoint, methods=["GET", "POST", "DELETE"])
    app = SimpleNamespace(
        state=SimpleNamespace(path="/mcp"),
        router=SimpleNamespace(routes=[base_route]),
    )

    bound_path = _attach_bound_mcp_route(app)
    route_paths = [getattr(route, "path", None) for route in app.router.routes]

    assert bound_path == "/mcp/instance/{instance:path}"
    assert route_paths == ["/mcp", "/mcp/instance/{instance:path}"]
