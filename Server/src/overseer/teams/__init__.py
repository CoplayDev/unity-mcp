"""Provider-neutral definitions for the five commercial agent servers."""

from .content_operations import TEAM_SPEC as CONTENT_OPERATIONS
from .customer_support import TEAM_SPEC as CUSTOMER_SUPPORT
from .inventory_operations import TEAM_SPEC as INVENTORY_OPERATIONS
from .lead_generation import TEAM_SPEC as LEAD_GENERATION
from .market_intelligence import TEAM_SPEC as MARKET_INTELLIGENCE

TEAM_SPECS = (
    LEAD_GENERATION,
    CUSTOMER_SUPPORT,
    CONTENT_OPERATIONS,
    MARKET_INTELLIGENCE,
    INVENTORY_OPERATIONS,
)

__all__ = [
    "CONTENT_OPERATIONS",
    "CUSTOMER_SUPPORT",
    "INVENTORY_OPERATIONS",
    "LEAD_GENERATION",
    "MARKET_INTELLIGENCE",
    "TEAM_SPECS",
]
