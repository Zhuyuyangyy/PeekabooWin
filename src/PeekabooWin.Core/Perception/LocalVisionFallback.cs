using System.Text.Json;
using PeekabooWin.Core.Infrastructure;
using WGM = Windows.Graphics.Imaging;
using WMD = Windows.Media.Ocr;
using WGL = Windows.Globalization;
using WS = Windows.Storage;

namespace PeekabooWin.Core.Perception;

/// <summary>
/// 本地视觉回退方案 — 使用 Windows.Media.Ocr 进行基础文字识别，
/// 将 OCR 结果格式化为类 LLM 结构化 JSON 输出。
/// 无需外部 API，始终可用，但质量不如真正的多模态 LLM。
/// </summary>
public class LocalVisionFallback : ILlmVisionClient
{
    private const string LogTag = "LocalVisionFallback";

    /// <inheritdoc />
    public string ProviderName => "local_ocr";

    /// <inheritdoc />
    public bool IsAvailable => true; // 无需 API Key，Windows OCR 始终可用

    /// <inheritdoc />
    public async Task<string> ChatWithImageAsync(
        string systemPrompt,
        string userPrompt,
        byte[] imageBytes,
        string mediaType = "image/png",
        CancellationToken ct = default)
    {
        if (imageBytes is not { Length: > 0 })
            return FormatEmptyResult("No image data provided");

        string? tempPath = null;
        try
        {
            // 将字节数组写入临时文件供 WinRT StorageFile 读取
            tempPath = Path.Combine(
                Path.GetTempPath(),
                $"peekaboo_vision_{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(tempPath, imageBytes, ct);

            PekaLogger.Debug(LogTag, $"Running local OCR on {imageBytes.Length} bytes");

            var ocrResult = await RunOcrAsync(tempPath);

            return FormatOcrAsLlmResponse(ocrResult);
        }
        catch (Exception ex)
        {
            PekaLogger.Error(LogTag, "Local vision fallback failed", ex);
            return FormatEmptyResult(ex.Message);
        }
        finally
        {
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    /// <summary>
    /// 使用 Windows.Media.Ocr 识别图片中的文字
    /// </summary>
    private async Task<LocalOcrData> RunOcrAsync(string imagePath)
    {
        var file = await WS.StorageFile.GetFileFromPathAsync(imagePath);
        using var stream = await file.OpenAsync(WS.FileAccessMode.Read);
        var decoder = await WGM.BitmapDecoder.CreateAsync(stream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        // 确保 BGRA8 格式
        WGM.SoftwareBitmap? converted = null;
        if (softwareBitmap.BitmapPixelFormat != WGM.BitmapPixelFormat.Bgra8)
        {
            converted = WGM.SoftwareBitmap.Convert(
                softwareBitmap,
                WGM.BitmapPixelFormat.Bgra8,
                WGM.BitmapAlphaMode.Premultiplied);
            softwareBitmap.Dispose();
            softwareBitmap = converted;
        }

        try
        {
            // 优先使用中文引擎，回退到用户语言
            WMD.OcrEngine? engine = null;
            try
            {
                var language = new WGL.Language("zh-CN");
                engine = WMD.OcrEngine.TryCreateFromLanguage(language);
            }
            catch (Exception ex)
            {
                PekaLogger.Warn(LogTag, "Chinese OCR language unavailable, falling back", ex);
            }

            engine ??= WMD.OcrEngine.TryCreateFromUserProfileLanguages();

            if (engine is null)
                return new LocalOcrData { Error = "No OCR engine available on this system" };

            var ocrResult = await engine.RecognizeAsync(softwareBitmap);
            if (ocrResult is null)
                return new LocalOcrData { Error = "OCR engine returned null result" };

            var words = new List<LocalOcrWordData>();
            foreach (var line in ocrResult.Lines)
            {
                foreach (var word in line.Words)
                {
                    var box = word.BoundingRect;
                    words.Add(new LocalOcrWordData
                    {
                        Text = word.Text ?? "",
                        X = (int)box.X,
                        Y = (int)box.Y,
                        Width = (int)box.Width,
                        Height = (int)box.Height
                    });
                }
            }

            return new LocalOcrData
            {
                FullText = ocrResult.Text ?? "",
                Words = words,
                Language = engine.RecognizerLanguage.LanguageTag
            };
        }
        finally
        {
            softwareBitmap.Dispose();
        }
    }

    /// <summary>
    /// 将 OCR 结果格式化为类 LLM 输出的结构化 JSON
    /// </summary>
    private static string FormatOcrAsLlmResponse(LocalOcrData data)
    {
        // 将每个 OCR word 转换为 UiElement 兼容的 JSON 结构
        var elements = data.Words.Select((w, i) => new
        {
            id = $"ocr_{i:D3}",
            type = "text",
            label = w.Text,
            name = w.Text,
            bounding_box = new
            {
                x = w.X,
                y = w.Y,
                width = w.Width,
                height = w.Height
            },
            is_enabled = true,
            is_focused = false,
            state = "normal",
            source = "ocr",
            confidence = 0.6, // OCR 置信度低于 LLM 视觉
            text_content = w.Text
        }).ToList();

        var response = new
        {
            screen_type = "unknown",
            description = data.FullText.Length > 200
                ? data.FullText[..200] + "..."
                : data.FullText,
            elements,
            state = new
            {
                total_elements = elements.Count,
                filled_inputs = 0,
                empty_inputs = 0,
                available_buttons = 0,
                has_primary_action = false,
                has_errors = false,
                focused_element_id = (string?)null
            },
            ocr_text = data.FullText,
            provider = "local_ocr",
            language = data.Language
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    /// <summary>
    /// 生成空结果 JSON（出错时使用）
    /// </summary>
    private static string FormatEmptyResult(string error)
    {
        var response = new
        {
            screen_type = "unknown",
            description = $"Local OCR failed: {error}",
            elements = Array.Empty<object>(),
            state = new
            {
                total_elements = 0,
                filled_inputs = 0,
                empty_inputs = 0,
                available_buttons = 0,
                has_primary_action = false,
                has_errors = true,
                focused_element_id = (string?)null
            },
            ocr_text = "",
            provider = "local_ocr",
            error
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }

    #region Internal Data Types

    private sealed class LocalOcrData
    {
        public string FullText { get; set; } = "";
        public List<LocalOcrWordData> Words { get; set; } = [];
        public string Language { get; set; } = "unknown";
        public string? Error { get; set; }
    }

    private sealed class LocalOcrWordData
    {
        public string Text { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    #endregion
}
