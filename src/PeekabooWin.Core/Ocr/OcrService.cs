using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PeekabooWin.Core.Models;
using WGM = Windows.Graphics.Imaging;
using WMD = Windows.Media.Ocr;
using WGL = Windows.Globalization;
using WS = Windows.Storage;

namespace PeekabooWin.Core.Ocr;

/// <summary>
/// Windows.Media.Ocr OCR 服务
/// 使用 Windows Runtime OCR API（内置 Windows 10/11）
/// 支持图像预处理以提升识别精度
/// </summary>
public class OcrService : IDisposable
{
    private readonly string _language;
    private readonly OcrPreprocessor _preprocessor;
    private readonly bool _usePreprocessing;
    private readonly OcrConfidenceEvaluator _confidenceEvaluator;

    public OcrService(string language = "zh-CN", bool enablePreprocessing = true)
    {
        _language = language;
        _usePreprocessing = enablePreprocessing;
        _preprocessor = new OcrPreprocessor
        {
            ScaleFactor = 2,
            EnableDenoising = true,
            EnableBinarization = true,
            DenoiseRadius = 2
        };
    }

    public OcrService(string language, OcrPreprocessor preprocessor)
    {
        _language = language;
        _preprocessor = preprocessor;
        _usePreprocessing = true;
        _confidenceEvaluator = new OcrConfidenceEvaluator();
    }

    public OcrPreprocessor Preprocessor => _preprocessor;
    internal OcrConfidenceEvaluator ConfidenceEvaluator => _confidenceEvaluator;

    /// <summary>
    /// 识别图片文件中的文字
    /// </summary>
    public async Task<OcrResult> RecognizeImageAsync(string imagePath)
    {
        try
        {
            if (!File.Exists(imagePath))
                return new OcrResult { Text = "", Error = $"File not found: {imagePath}" };

            return await Task.Run(async () =>
            {
                Bitmap bitmap = new Bitmap(imagePath);
                try
                {
                    if (_usePreprocessing)
                    {
                        using var preprocessed = _preprocessor.Preprocess(bitmap);
                        return await RecognizePreprocessedBitmapAsync(preprocessed);
                    }
                    return await RecognizePreprocessedBitmapAsync(bitmap);
                }
                finally
                {
                    bitmap.Dispose();
                }
            });
        }
        catch (Exception ex)
        {
            return new OcrResult { Text = "", Error = ex.Message };
        }
    }

    /// <summary>
    /// 识别 Bitmap 中的文字
    /// </summary>
    public async Task<OcrResult> RecognizeBitmapAsync(Bitmap bitmap)
    {
        return await Task.Run(async () =>
        {
            try
            {
                Bitmap processed = bitmap;
                bool weOwnIt = false;

                if (_usePreprocessing)
                {
                    processed = _preprocessor.Preprocess(bitmap);
                    weOwnIt = true;
                }

                try
                {
                    var result = await RecognizePreprocessedBitmapAsync(processed);
                    return result;
                }
                finally
                {
                    if (weOwnIt)
                        processed.Dispose();
                }
            }
            catch (Exception ex)
            {
                return new OcrResult { Text = "", Error = ex.Message };
            }
        });
    }

    private async Task<OcrResult> RecognizePreprocessedBitmapAsync(Bitmap bitmap)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid():N}.png");
        bitmap.Save(tempPath, ImageFormat.Png);
        try
        {
            var file = WS.StorageFile.GetFileFromPathAsync(tempPath).GetAwaiter().GetResult();
            using var stream = file.OpenAsync(WS.FileAccessMode.Read).GetAwaiter().GetResult();
            var decoder = WGM.BitmapDecoder.CreateAsync(stream).GetAwaiter().GetResult();
            var softwareBitmap = decoder.GetSoftwareBitmapAsync().GetAwaiter().GetResult();

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

            var result = RecognizeSoftwareBitmap(softwareBitmap);
            softwareBitmap.Dispose();
            return result;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>
    /// 识别屏幕区域
    /// </summary>
    public async Task<OcrResult> RecognizeRegionAsync(int x, int y, int width, int height)
    {
        try
        {
            using var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
            }
            return await RecognizeBitmapAsync(bitmap);
        }
        catch (Exception ex)
        {
            return new OcrResult { Text = "", Error = ex.Message };
        }
    }

    private OcrResult RecognizeSoftwareBitmap(WGM.SoftwareBitmap softwareBitmap)
    {
        try
        {
            WMD.OcrEngine? engine = null;
            var langTag = _language;

            try
            {
                var language = new WGL.Language(langTag);
                engine = WMD.OcrEngine.TryCreateFromLanguage(language);
            }
            catch { }

            if (engine == null)
                engine = WMD.OcrEngine.TryCreateFromUserProfileLanguages();

            if (engine == null)
                return new OcrResult { Text = "", Error = "No OCR engine available" };

            var ocrResult = engine.RecognizeAsync(softwareBitmap).GetAwaiter().GetResult();

            if (ocrResult == null)
                return new OcrResult { Text = "", Error = "OCR returned null result" };

            var words = new List<OcrWord>();

            foreach (var line in ocrResult.Lines)
            {
                foreach (var word in line.Words)
                {
                    var box = word.BoundingRect;
                    var ocrWord = new OcrWord
                    {
                        Text = word.Text ?? "",
                        BoundingBox = new OcrRect
                        {
                            X = box.X,
                            Y = box.Y,
                            Width = box.Width,
                            Height = box.Height
                        }
                    };
                    ocrWord.Confidence = _confidenceEvaluator.CalculateWordConfidence(ocrWord.Text, ocrWord.BoundingBox);
                    words.Add(ocrWord);
                }
            }

            var detectedText = ocrResult.Text ?? "";
            if (words.Count == 0 && !string.IsNullOrEmpty(detectedText))
            {
                var textParts = detectedText.Split(new[] { ' ', '\n', '\r', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in textParts)
                {
                    words.Add(new OcrWord
                    {
                        Text = part,
                        BoundingBox = null,
                        Confidence = _confidenceEvaluator.CalculateWordConfidence(part, null)
                    });
                }
            }

            var result = new OcrResult
            {
                Text = detectedText,
                Words = words,
                Language = engine.RecognizerLanguage.LanguageTag,
                Engine = "Windows.Media.Ocr"
            };

            result.Confidence = words.Count > 0 ? words.Average(w => w.Confidence) : 0;
            return result;
        }
        catch (Exception ex)
        {
            return new OcrResult { Text = "", Error = ex.Message };
        }
    }

    public List<OcrWord> FindWords(OcrResult result, string keyword, bool caseSensitive = false)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var wordMatches = result.Words
            .Where(w => w.Text.Contains(keyword, comparison))
            .ToList();
        if (wordMatches.Count > 0)
            return wordMatches;

        // Fallback: search keyword as substring in full OCR text
        // This handles cases where Windows.Media.Ocr splits tokens (e.g. "github.com" → "github" + "com")
        if (result.Text.Contains(keyword, comparison))
        {
            // Return a synthetic OcrWord with no bounding box for full-text matches
            return new List<OcrWord>
            {
                new OcrWord
                {
                    Text = keyword,
                    BoundingBox = null,
                    Confidence = 1.0
                }
            };
        }

        // For CJK (Chinese/Japanese/Korean), also try individual characters
        // This handles cases where Chinese text has no spaces between characters
        if (keyword.Length > 1 && ContainsChinese(keyword))
        {
            var charMatches = new List<OcrWord>();
            foreach (var ch in keyword)
            {
                charMatches.AddRange(result.Words
                    .Where(w => w.Text.Contains(ch.ToString(), comparison))
                    .ToList());
            }
            return charMatches;
        }

        return new List<OcrWord>();
    }

    private static bool ContainsChinese(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF) return true; // CJK Unified Ideographs
            if (c >= 0x3000 && c <= 0x303F) return true; // CJK Symbols
            if (c >= 0xFF00 && c <= 0xFFEF) return true; // Fullwidth forms
        }
        return false;
    }

    public (int x, int y)? FindWordCenter(OcrResult result, string keyword, bool caseSensitive = false)
    {
        var words = FindWords(result, keyword, caseSensitive);
        if (words.Count == 0) return null;

        var first = words[0];
        if (first.BoundingBox == null) return null;

        return (
            (int)(first.BoundingBox.X + first.BoundingBox.Width / 2),
            (int)(first.BoundingBox.Y + first.BoundingBox.Height / 2)
        );
    }

    public void Dispose() { }
}