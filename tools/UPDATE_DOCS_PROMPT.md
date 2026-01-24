# LLM Prompt for Updating Documentation

Copy and paste this prompt into your LLM when you need to update documentation after adding/removing/modifying MCP tools or resources.

## Example Usage

After adding a new tool called "manage_new_feature" and a new resource called "feature_resource", you would:
1. Copy the prompt in the section below
2. Paste it into your LLM
3. The LLM will analyze the codebase and update all documentation files
4. Review the changes and run the check script to verify

This ensures all documentation stays in sync across the repository.

---

## Prompt

I've just made changes to MCP tools or resources in this Unity MCP repository. Please update all documentation files to keep them in sync.

Here's what you need to do:

1. **Check the current tools and resources** by examining:
   - `Server/src/services/tools/` - Python tool implementations (look for @mcp_for_unity_tool decorators)
   - `Server/src/services/resources/` - Python resource implementations (look for @mcp_for_unity_resource decorators)

2. **Update these files**:
   
   a) **manifest.json** (root directory)
      - Update the "tools" array (lines 27-57)
      - Each tool needs: {"name": "tool_name", "description": "Brief description"}
      - Keep tools in alphabetical order
      - Note: Resources are not listed in manifest.json, only tools
   
   b) **README.md** (root directory)
      - Update "Available Tools" section (around line 78-79)
      - Format: `tool1` • `tool2` • `tool3`
      - Keep the same order as manifest.json
   
   c) **README.md** - Resources section
      - Update "Available Resources" section (around line 81-82)
      - Format: `resource1` • `resource2` • `resource3`
      - Resources come from Server/src/services/resources/ files
      - Keep resources in alphabetical order
   
   d) **docs/i18n/README-zh.md**
      - Find and update the "可用工具" (Available Tools) section
      - Find and update the "可用资源" (Available Resources) section
      - Keep tool/resource names in English, but you can translate descriptions if helpful

3. **Important formatting rules**:
   - Use backticks around tool/resource names
   - Separate items with • (bullet point)
   - Keep lists on single lines when possible
   - Maintain alphabetical ordering
   - Tools and resources are listed separately in documentation

4. **After updating**, run this check to verify:
   ```bash
   python3 tools/check_docs_sync.py
   ```
   It should show "All documentation is synchronized!"

Please show me the exact changes you're making to each file, and explain any discrepancies you find.

---
