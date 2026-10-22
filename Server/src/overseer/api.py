"""Read-only HTTP view of the overseer ledger."""

from __future__ import annotations

from dataclasses import asdict

from hmac import compare_digest
from decimal import Decimal
from typing import Collection

from fastapi import FastAPI, Header, HTTPException, Query

from .ledger import EventLedger


def create_overseer_app(
    ledger: EventLedger,
    *,
    api_key: str,
    authorized_team_ids: Collection[str] | None = None,
) -> FastAPI:
    """Create an authenticated, read-only API backed by an existing ledger."""
    if not api_key:
        raise ValueError("api_key is required")
    app = FastAPI(title="Overseer Control Plane", version="1.0")
    scoped_team_ids = (
        frozenset(authorized_team_ids) if authorized_team_ids is not None else None
    )

    def serialize(value: object) -> object:
        """Convert ledger values into JSON-compatible response values."""
        if isinstance(value, Decimal):
            return float(value)
        if isinstance(value, dict):
            return {key: serialize(item) for key, item in value.items()}
        if isinstance(value, list):
            return [serialize(item) for item in value]
        return value

    def authorize(requested_team_id: str | None, presented_key: str | None) -> None:
        """Validate the API key and optional team scope for an endpoint request."""
        if presented_key is None or not compare_digest(presented_key, api_key):
            raise HTTPException(status_code=401, detail="authentication required")
        if scoped_team_ids is not None:
            if requested_team_id is None or requested_team_id not in scoped_team_ids:
                raise HTTPException(status_code=403, detail="team access denied")

    @app.get("/events")
    def events(
        team_id: str | None = Query(default=None),
        limit: int = Query(default=100, ge=1, le=500),
        x_overseer_api_key: str | None = Header(default=None),
    ) -> list[dict]:
        """Return recent ledger events visible to the authenticated caller."""
        authorize(team_id, x_overseer_api_key)
        return [serialize(asdict(event)) for event in ledger.list_events(team_id, limit)]

    @app.get("/summaries")
    def summaries(
        team_id: str | None = Query(default=None),
        x_overseer_api_key: str | None = Header(default=None),
    ) -> list[dict]:
        """Return financial summaries visible to the authenticated caller."""
        authorize(team_id, x_overseer_api_key)
        return [serialize(asdict(summary)) for summary in ledger.summarize(team_id)]

    @app.get("/approvals")
    def pending_approvals(
        team_id: str | None = Query(default=None),
        limit: int = Query(default=100, ge=1, le=500),
        x_overseer_api_key: str | None = Header(default=None),
    ) -> list[dict]:
        """Return pending approval events visible to the authenticated caller."""
        authorize(team_id, x_overseer_api_key)
        return [
            serialize(asdict(event))
            for event in ledger.list_pending_approvals(team_id, limit)
        ]

    return app
