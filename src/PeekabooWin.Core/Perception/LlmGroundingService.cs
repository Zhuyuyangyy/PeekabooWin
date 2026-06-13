using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Perception;

/// <summary>
/// LLM 视觉定位服务 — 通过多模态大模型实现屏幕元素识别与定位
/// 
/// 核心流程：截图 → 降采样 → 发送给 LLM → 解析 JSON 响应 → 返回结构化结果
/// </summary>
public class LlmGroundingService
{
    private const string LogTag = "LlmGroundingService";

    /// <summary>
    /// 发送给 LLM 的最大图片宽度（像素），超出此宽度会降采样
    /// </summary>
    private const int MaxImageWidth = 1280;

    #region Prompt Templates

    /// <summary>
    /// 系统提示词 — 定义 LLM 的角色和输出格式
    /// </summary>
    private const string SystemPrompt = """
        You are a precise UI element grounding assistant. You analyze screenshots and identify interactive UI elements.
        
        RULES:
        - Output ONLY valid JSON, no markdown, no explanation outside JSON.
        - All bbox coordinates must be in the ORIGINAL image pixel space.
        - Only include elements visible in the screenshot.
        - Be precise with bbox coordinates — they will be used for automated clicking.
        - Assign confidence 0.0-1.0 based on how certain you are about each element.
        - Identify element types: button, input, checkbox, radio, link, text, image, dropdown, tab, menu, scrollbar, icon, label, toggle, slider, table, list_item.
        - For state, use: enabled, disabled, focused, checked, unchecked, empty, filled, normal.
        """;

    /// <summary>
    /// 全量元素定位提示词
    /// </summary>
    private const string GroundAllElementsPrompt = """
        Analyze this screenshot and identify ALL interactive and meaningful UI elements.
        
        {TaskContext}
        
        Return a JSON object with this exact structure:
        {
          "screen_type": "login_page|editor|browser|dialog|settings|list|dashboard|unknown",
          "window_title_estimate": "detected or inferred window title",
          "interactive_summary": "brief description of what's on screen and available interactions",
          "elements": [
            {
              "id": "e001",
              "type": "button|input|text|checkbox|link|dropdown|tab|icon|...",
              "label": "visible text or inferred purpose",
              "bbox": { "x": 100, "y": 200, "width": 80, "height": 30 },
              "confidence": 0.95,
              "state": "enabled|disabled|focused|empty|filled|normal",
              "description": "optional description of this element"
            }
          ]
        }
        """;

    /// <summary>
    /// 单元素查找提示词
    /// </summary>
    private const string FindSingleElementPrompt = """
        Find the SINGLE UI element that best matches this description: "{Description}"
        
        {TaskContext}
        
        Return ONLY a JSON object with this structure (no other text):
        {
          "found": true,
          "element": {
            "id": "e001",
            "type": "button|input|text|...",
            "label": "matched element label",
            "bbox": { "x": 100, "y": 200, "width": 80, "height": 30 },
            "confidence": 0.92,
            "state": "enabled",
            "description": "why this element matches"
          },
          "reasoning": "why this element matches the description"
        }
        
        If no matching element is found:
        {
          "found": false,
          "element": null,
          "reasoning": "why no element matches"
        }
        """;

    /// <summary>
    /// 全屏幕理解提示词
    /// </summary>
    private const string UnderstandScreenPrompt = """
        Perform a comprehensive analysis of this screenshot. Identify ALL interactive elements, 
        their states, relationships, and the overall screen purpose.
        
        Return a JSON object with this exact structure:
        {
          "screen_type": "login_page|editor|browser|dialog|settings|list|dashboard|unknown",
          "window_title": "detected or inferred window title",
          "elements": [
            {
              "id": "e001",
              "type": "button|input|text|checkbox|link|dropdown|tab|icon|...",
              "label": "visible text or inferred purpose",
              "bbox": { "x": 100, "y": 200, "width": 80, "height": 30 },
              "confidence": 0.95,
              "state": "enabled|disabled|focused|empty|filled|normal",
              "description": "detailed element description"
            }
          ],
          "summary": "overall screen summary including layout and interaction flow"
        }
        """;

    #endregion

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILlmVisionClient _visionClient;
    private readonly CaptureService _captureService;
    private readonly TempFileManager _tempFileManager;
    private readonly PerceptionCache? _cache;

    /// <summary>
    /// 创建 LLM 视觉定位服务
    /// </summary>
    /// <param name="visionClient">多模态视觉客户端</param>
    /// <param name="captureService">截屏服务</param>
    /// <param name="tempFileManager">临时文件管理</param>
    /// <param name="cache">可选的感知结果缓存</param>
    public LlmGroundingService(
        ILlmVisionClient visionClient,
        CaptureService captureService,
        TempFileManager tempFileManager,
        PerceptionCache? cache = null)
    {
        _visionClient = visionClient ?? throw new ArgumentNullException(nameof(visionClient));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _tempFileManager = tempFileManager ?? throw new ArgumentNullException(nameof(tempFileManager));
        _cache = cache;
    }

    /// <summary>
    /// 定位屏幕元素 — 捕获截图，发送给 LLM 进行全量元素识别
    /// </summary>
    /// <param name="windowKeyword">窗口关键词（null 或空表示全屏）</param>
    /// <param name="taskDescription">当前任务描述（帮助 LLM 聚焦相关元素）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>定位结果，失败返回 null</returns>
    public async Task<LlmGroundingResult?> GroundElementsAsync(
        string? windowKeyword,
        string taskDescription,
        CancellationToken ct)
    {
        try
        {
            var (imageBytes, captureResult) = await CaptureAndDownsampleAsync(windowKeyword, ct);
            if (imageBytes is null)
            {
                PekaLogger.Warn(LogTag, "Capture failed, cannot proceed with grounding");
                return null;
            }

            // 检查缓存
            var hash = PerceptionCache.ComputeHash(imageBytes);
            if (_cache is not null)
            {
                var cached = _cache.Get(hash, taskDescription);
                if (cached is not null)
                {
                    PekaLogger.Debug(LogTag, "Cache hit for grounding request");
                    return cached;
                }
            }

            var taskContext = string.IsNullOrEmpty(taskDescription)
                ? ""
                : $"Current task context: {taskDescription}";

            var userPrompt = GroundAllElementsPrompt.Replace("{TaskContext}", taskContext);

            PekaLogger.Info(LogTag,
                $"Grounding elements: window='{windowKeyword ?? "full screen"}', " +
                $"task='{taskDescription}', provider={_visionClient.ProviderName}");

            var response = await _visionClient.ChatWithImageAsync(
                SystemPrompt, userPrompt, imageBytes, ct: ct);

            var result = ParseGroundingResult(response, captureResult, imageBytes);
            if (result is not null)
            {
                // 写入缓存
                _cache?.Set(hash, taskDescription, result);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PekaLogger.Error(LogTag, "GroundElementsAsync failed", ex);
            return null;
        }
    }

    /// <summary>
    /// 查找单个元素 — 使用聚焦提示词定位最匹配描述的元素
    /// </summary>
    /// <param name="windowKeyword">窗口关键词（null 或空表示全屏）</param>
    /// <param name="description">元素描述</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>匹配的 GroundedElement，未找到返回 null</returns>
    public async Task<GroundedElement?> FindElementAsync(
        string? windowKeyword,
        string description,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            PekaLogger.Warn(LogTag, "FindElementAsync called with empty description");
            return null;
        }

        try
        {
            var (imageBytes, captureResult) = await CaptureAndDownsampleAsync(windowKeyword, ct);
            if (imageBytes is null) return null;

            var taskContext = string.IsNullOrEmpty(windowKeyword)
                ? ""
                : $"Window context: {windowKeyword}";

            var userPrompt = FindSingleElementPrompt
                .Replace("{Description}", description)
                .Replace("{TaskContext}", taskContext);

            PekaLogger.Info(LogTag,
                $"Finding element: '{description}', window='{windowKeyword ?? "full screen"}'");

            var response = await _visionClient.ChatWithImageAsync(
                SystemPrompt, userPrompt, imageBytes, ct: ct);

            var findResult = ParseFindResult(response, captureResult, imageBytes);
            if (findResult is not null && findResult.Found && findResult.Element is not null)
            {
                return findResult.Element;
            }

            PekaLogger.Debug(LogTag,
                $"Element not found: '{description}'. Reason: {findResult?.Reasoning ?? "parse failed"}");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PekaLogger.Error(LogTag, "FindElementAsync failed", ex);
            return null;
        }
    }

    /// <summary>
    /// 全屏幕理解 — 完整分析屏幕内容，返回所有交互元素和屏幕状态
    /// </summary>
    /// <param name="windowKeyword">窗口关键词（null 或空表示全屏）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>屏幕理解结果，失败返回 null</returns>
    public async Task<ScreenUnderstanding?> UnderstandScreenAsync(
        string? windowKeyword,
        CancellationToken ct)
    {
        try
        {
            var (imageBytes, captureResult) = await CaptureAndDownsampleAsync(windowKeyword, ct);
            if (imageBytes is null) return null;

            PekaLogger.Info(LogTag,
                $"Understanding screen: window='{windowKeyword ?? "full screen"}'");

            var response = await _visionClient.ChatWithImageAsync(
                SystemPrompt, UnderstandScreenPrompt, imageBytes, ct: ct);

            var result = ParseScreenUnderstanding(response, captureResult, imageBytes);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PekaLogger.Error(LogTag, "UnderstandScreenAsync failed", ex);
            return null;
        }
    }

    #region Capture & Image Processing

    /// <summary>
    /// 捕获截图并降采样，返回 (降采样后字节数组, 原始CaptureResult)
    /// </summary>
    private async Task<(byte[]? imageBytes, CaptureResult? captureResult)> CaptureAndDownsampleAsync(
        string? windowKeyword,
        CancellationToken ct)
    {
        var outputPath = _tempFileManager.CreateTempPath("grounding");

        CaptureResult captureResult;
        if (string.IsNullOrWhiteSpace(windowKeyword))
        {
            captureResult = _captureService.CaptureScreen(outputPath);
        }
        else
        {
            captureResult = _captureService.CaptureWindow(windowKeyword, outputPath);
        }

        if (!captureResult.Success || captureResult.Path is null)
        {
            PekaLogger.Warn(LogTag, $"Capture failed: {captureResult.Error}");
            return (null, null);
        }

        try
        {
            var originalBytes = await File.ReadAllBytesAsync(captureResult.Path, ct);
            var downsampledBytes = DownsampleImage(originalBytes, MaxImageWidth);
            return (downsampledBytes, captureResult);
        }
        catch (Exception ex)
        {
            PekaLogger.Error(LogTag, "Failed to read/downsample captured image", ex);
            return (null, captureResult);
        }
    }

    /// <summary>
    /// 降采样图片 — 等比缩放到 maxWidth 以内，返回 JPEG 字节数组
    /// </summary>
    private static byte[] DownsampleImage(byte[] imageBytes, int maxWidth)
    {
        using var ms = new MemoryStream(imageBytes);
        using var original = Image.FromStream(ms);

        if (original.Width <= maxWidth)
        {
            // 不需要降采样，直接返回原始字节
            return imageBytes;
        }

        var ratio = (double)maxWidth / original.Width;
        var newWidth = maxWidth;
        var newHeight = (int)(original.Height * ratio);

        using var resized = new Bitmap(newWidth, newHeight);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(original, 0, 0, newWidth, newHeight);
        }

        using var output = new MemoryStream();
        resized.Save(output, ImageFormat.Jpeg);
        return output.ToArray();
    }

    /// <summary>
    /// 计算降采样缩放因子
    /// </summary>
    private static (double scaleX, double scaleY) ComputeScaleFactors(
        CaptureResult? originalCapture, byte[] downsampledBytes)
    {
        if (originalCapture is null) return (1.0, 1.0);

        var downsampledSize = GetImageDimensions(downsampledBytes);
        if (downsampledSize.width == 0 || downsampledSize.height == 0)
            return (1.0, 1.0);

        var scaleX = (double)originalCapture.Width / downsampledSize.width;
        var scaleY = (double)originalCapture.Height / downsampledSize.height;
        return (scaleX, scaleY);
    }

    /// <summary>
    /// 将元素坐标从降采样空间映射回原始截图空间，并设置 ClickPoint
    /// </summary>
    private static void ScaleAndFinalizeElement(GroundedElement element, double scaleX, double scaleY)
    {
        // 缩放 BBox
        if (Math.Abs(scaleX - 1.0) >= 0.01 || Math.Abs(scaleY - 1.0) >= 0.01)
        {
            element.BBox = new BoundingBox
            {
                X = (int)(element.BBox.X * scaleX),
                Y = (int)(element.BBox.Y * scaleY),
                Width = (int)(element.BBox.Width * scaleX),
                Height = (int)(element.BBox.Height * scaleY)
            };
        }

        // 设置 ClickPoint 为 BBox 中心
        element.ClickPoint = new ClickPoint
        {
            X = element.BBox.X + element.BBox.Width / 2,
            Y = element.BBox.Y + element.BBox.Height / 2
        };
    }

    /// <summary>
    /// 获取图片尺寸
    /// </summary>
    private static (int width, int height) GetImageDimensions(byte[] imageBytes)
    {
        try
        {
            using var ms = new MemoryStream(imageBytes);
            using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            return (img.Width, img.Height);
        }
        catch
        {
            return (0, 0);
        }
    }

    #endregion

    #region JSON Parsing

    /// <summary>
    /// 解析全量定位结果 — 使用 JsonDocument 手动映射到已有的 LlmGroundingResult 类型
    /// </summary>
    private LlmGroundingResult? ParseGroundingResult(
        string response, CaptureResult? captureResult, byte[] downsampledBytes)
    {
        try
        {
            var json = ExtractJsonFromResponse(response);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var (scaleX, scaleY) = ComputeScaleFactors(captureResult, downsampledBytes);

            var elements = ParseElementsArray(root, scaleX, scaleY);

            var result = new LlmGroundingResult
            {
                ScreenType = root.GetStringOrDefault("screen_type") ?? "unknown",
                WindowTitleEstimate = root.GetStringOrDefault("window_title_estimate") ?? "",
                InteractiveSummary = root.GetStringOrDefault("interactive_summary") ?? "",
                Elements = elements
            };

            return result;
        }
        catch (JsonException ex)
        {
            PekaLogger.Error(LogTag, $"Failed to parse grounding JSON: {ex.Message}", ex);
            PekaLogger.Debug(LogTag, $"Raw response: {TruncateForLog(response)}");
            return null;
        }
        catch (Exception ex)
        {
            PekaLogger.Error(LogTag, $"Unexpected error parsing grounding result: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// 解析单元素查找结果
    /// </summary>
    private FindElementResult? ParseFindResult(
        string response, CaptureResult? captureResult, byte[] downsampledBytes)
    {
        try
        {
            var json = ExtractJsonFromResponse(response);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var found = root.GetProperty("found").GetBoolean();
            var reasoning = root.GetStringOrDefault("reasoning") ?? "";

            if (!found || !root.TryGetProperty("element", out var elementNode)
                || elementNode.ValueKind == JsonValueKind.Null)
            {
                return new FindElementResult { Found = false, Reasoning = reasoning };
            }

            var (scaleX, scaleY) = ComputeScaleFactors(captureResult, downsampledBytes);
            var element = ParseSingleElement(elementNode, scaleX, scaleY);

            return new FindElementResult
            {
                Found = true,
                Element = element,
                Reasoning = reasoning
            };
        }
        catch (JsonException ex)
        {
            PekaLogger.Error(LogTag, $"Failed to parse find-element JSON: {ex.Message}", ex);
            PekaLogger.Debug(LogTag, $"Raw response: {TruncateForLog(response)}");
            return null;
        }
        catch (Exception ex)
        {
            PekaLogger.Error(LogTag, $"Unexpected error parsing find result: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// 解析全屏幕理解结果
    /// </summary>
    private ScreenUnderstanding? ParseScreenUnderstanding(
        string response, CaptureResult? captureResult, byte[] downsampledBytes)
    {
        try
        {
            var json = ExtractJsonFromResponse(response);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var (scaleX, scaleY) = ComputeScaleFactors(captureResult, downsampledBytes);
            var elements = ParseElementsArray(root, scaleX, scaleY);

            var result = new ScreenUnderstanding
            {
                ScreenType = root.GetStringOrDefault("screen_type") ?? "unknown",
                WindowTitle = root.GetStringOrDefault("window_title") ?? "",
                Summary = root.GetStringOrDefault("summary") ?? "",
                Elements = elements,
                Source = PerceptionSource.LLM_Vision
            };

            return result;
        }
        catch (JsonException ex)
        {
            PekaLogger.Error(LogTag, $"Failed to parse screen-understanding JSON: {ex.Message}", ex);
            PekaLogger.Debug(LogTag, $"Raw response: {TruncateForLog(response)}");
            return null;
        }
        catch (Exception ex)
        {
            PekaLogger.Error(LogTag, $"Unexpected error parsing screen understanding: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// 解析 JSON elements 数组为 GroundedElement 列表
    /// </summary>
    private static List<GroundedElement> ParseElementsArray(
        JsonElement root, double scaleX, double scaleY)
    {
        var elements = new List<GroundedElement>();

        if (!root.TryGetProperty("elements", out var elementsArray)
            || elementsArray.ValueKind != JsonValueKind.Array)
            return elements;

        foreach (var item in elementsArray.EnumerateArray())
        {
            try
            {
                var element = ParseSingleElement(item, scaleX, scaleY);
                if (element is not null)
                    elements.Add(element);
            }
            catch
            {
                // 跳过解析失败的单个元素
            }
        }

        return elements;
    }

    /// <summary>
    /// 从 JsonElement 解析单个 GroundedElement
    /// </summary>
    private static GroundedElement ParseSingleElement(JsonElement node, double scaleX, double scaleY)
    {
        var bbox = new BoundingBox();
        if (node.TryGetProperty("bbox", out var bboxNode) && bboxNode.ValueKind == JsonValueKind.Object)
        {
            bbox = new BoundingBox
            {
                X = bboxNode.GetIntOrDefault("x"),
                Y = bboxNode.GetIntOrDefault("y"),
                Width = bboxNode.GetIntOrDefault("width"),
                Height = bboxNode.GetIntOrDefault("height")
            };
        }

        // 缩放坐标
        if (Math.Abs(scaleX - 1.0) >= 0.01 || Math.Abs(scaleY - 1.0) >= 0.01)
        {
            bbox = new BoundingBox
            {
                X = (int)(bbox.X * scaleX),
                Y = (int)(bbox.Y * scaleY),
                Width = (int)(bbox.Width * scaleX),
                Height = (int)(bbox.Height * scaleY)
            };
        }

        var element = new GroundedElement
        {
            Id = node.GetStringOrDefault("id") ?? $"llm_{Guid.NewGuid():N}"[..8],
            Type = node.GetStringOrDefault("type") ?? "unknown",
            Label = node.GetStringOrDefault("label") ?? "",
            BBox = bbox,
            ClickPoint = new ClickPoint
            {
                X = bbox.X + bbox.Width / 2,
                Y = bbox.Y + bbox.Height / 2
            },
            Confidence = node.GetDoubleOrDefault("confidence"),
            State = node.GetStringOrDefault("state") ?? "normal",
            Source = PerceptionSource.LLM_Vision,
            Description = node.GetStringOrDefault("description")
        };

        return element;
    }

    /// <summary>
    /// 从 LLM 响应中提取 JSON 字符串（处理 markdown 代码块包裹等情况）
    /// </summary>
    private static string ExtractJsonFromResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "{}";

        var trimmed = response.Trim();

        // 去除 markdown 代码块包裹: ```json ... ``` 或 ``` ... ```
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            if (trimmed.EndsWith("```"))
            {
                trimmed = trimmed[..^3];
            }

            trimmed = trimmed.Trim();
        }

        // 尝试找到第一个 { 和最后一个 } 之间的内容
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }

        return trimmed;
    }

    private static string TruncateForLog(string text, int maxLength = 500)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...(truncated)";
    }

    #endregion
}

#region JsonElement Extension Helpers

/// <summary>
/// JsonElement 安全读取扩展方法
/// </summary>
internal static class JsonElementExtensions
{
    public static string? GetStringOrDefault(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    public static int GetIntOrDefault(this JsonElement element, string propertyName, int defaultValue = 0)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
                return val;
        }
        return defaultValue;
    }

    public static double GetDoubleOrDefault(this JsonElement element, string propertyName, double defaultValue = 0.0)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var val))
                return val;
        }
        return defaultValue;
    }
}

#endregion

#region Find Result Type

/// <summary>
/// 单元素查找结果 — LLM 返回的查找响应
/// </summary>
public class FindElementResult
{
    [JsonPropertyName("found")]
    public bool Found { get; set; }

    [JsonPropertyName("element")]
    public GroundedElement? Element { get; set; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";
}

#endregion
