using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Perception;

/// <summary>
/// UI 元素模型 — Screen State Graph 的基本节点
/// </summary>
public class UiElement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // input, button, text, image, checkbox, etc.

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("automation_id")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("bounding_box")]
    public BoundingBox BBox { get; set; } = new();

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("is_focused")]
    public bool IsFocused { get; set; } = false;

    [JsonPropertyName("state")]
    public string State { get; set; } = "normal"; // normal, empty, filled, disabled, focused

    [JsonPropertyName("role")]
    public string? Role { get; set; } // primary_action, secondary_action, navigation

    [JsonPropertyName("source")]
    public string Source { get; set; } = "unknown"; // uia, ocr, vision

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 0.0; // 0.0 ~ 1.0

    [JsonPropertyName("text_content")]
    public string? TextContent { get; set; }
}

/// <summary>
/// 边界框
/// </summary>
public class BoundingBox
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("center_x")]
    public int CenterX => X + Width / 2;

    [JsonPropertyName("center_y")]
    public int CenterY => Y + Height / 2;

    public double DistanceTo(BoundingBox other)
    {
        var dx = CenterX - other.CenterX;
        var dy = CenterY - other.CenterY;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// 屏幕状态图 — G_t = (E_t, R_t, S_t)
/// </summary>
public class ScreenStateGraph
{
    [JsonPropertyName("screen_id")]
    public string ScreenId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    [JsonPropertyName("screen_type")]
    public string ScreenType { get; set; } = "unknown"; // login_page, editor, browser, dialog, etc.

    [JsonPropertyName("window_title")]
    public string WindowTitle { get; set; } = "";

    [JsonPropertyName("window_handle")]
    public IntPtr WindowHandle { get; set; }

    /// <summary>
    /// E_t — 元素集合
    /// </summary>
    [JsonPropertyName("elements")]
    public List<UiElement> Elements { get; set; } = new();

    /// <summary>
    /// R_t — 元素间关系 (空间+语义)
    /// </summary>
    [JsonPropertyName("relations")]
    public List<ElementRelation> Relations { get; set; } = new();

    /// <summary>
    /// S_t — 页面状态
    /// </summary>
    [JsonPropertyName("state")]
    public ScreenState State { get; set; } = new();

    /// <summary>
    /// 全局描述
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

/// <summary>
/// 元素关系
/// </summary>
public class ElementRelation
{
    [JsonPropertyName("from_id")]
    public string FromId { get; set; } = "";

    [JsonPropertyName("to_id")]
    public string ToId { get; set; } = "";

    [JsonPropertyName("relation_type")]
    public string RelationType { get; set; } = ""; // above, below, left_of, right_of, contains, role

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

/// <summary>
/// 页面状态快照
/// </summary>
public class ScreenState
{
    [JsonPropertyName("total_elements")]
    public int TotalElements { get; set; }

    [JsonPropertyName("filled_inputs")]
    public int FilledInputs { get; set; }

    [JsonPropertyName("empty_inputs")]
    public int EmptyInputs { get; set; }

    [JsonPropertyName("available_buttons")]
    public int AvailableButtons { get; set; }

    [JsonPropertyName("has_primary_action")]
    public bool HasPrimaryAction { get; set; }

    [JsonPropertyName("has_errors")]
    public bool HasErrors { get; set; }

    [JsonPropertyName("focused_element_id")]
    public string? FocusedElementId { get; set; }
}