"""Specialized customer support server definition."""

TEAM_SPEC = {
    "team_id": "customer-support",
    "name": "Specialized Customer Support",
    "purpose": "Resolve approved tier-one support requests using customer documentation and systems.",
    "agents": [
        "ticket-triage-agent",
        "knowledge-retrieval-agent",
        "response-drafting-agent",
        "escalation-agent",
        "support-analyst",
    ],
    "requires_human_approval_for": ["refund", "account_change", "production_action"],
    "planned_revenue_model": "subscription priced by support volume",
}
