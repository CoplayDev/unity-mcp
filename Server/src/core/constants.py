"""Server-wide protocol constants."""

# HTTP header name for API key authentication
API_KEY_HEADER = "X-API-Key"

# HTTP header carrying the local-bridge shared secret in HTTP-local mode
# (harden/security, R5). Distinct from the remote-hosted API key.
BRIDGE_TOKEN_HEADER = "X-Bridge-Token"
