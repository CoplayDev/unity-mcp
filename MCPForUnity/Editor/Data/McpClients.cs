using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Data
{
    public class McpClients
    {
        public List<McpClient> clients = new()
        {
            // 1) Cursor
            new()
            {
                name = "Cursor",
                windowsConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cursor",
                    "mcp.json"
                ),
                macConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cursor",
                    "mcp.json"
                ),
                linuxConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cursor",
                    "mcp.json"
                ),
                mcpType = McpTypes.Cursor,
                configStatus = "Not Configured",
            },
            // 2) Claude Code
            new()
            {
                name = "Claude Code",
                windowsConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude.json"
                ),
                macConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude.json"
                ),
                linuxConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude.json"
                ),
                mcpType = McpTypes.ClaudeCode,
                configStatus = "Not Configured",
            },
            // 3) Windsurf
            new()
            {
                name = "Windsurf",
                windowsConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codeium",
                    "windsurf",
                    "mcp_config.json"
                ),
                macConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codeium",
                    "windsurf",
                    "mcp_config.json"
                ),
                linuxConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codeium",
                    "windsurf",
                    "mcp_config.json"
                ),
                mcpType = McpTypes.Windsurf,
                configStatus = "Not Configured",
            },
            // 4) Claude Desktop
            new()
            {
                name = "Claude Desktop",
                windowsConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Claude",
                    "claude_desktop_config.json"
                ),

                macConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    "Claude",
                    "claude_desktop_config.json"
                ),
                linuxConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "Claude",
                    "claude_desktop_config.json"
                ),

                mcpType = McpTypes.ClaudeDesktop,
                configStatus = "Not Configured",
            },
            // 5) VSCode GitHub Copilot
            new()
            {
                name = "VSCode GitHub Copilot",
                // Windows path is canonical under %AppData%\Code\User
                windowsConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Code",
                    "User",
                    "mcp.json"
                ),
                // macOS: ~/Library/Application Support/Code/User/mcp.json
                macConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    "Code",
                    "User",
                    "mcp.json"
                ),
                // Linux: ~/.config/Code/User/mcp.json
                linuxConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "Code",
                    "User",
                    "mcp.json"
                ),
                mcpType = McpTypes.VSCode,
                configStatus = "Not Configured",
            },
            // Trae IDE
            new()
            {
                name = "Trae",
                // Windows: %AppData%\Trae\mcp.json
                windowsConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Trae",
                    "mcp.json"
                ),
                // macOS: ~/Library/Application Support/Trae/mcp.json
                macConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    "Trae",
                    "mcp.json"
                ),
                // Linux: ~/.config/Trae/mcp.json
                linuxConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "Trae",
                    "mcp.json"
                ),
                mcpType = McpTypes.Trae,
                configStatus = "Not Configured",
            },
            // 3) Kiro
            new()
            {
                name = "Kiro",
                windowsConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".kiro",
                    "settings",
                    "mcp.json"
                ),
                macConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".kiro",
                    "settings",
                    "mcp.json"
                ),
                linuxConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".kiro",
                    "settings",
                    "mcp.json"
                ),
                mcpType = McpTypes.Kiro,
                configStatus = "Not Configured",
            },
            // 4) Codex CLI
            new()
            {
                name = "Codex CLI",
                windowsConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codex",
                    "config.toml"
                ),
                macConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codex",
                    "config.toml"
                ),
                linuxConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codex",
                    "config.toml"
                ),
                mcpType = McpTypes.Codex,
                configStatus = "Not Configured",
            },
        };

        // Initialize status enums after construction
        public McpClients()
        {
            foreach (var client in clients)
            {
                if (client.configStatus == "Not Configured")
                {
                    client.status = McpStatus.NotConfigured;
                }
            }
        }
    }
}
