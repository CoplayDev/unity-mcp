import sqlite3
from decimal import Decimal

import pytest
from fastapi.testclient import TestClient

from overseer import EventLedger, create_overseer_app, run_support_ticket_simulation


@pytest.mark.parametrize(
    ("subscription_amount", "model_cost"),
    [(float("nan"), 31.25), (499, float("inf")), (-1, 31.25), (499, -1)],
)
def test_simulation_validates_amounts_before_recording_events(
    subscription_amount, model_cost
):
    ledger = EventLedger(sqlite3.connect(":memory:"))

    with pytest.raises(ValueError, match="finite and non-negative"):
        run_support_ticket_simulation(
            ledger,
            subscription_amount=subscription_amount,
            model_cost=model_cost,
        )

    assert ledger.list_events() == []


def test_simulation_records_full_lifecycle_for_valid_amounts():
    ledger = EventLedger(sqlite3.connect(":memory:"))

    events = run_support_ticket_simulation(
        ledger,
        ticket_id="T-42",
        subscription_amount=Decimal("499.00"),
        model_cost=Decimal("31.25"),
    )

    assert [event.event_type for event in events] == [
        "ticket_received",
        "ticket_resolved",
        "model_usage",
        "subscription_paid",
    ]
    assert len(ledger.list_events()) == 4
    assert events[2].amount == Decimal("31.25")
    assert events[3].amount == Decimal("499.00")


def test_api_exposes_simulation_events_and_financial_summary():
    ledger = EventLedger(sqlite3.connect(":memory:"))
    run_support_ticket_simulation(ledger, ticket_id="T-42")
    client = TestClient(create_overseer_app(ledger, api_key="test-key"))

    headers = {"X-Overseer-Api-Key": "test-key"}
    events = client.get("/events?team_id=support", headers=headers).json()
    summary = client.get("/summaries?team_id=support", headers=headers).json()

    assert len(events) == 4
    assert summary == [
        {
            "team_id": "support",
            "currency": "USD",
            "revenue": 499,
            "costs": 31.25,
            "profit": 467.75,
            "activity_count": 4,
            "pending_approvals": 0,
        }
    ]


def test_api_only_returns_pending_approvals():
    ledger = EventLedger(sqlite3.connect(":memory:"))
    pending = ledger.record(
        category="cost",
        event_type="refund_requested",
        team_id="support",
        agent_id="billing-agent",
        amount=50,
        requires_approval=True,
    )
    ledger.record(
        category="activity",
        event_type="ticket_resolved",
        team_id="support",
        agent_id="support-agent",
    )
    client = TestClient(create_overseer_app(ledger, api_key="test-key"))

    approvals = client.get("/approvals", headers={"X-Overseer-Api-Key": "test-key"}).json()

    assert len(approvals) == 1
    assert approvals[0]["id"] == pending.id


def test_api_requires_authentication_and_enforces_team_scope():
    ledger = EventLedger(sqlite3.connect(":memory:"))
    client = TestClient(
        create_overseer_app(
            ledger,
            api_key="test-key",
            authorized_team_ids={"support"},
        )
    )

    assert client.get("/events").status_code == 401
    assert (
        client.get(
            "/events",
            headers={"X-Overseer-Api-Key": "test-key"},
        ).status_code
        == 403
    )
    assert (
        client.get(
            "/events?team_id=lead-generation",
            headers={"X-Overseer-Api-Key": "test-key"},
        ).status_code
        == 403
    )

    for path in ("/summaries", "/approvals"):
        assert (
            client.get(path, headers={"X-Overseer-Api-Key": "test-key"}).status_code
            == 403
        )
