using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Models;
using WGM = Windows.Graphics.Imaging;
using WMD = Windows.Media.Ocr;
using WGL = Windows.Globalization;
using WS = Windows.Storage;

namespace PeekabooWin.Core.Ocr;

/// <summary>
/// Windows.Media.Ocr OCR 服务
/// 使用 Windows Runtime OCR API（内置 Windows 10/11）
/// </summary>
public class OcrService : IDisposable
{
    private readonly string _language;

    public OcrService(string language = "zh-CN")
    {
        _language = language;
    }

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
                // Load via WinRT StorageFile
                var file = await WS.StorageFile.GetFileFromPathAsync(imagePath);
                using var stream = await file.OpenAsync(WS.FileAccessMode.Read);
                var decoder = await WGM.BitmapDecoder.CreateAsync(stream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                // Ensure BGRA8 format for OcrEngine
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

                var result = await RecognizeSoftwareBitmapAsync(softwareBitmap);
                softwareBitmap.Dispose();
                return result;
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
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid():N}.png");
            bitmap.Save(tempPath, ImageFormat.Png);
            try
            {
                var file = await WS.StorageFile.GetFileFromPathAsync(tempPath);
                using var stream = await file.OpenAsync(WS.FileAccessMode.Read);
                var decoder = await WGM.BitmapDecoder.CreateAsync(stream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

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

                var result = await RecognizeSoftwareBitmapAsync(softwareBitmap);
                softwareBitmap.Dispose();
                return result;
            }
            finally
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            return new OcrResult { Text = "", Error = ex.Message };
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

    private async Task<OcrResult> RecognizeSoftwareBitmapAsync(WGM.SoftwareBitmap softwareBitmap)
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
            catch (Exception ex) { PekaLogger.Warn("OcrService", "Language creation failed", ex); }

            if (engine == null)
                engine = WMD.OcrEngine.TryCreateFromUserProfileLanguages();

            if (engine == null)
                return new OcrResult { Text = "", Error = "No OCR engine available" };

            var ocrResult = await engine.RecognizeAsync(softwareBitmap);

            if (ocrResult == null)
                return new OcrResult { Text = "", Error = "OCR returned null result" };

            var words = new List<OcrWord>();

            // Iterate lines
            foreach (var line in ocrResult.Lines)
            {
                foreach (var word in line.Words)
                {
                    var box = word.BoundingRect;
                    words.Add(new OcrWord
                    {
                        Text = word.Text ?? "",
                        BoundingBox = new OcrRect
                        {
                            X = box.X,
                            Y = box.Y,
                            Width = box.Width,
                            Height = box.Height
                        },
                        Confidence = 1.0
                    });
                }
            }

            // Also check if text was recognized but lines were empty
            var detectedText = ocrResult.Text ?? "";
            if (words.Count == 0 && !string.IsNullOrEmpty(detectedText))
            {
                // Try to split text into words manually for word-level operations
                // Windows.Media.Ocr doesn't always provide word boxes
                var textParts = detectedText.Split(new[] { ' ', '\n', '\r', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in textParts)
                {
                    words.Add(new OcrWord
                    {
                        Text = part,
                        BoundingBox = null,
                        Confidence = 1.0
                    });
                }
            }

            return new OcrResult
            {
                Text = detectedText,
                Words = words,
                Language = engine.RecognizerLanguage.LanguageTag,
                Confidence = words.Count > 0 ? 1.0 : 0,
                Engine = "Windows.Media.Ocr"
            };
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
            // Try to find the keyword spanning adjacent words in a single line
            // and compute a merged bounding box from those words.
            foreach (var line in result.Words
                .GroupBy(w => Math.Round(w.BoundingBox?.Y ?? 0, 0))
                .OrderBy(g => g.Key))
            {
                var lineWords = line.OrderBy(w => w.BoundingBox?.X ?? 0).ToList();
                var joined = string.Join(" ", lineWords.Select(w => w.Text));
                var idx = joined.IndexOf(keyword, comparison);
                if (idx >= 0)
                {
                    int charPos = 0;
                    int startW = -1, endW = -1;
                    for (int i = 0; i < lineWords.Count; i++)
                    {
                        int wordStart = charPos;
                        int wordEnd = charPos + lineWords[i].Text.Length;
                        if (startW < 0 && wordEnd > idx) startW = i;
                        if (wordStart < idx + keyword.Length) endW = i;
                        charPos = wordEnd + 1; // +1 for the space from Join
                    }

                    if (startW >= 0 && endW >= 0
                        && lineWords[startW].BoundingBox != null
                        && lineWords[endW].BoundingBox != null)
                    {
                        var firstBox = lineWords[startW].BoundingBox!;
                        var lastBox = lineWords[endW].BoundingBox!;
                        double x1 = firstBox.X, y1 = firstBox.Y;
                        double x2 = lastBox.X + lastBox.Width;
                        double y2 = Math.Max(firstBox.Y + firstBox.Height, lastBox.Y + lastBox.Height);

                        return new List<OcrWord>
                        {
                            new OcrWord
                            {
                                Text = keyword,
                                BoundingBox = new OcrRect
                                {
                                    X = x1, Y = y1, Width = x2 - x1, Height = y2 - y1
                                },
                                Confidence = 0.8
                            }
                        };
                    }
                }
            }

            // Could not compute bounding box from lines; return synthetic match with null bbox
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

        // If we have a valid bounding box, return its center directly
        if (first.BoundingBox != null)
        {
            return (
                (int)(first.BoundingBox.X + first.BoundingBox.Width / 2),
                (int)(first.BoundingBox.Y + first.BoundingBox.Height / 2)
            );
        }

        // BoundingBox is null (full-text fallback could not estimate coordinates).
        // For CJK text, try individual character matching to recover coordinates.
        if (keyword.Length > 1 && ContainsChinese(keyword))
        {
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var charMatchWords = new List<OcrWord>();
            foreach (var ch in keyword)
            {
                charMatchWords.AddRange(result.Words
                    .Where(w => w.BoundingBox != null && w.Text.Contains(ch.ToString(), comparison))
                    .ToList());
            }

            if (charMatchWords.Count > 0)
            {
                // Compute center of the bounding rectangle covering all matched characters
                double minX = charMatchWords.Min(w => w.BoundingBox!.X);
                double minY = charMatchWords.Min(w => w.BoundingBox!.Y);
                double maxX = charMatchWords.Max(w => w.BoundingBox!.X + w.BoundingBox!.Width);
                double maxY = charMatchWords.Max(w => w.BoundingBox!.Y + w.BoundingBox!.Height);
                return ((int)((minX + maxX) / 2), (int)((minY + maxY) / 2));
            }
        }

        // For non-CJK text, try to find the keyword inside any single OCR word with coordinates
        {
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var partialMatch = result.Words
                .FirstOrDefault(w => w.BoundingBox != null && w.Text.Contains(keyword, comparison));
            if (partialMatch?.BoundingBox != null)
            {
                return (
                    (int)(partialMatch.BoundingBox.X + partialMatch.BoundingBox.Width / 2),
                    (int)(partialMatch.BoundingBox.Y + partialMatch.BoundingBox.Height / 2)
                );
            }
        }

        return null;
    }

    public void Dispose() { }
}