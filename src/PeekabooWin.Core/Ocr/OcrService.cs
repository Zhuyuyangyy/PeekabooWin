using System.Drawing;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Ocr;

/// <summary>
/// Tesseract OCR 服务
/// 使用 Tesseract 5.x C# 绑定（支持中文）
/// </summary>
public class OcrService : IDisposable
{
    private readonly string _lang;
    private Tesseract.TesseractEngine? _engine;
    private bool _disposed;

    public OcrService(string language = "chi_sim+eng")
    {
        _lang = language;
    }

    private Tesseract.TesseractEngine GetEngine()
    {
        if (_engine == null)
        {
            // tessdata must be at repo root: D:\GITHUB\PeekabooWin\tessdata\
            // When running from bin/Debug/net8.0-windows/, go up 4 dirs to repo root
            var baseDir = AppContext.BaseDirectory;

            // Try multiple possible tessdata locations
            var possiblePaths = new[]
            {
                Path.Combine(baseDir, "tessdata"),
                Path.Combine(baseDir, "..", "tessdata"),
                Path.Combine(baseDir, "..", "..", "tessdata"),
                Path.Combine(baseDir, "..", "..", "..", "tessdata"),
                Path.Combine(baseDir, "..", "..", "..", "..", "tessdata"),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "tessdata"),
                Path.Combine(Directory.GetCurrentDirectory(), "tessdata"),
                "D:\\GITHUB\\PeekabooWin\\tessdata"
            };

            string? tessdataPath = null;
            foreach (var p in possiblePaths)
            {
                var full = Path.GetFullPath(p);
                if (Directory.Exists(full) && File.Exists(Path.Combine(full, "chi_sim.traineddata")))
                {
                    tessdataPath = full;
                    break;
                }
            }

            if (tessdataPath == null)
                throw new FileNotFoundException($"tessdata not found. Tried: {string.Join(", ", possiblePaths.Select(Path.GetFullPath))}");

            _engine = new Tesseract.TesseractEngine(tessdataPath, _lang, Tesseract.EngineMode.Default);
        }
        return _engine;
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

            return await Task.Run(() =>
            {
                using var img = Tesseract.Pix.LoadFromFile(imagePath);
                return RecognizePix(img);
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
        return await Task.Run(() =>
        {
            try
            {
                // 保存到临时文件（Pix.LoadFromFile 需要文件路径）
                var tempPath = Path.GetTempFileName() + ".bmp";
                bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Bmp);
                try
                {
                    using var pix = Tesseract.Pix.LoadFromFile(tempPath);
                    var result = RecognizePix(pix);
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
        });
    }

    /// <summary>
    /// 识别屏幕指定区域
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

    private OcrResult RecognizePix(Tesseract.Pix pix)
    {
        try
        {
            var engine = GetEngine();
            using var page = engine.Process(pix);

            var text = page.GetText();
            var confidence = page.GetMeanConfidence();

            var words = new List<OcrWord>();
            using var iterator = page.GetIterator();
            iterator.Begin();

            do
            {
                if (iterator.TryGetBoundingBox(Tesseract.PageIteratorLevel.Word, out var box))
                {
                    var wordText = iterator.GetText(Tesseract.PageIteratorLevel.Word) ?? "";
                    if (!string.IsNullOrWhiteSpace(wordText))
                    {
                        words.Add(new OcrWord
                        {
                            Text = wordText.Trim(),
                            BoundingBox = new OcrRect
                            {
                                X = box.X1,
                                Y = box.Y1,
                                Width = box.X2 - box.X1,
                                Height = box.Y2 - box.Y1
                            },
                            Confidence = confidence
                        });
                    }
                }
            } while (iterator.Next(Tesseract.PageIteratorLevel.Word));

            return new OcrResult
            {
                Text = text.Trim(),
                Words = words,
                Language = _lang,
                Confidence = confidence,
                Engine = "Tesseract 5.x"
            };
        }
        catch (Exception ex)
        {
            return new OcrResult { Text = "", Error = ex.Message };
        }
    }

    /// <summary>
    /// 在 OCR 结果中搜索包含指定关键词的词
    /// </summary>
    public List<OcrWord> FindWords(OcrResult result, string keyword, bool caseSensitive = false)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return result.Words
            .Where(w => w.Text.Contains(keyword, comparison))
            .ToList();
    }

    /// <summary>
    /// 在 OCR 结果中搜索第一个包含指定关键词的词，并返回其中心坐标
    /// </summary>
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _engine?.Dispose();
            _disposed = true;
        }
    }
}