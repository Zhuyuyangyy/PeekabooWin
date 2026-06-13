using System.Text.Json.Serialization;
using System.Windows.Automation;

namespace PeekabooWin.Core.Perception;

/// <summary>
/// 统一元素模型 — 不管来源（UIA/LLM/OCR），都归一化为 GroundedElement
/// </summary>
public class GroundedElement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // button, textbox, checkbox, dropdown, link, icon, menu, tab, slider

    [JsonPropertyName("label")]
    public string Label { get; set; } = ""; // visible text or functional description

    [JsonPropertyName("automation_id")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("bbox")]
    public BoundingBox BBox { get; set; } = new();

    [JsonPropertyName("click_point")]
    public ClickPoint ClickPoint { get; set; } = new();

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "normal"; // enabled, disabled, focused, checked, unchecked, empty, filled

    [JsonPropertyName("source")]
    public PerceptionSource Source { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    // UIA-specific: raw element reference (not serialized)
    [JsonIgnore]
    public AutomationElement? RawUiaElement { get; set; }

    [JsonPropertyName("supported_patterns")]
    public string[]? SupportedPatterns { get; set; }

    /// <summary>
    /// 推荐的点击策略
    /// </summary>
    [JsonPropertyName("click_strategy")]
    public ClickStrategy PreferredClickStrategy
    {
        get
        {
            if (RawUiaElement != null && SupportedPatterns?.Contains("Invoke", StringComparer.OrdinalIgnoreCase) == true)
                return ClickStrategy.UIA_Invoke;
            if (RawUiaElement != null && SupportedPatterns?.Contains("Value", StringComparer.OrdinalIgnoreCase) == true)
                return ClickStrategy.UIA_Value;
            return ClickStrategy.CoordinateClick;
        }
    }
}

public class ClickPoint
{
    [JsonPropertyName("x")]
    public int X { get; set; }
    [JsonPropertyName("y")]
    public int Y { get; set; }
}

public enum PerceptionSource
{
    UIA,
    UIA_Fuzzy,
    LLM_Vision,
    OCR_Fallback,
    NotFound
}

public enum ClickStrategy
{
    UIA_Invoke,
    UIA_Value,
    CoordinateClick,
    NoAction
}
