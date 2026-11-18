"""Lead generation and sales qualification server definition."""

TEAM_SPEC = {
    "team_id": "lead-generation",
    "name": "Lead Generation and Sales Qualification",
    "purpose": "Find suitable prospects, prepare personalized drafts, and qualify inbound leads.",
    "agents": [
        "prospect-researcher",
        "data-enrichment-agent",
        "personalization-agent",
        "qualification-agent",
        "pipeline-analyst",
    ],
    "requires_human_approval_for": ["outbound_message", "contact_import", "pricing_offer"],
    "planned_revenue_model": "monthly retainer or qualified-meeting fee",
}
