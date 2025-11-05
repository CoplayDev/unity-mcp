from .test_helpers import DummyContext
import tools.manage_gameobject as manage_go_mod


def test_manage_gameobject_boolean_and_tag_mapping(monkeypatch):
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


