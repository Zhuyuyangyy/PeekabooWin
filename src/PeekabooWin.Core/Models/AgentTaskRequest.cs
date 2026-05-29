using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Models;

public class AgentTaskRequest
{
    [JsonPropertyName("task")]
    public string Task { get; set; } = "";

    [JsonPropertyName("context")]
    public string? Context { get; set; }

    [JsonPropertyName("max_steps")]
    public int MaxSteps { get; set; } = 5;

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; set; } = false;

    [JsonPropertyName("timeout_ms")]
    public int TimeoutMs { get; set; } = 30000;
}

public class AgentStep
{
    [JsonPropertyName("step")]
    public int Step { get; set; }

    [JsonPropertyName("thought")]
    public string Thought { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("args")]
    public Dictionary<string, string> Args { get; set; } = new();

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class AgentTaskResponse
{
    [JsonPropertyName("task")]
    public string Task { get; set; } = "";

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("steps")]
    public List<AgentStep> Steps { get; set; } = new();

    [JsonPropertyName("final_result")]
    public string? FinalResult { get; set; }

    [JsonPropertyName("llm_model")]
    public string LlmModel { get; set; } = "minimax/MiniMax-M2.7";

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("timeout_ms")]
    public int TimeoutMs { get; set; }

    [JsonPropertyName("cancelled")]
    public bool Cancelled { get; set; }

    [JsonPropertyName("timeout_triggered")]
    public bool TimeoutTriggered { get; set; }

    [JsonPropertyName("parser_mode")]
    public string ParserMode { get; set; } = "none";

    [JsonPropertyName("llm_enabled")]
    public bool LlmEnabled { get; set; } = true;

    [JsonPropertyName("fallback_reason")]
    public string FallbackReason { get; set; } = "";

    [JsonPropertyName("llm_error_code")]
    public string LlmErrorCode { get; set; } = "";
}

public class ToolDescriptor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new();
}