using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Models;

public class UIAElementInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("automation_id")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("control_type")]
    public string ControlType { get; set; } = "";

    [JsonPropertyName("class_name")]
    public string? ClassName { get; set; }

    [JsonPropertyName("bounding_box")]
    public RectInfo? BoundingBox { get; set; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("is_visible")]
    public bool IsVisible { get; set; }

    [JsonPropertyName("patterns")]
    public List<string> Patterns { get; set; } = new();

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("children")]
    public List<UIAElementInfo> Children { get; set; } = new();
}

public class UIAInspectResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("window")]
    public string WindowTitle { get; set; } = "";

    [JsonPropertyName("window_handle")]
    public long WindowHandle { get; set; }

    [JsonPropertyName("element_count")]
    public int ElementCount { get; set; }

    [JsonPropertyName("root_elements")]
    public List<UIAElementInfo> RootElements { get; set; } = new();

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class UIAFindResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("matches")]
    public List<UIAElementInfo> Matches { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
