from __future__ import annotations

import asyncio
from types import SimpleNamespace

from services.tools.manage_vfx import manage_vfx


def test_manage_vfx_accepts_particle_create(monkeypatch) -> None:
    captured: dict[str, object] = {}

    async def fake_send_with_unity_instance(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_vfx.get_unity_instance_from_context",
        lambda _ctx: "unity-instance-1",
    )
    monkeypatch.setattr(
        "services.tools.manage_vfx.send_with_unity_instance",
        fake_send_with_unity_instance,
    )

    result = asyncio.run(
        manage_vfx(
            SimpleNamespace(),
            action="particle_create",
            target="BudGrowth",
            properties={"position": [0, 1, 0]},
        )
    )

    assert result["success"] is True
    assert captured["unity_instance"] == "unity-instance-1"
    assert captured["tool_name"] == "manage_vfx"
    assert captured["params"] == {
        "action": "particle_create",
        "target": "BudGrowth",
        "properties": {"position": [0, 1, 0]},
    }
