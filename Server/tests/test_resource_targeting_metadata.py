"""Regression tests for per-call Unity targeting in resource metadata."""

import services.resources as resources_module


async def _resource(_ctx):
    return {"success": True}


class _RecordingMcp:
    """Capture registrations without importing a live FastMCP server."""

    def __init__(self):
        self.registrations = []

    def resource(self, *, uri, name, description, **kwargs):
        def decorator(func):
            self.registrations.append(
                {
                    "uri": uri,
                    "name": name,
                    "description": description,
                    "kwargs": kwargs,
                    "func": func,
                }
            )
            return func

        return decorator


def test_targetable_resource_is_advertised_in_both_resource_lists(monkeypatch):
    """Concrete and template registrations both describe the routing query."""
    resource_uri = "mcpforunity://editor/state"
    resource_info = {
        "func": _resource,
        "uri": resource_uri,
        "name": "editor_state",
        "description": "Editor state.",
        "unity_targetable": True,
        "kwargs": {},
    }

    monkeypatch.setattr(resources_module, "discover_modules", lambda *_args: [])
    monkeypatch.setattr(
        resources_module,
        "get_registered_resources",
        lambda: [resource_info],
    )

    mcp = _RecordingMcp()
    resources_module.register_all_resources(mcp)

    resource = next(
        item for item in mcp.registrations if item["uri"] == resource_uri
    )
    template = next(
        item
        for item in mcp.registrations
        if "editor/state" in item["uri"] and "{?" in item["uri"]
    )

    assert "unity_instance" in (resource["description"] or "")
    assert template["uri"] == f"{resource_uri}{{?unity_instance}}"
