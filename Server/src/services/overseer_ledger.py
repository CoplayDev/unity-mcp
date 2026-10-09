"""Local customer-support overseer simulation and read-only dashboard routes.

This module deliberately has no provider integrations.  It gives the dashboard a
deterministic event stream that can be replaced by real adapters later without
changing the read model or its authorization boundary.
"""

from __future__ import annotations

import copy
import os
import threading
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from enum import Enum
from typing import Any, Callable, Mapping

from starlette.requests import Request
from starlette.responses import JSONResponse


class EventType(str, Enum):
    """Event categories emitted by the support workflow."""

    AGENT_ACTIVITY = "agent_activity"
    REVENUE = "revenue"
    COST = "cost"
    APPROVAL_PENDING = "approval_pending"
    APPROVAL_RESOLVED = "approval_resolved"
    FAILED_ACTION = "failed_action"
    AGENT_HEALTH = "agent_health"


class Role(str, Enum):
    """Roles supported by the local dashboard authentication boundary."""

    VIEWER = "viewer"
    OPERATOR = "operator"
    ADMIN = "admin"


@dataclass(frozen=True)
class AuthPrincipal:
    """The role associated with a successfully authenticated bearer token."""

    role: Role


@dataclass(frozen=True)
class OverseerEvent:
    """An immutable ledger entry."""

    event_id: str
    event_type: EventType
    occurred_at: str
    run_id: str
    team: str | None = None
    agent_id: str | None = None
    data: Mapping[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        payload = asdict(self)
        payload["event_type"] = self.event_type.value
        payload["data"] = copy.deepcopy(dict(self.data))
        return payload


@dataclass(frozen=True)
class SimulationReport:
    """Summary returned after a deterministic simulation run."""

    run_id: str
    emitted_event_ids: tuple[str, ...]
    event_count: int

    def to_dict(self) -> dict[str, Any]:
        return {
            "run_id": self.run_id,
            "emitted_event_ids": list(self.emitted_event_ids),
            "event_count": self.event_count,
        }


class EventLedger:
    """Thread-safe in-memory event ledger and read-model projections."""

    def __init__(self, clock: Callable[[], datetime] | None = None):
        self._clock = clock or (lambda: datetime.now(timezone.utc))
        self._events: list[OverseerEvent] = []
        self._event_sequence = 0
        self._run_sequence = 0
        self._lock = threading.RLock()

    @property
    def events(self) -> list[OverseerEvent]:
        """Return a copy so callers cannot mutate the ledger."""
        with self._lock:
            return copy.deepcopy(self._events)

    def emit(
        self,
        event_type: EventType,
        *,
        run_id: str,
        team: str | None = None,
        agent_id: str | None = None,
        data: Mapping[str, Any] | None = None,
    ) -> OverseerEvent:
        with self._lock:
            self._event_sequence += 1
            event = OverseerEvent(
                event_id=f"evt-{self._event_sequence:06d}",
                event_type=event_type,
                occurred_at=self._clock().astimezone(timezone.utc).isoformat(),
                run_id=run_id,
                team=team,
                agent_id=agent_id,
                data=copy.deepcopy(dict(data or {})),
            )
            self._events.append(event)
            return event

    def run_simulation(self, cycles: int = 1) -> SimulationReport:
        """Emit a predictable support workflow without contacting any provider."""
        if not isinstance(cycles, int) or isinstance(cycles, bool) or not 1 <= cycles <= 10:
            raise ValueError("cycles must be an integer between 1 and 10")

        emitted: list[str] = []
        with self._lock:
            for _ in range(cycles):
                self._run_sequence += 1
                run_id = f"sim-{self._run_sequence:03d}"
                specs = (
                    ("agent-support-1", "support", "ticket_triage", 4800, 1200, 0.98),
                    ("agent-sales-1", "sales", "renewal_followup", 7200, 2100, 0.96),
                )
                for agent_id, team, task, revenue, cost, health in specs:
                    emitted.append(self.emit(
                        EventType.AGENT_ACTIVITY,
                        run_id=run_id,
                        team=team,
                        agent_id=agent_id,
                        data={"status": "working", "task": task, "progress": 0.5},
                    ).event_id)
                    emitted.append(self.emit(
                        EventType.REVENUE,
                        run_id=run_id,
                        team=team,
                        agent_id=agent_id,
                        data={"amount": revenue, "currency": "USD", "source": "simulation"},
                    ).event_id)
                    emitted.append(self.emit(
                        EventType.COST,
                        run_id=run_id,
                        team=team,
                        agent_id=agent_id,
                        data={"amount": cost, "currency": "USD", "source": "simulation"},
                    ).event_id)
                    emitted.append(self.emit(
                        EventType.AGENT_HEALTH,
                        run_id=run_id,
                        team=team,
                        agent_id=agent_id,
                        data={"status": "healthy", "score": health},
                    ).event_id)

                approval_id = f"approval-{self._run_sequence:03d}"
                emitted.append(self.emit(
                    EventType.APPROVAL_PENDING,
                    run_id=run_id,
                    team="support",
                    agent_id="agent-support-1",
                    data={
                        "approval_id": approval_id,
                        "action": "issue_refund",
                        "amount": 325,
                        "currency": "USD",
                        "status": "pending",
                    },
                ).event_id)
                emitted.append(self.emit(
                    EventType.FAILED_ACTION,
                    run_id=run_id,
                    team="support",
                    agent_id="agent-support-1",
                    data={
                        "action_id": f"failed-{self._run_sequence:03d}",
                        "action": "send_email",
                        "reason": "provider_not_connected",
                        "retryable": True,
                    },
                ).event_id)

        return SimulationReport(run_id=run_id, emitted_event_ids=tuple(emitted), event_count=len(emitted))

    def _approval_projection(self) -> dict[str, dict[str, Any]]:
        approvals: dict[str, dict[str, Any]] = {}
        for event in self._events:
            if event.event_type == EventType.APPROVAL_PENDING:
                approval_id = str(event.data["approval_id"])
                approvals[approval_id] = {
                    "approval_id": approval_id,
                    "team": event.team,
                    "agent_id": event.agent_id,
                    "run_id": event.run_id,
                    "action": event.data.get("action"),
                    "amount": event.data.get("amount"),
                    "currency": event.data.get("currency"),
                    "status": "pending",
                    "requested_at": event.occurred_at,
                }
            elif event.event_type == EventType.APPROVAL_RESOLVED:
                approval_id = str(event.data["approval_id"])
                if approval_id in approvals:
                    approvals[approval_id].update({
                        "status": event.data.get("status", "approved"),
                        "resolved_at": event.occurred_at,
                        "resolved_by": event.data.get("resolved_by"),
                    })
        return approvals

    def pending_approvals(self) -> list[dict[str, Any]]:
        with self._lock:
            return [
                copy.deepcopy(approval)
                for approval in self._approval_projection().values()
                if approval["status"] == "pending"
            ]

    def approve(self, approval_id: str, resolved_by: str = "admin") -> dict[str, Any]:
        """Resolve one approval by appending an event, never editing history."""
        with self._lock:
            approval = self._approval_projection().get(approval_id)
            if approval is None:
                raise KeyError(f"Approval '{approval_id}' was not found")
            if approval["status"] != "pending":
                raise ValueError(f"Approval '{approval_id}' is already resolved")
            event = self.emit(
                EventType.APPROVAL_RESOLVED,
                run_id=approval["run_id"],
                team=approval["team"],
                agent_id=approval["agent_id"],
                data={
                    "approval_id": approval_id,
                    "status": "approved",
                    "resolved_by": resolved_by,
                },
            )
            result = copy.deepcopy(approval)
            result.update({
                "status": "approved",
                "resolved_at": event.occurred_at,
                "resolved_by": resolved_by,
            })
            return result

    def activity(self, limit: int = 50) -> list[dict[str, Any]]:
        with self._lock:
            events = [event for event in self._events if event.event_type == EventType.AGENT_ACTIVITY]
            return [event.to_dict() for event in reversed(events[-limit:])]

    def financials(self) -> list[dict[str, Any]]:
        with self._lock:
            totals: dict[str, dict[str, Any]] = {}
            for event in self._events:
                if event.event_type not in (EventType.REVENUE, EventType.COST) or not event.team:
                    continue
                team = totals.setdefault(event.team, {
                    "team": event.team, "currency": event.data.get("currency", "USD"),
                    "revenue": 0, "costs": 0, "profit": 0,
                })
                amount = event.data.get("amount", 0)
                if event.event_type == EventType.REVENUE:
                    team["revenue"] += amount
                else:
                    team["costs"] += amount
            for team in totals.values():
                team["profit"] = team["revenue"] - team["costs"]
            return [copy.deepcopy(totals[name]) for name in sorted(totals)]

    def failed_actions(self, limit: int = 50) -> list[dict[str, Any]]:
        with self._lock:
            events = [event for event in self._events if event.event_type == EventType.FAILED_ACTION]
            return [event.to_dict() for event in reversed(events[-limit:])]

    def health(self) -> list[dict[str, Any]]:
        with self._lock:
            latest: dict[str, OverseerEvent] = {}
            for event in self._events:
                if event.event_type == EventType.AGENT_HEALTH and event.agent_id:
                    latest[event.agent_id] = event
            return [event.to_dict() for event in (latest[name] for name in sorted(latest))]

    def dashboard(self, limit: int = 50) -> dict[str, Any]:
        return {
            "activity": self.activity(limit),
            "pending_approvals": self.pending_approvals(),
            "financials": self.financials(),
            "failed_actions": self.failed_actions(limit),
            "health": self.health(),
        }


class AuthError(Exception):
    """HTTP-friendly authentication or authorization failure."""

    def __init__(self, status_code: int, message: str):
        super().__init__(message)
        self.status_code = status_code


class OverseerAuth:
    """Bearer-token authentication with a local-only deterministic fallback."""

    _ROLE_ORDER = {Role.VIEWER: 1, Role.OPERATOR: 2, Role.ADMIN: 3}

    def __init__(self, tokens: Mapping[Role | str, str]):
        normalized: dict[str, Role] = {}
        for role, token in tokens.items():
            role = Role(role)
            if token:
                normalized[token] = role
        self._tokens = normalized

    @classmethod
    def from_environment(cls, allow_defaults: bool = True) -> "OverseerAuth":
        defaults = {
            Role.VIEWER: "overseer-viewer",
            Role.OPERATOR: "overseer-operator",
            Role.ADMIN: "overseer-admin",
        }
        tokens = {}
        for role in Role:
            env_name = f"UNITY_MCP_OVERSEER_{role.value.upper()}_TOKEN"
            token = os.environ.get(env_name)
            if token is None and allow_defaults:
                token = defaults[role]
            tokens[role] = token or ""
        return cls(tokens)

    def authorize(self, authorization: str | None, minimum_role: Role = Role.VIEWER) -> AuthPrincipal:
        if not authorization or not authorization.startswith("Bearer "):
            raise AuthError(401, "Bearer token required")
        token = authorization[7:].strip()
        role = self._tokens.get(token)
        if role is None:
            raise AuthError(401, "Invalid bearer token")
        if self._ROLE_ORDER[role] < self._ROLE_ORDER[minimum_role]:
            raise AuthError(403, f"{minimum_role.value} role required")
        return AuthPrincipal(role=role)


def _response(payload: dict[str, Any], status_code: int = 200) -> JSONResponse:
    return JSONResponse({"success": True, **payload}, status_code=status_code)


def _error(error: AuthError | Exception, status_code: int | None = None) -> JSONResponse:
    return JSONResponse(
        {"success": False, "error": str(error)},
        status_code=status_code or getattr(error, "status_code", 500),
    )


def _limit(request: Request) -> int:
    try:
        value = int(request.query_params.get("limit", "50"))
    except ValueError as exc:
        raise ValueError("limit must be an integer between 1 and 200") from exc
    if not 1 <= value <= 200:
        raise ValueError("limit must be an integer between 1 and 200")
    return value


def register_overseer_routes(
    mcp: Any,
    ledger: EventLedger | None = None,
    auth: OverseerAuth | None = None,
    *,
    include_internal_routes: bool = True,
    allow_default_tokens: bool = True,
) -> EventLedger:
    """Register authenticated dashboard routes and optional local action routes."""
    ledger = ledger or EventLedger()
    auth = auth or OverseerAuth.from_environment(allow_defaults=allow_default_tokens)

    async def require(request: Request, role: Role = Role.VIEWER) -> AuthPrincipal:
        return auth.authorize(request.headers.get("authorization"), role)

    @mcp.custom_route("/api/overseer/activity", methods=["GET"])
    async def overseer_activity(request: Request) -> JSONResponse:
        try:
            await require(request)
            return _response({"activity": ledger.activity(_limit(request))})
        except (AuthError, ValueError) as exc:
            return _error(exc, getattr(exc, "status_code", 400))

    @mcp.custom_route("/api/overseer/approvals", methods=["GET"])
    async def overseer_approvals(request: Request) -> JSONResponse:
        try:
            await require(request)
            return _response({"pending_approvals": ledger.pending_approvals()})
        except AuthError as exc:
            return _error(exc)

    @mcp.custom_route("/api/overseer/financials", methods=["GET"])
    async def overseer_financials(request: Request) -> JSONResponse:
        try:
            await require(request)
            return _response({"financials": ledger.financials()})
        except AuthError as exc:
            return _error(exc)

    @mcp.custom_route("/api/overseer/failed-actions", methods=["GET"])
    async def overseer_failed_actions(request: Request) -> JSONResponse:
        try:
            await require(request)
            return _response({"failed_actions": ledger.failed_actions(_limit(request))})
        except (AuthError, ValueError) as exc:
            return _error(exc, getattr(exc, "status_code", 400))

    @mcp.custom_route("/api/overseer/health", methods=["GET"])
    async def overseer_health(request: Request) -> JSONResponse:
        try:
            await require(request)
            return _response({"health": ledger.health()})
        except AuthError as exc:
            return _error(exc)

    @mcp.custom_route("/api/overseer/dashboard", methods=["GET"])
    async def overseer_dashboard(request: Request) -> JSONResponse:
        try:
            await require(request)
            return _response({"dashboard": ledger.dashboard(_limit(request))})
        except (AuthError, ValueError) as exc:
            return _error(exc, getattr(exc, "status_code", 400))

    if include_internal_routes:
        @mcp.custom_route("/api/internal/overseer/simulate", methods=["POST"])
        async def overseer_simulate(request: Request) -> JSONResponse:
            try:
                await require(request, Role.OPERATOR)
                body = await request.json()
                report = ledger.run_simulation(body.get("cycles", 1) if isinstance(body, dict) else 1)
                return _response({"simulation": report.to_dict()}, status_code=201)
            except AuthError as exc:
                return _error(exc)
            except (ValueError, TypeError) as exc:
                return _error(exc, 400)

        @mcp.custom_route(
            "/api/internal/overseer/approvals/{approval_id}/approve",
            methods=["POST"],
        )
        async def overseer_approve(request: Request) -> JSONResponse:
            try:
                principal = await require(request, Role.ADMIN)
                approval_id = request.path_params["approval_id"]
                return _response({
                    "approval": ledger.approve(approval_id, resolved_by=principal.role.value),
                })
            except AuthError as exc:
                return _error(exc)
            except (KeyError, ValueError) as exc:
                return _error(exc, 404 if isinstance(exc, KeyError) else 409)

    # Expose the ledger for embedding applications and focused tests without
    # making it part of the HTTP API.
    setattr(mcp, "overseer_ledger", ledger)
    return ledger
