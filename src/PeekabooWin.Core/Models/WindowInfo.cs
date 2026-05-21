using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Models;

/// <summary>
/// 代表一个 Windows 窗口
/// </summary>
public class WindowInfo
{
    [JsonPropertyName("id")]
    public long Handle { get; set; }

    [JsonIgnore]
    public IntPtr HandleIntPtr => (IntPtr)Handle;

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("process")]
    public string ProcessName { get; set; } = "";

    [JsonPropertyName("process_id")]
    public int ProcessId { get; set; }

    [JsonPropertyName("class_name")]
    public string ClassName { get; set; } = "";

    [JsonPropertyName("rect")]
    public RectInfo Rect { get; set; } = new();

    [JsonPropertyName("visible")]
    public bool IsVisible { get; set; }

    [JsonPropertyName("enabled")]
    public bool IsEnabled { get; set; }
}

public class RectInfo
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}
