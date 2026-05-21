using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Models;

/// <summary>
/// V0.3 see command unified output
/// </summary>
public class SeeResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = "see";

    [JsonPropertyName("active_window")]
    public SeeWindowInfo? ActiveWindow { get; set; }

    [JsonPropertyName("screenshot")]
    public SeeScreenshot? Screenshot { get; set; }

    [JsonPropertyName("ui_tree_summary")]
    public UiTreeSummary? UiTreeSummary { get; set; }

    [JsonPropertyName("clickable_elements")]
    public List<SeeElement> ClickableElements { get; set; } = new();

    [JsonPropertyName("editable_elements")]
    public List<SeeElement> EditableElements { get; set; } = new();

    [JsonPropertyName("text_elements")]
    public List<SeeElement> TextElements { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class SeeWindowInfo
{
    [JsonPropertyName("handle")]
    public long Handle { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("process_name")]
    public string ProcessName { get; set; } = "";

    [JsonPropertyName("process_id")]
    public int ProcessId { get; set; }

    [JsonPropertyName("class_name")]
    public string ClassName { get; set; } = "";

    [JsonPropertyName("rect")]
    public RectInfo? Rect { get; set; }
}

public class SeeScreenshot
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

public class UiTreeSummary
{
    [JsonPropertyName("total_elements")]
    public int TotalElements { get; set; }

    [JsonPropertyName("control_type_counts")]
    public Dictionary<string, int> ControlTypeCounts { get; set; } = new();

    [JsonPropertyName("depth")]
    public int Depth { get; set; }
}

/// <summary>
/// V0.3 standardized element with stable element_id
/// </summary>
public class SeeElement
{
    [JsonPropertyName("element_id")]
    public string ElementId { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("automation_id")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("control_type")]
    public string ControlType { get; set; } = "";

    [JsonPropertyName("bounding_box")]
    public RectInfo? BoundingBox { get; set; }

    [JsonPropertyName("click_point")]
    public ClickPoint? ClickPoint { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("is_offscreen")]
    public bool IsOffscreen { get; set; }

    [JsonPropertyName("supported_patterns")]
    public List<string> SupportedPatterns { get; set; } = new();

    [JsonPropertyName("source")]
    public string Source { get; set; } = "uia";

    /// <summary>
    /// True if the element name suggests a dangerous operation (close/delete/pay/etc.)
    /// </summary>
    [JsonPropertyName("is_dangerous")]
    public bool IsDangerous { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("class_name")]
    public string? ClassName { get; set; }
}

public class ClickPoint
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

/// <summary>
/// Element catalog loaded from a see JSON file for click-element --from
/// </summary>
public class SeeElementCatalog
{
    [JsonPropertyName("active_window")]
    public SeeWindowInfo? ActiveWindow { get; set; }

    [JsonPropertyName("elements")]
    public List<SeeElement> Elements { get; set; } = new();
}