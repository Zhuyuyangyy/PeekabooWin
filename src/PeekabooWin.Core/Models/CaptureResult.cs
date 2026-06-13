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

    /// <summary>
    /// The DPI scale factor at the time of capture.
    /// A value of 1.0 means 100% scaling; 1.5 means 150% scaling, etc.
    /// </summary>
    [JsonPropertyName("scale_factor")]
    public double ScaleFactor { get; set; } = 1.0;
}
