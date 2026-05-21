using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Models;

public class CaptureResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("window_title")]
    public string? WindowTitle { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
