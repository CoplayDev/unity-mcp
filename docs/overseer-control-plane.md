# Overseer control plane

The first commercial-agent capability is an event ledger, not an autonomous
outreach or payment connector. `Server/src/overseer/ledger.py` records provider-
neutral facts that a future dashboard can display:

- agent activity and task history;
- revenue and operating costs;
- pending and completed human approvals; and
- per-team profit summaries.

## Simulation

```python
import sqlite3
from overseer import EventLedger

ledger = EventLedger(sqlite3.connect("overseer.db", check_same_thread=False))
ledger.record(
    category="activity",
    event_type="ticket_resolved",
    team_id="support",
    agent_id="support-agent",
    task_id="ticket:T-100",
    metadata={"source": "approved-product-docs", "confidence": 0.94},
)
ledger.record(
    category="revenue",
    event_type="subscription_paid",
    team_id="support",
    agent_id="billing-agent",
    amount=499,
    currency="USD",
)
ledger.record(
    category="cost",
    event_type="model_usage",
    team_id="support",
    agent_id="support-agent",
    amount=31.25,
    currency="USD",
)
print(ledger.summarize())
```

## Read-only dashboard API

The API adapter exposes the same facts without granting dashboard clients
permission to mutate them:

```python
from overseer import create_overseer_app

app = create_overseer_app(ledger, api_key="set-this-from-secret-storage")
```

It provides authenticated `GET /events`, `GET /summaries`, and `GET /approvals`
using the `X-Overseer-Api-Key` header. Pass `authorized_team_ids` when the
operator should be restricted to a subset of teams; scoped requests must
include a team ID from that set. The
`run_support_ticket_simulation()` helper can populate a local demo ledger so
the dashboard can be built and reviewed before any provider credentials exist.
When the API is used, `EventLedger` copies the SQLite connection to one that is
safe for API handlers. File-
backed databases remain persistent, while in-memory databases are copied into
an isolated database owned by the ledger, with access serialized for FastAPI
worker threads.

Amounts are stored as exact decimals and summaries are separated by currency.
Existing ledgers with the earlier SQLite `REAL` amount column are migrated to
the exact `TEXT` representation before new events are written.
Pending approval events remain visible in the activity and approval feeds but
are excluded from realized revenue, cost, and profit totals until approved.

The ledger intentionally does not send emails, place calls, issue refunds, or
connect to a payment provider. Those actions must be implemented as connectors
that emit ledger events and use `requires_approval=True` for irreversible work.
The dashboard should read the same event stream, so the operator can inspect
what happened before enabling production credentials.

## Five agent servers

The planned commercial servers are defined as provider-neutral configuration
under `Server/src/overseer/teams/`. Each definition contains five specialist
agents, the server purpose, its planned revenue model, and actions that remain
human-approval gated:

- `lead_generation.py`
- `customer_support.py`
- `content_operations.py`
- `market_intelligence.py`
- `inventory_operations.py`

These files do not connect to email, CRM, voice, payment, customer, or
production systems. They are the supervised server boundaries that connectors
and workflows can be added to later.
