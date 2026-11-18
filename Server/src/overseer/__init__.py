"""Provider-neutral control-plane primitives for supervising commercial AI agents."""

from .ledger import EventLedger, LedgerEvent, TeamSummary
from .simulation import run_support_ticket_simulation
from .teams import TEAM_SPECS


def create_overseer_app(*args, **kwargs):
    """Lazily create the optional FastAPI dashboard adapter."""
    from .api import create_overseer_app as _create_overseer_app

    return _create_overseer_app(*args, **kwargs)


__all__ = [
    "EventLedger",
    "LedgerEvent",
    "TeamSummary",
    "create_overseer_app",
    "run_support_ticket_simulation",
    "TEAM_SPECS",
]
