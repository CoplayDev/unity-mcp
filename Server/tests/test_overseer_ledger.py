"""Focused tests for the local overseer simulation and dashboard boundary."""

import json
import asyncio
from datetime import datetime, timezone

import pytest
from starlette.requests import Request

from services.overseer_ledger import (
    EventLedger,
    EventType,
    OverseerAuth,
    Role,
    register_overseer_routes,
)


class RecordingMcp:
    def __init__(self):
        self.routes = {}

    def custom_route(self, path, methods=None):
        def decorator(handler):
            self.routes[(path, tuple(methods or ()))] = handler
            return handler

        return decorator


def request(path="/", token="overseer-viewer", method="GET", body=None, path_params=None):
    headers = [(b"authorization", f"Bearer {token}".encode())]
    raw_body = json.dumps(body).encode() if body is not None else b""
    headers.append((b"content-type", b"application/json"))

    async def receive():
        return {"type": "http.request", "body": raw_body, "more_body": False}

    scope = {
        "type": "http",
        "method": method,
        "path": path,
        "query_string": b"",
        "headers": headers,
        "path_params": path_params or {},
    }
    return Request(scope, receive)


def test_simulation_emits_all_event_categories_and_aggregates():
    clock = lambda: datetime(2026, 1, 1, tzinfo=timezone.utc)
    ledger = EventLedger(clock=clock)

    report = ledger.run_simulation()

    assert report.event_count == 10
    assert {event.event_type for event in ledger.events} == {
        EventType.AGENT_ACTIVITY,
        EventType.REVENUE,
        EventType.COST,
        EventType.APPROVAL_PENDING,
        EventType.FAILED_ACTION,
        EventType.AGENT_HEALTH,
    }
    assert ledger.financials() == [
        {"team": "sales", "currency": "USD", "revenue": 7200, "costs": 2100, "profit": 5100},
        {"team": "support", "currency": "USD", "revenue": 4800, "costs": 1200, "profit": 3600},
    ]
    assert len(ledger.pending_approvals()) == 1
    assert len(ledger.failed_actions()) == 1
    assert len(ledger.health()) == 2


def test_approval_is_append_only_and_requires_pending_approval():
    ledger = EventLedger()
    ledger.run_simulation()
    approved = ledger.approve("approval-001")

    assert approved["status"] == "approved"
    assert ledger.pending_approvals() == []
    assert ledger.events[-1].event_type == EventType.APPROVAL_RESOLVED
    with pytest.raises(ValueError):
        ledger.approve("approval-001")


def test_dashboard_routes_require_bearer_auth_and_return_read_models():
    mcp = RecordingMcp()
    ledger = EventLedger()
    ledger.run_simulation()
    auth = OverseerAuth({
        Role.VIEWER: "viewer-token",
        Role.OPERATOR: "operator-token",
        Role.ADMIN: "admin-token",
    })
    register_overseer_routes(mcp, ledger, auth)

    dashboard = mcp.routes[("/api/overseer/dashboard", ("GET",))]
    unauthorized = asyncio.run(dashboard(request(token="wrong-token")))
    assert unauthorized.status_code == 401

    response = asyncio.run(dashboard(request(token="viewer-token")))
    assert response.status_code == 200
    payload = json.loads(response.body)
    assert payload["success"] is True
    assert payload["dashboard"]["financials"][0]["profit"] > 0


def test_internal_routes_enforce_operator_and_admin_roles():
    mcp = RecordingMcp()
    ledger = EventLedger()
    auth = OverseerAuth({
        Role.VIEWER: "viewer-token",
        Role.OPERATOR: "operator-token",
        Role.ADMIN: "admin-token",
    })
    register_overseer_routes(mcp, ledger, auth)

    simulate = mcp.routes[("/api/internal/overseer/simulate", ("POST",))]
    forbidden = asyncio.run(simulate(request(
        method="POST", token="viewer-token", body={"cycles": 1},
    )))
    assert forbidden.status_code == 403
    created = asyncio.run(simulate(request(
        method="POST", token="operator-token", body={"cycles": 1},
    )))
    assert created.status_code == 201

    approve = mcp.routes[(
        "/api/internal/overseer/approvals/{approval_id}/approve",
        ("POST",),
    )]
    operator_response = asyncio.run(approve(request(
        method="POST",
        token="operator-token",
        path_params={"approval_id": "approval-001"},
    )))
    assert operator_response.status_code == 403
    admin_response = asyncio.run(approve(request(
        method="POST",
        token="admin-token",
        path_params={"approval_id": "approval-001"},
    )))
    assert admin_response.status_code == 200
