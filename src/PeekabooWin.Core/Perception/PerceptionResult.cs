using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Perception;

/// <summary>
/// 感知路由器返回结果
/// </summary>
public class PerceptionResult
{
    [JsonPropertyName("element")]
    public GroundedElement? Element { get; set; }

    [JsonPropertyName("source")]
    public PerceptionSource Source { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("latency_ms")]
    public double LatencyMs { get; set; }

    [JsonPropertyName("fallback_reason")]
    public string? FallbackReason { get; set; }

    [JsonPropertyName("candidates_tried")]
    public int CandidatesTried { get; set; }

    public bool IsConfident => Element != null && Confidence >= 0.7;

    public static PerceptionResult NotFound(string reason = "Element not found via any perception channel")
    {
        return new PerceptionResult
        {
            Source = PerceptionSource.NotFound,
            Confidence = 0,
            FallbackReason = reason
        };
    }
}

/// <summary>
/// LLM 视觉 grounding 结果
/// </summary>
public class LlmGroundingResult
{
    [JsonPropertyName("screen_type")]
    public string ScreenType { get; set; } = "";

    [JsonPropertyName("window_title_estimate")]
    public string WindowTitleEstimate { get; set; } = "";

    [JsonPropertyName("elements")]
    public List<GroundedElement> Elements { get; set; } = new();

    [JsonPropertyName("interactive_summary")]
    public string InteractiveSummary { get; set; } = "";

    public double LatencyMs { get; set; }
    public string ModelUsed { get; set; } = "";
}

/// <summary>
/// 全场景理解结果
/// </summary>
public class ScreenUnderstanding
{
    public string ScreenType { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public List<GroundedElement> Elements { get; set; } = new();
    public string Summary { get; set; } = "";
    public PerceptionSource Source { get; set; }
    public double LatencyMs { get; set; }
}

/// <summary>
/// UIA Invoke 结果
/// </summary>
public class InvokeResult
{
    public bool Success { get; set; }
    public string Method { get; set; } = ""; // InvokePattern, ValuePattern, CoordinateClick
    public string? ErrorDetail { get; set; }
}
