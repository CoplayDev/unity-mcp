using System.Collections.Generic;

namespace MCPForUnity.Editor.Services
{
    public class ToolSyncResult
    {
        public int CopiedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }
        public List<string> Messages { get; set; } = new List<string>();
        public bool Success => ErrorCount == 0;
    }

    public interface IToolSyncService
    {
        ToolSyncResult SyncProjectTools(string destToolsDir);
    }
}
