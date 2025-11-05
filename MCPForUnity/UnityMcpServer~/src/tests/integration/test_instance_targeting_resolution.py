from .test_helpers import DummyContext


def test_manage_gameobject_uses_session_state(monkeypatch):
    """Test that tools use session-stored active instance via middleware"""

    from unity_instance_middleware import UnityInstanceMiddleware, set_unity_instance_middleware

    # Arrange: Initialize middleware and set a session-scoped active instance
    middleware = UnityInstanceMiddleware()
    set_unity_instance_middleware(middleware)

    ctx = DummyContext()
    middleware.set_active_instance(ctx, "SessionProj@AAAA1111")
    assert middleware.get_active_instance(ctx) == "SessionProj@AAAA1111"

    # Simulate middleware injection into request state
    ctx.set_state("unity_instance", "SessionProj@AAAA1111")

    captured = {}

    # Monkeypatch transport to capture the resolved instance_id
    def fake_send(command_type, params, **kwargs):
        captured["command_type"] = command_type
        captured["params"] = params
        captured["instance_id"] = kwargs.get("instance_id")
        return {"success": True, "data": {}}

    import tools.manage_gameobject as mg
    monkeypatch.setattr("tools.manage_gameobject.send_command_with_retry", fake_send)

    # Act: call tool - should use session state from context
    res = mg.manage_gameobject(
        ctx,
        action="create",
        name="SessionSphere",
        primitive_type="Sphere",
    )

    # Assert: uses session-stored instance
    assert res.get("success") is True
    assert captured.get("command_type") == "manage_gameobject"
    assert captured.get("instance_id") == "SessionProj@AAAA1111"


def test_manage_gameobject_without_active_instance(monkeypatch):
    """Test that tools work with no active instance set (uses None/default)"""

    from unity_instance_middleware import UnityInstanceMiddleware, set_unity_instance_middleware

    # Arrange: Initialize middleware with no active instance set
    middleware = UnityInstanceMiddleware()
    set_unity_instance_middleware(middleware)

    ctx = DummyContext()
    assert middleware.get_active_instance(ctx) is None
    # Don't set any state in context

    captured = {}

    def fake_send(command_type, params, **kwargs):
        captured["instance_id"] = kwargs.get("instance_id")
        return {"success": True, "data": {}}

    import tools.manage_gameobject as mg
    monkeypatch.setattr("tools.manage_gameobject.send_command_with_retry", fake_send)

    # Act: call without active instance
    res = mg.manage_gameobject(
        ctx,
        action="create",
        name="DefaultSphere",
        primitive_type="Sphere",
    )

    # Assert: uses None (connection pool will pick default)
    assert res.get("success") is True
    assert captured.get("instance_id") is None


