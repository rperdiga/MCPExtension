using System;

namespace MCPExtension.MCP
{
    public enum ToolCallStatus
    {
        Started,
        Completed,
        Failed
    }

    public class ToolCallEventArgs
    {
        public string CallId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public ToolCallStatus Status { get; set; }
        public long? DurationMs { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
