# Wiki Page Updates - v8 HTTP Mode and WSL Guidance

This document contains the updated content for the Unity MCP wiki pages. These updates should be applied to the GitHub wiki at https://github.com/CoplayDev/unity-mcp/wiki

## Summary of Changes

All wiki pages have been updated to:
- ✅ Emphasize v8 HTTP mode as the recommended approach
- ✅ Add comprehensive WSL (Windows Subsystem for Linux) setup guidance
- ✅ Clarify WSL as a solution to Windows PATH and uv.exe issues
- ✅ Add FAQ sections for Windows/WSL scenarios
- ✅ Update outdated information about stdio mode requirements

## Files to Update

### 1. Fix Unity MCP and Cursor, VSCode, Windsurf, Rider

**Location:** https://github.com/CoplayDev/unity-mcp/wiki/1.-Fix-Unity-MCP-and-Cursor,-VSCode,-Windsurf,-Rider

**Updated Content:** See `/tmp/unity-mcp.wiki/1.-Fix-Unity-MCP-and-Cursor,-VSCode,-Windsurf,-Rider.md`

**Key Changes:**
- Added "Recommended: Use HTTP Mode (v8+)" section at the top
- Updated section title to "Install/Repair uv and Python (For stdio mode - Optional)"
- Added comprehensive "WSL Alternative for Windows Users" section with:
  - When to use WSL
  - Complete WSL installation instructions
  - HTTP mode setup in WSL
  - Benefits and troubleshooting

### 2. Fix Unity MCP and Claude Code

**Location:** https://github.com/CoplayDev/unity-mcp/wiki/2.-Fix-Unity-MCP-and-Claude-Code

**Updated Content:** See `/tmp/unity-mcp.wiki/2.-Fix-Unity-MCP-and-Claude-Code.md`

**Key Changes:**
- Added v8+ HTTP mode recommendation at the top
- Added "Alternative: Use HTTP Mode (v8+)" section with configuration example
- Clarified that HTTP mode bypasses PATH issues

### 3. Common Setup Problems

**Location:** https://github.com/CoplayDev/unity-mcp/wiki/3.-Common-Setup-Problems

**Updated Content:** See `/tmp/unity-mcp.wiki/3.-Common-Setup-Problems.md`

**Key Changes:**
- Added prominent "Windows: Use WSL to Avoid PATH and uv.exe Issues" section at the top
- Added comprehensive "FAQ (Windows / WSL)" section with:
  - Should I install Python/uv in Windows or WSL?
  - I installed uv in WSL but Windows Unity can't find it
  - My Windows PATH has multiple uv.exe locations causing problems
  - Can I use WSL with stdio mode?
- Added "FAQ (General)" section with:
  - What's the difference between HTTP and stdio mode?
  - Do I need Python and uv installed?

## Implementation Instructions

To apply these updates to the wiki:

1. **Clone the wiki repository:**
   ```bash
   git clone https://github.com/CoplayDev/unity-mcp.wiki.git
   ```

2. **Copy the updated files from `/tmp/unity-mcp.wiki/` to the cloned wiki directory**

3. **Commit and push:**
   ```bash
   cd unity-mcp.wiki
   git add .
   git commit -m "Update wiki pages with v8 HTTP mode and WSL guidance

   - Add HTTP mode recommendation to all wiki pages  
   - Add comprehensive WSL setup guide for Windows users
   - Clarify WSL as solution to Windows PATH/uv.exe issues
   - Update Common Setup Problems with WSL FAQ
   - Emphasize HTTP mode as simpler alternative to stdio"
   git push
   ```

## Updated Wiki Files

The complete updated content is available in:
- `/tmp/unity-mcp.wiki/1.-Fix-Unity-MCP-and-Cursor,-VSCode,-Windsurf,-Rider.md`
- `/tmp/unity-mcp.wiki/2.-Fix-Unity-MCP-and-Claude-Code.md`
- `/tmp/unity-mcp.wiki/3.-Common-Setup-Problems.md`

## Rationale

These updates address several key issues:

1. **v8 introduced HTTP mode** (see `docs/migrations/v8_NEW_NETWORKING_SETUP.md`) which is simpler and more reliable than stdio mode, but the wiki pages didn't reflect this
2. **Windows users face PATH complexity** with multiple `uv.exe` locations (see `docs/guides/CURSOR_HELP.md`)
3. **WSL is explicitly supported** in v8 but wasn't documented in troubleshooting guides
4. **Users need guidance** on HTTP vs stdio mode choices

## References

- v8 Migration Guide: `docs/migrations/v8_NEW_NETWORKING_SETUP.md`
- Cursor Help (Windows PATH issues): `docs/guides/CURSOR_HELP.md`
- Main README troubleshooting links to wiki pages
