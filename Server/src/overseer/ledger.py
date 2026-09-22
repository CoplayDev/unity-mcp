"""Durable activity, approval, and economics ledger for the overseer dashboard.

The ledger deliberately stores facts rather than provider-specific objects. Connectors
for a CRM, billing provider, or model runtime can translate their callbacks into events.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
import json
import sqlite3
from threading import RLock
from typing import Any
from uuid import uuid4


@dataclass(frozen=True)
class LedgerEvent:
    """Immutable representation of one activity, revenue, or cost event."""

    id: str
    category: str
    event_type: str
    team_id: str
    agent_id: str
    task_id: str | None
    amount: Decimal
    currency: str
    requires_approval: bool
    approved_by: str | None
    metadata: dict[str, Any]
    created_at: str


@dataclass(frozen=True)
class TeamSummary:
    """Aggregated financial and approval state for one team and currency."""

    team_id: str
    currency: str
    revenue: Decimal
    costs: Decimal
    profit: Decimal
    activity_count: int
    pending_approvals: int


class EventLedger:
    """SQLite-backed ledger suitable for simulation and a later API adapter."""

    def __init__(self, connection: sqlite3.Connection):
        """Initialize the ledger and migrate an existing database if needed."""
        self._lock = RLock()
        database_path = connection.execute("PRAGMA database_list").fetchone()[2]
        if database_path:
            self._connection = sqlite3.connect(database_path, check_same_thread=False)
        else:
            self._connection = sqlite3.connect(":memory:", check_same_thread=False)
            connection.backup(self._connection)
        self._connection.row_factory = sqlite3.Row
        self._connection.execute("PRAGMA foreign_keys = ON")
        self._connection.executescript(
            """
            CREATE TABLE IF NOT EXISTS ledger_events (
                id TEXT PRIMARY KEY,
                category TEXT NOT NULL CHECK (category IN ('activity', 'revenue', 'cost')),
                event_type TEXT NOT NULL,
                team_id TEXT NOT NULL,
                agent_id TEXT NOT NULL,
                task_id TEXT,
                amount TEXT NOT NULL DEFAULT '0',
                currency TEXT NOT NULL,
                requires_approval INTEGER NOT NULL DEFAULT 0,
                approved_by TEXT,
                metadata_json TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """
        )
        self._migrate_legacy_amount_column()
        self._connection.executescript(
            """
            CREATE INDEX IF NOT EXISTS idx_ledger_events_team
                ON ledger_events(team_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_ledger_events_approval
                ON ledger_events(requires_approval, approved_by);
            """
        )
        self._connection.commit()

    def _migrate_legacy_amount_column(self) -> None:
        """Rewrite legacy REAL amounts as text to preserve exact decimals."""
        columns = self._connection.execute("PRAGMA table_info(ledger_events)").fetchall()
        amount_column = next((column for column in columns if column["name"] == "amount"), None)
        if amount_column is None or amount_column["type"].upper() != "REAL":
            return

        self._connection.execute("BEGIN")
        try:
            self._connection.execute("ALTER TABLE ledger_events RENAME TO ledger_events_legacy")
            self._connection.execute(
                """
                CREATE TABLE ledger_events (
                    id TEXT PRIMARY KEY,
                    category TEXT NOT NULL CHECK (category IN ('activity', 'revenue', 'cost')),
                    event_type TEXT NOT NULL,
                    team_id TEXT NOT NULL,
                    agent_id TEXT NOT NULL,
                    task_id TEXT,
                    amount TEXT NOT NULL DEFAULT '0',
                    currency TEXT NOT NULL,
                    requires_approval INTEGER NOT NULL DEFAULT 0,
                    approved_by TEXT,
                    metadata_json TEXT NOT NULL,
                    created_at TEXT NOT NULL
                )
                """
            )
            self._connection.execute(
                """
                INSERT INTO ledger_events
                SELECT id, category, event_type, team_id, agent_id, task_id,
                       CAST(amount AS TEXT), currency, requires_approval, approved_by,
                       metadata_json, created_at
                FROM ledger_events_legacy
                """
            )
            self._connection.execute("DROP TABLE ledger_events_legacy")
            self._connection.commit()
        except Exception:
            self._connection.rollback()
            raise

    def record(
        self,
        *,
        category: str,
        event_type: str,
        team_id: str,
        agent_id: str,
        task_id: str | None = None,
        amount: Decimal | int | float | str = 0,
        currency: str = "USD",
        requires_approval: bool = False,
        approved_by: str | None = None,
        metadata: dict[str, Any] | None = None,
        created_at: datetime | None = None,
    ) -> LedgerEvent:
        """Validate and persist one ledger event, returning its immutable record."""
        if category not in {"activity", "revenue", "cost"}:
            raise ValueError("category must be activity, revenue, or cost")
        if not team_id or not agent_id or not event_type:
            raise ValueError("team_id, agent_id, and event_type are required")
        if requires_approval and approved_by is not None:
            raise ValueError("an event cannot require approval and already be approved")
        try:
            exact_amount = Decimal(str(amount))
        except (InvalidOperation, ValueError):
            raise ValueError("amount must be a finite non-negative number") from None
        if not exact_amount.is_finite():
            raise ValueError("amount must be finite and non-negative")
        if exact_amount < 0:
            raise ValueError("amount must be non-negative; use category to distinguish revenue and cost")
        if created_at is not None:
            if created_at.tzinfo is None or created_at.utcoffset() is None:
                raise ValueError("created_at must be timezone-aware")
            normalized_created_at = created_at.astimezone(timezone.utc)
        else:
            normalized_created_at = datetime.now(timezone.utc)

        event = LedgerEvent(
            id=str(uuid4()),
            category=category,
            event_type=event_type,
            team_id=team_id,
            agent_id=agent_id,
            task_id=task_id,
            amount=exact_amount,
            currency=currency.upper(),
            requires_approval=requires_approval,
            approved_by=approved_by,
            metadata=metadata or {},
            created_at=normalized_created_at.isoformat(),
        )
        with self._lock:
            self._connection.execute(
                """
                INSERT INTO ledger_events
                (id, category, event_type, team_id, agent_id, task_id, amount, currency,
                 requires_approval, approved_by, metadata_json, created_at)
                VALUES (:id, :category, :event_type, :team_id, :agent_id, :task_id, :amount,
                        :currency, :requires_approval, :approved_by, :metadata_json, :created_at)
                """,
                {**asdict(event), "requires_approval": int(event.requires_approval),
                 "amount": str(event.amount),
                 "metadata_json": json.dumps(event.metadata, sort_keys=True)},
            )
            self._connection.commit()
        return event

    def approve(self, event_id: str, approver_id: str) -> LedgerEvent:
        """Approve a pending event and return the updated record."""
        if not approver_id:
            raise ValueError("approver_id is required")
        with self._lock:
            cursor = self._connection.execute(
                """
                UPDATE ledger_events
                SET requires_approval = 0, approved_by = ?
                WHERE id = ? AND requires_approval = 1 AND approved_by IS NULL
                """,
                (approver_id, event_id),
            )
            if cursor.rowcount != 1:
                raise LookupError("pending approval not found")
            self._connection.commit()
            return self.get(event_id)

    def get(self, event_id: str) -> LedgerEvent:
        """Load one event by ID or raise when it does not exist."""
        with self._lock:
            row = self._connection.execute(
                "SELECT * FROM ledger_events WHERE id = ?", (event_id,)
            ).fetchone()
        if row is None:
            raise LookupError("ledger event not found")
        return self._row_to_event(row)

    def list_events(self, team_id: str | None = None, limit: int | None = None) -> list[LedgerEvent]:
        """Return newest events, optionally filtered by team and limited in count."""
        limit_sql = "" if limit is None else " LIMIT ?"
        limit_params: tuple[Any, ...] = () if limit is None else (limit,)
        with self._lock:
            if team_id is None:
                rows = self._connection.execute(
                    "SELECT * FROM ledger_events ORDER BY created_at DESC" + limit_sql,
                    limit_params,
                ).fetchall()
            else:
                rows = self._connection.execute(
                    "SELECT * FROM ledger_events WHERE team_id = ? ORDER BY created_at DESC" + limit_sql,
                    (team_id, *limit_params),
                ).fetchall()
        return [self._row_to_event(row) for row in rows]

    def list_pending_approvals(
        self, team_id: str | None = None, limit: int | None = None
    ) -> list[LedgerEvent]:
        """Return newest events that still require human approval."""
        query = "SELECT * FROM ledger_events WHERE requires_approval = 1"
        params: tuple[Any, ...] = ()
        if team_id is not None:
            query += " AND team_id = ?"
            params += (team_id,)
        query += " ORDER BY created_at DESC"
        if limit is not None:
            query += " LIMIT ?"
            params += (limit,)
        with self._lock:
            rows = self._connection.execute(query, params).fetchall()
        return [self._row_to_event(row) for row in rows]

    def summarize(self, team_id: str | None = None) -> list[TeamSummary]:
        """Aggregate revenue, costs, activity, and approvals by team and currency."""
        where = "" if team_id is None else "WHERE team_id = ?"
        params: tuple[Any, ...] = () if team_id is None else (team_id,)
        with self._lock:
            rows = self._connection.execute(
                """
                SELECT team_id, currency, category, requires_approval, amount
                FROM ledger_events
                """ + where + """
                ORDER BY team_id, currency
                """,
                params,
            ).fetchall()
        totals: dict[tuple[str, str], dict[str, Any]] = {}
        for row in rows:
            key = (row["team_id"], row["currency"])
            total = totals.setdefault(
                key,
                {
                    "revenue": Decimal("0"),
                    "costs": Decimal("0"),
                    "activity_count": 0,
                    "pending_approvals": 0,
                },
            )
            total["activity_count"] += 1
            if row["requires_approval"]:
                total["pending_approvals"] += 1
            elif row["category"] == "revenue":
                total["revenue"] += Decimal(row["amount"])
            elif row["category"] == "cost":
                total["costs"] += Decimal(row["amount"])
        return [
            TeamSummary(
                team_id=team_id,
                currency=currency,
                revenue=total["revenue"],
                costs=total["costs"],
                profit=total["revenue"] - total["costs"],
                activity_count=total["activity_count"],
                pending_approvals=total["pending_approvals"],
            )
            for (team_id, currency), total in sorted(totals.items())
        ]

    @staticmethod
    def _row_to_event(row: sqlite3.Row) -> LedgerEvent:
        """Convert a SQLite row into the public immutable event model."""
        return LedgerEvent(
            id=row["id"],
            category=row["category"],
            event_type=row["event_type"],
            team_id=row["team_id"],
            agent_id=row["agent_id"],
            task_id=row["task_id"],
            amount=Decimal(str(row["amount"])),
            currency=row["currency"],
            requires_approval=bool(row["requires_approval"]),
            approved_by=row["approved_by"],
            metadata=json.loads(row["metadata_json"]),
            created_at=row["created_at"],
        )
