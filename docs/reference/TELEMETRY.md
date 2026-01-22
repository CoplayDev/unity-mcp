# MCP for Unity Telemetry

MCP for Unity includes privacy-focused, anonymous telemetry to help us improve the product. This document explains what data is collected, how to opt out, and our privacy practices.

## 🔒 Privacy First

- **Anonymous**: We use randomly generated UUIDs - no personal information
- **Non-blocking**: Telemetry never interferes with your Unity workflow  
- **Easy opt-out**: Simple environment variable or Unity Editor setting
- **Transparent**: All collected data types are documented here

## 📊 What We Collect

### Usage Analytics
- **Tool Usage**: Which MCP tools you use (manage_script, manage_scene, etc.)
- **Performance**: Execution times and success/failure rates
- **System Info**: Unity version, platform (Windows/Mac/Linux), MCP version
- **Milestones**: First-time usage events (first script creation, first tool use, etc.)

### Technical Diagnostics  
- **Connection Events**: Bridge startup/connection success/failures
- **Error Reports**: Anonymized error messages (truncated to 200 chars)
- **Server Health**: Startup time, connection latency

### What We **DON'T** Collect
- ❌ Your code or script contents
- ❌ Project names, file names, or paths
- ❌ Personal information or identifiers
- ❌ Sensitive project data
- ❌ IP addresses (beyond what's needed for HTTP requests)

## 🚫 How to Opt Out

### Method 1: Environment Variable (Recommended)
Set any of these environment variables to `true`:

```bash
# Disable all telemetry
export DISABLE_TELEMETRY=true

# MCP for Unity specific
export UNITY_MCP_DISABLE_TELEMETRY=true

# MCP protocol wide  
export MCP_DISABLE_TELEMETRY=true
```

### Method 2: Unity Editor (Coming Soon)
In Unity Editor: `Window > MCP for Unity > Settings > Disable Telemetry`

### Method 3: Manual Config
Add to your MCP client config:
```json
{
  "env": {
    "DISABLE_TELEMETRY": "true"
  }
}
```

## 🔧 Technical Implementation

### Architecture
- **Python Server**: Core telemetry collection and transmission
- **Unity Bridge**: Local event collection from Unity Editor
- **Anonymous UUIDs**: Generated per-installation for aggregate analytics
- **Thread-safe**: Non-blocking background transmission
- **Fail-safe**: Errors never interrupt your workflow

### Data Storage
Telemetry data is stored locally in:
- **Windows**: `%APPDATA%\UnityMCP\`
- **macOS**: `~/Library/Application Support/UnityMCP/`  
- **Linux**: `~/.local/share/UnityMCP/`

Files created:
- `customer_uuid.txt`: Anonymous identifier
- `milestones.json`: One-time events tracker

### Data Transmission
- **Endpoint**: `https://api-prod.coplay.dev/telemetry/events`
- **Method**: HTTPS POST with JSON payload
- **Retry**: Background thread with graceful failure
- **Timeout**: 10 second timeout, no retries on failure

## 📈 How We Use This Data

### Product Improvement
- **Feature Usage**: Understand which tools are most/least used
- **Performance**: Identify slow operations to optimize
- **Reliability**: Track error rates and connection issues
- **Compatibility**: Ensure Unity version compatibility

### Development Priorities
- **Roadmap**: Focus development on most-used features
- **Bug Fixes**: Prioritize fixes based on error frequency
- **Platform Support**: Allocate resources based on platform usage
- **Documentation**: Improve docs for commonly problematic areas

### What We Don't Do
- ❌ Sell data to third parties
- ❌ Use data for advertising/marketing
- ❌ Track individual developers
- ❌ Store sensitive project information

## 🛠️ For Developers

### Custom Telemetry Events
```python
core.telemetry import record_telemetry, RecordType

record_telemetry(RecordType.USAGE, {
    "custom_event": "my_feature_used",
    "metadata": "optional_data"
})
```

### Telemetry Status Check
```python  
core.telemetry import is_telemetry_enabled

if is_telemetry_enabled():
    print("Telemetry is active")
else:
    print("Telemetry is disabled")
```

## 📋 Data Retention Policy

- **Aggregated Data**: Retained indefinitely for product insights
- **Raw Events**: Automatically purged after 90 days
- **Personal Data**: None collected, so none to purge
- **Opt-out**: Immediate - no data sent after opting out

## 🤝 Contact & Transparency

- **Questions**: [Discord Community](https://discord.gg/y4p8KfzrN4)
- **Issues**: [GitHub Issues](https://github.com/CoplayDev/unity-mcp/issues)
- **Privacy Concerns**: Create a GitHub issue with "Privacy" label
- **Source Code**: All telemetry code is open source in this repository

## 📊 Example Telemetry Event

Here's what a typical telemetry event looks like:

```json
{
  "record": "tool_execution",
  "timestamp": 1704067200,
  "customer_uuid": "550e8400-e29b-41d4-a716-446655440000", 
  "session_id": "abc123-def456-ghi789",
  "version": "3.0.2",
  "platform": "posix",
  "data": {
    "tool_name": "manage_script",
    "success": true,
    "duration_ms": 42.5
  }
}
```

Notice:
- ✅ Anonymous UUID (randomly generated)
- ✅ Tool performance metrics  
- ✅ Success/failure tracking
- ❌ No code content
- ❌ No project information
- ❌ No personal data

---

*MCP for Unity Telemetry is designed to respect your privacy while helping us build a better tool. Thank you for helping improve MCP for Unity!*
