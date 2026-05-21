using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Models;

/// <summary>
/// Agent 自然语言任务请求
/// </summary>
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
}

/// <summary>
/// Agent 执行的单步动作
/// </summary>
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

/// <summary>
/// Agent 任务执行结果
/// </summary>
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
}

/// <summary>
/// 可用工具的描述（给 LLM 看的工具清单）
/// </summary>
public class ToolDescriptor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new();
}