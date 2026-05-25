using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Ocr;

public class TesseractOcrEngine : IOcrEngine
{
    private readonly string _tessdataPath;
    private readonly string _language;
    private readonly string _tesseractExe;

    public string Name => "Tesseract";
    public string DefaultLanguage => "eng";

    public TesseractOcrEngine(string tessdataPath, string language = "eng")
    {
        _tessdataPath = tessdataPath;
        _language = language;
        _tesseractExe = FindTesseractExe();
    }

    private string FindTesseractExe()
    {
        var searchPaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tesseract.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "tesseract.exe"),
            @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe",
            "tesseract.exe"
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
                return path;
        }

        var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in envPath.Split(Path.PathSeparator))
        {
            var exePath = Path.Combine(dir, "tesseract.exe");
            if (File.Exists(exePath))
                return exePath;
        }

        return "tesseract.exe";
    }

    public OcrResult Recognize(Bitmap bitmap)
    {
        var tempInput = Path.Combine(Path.GetTempPath(), $"tess_in_{Guid.NewGuid():N}.png");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"tess_out_{Guid.NewGuid():N}");
        var tempHocr = Path.Combine(Path.GetTempPath(), $"tess_hocr_{Guid.NewGuid():N}");

        try
        {
            bitmap.Save(tempInput, System.Drawing.Imaging.ImageFormat.Png);

            var psi = new ProcessStartInfo
            {
                FileName = _tesseractExe,
                Arguments = $"\"{tempInput}\" \"{tempOutput}\" -l {_language} hocr",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return new OcrResult { Error = "Failed to start Tesseract process" };

            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            if (process.ExitCode != 0)
            {
                return new OcrResult { Error = $"Tesseract error: {error}" };
            }

            var hocrPath = tempOutput + ".hocr";
            if (!File.Exists(hocrPath))
                return new OcrResult { Error = "Tesseract did not produce hocr output" };

            var hocrContent = File.ReadAllText(hocrPath);
            return ParseHocrResult(hocrContent);
        }
        catch (Exception ex)
        {
            return new OcrResult { Error = ex.Message };
        }
        finally
        {
            try
            {
                if (File.Exists(tempInput)) File.Delete(tempInput);
                if (File.Exists(tempOutput + ".hocr")) File.Delete(tempOutput + ".hocr");
                if (File.Exists(tempOutput + ".txt")) File.Delete(tempOutput + ".txt");
            }
            catch { }
        }
    }

    public OcrResult Recognize(string imagePath)
    {
        using var bitmap = new Bitmap(imagePath);
        return Recognize(bitmap);
    }

    private OcrResult ParseHocrResult(string hocrContent)
    {
        var words = new List<OcrWord>();
        var textBuilder = new System.Text.StringBuilder();

        var wordRegex = new System.Text.RegularExpressions.Regex(
            @"<span class='ocrx_word'[^>]*>.*?<strong>([^<]*)</strong>.*?</span>",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var bboxRegex = new System.Text.RegularExpressions.Regex(
            @"title='bbox (\d+) (\d+) (\d+) (\d+)'",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var confRegex = new System.Text.RegularExpressions.Regex(
            @"title='x_wconf (\d+)'",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var matches = wordRegex.Matches(hocrContent);
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var wordText = match.Groups[1].Value.Trim();
            var bboxMatch = bboxRegex.Match(match.Value);
            var confMatch = confRegex.Match(match.Value);

            var word = new OcrWord { Text = wordText };

            if (bboxMatch.Success)
            {
                word.BoundingBox = new OcrRect
                {
                    X = int.Parse(bboxMatch.Groups[1].Value),
                    Y = int.Parse(bboxMatch.Groups[2].Value),
                    Width = int.Parse(bboxMatch.Groups[3].Value) - int.Parse(bboxMatch.Groups[1].Value),
                    Height = int.Parse(bboxMatch.Groups[4].Value) - int.Parse(bboxMatch.Groups[2].Value)
                };
            }

            if (confMatch.Success)
            {
                word.Confidence = int.Parse(confMatch.Groups[1].Value) / 100.0;
            }
            else
            {
                word.Confidence = 0.9;
            }

            words.Add(word);
            if (textBuilder.Length > 0)
                textBuilder.Append(' ');
            textBuilder.Append(wordText);
        }

        var lineRegex = new System.Text.RegularExpressions.Regex(
            @"<span class='ocr_line'[^>]*>(.*?)</span>",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var lineMatches = lineRegex.Matches(hocrContent);

        return new OcrResult
        {
            Text = textBuilder.ToString(),
            Words = words,
            Language = _language,
            Confidence = words.Count > 0 ? words.Average(w => w.Confidence) : 0,
            Engine = "Tesseract"
        };
    }

    public List<OcrWord> FindWords(OcrResult result, string keyword, bool caseSensitive = false)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return result.Words
            .Where(w => w.Text.Contains(keyword, comparison))
            .ToList();
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
}
