import sqlite3
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from decimal import Decimal

import pytest

from overseer import EventLedger


@pytest.fixture
def ledger():
    return EventLedger(sqlite3.connect(":memory:"))


def test_records_activity_and_summarizes_revenue_cost_and_profit(ledger):
    ledger.record(
        category="revenue",
        event_type="subscription_paid",
        team_id="support",
        agent_id="billing-agent",
        amount=500,
        currency="usd",
    )
    ledger.record(
        category="cost",
        event_type="model_usage",
        team_id="support",
        agent_id="support-agent",
        amount=125,
        currency="USD",
    )
    ledger.record(
        category="activity",
        event_type="ticket_resolved",
        team_id="support",
        agent_id="support-agent",
        metadata={"ticket_id": "T-1"},
    )

    summary = ledger.summarize()[0]
    assert summary.team_id == "support"
    assert summary.revenue == 500
    assert summary.costs == 125
    assert summary.profit == 375
    assert summary.activity_count == 3


def test_exact_money_is_currency_aware_and_pending_amounts_are_excluded(ledger):
    ledger.record(
        category="revenue",
        event_type="paid",
        team_id="support",
        agent_id="billing",
        amount="0.10",
        currency="usd",
    )
    ledger.record(
        category="revenue",
        event_type="paid",
        team_id="support",
        agent_id="billing",
        amount="0.20",
        currency="usd",
    )
    pending_revenue = ledger.record(
        category="revenue",
        event_type="pending",
        team_id="support",
        agent_id="billing",
        amount="100",
        currency="eur",
        requires_approval=True,
    )
    pending_cost = ledger.record(
        category="cost",
        event_type="refund_requested",
        team_id="support",
        agent_id="billing",
        amount="0.05",
        currency="usd",
        requires_approval=True,
    )

    summaries = ledger.summarize()
    by_currency = {summary.currency: summary for summary in summaries}
    assert by_currency["USD"].revenue == Decimal("0.30")
    assert by_currency["USD"].costs == Decimal("0")
    assert by_currency["USD"].profit == Decimal("0.30")
    assert by_currency["EUR"].revenue == Decimal("0")
    assert by_currency["EUR"].pending_approvals == 1
    assert by_currency["EUR"].costs == Decimal("0")
    assert by_currency["EUR"].profit == Decimal("0")
    ledger.approve(pending_revenue.id, "operator-1")
    ledger.approve(pending_cost.id, "operator-1")
    by_currency = {summary.currency: summary for summary in ledger.summarize()}
    assert by_currency["EUR"].revenue == Decimal("100")
    assert by_currency["EUR"].profit == Decimal("100")
    assert by_currency["USD"].costs == Decimal("0.05")
    assert by_currency["USD"].profit == Decimal("0.25")


def test_migrates_legacy_real_amount_column_before_new_writes(tmp_path):
    connection = sqlite3.connect(tmp_path / "legacy.db")
    connection.execute(
        """
        CREATE TABLE ledger_events (
            id TEXT PRIMARY KEY,
            category TEXT NOT NULL,
            event_type TEXT NOT NULL,
            team_id TEXT NOT NULL,
            agent_id TEXT NOT NULL,
            task_id TEXT,
            amount REAL NOT NULL DEFAULT 0,
            currency TEXT NOT NULL,
            requires_approval INTEGER NOT NULL DEFAULT 0,
            approved_by TEXT,
            metadata_json TEXT NOT NULL,
            created_at TEXT NOT NULL
        )
        """
    )

    ledger = EventLedger(connection)
    amount_type = connection.execute(
        "PRAGMA table_info(ledger_events)"
    ).fetchall()[6][2]
    ledger.record(
        category="revenue",
        event_type="paid",
        team_id="support",
        agent_id="billing",
        amount="123.456789",
        currency="USD",
    )

    assert amount_type == "TEXT"
    assert ledger.list_events(limit=1)[0].amount == Decimal("123.456789")


def test_rejects_non_finite_or_naive_timestamps(ledger):
    with pytest.raises(ValueError, match="finite"):
        ledger.record(category="cost", event_type="x", team_id="t", agent_id="a", amount=float("nan"))
    with pytest.raises(ValueError, match="finite"):
        ledger.record(category="cost", event_type="x", team_id="t", agent_id="a", amount=float("inf"))
    with pytest.raises(ValueError, match="finite"):
        ledger.record(category="cost", event_type="x", team_id="t", agent_id="a", amount=float("-inf"))
    with pytest.raises(ValueError, match="timezone-aware"):
        ledger.record(
            category="activity",
            event_type="x",
            team_id="t",
            agent_id="a",
            created_at=datetime(2025, 1, 1),
        )

    event = ledger.record(
        category="activity",
        event_type="x",
        team_id="t",
        agent_id="a",
        created_at=datetime(2025, 1, 1, tzinfo=timezone.utc),
    )
    assert event.created_at.endswith("+00:00")

    offset_event = ledger.record(
        category="activity",
        event_type="x",
        team_id="t",
        agent_id="a",
        created_at=datetime.fromisoformat("2025-01-01T01:00:00+01:00"),
    )
    assert offset_event.created_at == "2025-01-01T00:00:00+00:00"


def test_orders_events_by_utc_time_when_offsets_differ(ledger):
    ledger.record(
        category="activity",
        event_type="older",
        team_id="t",
        agent_id="a",
        created_at=datetime.fromisoformat("2025-01-01T00:00:00+14:00"),
    )
    ledger.record(
        category="activity",
        event_type="newer",
        team_id="t",
        agent_id="a",
        created_at=datetime.fromisoformat("2024-12-31T23:00:00+00:00"),
    )

    assert [event.event_type for event in ledger.list_events()] == ["newer", "older"]


def test_requires_approval_is_visible_and_can_be_approved(ledger):
    event = ledger.record(
        category="cost",
        event_type="refund_requested",
        team_id="support",
        agent_id="support-agent",
        amount=50,
        requires_approval=True,
    )

    assert ledger.summarize()[0].pending_approvals == 1
    approved = ledger.approve(event.id, "operator-1")
    assert approved.approved_by == "operator-1"
    assert not approved.requires_approval
    assert ledger.summarize()[0].pending_approvals == 0


def test_rejects_unsafe_or_ambiguous_events(ledger):
    with pytest.raises(ValueError, match="category"):
        ledger.record(
            category="unknown",
            event_type="x",
            team_id="support",
            agent_id="agent",
        )
    with pytest.raises(ValueError, match="non-negative"):
        ledger.record(
            category="cost",
            event_type="x",
            team_id="support",
            agent_id="agent",
            amount=-1,
        )
    with pytest.raises(ValueError, match="already be approved"):
        ledger.record(
            category="activity",
            event_type="x",
            team_id="support",
            agent_id="agent",
            requires_approval=True,
            approved_by="operator",
        )


def test_serializes_concurrent_worker_thread_access(ledger):
    def record_event(index):
        ledger.record(
            category="activity",
            event_type="worker_event",
            team_id="support",
            agent_id=f"agent-{index}",
        )

    with ThreadPoolExecutor(max_workers=8) as executor:
        list(executor.map(record_event, range(40)))

    assert len(ledger.list_events(team_id="support")) == 40
