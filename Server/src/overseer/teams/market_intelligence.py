"""Financial and market intelligence server definition."""

TEAM_SPEC = {
    "team_id": "market-intelligence",
    "name": "Financial and Market Intelligence",
    "purpose": "Collect public business signals and produce traceable research reports and metrics.",
    "agents": [
        "source-monitor-agent",
        "filings-extraction-agent",
        "competitor-pricing-agent",
        "report-builder-agent",
        "evidence-review-agent",
    ],
    "requires_human_approval_for": ["client_report", "investment_claim", "restricted_source_access"],
    "planned_revenue_model": "custom research packs or recurring intelligence subscription",
}
