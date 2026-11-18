"""Automated content operations server definition."""

TEAM_SPEC = {
    "team_id": "content-operations",
    "name": "Automated Content and Repurposing",
    "purpose": "Turn approved audio or video into edited clips, transcripts, and channel-ready drafts.",
    "agents": [
        "media-intake-agent",
        "transcription-agent",
        "highlight-editor-agent",
        "social-copy-agent",
        "content-quality-agent",
    ],
    "requires_human_approval_for": ["publication", "brand_claim", "copyrighted_asset_use"],
    "planned_revenue_model": "monthly content-operations package",
}
