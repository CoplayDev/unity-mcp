"""A custom tool that re-registers with a CHANGED SCHEMA must be replaced, not ignored.

Unity re-registers its custom tools on every domain reload, so a tool's signature is re-declared constantly.
The registry used to keep whatever definition arrived first and log "keeping existing definition" for the
rest, which froze the signature at its earliest — and least correct — version: you add a tool, call it,
discover the arguments are wrong, fix them, and the server goes on rejecting the arguments the tool now
declares. Only a server restart cleared it, and nothing in the symptom pointed there.
"""

import pytest
from unittest.mock import Mock

from models.models import ToolDefinitionModel, ToolParameterModel
from services.custom_tool_service import CustomToolService


class _RecordingMcp:
    """Minimal FastMCP stand-in that records which tools were added and removed."""

    def __init__(self, supports_removal: bool = True):
        self.added: list[str] = []
        self.removed: list[str] = []
        if supports_removal:
            self.remove_tool = self._remove_tool

    def custom_route(self, _path, methods=None):  # noqa: ARG002
        def _decorator(fn):
            return fn

        return _decorator

    def tool(self, name=None, description=None):  # noqa: ARG002
        self.added.append(name)

        def _decorator(fn):
            return fn

        return _decorator

    def _remove_tool(self, name):
        self.removed.append(name)


def _definition(params: list[str]) -> ToolDefinitionModel:
    return ToolDefinitionModel(
        name="my_tool",
        description="a custom tool",
        parameters=[ToolParameterModel(name=p, description=p) for p in params],
    )


def test_changed_schema_replaces_the_previous_registration():
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)

    service._register_global_tool(_definition([]))            # first sight: no parameters
    service._register_global_tool(_definition(["name"]))      # author adds one

    assert mcp.removed == ["my_tool"], "the stale signature must be removed from FastMCP"
    assert mcp.added == ["my_tool", "my_tool"], "the new signature must be registered"
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["name"]


def test_identical_schema_is_not_re_registered():
    """Re-registering the SAME definition must stay a no-op — a domain reload happens constantly and must
    not churn the tool registry (or its telemetry wrappers)."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)

    service._register_global_tool(_definition(["name"]))
    service._register_global_tool(_definition(["name"]))

    assert mcp.removed == []
    assert mcp.added == ["my_tool"]


def test_missing_removal_api_keeps_the_old_tool_rather_than_losing_it():
    """On a FastMCP with no removal API, the old signature survives — degraded but serving. Dropping it from
    the registry while FastMCP still routed to it would be worse than a stale schema."""
    mcp = _RecordingMcp(supports_removal=False)
    service = CustomToolService(mcp, project_scoped_tools=False)

    assert service._register_global_tool(_definition([]))
    assert service._register_global_tool(_definition(["name"]))

    # The new schema is not registered on top of the tool FastMCP still routes: the first one keeps serving.
    assert mcp.added == ["my_tool"]
    assert service._global_tools["my_tool"].model_dump() == _definition([]).model_dump()


def test_a_tool_fastmcp_refuses_is_reported_as_skipped(caplog):
    """Only tools FastMCP accepted count as exposed in the summary."""
    mcp = _RecordingMcp()

    def refusing_tool(name=None, description=None):  # noqa: ARG001
        raise ValueError("conflict")

    mcp.tool = refusing_tool
    service = CustomToolService(mcp, project_scoped_tools=False)
    service._get_builtin_tool_names = lambda: set()

    with caplog.at_level("INFO"):
        service.register_global_tools([_definition(["name"])])

    assert "my_tool" not in service._global_tools
    summary = [r.getMessage() for r in caplog.records if "Custom tools exposed globally" in r.getMessage()]
    assert summary and summary[-1].startswith("Custom tools exposed globally: 0 (none)")
    assert "skipped 1: my_tool" in summary[-1]


def test_optional_parameter_declared_first_does_not_break_registration():
    """A tool may declare its optional parameters before its required ones.

    Python forbids a non-default argument after a defaulted one, and the incoming order is whatever the
    plugin declared — C# reflection order in the Unity package, which is not guaranteed. Building the
    signature in that order raised ValueError, which escaped register_global_tools and took every OTHER
    custom tool in the batch with it: a real plugin registered 40 tools and none of them appeared.
    """
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)

    definition = ToolDefinitionModel(
        name="awkward_tool",
        description="optional first, required second",
        parameters=[
            ToolParameterModel(name="action", description="optional", required=False, default_value="audit"),
            ToolParameterModel(name="name", description="required", required=True),
        ],
    )
    service._register_global_tool(definition)

    assert mcp.added == ["awkward_tool"]
    sig = service._build_signature(definition)
    names = [p for p in sig.parameters if p != "ctx"]
    assert names == ["name", "action"], "required parameters must be ordered first"


def test_one_bad_tool_does_not_block_the_rest_of_the_batch():
    """Registration is per-tool. A tool that cannot be registered is skipped and named, not fatal."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)

    exploding = ToolDefinitionModel(name="bad_tool", description="boom")
    good = ToolDefinitionModel(name="good_tool", description="fine")

    original = service._build_global_tool_handler

    def _sometimes_explode(definition):
        if definition.name == "bad_tool":
            raise ValueError("simulated registration failure")
        return original(definition)

    service._build_global_tool_handler = _sometimes_explode
    service.register_global_tools([exploding, good])

    assert mcp.added == ["good_tool"], "a failing tool must not stop the ones after it"
    assert "good_tool" in service._global_tools
    assert "bad_tool" not in service._global_tools


def test_wrapped_handler_keeps_its_declared_parameters():
    """The object handed to FastMCP must carry the signature ITSELF, not via ``__wrapped__``.

    ``_build_global_tool_handler`` stamps a synthetic signature, then two ``functools.wraps`` decorators wrap
    it. ``inspect.signature`` follows ``__wrapped__`` and reports the right thing, so this looked correct from
    every angle — but FastMCP builds its schema with pydantic's TypeAdapter, which reads the WRAPPER's own
    (empty) ``__annotations__`` and raises ``KeyError`` on the first parameter name. Every custom tool with
    parameters was silently dropped.
    """
    import inspect as _inspect
    from services.custom_tool_service import CustomToolService as _S

    mcp = _RecordingMcp()
    service = _S(mcp, project_scoped_tools=False)
    definition = _definition(["name", "frame"])

    captured = {}
    real_tool = mcp.tool

    def _capture(name=None, description=None):
        def _decorator(fn):
            captured["fn"] = fn
            return real_tool(name=name, description=description)(fn)
        return _decorator

    mcp.tool = _capture
    service._register_global_tool(definition)

    fn = captured["fn"]
    assert not hasattr(fn, "__wrapped__"), "__wrapped__ must not point at a differently-shaped function"
    assert sorted(k for k in fn.__annotations__ if k != "return") == ["ctx", "frame", "name"]
    assert [p for p in _inspect.signature(fn).parameters if p != "ctx"] == ["name", "frame"]


def test_a_refused_replacement_restores_the_previous_tool():
    """When FastMCP refuses the new schema after the old tool was removed, the old one is put back."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    assert service._register_global_tool(_definition([]))

    real_tool = mcp.tool
    refuse = {"name"}

    def picky_tool(name=None, description=None):
        if refuse:
            refuse.clear()
            raise ValueError("schema rejected")
        return real_tool(name=name, description=description)

    mcp.tool = picky_tool

    assert service._register_global_tool(_definition(["name"]))
    assert mcp.removed == ["my_tool"]
    assert mcp.added == ["my_tool", "my_tool"]  # first registration, then the restored one
    assert service._global_tools["my_tool"].model_dump() == _definition([]).model_dump()


async def _register_tools_through_the_hub(monkeypatch, sessions: list[dict], from_session: str):
    """Drive PluginHub._handle_register_tools over a real PluginRegistry; return the call order and the
    owner arguments the service received."""
    import asyncio

    from transport.models import RegisterToolsMessage
    from transport.plugin_hub import PluginHub
    from transport.plugin_registry import PluginRegistry

    registry = PluginRegistry()
    for s in sessions:
        await registry.register(s["session_id"], "Playground", s["project_hash"], "6000.0", user_id=s.get("user_id"))

    order: list[str] = []
    websocket = object()
    monkeypatch.setattr(PluginHub, "_registry", registry)
    monkeypatch.setattr(PluginHub, "_lock", asyncio.Lock())
    monkeypatch.setattr(PluginHub, "_connections", {from_session: websocket})
    monkeypatch.setattr(PluginHub, "_sync_server_tool_visibility", classmethod(lambda cls, tools: None))

    async def notify(cls):
        order.append("notify")

    monkeypatch.setattr(PluginHub, "_notify_mcp_tool_list_changed", classmethod(notify))
    seen: dict = {}

    def register(tools, owner=None, live_owners=None):
        order.append("register")
        seen.update(owner=owner, live_owners=live_owners)

    service = Mock()
    service.register_global_tools = register
    monkeypatch.setattr(CustomToolService, "get_instance", classmethod(lambda cls: service))

    hub = PluginHub.__new__(PluginHub)
    await hub._handle_register_tools(websocket, RegisterToolsMessage(type="register_tools", tools=[_definition(["name"])]))
    return order, seen


@pytest.mark.asyncio
async def test_clients_are_notified_after_custom_tools_are_registered(monkeypatch):
    """The tool-list notification follows registration, so a client re-fetch sees the new signatures."""
    order, seen = await _register_tools_through_the_hub(
        monkeypatch,
        [{"session_id": "session-1", "project_hash": "aaaa1111"},
         {"session_id": "session-2", "project_hash": "bbbb2222"}],
        "session-1",
    )

    assert order == ["register", "notify"]
    assert seen == {"owner": "aaaa1111", "live_owners": {"aaaa1111", "bbbb2222"}}


@pytest.mark.asyncio
async def test_remote_hosted_registration_still_exposes_tools(monkeypatch):
    """In remote-hosted mode list_sessions refuses a call without a user; the hub must still register the
    tools, judging ownership across every user, since global tool names are shared by all of them."""
    from core.config import config

    monkeypatch.setattr(config, "http_remote_hosted", True)
    order, seen = await _register_tools_through_the_hub(
        monkeypatch,
        [{"session_id": "session-1", "project_hash": "aaaa1111", "user_id": "user-a"},
         {"session_id": "session-2", "project_hash": "aaaa1111", "user_id": "user-b"}],
        "session-1",
    )

    assert order == ["register", "notify"]
    assert seen == {"owner": "user-a/aaaa1111", "live_owners": {"user-a/aaaa1111", "user-b/aaaa1111"}}

def test_another_connected_projects_schema_is_kept():
    """FastMCP serves one schema per tool name and validates calls against it before routing them to a project.
    A second connected project declaring the same tool differently must not take it from the first: the first
    project's calls would fail validation, and two editors reloading in turn would swap it back and forth."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    live = {"aaaa1111", "bbbb2222"}

    assert service._register_global_tool(_definition(["name"]), owner="aaaa1111", live_owners=live)
    assert service._register_global_tool(_definition(["path"]), owner="bbbb2222", live_owners=live)

    assert mcp.removed == []
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["name"]


def test_the_owning_project_still_replaces_its_schema_beside_another_project():
    """A project sharing an identical definition becomes a co-owner; the domain reload of either one that changes
    the schema is then refused while the other is connected, and allowed once it is gone."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)

    service._register_global_tool(_definition(["name"]), owner="aaaa1111", live_owners={"aaaa1111"})
    service._register_global_tool(_definition(["name"]), owner="bbbb2222", live_owners={"aaaa1111", "bbbb2222"})
    service._register_global_tool(_definition(["path"]), owner="aaaa1111", live_owners={"aaaa1111", "bbbb2222"})
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["name"]

    service._register_global_tool(_definition(["path"]), owner="aaaa1111", live_owners={"aaaa1111"})
    assert mcp.removed == ["my_tool"]
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["path"]


def test_a_disconnected_projects_schema_gives_way():
    """An owner that is no longer connected does not hold the name: the next project's schema replaces it."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)

    service._register_global_tool(_definition(["name"]), owner="aaaa1111", live_owners={"aaaa1111"})
    service._register_global_tool(_definition(["path"]), owner="bbbb2222", live_owners={"bbbb2222"})

    assert mcp.removed == ["my_tool"]
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["path"]
    assert service._served_owners("my_tool") == {"bbbb2222"}


def test_user_ids_differing_only_in_case_are_different_owners():
    """User ids are opaque and case-sensitive: user-A's project must not pass for user-a's. The project hash
    itself ignores case."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    live = {"user-a/aaaa1111", "user-A/aaaa1111"}

    service._register_global_tool(_definition(["name"]), owner="user-a/aaaa1111", live_owners=live)
    service._register_global_tool(_definition(["path"]), owner="user-A/aaaa1111", live_owners=live)
    assert mcp.removed == []
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["name"]

    service._register_global_tool(_definition(["path"]), owner="user-a/AAAA1111", live_owners={"user-a/aaaa1111"})
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["path"]


def test_a_tool_a_project_stops_declaring_is_no_longer_its_own():
    """A project's tool list is complete: dropping a tool releases it, so another connected project's changed
    schema is not refused on behalf of a project that no longer declares it."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    service._get_builtin_tool_names = lambda: set()
    live = {"aaaa1111", "bbbb2222"}

    service.register_global_tools([_definition(["name"])], owner="aaaa1111", live_owners=live)
    service.register_global_tools([], owner="aaaa1111", live_owners=live)  # reloads without the tool
    service.register_global_tools([_definition(["path"])], owner="bbbb2222", live_owners=live)

    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["path"]
    assert service._served_owners("my_tool") == {"bbbb2222"}


def test_a_skipped_tool_is_logged_with_its_traceback(caplog):
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    service._get_builtin_tool_names = lambda: set()

    def broken(*_args, **_kwargs):
        raise RuntimeError("bad schema")

    service._register_global_tool = broken
    with caplog.at_level("WARNING"):
        service.register_global_tools([_definition(["name"])])

    skipped = [r for r in caplog.records if "could not be registered" in r.getMessage()]
    assert skipped and skipped[0].exc_info is not None


def test_projects_sharing_a_tool_can_both_move_to_a_new_schema():
    """Two connected projects serve the same definition and both update it. The first update is refused while
    the other still serves the old schema, but the first project no longer holds it: the second update replaces
    it, instead of each project keeping the old schema on the other's behalf."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    live = {"aaaa1111", "bbbb2222"}

    service._register_global_tool(_definition(["name"]), owner="aaaa1111", live_owners=live)
    service._register_global_tool(_definition(["name"]), owner="bbbb2222", live_owners=live)
    service._register_global_tool(_definition(["path"]), owner="aaaa1111", live_owners=live)
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["name"]
    service._register_global_tool(_definition(["path"]), owner="bbbb2222", live_owners=live)

    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["path"]
    service._register_global_tool(_definition(["path"]), owner="aaaa1111", live_owners=live)
    assert service._served_owners("my_tool") == {"aaaa1111", "bbbb2222"}


class _RouteMcp(_RecordingMcp):
    def __init__(self):
        super().__init__()
        self.routes: dict = {}

    def custom_route(self, path, methods=None):  # noqa: ARG002
        def _decorator(fn):
            self.routes[path] = fn
            return fn

        return _decorator


class _JsonRequest:
    def __init__(self, body):
        self._body = body

    async def json(self):
        return self._body


@pytest.mark.asyncio
async def test_a_hashless_registration_is_refused_while_the_project_owns_global_tools():
    """Without its hash a registration cannot say which instance of the project it speaks for, so it can neither
    release that instance's dropped tools nor leave the others' alone: it is refused until it sends the hash."""
    import json

    mcp = _RouteMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    route = mcp.routes["/register-tools"]
    tool = _definition(["name"]).model_dump()

    first = await route(_JsonRequest({"project_id": "proj", "tools": [tool]}))
    assert first.status_code == 200, "a project owning nothing still registers without a hash"

    await route(_JsonRequest({"project_id": "proj", "project_hash": "aaaa1111", "tools": [tool]}))
    refused = await route(_JsonRequest({"project_id": "proj", "tools": []}))
    assert refused.status_code == 409
    assert json.loads(refused.body)["success"] is False
    assert service._served_owners("my_tool") == {"<register-tools>/aaaa1111"}, "a refused request registers nothing"

    released = await route(_JsonRequest({"project_id": "proj", "project_hash": "aaaa1111", "tools": []}))
    assert released.status_code == 200
    assert service._served_owners("my_tool") == set()


@pytest.mark.asyncio
async def test_project_scoped_mode_keeps_accepting_hashless_registrations():
    mcp = _RouteMcp()
    CustomToolService(mcp, project_scoped_tools=True)
    route = mcp.routes["/register-tools"]
    tool = _definition(["name"]).model_dump()

    await route(_JsonRequest({"project_id": "proj", "project_hash": "aaaa1111", "tools": [tool]}))
    response = await route(_JsonRequest({"project_id": "proj", "tools": [tool]}))
    assert response.status_code == 200


def test_a_project_whose_schema_is_adopted_owns_it_without_registering_again():
    """A moves to a new schema first and is kept out while B serves the old one; B then moves to the same schema.
    A owns the replacement at once: after B leaves, a third project cannot take the tool from A."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    live = {"aaaa1111", "bbbb2222"}

    service._register_global_tool(_definition(["name"]), owner="aaaa1111", live_owners=live)
    service._register_global_tool(_definition(["name"]), owner="bbbb2222", live_owners=live)
    service._register_global_tool(_definition(["path"]), owner="aaaa1111", live_owners=live)
    service._register_global_tool(_definition(["path"]), owner="bbbb2222", live_owners=live)
    assert service._served_owners("my_tool") == {"aaaa1111", "bbbb2222"}

    service._register_global_tool(_definition(["other"]), owner="cccc3333", live_owners={"aaaa1111", "cccc3333"})
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["path"]


@pytest.mark.asyncio
async def test_a_hashless_registration_is_refused_while_its_hub_project_owns_global_tools():
    """A project registered through PluginHub is known by its hash, which is its project id: a hash-less
    /register-tools request naming it is refused too, not passed through without releasing anything."""
    mcp = _RouteMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    service._get_builtin_tool_names = lambda: set()
    route = mcp.routes["/register-tools"]

    service.register_global_tools([_definition(["name"])], owner="user-a/AAAA1111", live_owners={"user-a/AAAA1111"})
    refused = await route(_JsonRequest({"project_id": "aaaa1111", "tools": []}))
    assert refused.status_code == 409
    other = await route(_JsonRequest({"project_id": "bbbb2222", "tools": []}))
    assert other.status_code == 200, "another project owning nothing is not refused"


def _hub_with_sessions(monkeypatch, owner_keys: set[str]):
    """Configure PluginHub with a registry whose connected sessions have these owner keys."""
    import asyncio

    from transport.plugin_hub import PluginHub

    class _Registry:
        async def owner_keys(self):
            return set(owner_keys)

    monkeypatch.setattr(PluginHub, "_registry", _Registry())
    monkeypatch.setattr(PluginHub, "_lock", asyncio.Lock())


@pytest.mark.asyncio
async def test_a_disconnected_hub_projects_schema_gives_way_to_a_route_registration(monkeypatch):
    """A hub project that has disconnected does not hold its schema against a /register-tools registration."""
    mcp = _RouteMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    service._get_builtin_tool_names = lambda: set()
    route = mcp.routes["/register-tools"]

    service.register_global_tools([_definition(["name"])], owner="aaaa1111", live_owners={"aaaa1111"})
    _hub_with_sessions(monkeypatch, set())  # aaaa1111 has disconnected
    response = await route(_JsonRequest(
        {"project_id": "proj-b", "project_hash": "bbbb2222", "tools": [_definition(["path"]).model_dump()]}))

    assert response.status_code == 200
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["path"]


@pytest.mark.asyncio
async def test_a_connected_hub_projects_schema_is_kept_against_a_route_registration(monkeypatch):
    mcp = _RouteMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    service._get_builtin_tool_names = lambda: set()
    route = mcp.routes["/register-tools"]

    service.register_global_tools([_definition(["name"])], owner="aaaa1111", live_owners={"aaaa1111"})
    _hub_with_sessions(monkeypatch, {"aaaa1111"})
    await route(_JsonRequest(
        {"project_id": "proj-b", "project_hash": "bbbb2222", "tools": [_definition(["path"]).model_dump()]}))

    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["name"]


@pytest.mark.asyncio
async def test_a_hub_registration_keeps_route_registered_projects(monkeypatch):
    """/register-tools has no connection whose end could be seen, so its projects count as connected: a hub
    registration neither drops their declarations nor replaces the schema they serve."""
    mcp = _RouteMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    service._get_builtin_tool_names = lambda: set()
    route = mcp.routes["/register-tools"]

    _hub_with_sessions(monkeypatch, set())
    await route(_JsonRequest(
        {"project_id": "proj-b", "project_hash": "bbbb2222", "tools": [_definition(["name"]).model_dump()]}))
    service.register_global_tools([_definition(["path"])], owner="aaaa1111", live_owners={"aaaa1111"})

    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["name"]
    assert service._served_owners("my_tool") == {"<register-tools>/bbbb2222"}


@pytest.mark.asyncio
async def test_a_route_registration_does_not_keep_the_same_hashs_hub_session_alive(monkeypatch):
    """A hash registered over /register-tools (with no tools) and through PluginHub: the route's registration
    always counts as connected, but must not keep the hub session's declaration alive once it disconnects."""
    mcp = _RouteMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    service._get_builtin_tool_names = lambda: set()
    route = mcp.routes["/register-tools"]

    _hub_with_sessions(monkeypatch, {"aaaa1111"})
    await route(_JsonRequest({"project_id": "proj-a", "project_hash": "aaaa1111", "tools": []}))
    service.register_global_tools([_definition(["name"])], owner="aaaa1111", live_owners={"aaaa1111"})

    # The hub session disconnects; another project changes the tool's schema.
    service.register_global_tools([_definition(["path"])], owner="bbbb2222", live_owners={"bbbb2222"})

    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["path"]


@pytest.mark.asyncio
async def test_register_tools_notifies_clients_only_when_the_served_tools_change(monkeypatch):
    from transport.plugin_hub import PluginHub

    mcp = _RouteMcp()
    CustomToolService(mcp, project_scoped_tools=False)
    route = mcp.routes["/register-tools"]
    notified: list[bool] = []

    async def record():
        notified.append(True)

    monkeypatch.setattr(PluginHub, "_notify_mcp_tool_list_changed", record)
    body = {"project_id": "proj", "project_hash": "aaaa1111", "tools": [_definition(["name"]).model_dump()]}

    await route(_JsonRequest(body))
    assert notified == [True], "a newly served tool is announced"
    await route(_JsonRequest(body))
    assert notified == [True], "an identical re-registration changes nothing and is not announced"
    await route(_JsonRequest({**body, "tools": [_definition(["path"]).model_dump()]}))
    assert notified == [True, True], "a changed schema is announced"


@pytest.mark.asyncio
async def test_a_stalled_client_does_not_hold_up_the_tool_list_notification(monkeypatch):
    import asyncio
    import time

    import transport.plugin_hub as plugin_hub
    from transport.plugin_hub import PluginHub

    class _Session:
        def __init__(self, stall: bool):
            self.stall = stall
            self.notified = False

        async def send_tool_list_changed(self):
            if self.stall:
                await asyncio.sleep(3600)
            self.notified = True

    stalled, healthy = _Session(stall=True), _Session(stall=False)
    sessions = plugin_hub.weakref.WeakSet([stalled, healthy])
    monkeypatch.setattr(plugin_hub, "_active_mcp_sessions", sessions)
    monkeypatch.setattr(plugin_hub, "_TOOL_LIST_NOTIFY_TIMEOUT_S", 0.1, raising=False)

    started = time.monotonic()
    await asyncio.wait_for(PluginHub._notify_mcp_tool_list_changed(), timeout=2.0)

    assert healthy.notified
    assert time.monotonic() - started < 1.0


@pytest.mark.asyncio
async def test_one_broken_tool_does_not_cost_the_rest_of_a_route_registration(monkeypatch):
    import json

    from transport.plugin_hub import PluginHub

    mcp = _RouteMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    route = mcp.routes["/register-tools"]
    notified: list[bool] = []

    async def record():
        notified.append(True)

    monkeypatch.setattr(PluginHub, "_notify_mcp_tool_list_changed", record)
    build_signature = service._build_signature

    def failing_signature(definition):
        if definition.name == "broken_tool":
            raise RuntimeError("cannot build signature")
        return build_signature(definition)

    monkeypatch.setattr(service, "_build_signature", failing_signature)
    broken = ToolDefinitionModel(name="broken_tool", description="broken")
    after = ToolDefinitionModel(name="later_tool", description="registered after the broken one")
    body = {"project_id": "proj", "project_hash": "aaaa1111",
            "tools": [_definition(["name"]).model_dump(), broken.model_dump(), after.model_dump()]}

    response = await route(_JsonRequest(body))

    assert response.status_code == 200
    assert json.loads(response.body)["registered"] == ["my_tool", "later_tool"], "the skipped tool is not reported"
    assert "my_tool" in service._global_tools
    assert "later_tool" in service._global_tools, "a broken tool must not cost the rest of the batch"
    assert "broken_tool" not in service._global_tools
    assert notified == [True], "the tools served around the failure are announced"


def test_a_changed_definition_that_cannot_be_built_leaves_the_working_tool():
    """The replacement is prepared before the previous tool is removed: a parameter named ctx, which the
    handler's own ctx collides with, fails the replacement and keeps the working tool served."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)

    assert service._register_global_tool(_definition(["name"]))
    with pytest.raises(ValueError, match="duplicate parameter name: 'ctx'"):
        service._register_global_tool(_definition(["ctx"]))

    assert mcp.removed == []
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["name"]


@pytest.mark.asyncio
async def test_a_route_registration_reports_only_the_tools_fastmcp_serves():
    """A tool FastMCP refuses (and that has no previous definition to fall back on) is not reported as registered;
    in project-scoped mode, where no global tool is registered, every tool still counts."""
    import json

    mcp = _RouteMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    refused = mcp.tool

    def refusing(name=None, description=None):
        if name == "refused_tool":
            raise ValueError("conflict")
        return refused(name=name, description=description)

    mcp.tool = refusing
    route = mcp.routes["/register-tools"]
    tools = [_definition(["name"]).model_dump(),
             ToolDefinitionModel(name="refused_tool", description="refused").model_dump()]

    response = await route(_JsonRequest({"project_id": "proj", "project_hash": "aaaa1111", "tools": tools}))
    assert json.loads(response.body)["registered"] == ["my_tool"]

    scoped = _RouteMcp()
    CustomToolService(scoped, project_scoped_tools=True)
    response = await scoped.routes["/register-tools"](
        _JsonRequest({"project_id": "proj", "project_hash": "aaaa1111", "tools": tools}))
    assert json.loads(response.body)["registered"] == ["my_tool", "refused_tool"]


def test_a_replacement_that_cannot_be_built_keeps_the_owners_declaration():
    """A failed replacement is not recorded: the project still owns the schema that is served, and another
    connected project cannot take the tool from it."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    live = {"aaaa1111", "bbbb2222"}

    service._register_global_tool(_definition(["name"]), owner="aaaa1111", live_owners=live)
    with pytest.raises(ValueError, match="duplicate parameter name: 'ctx'"):
        service._register_global_tool(_definition(["ctx"]), owner="aaaa1111", live_owners=live)
    assert service._served_owners("my_tool") == {"aaaa1111"}

    service._register_global_tool(_definition(["path"]), owner="bbbb2222", live_owners=live)
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["name"]


def test_a_required_parameter_that_cannot_be_exposed_refuses_the_tool():
    """FastMCP validates calls against the signature, which can only hold identifiers: a required 'foo-bar' could
    never be passed, so the tool is refused with the name; an optional one is left out, as before."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    required = ToolDefinitionModel(
        name="dashed_tool", description="d",
        parameters=[ToolParameterModel(name="foo-bar", description="x", required=True)])

    with pytest.raises(ValueError, match="required parameter\\(s\\) that are not valid identifiers: foo-bar"):
        service._register_global_tool(required)
    assert "dashed_tool" not in service._global_tools

    optional = ToolDefinitionModel(
        name="dashed_tool", description="d",
        parameters=[ToolParameterModel(name="name", description="n"),
                    ToolParameterModel(name="foo-bar", description="x", required=False)])
    assert service._register_global_tool(optional)
    assert "dashed_tool" in service._global_tools


def test_a_kept_out_schema_that_cannot_be_built_keeps_the_owners_declaration():
    """A and B serve the same schema; A's changed one, kept out while B serves the old, cannot be built (ctx).
    It is not recorded: A still owns the served schema, so B cannot then replace it under A."""
    mcp = _RecordingMcp()
    service = CustomToolService(mcp, project_scoped_tools=False)
    live = {"aaaa1111", "bbbb2222"}

    service._register_global_tool(_definition(["name"]), owner="aaaa1111", live_owners=live)
    service._register_global_tool(_definition(["name"]), owner="bbbb2222", live_owners=live)
    with pytest.raises(ValueError, match="duplicate parameter name: 'ctx'"):
        service._register_global_tool(_definition(["ctx"]), owner="aaaa1111", live_owners=live)
    assert service._served_owners("my_tool") == {"aaaa1111", "bbbb2222"}

    service._register_global_tool(_definition(["path"]), owner="bbbb2222", live_owners=live)
    assert [p.name for p in service._global_tools["my_tool"].parameters] == ["name"]
