"""Inventory and operations server definition."""

TEAM_SPEC = {
    "team_id": "inventory-operations",
    "name": "Inventory and Operations Management",
    "purpose": "Monitor stock, identify demand signals, and prepare purchasing recommendations.",
    "agents": [
        "inventory-monitor-agent",
        "demand-forecast-agent",
        "reorder-planning-agent",
        "purchase-order-agent",
        "operations-analyst",
    ],
    "requires_human_approval_for": ["purchase_order", "supplier_change", "stock_adjustment"],
    "planned_revenue_model": "setup fee plus monthly maintenance",
}
