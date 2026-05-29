using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Models;

/// <summary>
/// OCR 识别结果
/// </summary>
public class OcrResult
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("words")]
    public List<OcrWord> Words { get; set; } = new();

    [JsonPropertyName("language")]
    public string Language { get; set; } = "zh-CN";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "";

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// 单个识别出来的词
/// </summary>
public class OcrWord
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("bounding_box")]
    public OcrRect? BoundingBox { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}

/// <summary>
/// OCR 矩形区域（像素坐标）
/// </summary>
public class OcrRect
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("left")]
    public double Left => X;

    [JsonPropertyName("top")]
    public double Top => Y;

    [JsonPropertyName("right")]
    public double Right => X + Width;

    [JsonPropertyName("bottom")]
    public double Bottom => Y + Height;
}