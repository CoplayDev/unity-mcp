import inspect

import pytest

from services.registry import get_registered_resources, mcp_for_unity_resource
import services.registry.resource_registry as resource_registry_module


@pytest.fixture(autouse=True)
def restore_resource_registry_state():
    original_registry = list(resource_registry_module._resource_registry)
    try:
        yield
    finally:
        resource_registry_module._resource_registry[:] = original_registry


@pytest.mark.asyncio
async def test_resource_registry_defaults_to_explicit_unity_targeting():
    @mcp_for_unity_resource(uri="mcpforunity://scene/example", name="example_resource")
    async def _example_resource(ctx):
        return ctx

    registered_resources = get_registered_resources()
    resource_info = next(item for item in registered_resources if item["name"] == "example_resource")

    assert resource_info["unity_target"] is True
    assert resource_info["uri"] == "mcpforunity://scene/example{?unity_instance}"

    signature = inspect.signature(_example_resource)
    assert "unity_instance" in signature.parameters
    assert signature.parameters["unity_instance"].default is None

    result = await _example_resource("ctx", unity_instance="Project@abc123")
    assert result == "ctx"


def test_resource_registry_supports_server_only_resources():
    @mcp_for_unity_resource(
        uri="mcpforunity://instances",
        name="instances_resource",
        unity_target=False,
    )
    def _instances_resource():
        return None

    registered_resources = get_registered_resources()
    resource_info = next(item for item in registered_resources if item["name"] == "instances_resource")

    assert resource_info["unity_target"] is False
    assert resource_info["uri"] == "mcpforunity://instances"
    assert "unity_instance" not in inspect.signature(_instances_resource).parameters


def test_resource_registry_merges_existing_query_templates():
    @mcp_for_unity_resource(
        uri="mcpforunity://scene/example{?cursor,page_size}",
        name="templated_resource",
    )
    def _templated_resource():
        return None

    registered_resources = get_registered_resources()
    resource_info = next(item for item in registered_resources if item["name"] == "templated_resource")
    assert resource_info["uri"] == "mcpforunity://scene/example{?cursor,page_size,unity_instance}"
