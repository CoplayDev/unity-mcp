"""Deterministic support-agent simulation for exercising the overseer ledger."""

from __future__ import annotations

from decimal import Decimal, InvalidOperation

from .ledger import EventLedger, LedgerEvent


def run_support_ticket_simulation(
    ledger: EventLedger,
    *,
    ticket_id: str = "T-100",
    subscription_amount: float = 499,
    model_cost: float = 31.25,
) -> list[LedgerEvent]:
    """Record one complete support-ticket lifecycle without external side effects."""
    try:
        exact_subscription_amount = Decimal(str(subscription_amount))
        exact_model_cost = Decimal(str(model_cost))
    except (InvalidOperation, TypeError, ValueError):
        raise ValueError(
            "subscription_amount and model_cost must be finite and non-negative"
        ) from None
    if any(
        not amount.is_finite() or amount < 0
        for amount in (exact_subscription_amount, exact_model_cost)
    ):
        raise ValueError("subscription_amount and model_cost must be finite and non-negative")
    task_id = f"ticket:{ticket_id}"
    events = [
        ledger.record(
            category="activity",
            event_type="ticket_received",
            team_id="support",
            agent_id="triage-agent",
            task_id=task_id,
            metadata={"channel": "simulation"},
        ),
        ledger.record(
            category="activity",
            event_type="ticket_resolved",
            team_id="support",
            agent_id="support-agent",
            task_id=task_id,
            metadata={"confidence": 0.94, "source": "approved-product-docs"},
        ),
        ledger.record(
            category="cost",
            event_type="model_usage",
            team_id="support",
            agent_id="support-agent",
            task_id=task_id,
            amount=exact_model_cost,
            currency="USD",
        ),
        ledger.record(
            category="revenue",
            event_type="subscription_paid",
            team_id="support",
            agent_id="billing-agent",
            task_id=task_id,
            amount=exact_subscription_amount,
            currency="USD",
        ),
    ]
    return events
