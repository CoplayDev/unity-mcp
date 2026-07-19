from types import SimpleNamespace

import pytest

from core.config import config
from transport.unity_instance_middleware import (
    InstanceTargetError,
    resolve_instance_identifier,
)
from transport.unity_transport import send_with_unity_instance


def _instances():
    return [
        SimpleNamespace(
            id="Project@aaa111",
            project="Project",
            name="Project",
            hash="aaa111",
            port=6400,
        ),
        SimpleNamespace(
            id="Project@bbb222",
            project="Project",
            name="Project",
            hash="bbb222",
            port=6401,
        ),
        SimpleNamespace(
            id="Numeric@12345678",
            project="Numeric",
            name="Numeric",
            hash="12345678",
            port=6402,
        ),
    ]


@pytest.mark.parametrize(
    ("token", "expected_id"),
    [
        ("Project@bbb222", "Project@bbb222"),
        ("bbb2", "Project@bbb222"),
        ("123", "Numeric@12345678"),
        ("12345678", "Numeric@12345678"),
    ],
)
def test_http_target_resolution_returns_canonical_id(token, expected_id):
    assert resolve_instance_identifier(
        token,
        _instances(),
        transport_mode="http",
    ) == expected_id


def test_http_target_resolution_rejects_duplicate_project_name():
    with pytest.raises(InstanceTargetError, match="Project name 'Project'.*multiple"):
        resolve_instance_identifier("Project", _instances(), transport_mode="http")


def test_http_target_resolution_rejects_wrong_hash_without_name_fallback():
    with pytest.raises(InstanceTargetError, match="not found"):
        resolve_instance_identifier("Project@wrong", _instances(), transport_mode="http")


def test_http_target_resolution_rejects_ambiguous_hash_prefix():
    with pytest.raises(InstanceTargetError, match="ambiguous"):
        resolve_instance_identifier(
            "b",
            [
                SimpleNamespace(id="A@bbb111", project="A", hash="bbb111"),
                SimpleNamespace(id="B@bbb222", project="B", hash="bbb222"),
            ],
            transport_mode="http",
        )


def test_http_target_resolution_rejects_port_targeting():
    with pytest.raises(InstanceTargetError, match="not supported in HTTP transport mode"):
        resolve_instance_identifier("6401", _instances(), transport_mode="http")


def test_http_numeric_hash_precedes_port_rejection():
    instance = SimpleNamespace(
        id="Numeric@1234",
        project="Numeric",
        name="Numeric",
        hash="1234",
        port=1234,
    )

    assert resolve_instance_identifier(
        "1234", [instance], transport_mode="http") == "Numeric@1234"


@pytest.mark.asyncio
async def test_stdio_transport_keeps_routing_out_of_command_params(monkeypatch):
    monkeypatch.setattr(config, "transport_mode", "stdio")
    captured = {}

    async def fake_send(command_type, params, **kwargs):
        captured.update(command_type=command_type, params=params, kwargs=kwargs)
        return {"success": True}

    await send_with_unity_instance(
        fake_send,
        "Project@bbb222",
        "manage_scene",
        {"action": "get_active"},
    )

    assert captured == {
        "command_type": "manage_scene",
        "params": {"action": "get_active"},
        "kwargs": {"instance_id": "Project@bbb222"},
    }
