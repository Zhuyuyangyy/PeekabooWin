using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Ocr;

public class MultiEngineOcrService : IDisposable
{
    private readonly List<IOcrEngine> _engines;
    private readonly OcrPreprocessor _preprocessor;
    private readonly string _defaultLanguage;

    public MultiEngineOcrService(string tessdataPath = "", string defaultLanguage = "chi_sim+eng")
    {
        _defaultLanguage = defaultLanguage;
        _engines = new List<IOcrEngine>();
        _preprocessor = new OcrPreprocessor
        {
            ScaleFactor = 2,
            EnableDenoising = true,
            EnableBinarization = true,
            DenoiseRadius = 2
        };

        _engines.Add(new WindowsMediaOcrEngine(defaultLanguage));

        if (!string.IsNullOrEmpty(tessdataPath) && Directory.Exists(tessdataPath))
        {
            var lang = defaultLanguage.Split('+')[0];
            _engines.Add(new TesseractOcrEngine(tessdataPath, lang));
        }
        else
        {
            var defaultTessdata = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            if (Directory.Exists(defaultTessdata))
            {
                var lang = defaultLanguage.Split('+')[0];
                _engines.Add(new TesseractOcrEngine(defaultTessdata, lang));
            }
        }
    }

    public MultiEngineOcrService(OcrPreprocessor preprocessor, params IOcrEngine[] engines)
    {
        _preprocessor = preprocessor ?? new OcrPreprocessor();
        _engines = engines.ToList();
        _defaultLanguage = "auto";
    }

    public IReadOnlyList<IOcrEngine> Engines => _engines.AsReadOnly();

    public OcrPreprocessor Preprocessor => _preprocessor;

    public MultiOcrResult RecognizeAllEngines(Bitmap bitmap)
    {
        using var preprocessed = _preprocessor.Preprocess(bitmap);
        return RecognizeAllEngines(preprocessed, disposeOriginal: false);
    }

    public MultiOcrResult RecognizeAllEngines(Bitmap bitmap, bool disposeOriginal)
    {
        try
        {
            var results = new Dictionary<string, OcrResult>();

            foreach (var engine in _engines)
            {
                try
                {
                    var result = engine.Recognize(bitmap);
                    results[engine.Name] = result;
                }
                catch (Exception ex)
                {
                    results[engine.Name] = new OcrResult { Error = ex.Message };
                }
            }

            var fused = FuseResults(results);
            return new MultiOcrResult
            {
                Results = results,
                FusedResult = fused,
                BestEngine = results
                    .Where(r => r.Value.Error == null)
                    .OrderByDescending(r => r.Value.Confidence)
                    .FirstOrDefault().Key ?? ""
            };
        }
        finally
        {
            if (disposeOriginal)
                bitmap.Dispose();
        }
    }

    public MultiOcrResult RecognizeAllEngines(string imagePath)
    {
        using var bitmap = new Bitmap(imagePath);
        return RecognizeAllEngines(bitmap, disposeOriginal: false);
    }

    private OcrResult FuseResults(Dictionary<string, OcrResult> results)
    {
        var validResults = results.Where(r => r.Value.Error == null && r.Value.Words.Count > 0).ToList();

        if (validResults.Count == 0)
            return new OcrResult { Error = "No engine produced valid results" };

        if (validResults.Count == 1)
            return validResults[0].Value;

        var fusedWords = new List<OcrWord>();
        var wordDict = new Dictionary<string, List<OcrWord>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (engine, result) in validResults)
        {
            foreach (var word in result.Words)
            {
                var key = word.Text.ToLower();
                if (!wordDict.ContainsKey(key))
                    wordDict[key] = new List<OcrWord>();
                wordDict[key].Add(word);
            }
        }

        foreach (var kvp in wordDict)
        {
            var wordTexts = kvp.Value;
            var fusedWord = new OcrWord
            {
                Text = wordTexts.OrderByDescending(w => w.Confidence).First().Text,
                Confidence = wordTexts.Average(w => w.Confidence),
                BoundingBox = wordTexts
                    .Where(w => w.BoundingBox != null)
                    .OrderByDescending(w => w.Confidence)
                    .FirstOrDefault()?.BoundingBox
            };
            fusedWords.Add(fusedWord);
        }

        var textParts = fusedWords.Select(w => w.Text).ToList();
        var uniqueText = string.Join(" ", textParts);

        return new OcrResult
        {
            Text = uniqueText,
            Words = fusedWords,
            Language = _defaultLanguage,
            Confidence = fusedWords.Count > 0 ? fusedWords.Average(w => w.Confidence) : 0,
            Engine = $"Fused({string.Join("+", validResults.Select(r => r.Key))})"
        };
    }

    public List<OcrWord> FindWords(MultiOcrResult multiResult, string keyword, bool caseSensitive = false)
    {
        return multiResult.FusedResult.Words
            .Where(w => w.Text.Contains(keyword, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public (int x, int y)? FindWordCenter(MultiOcrResult multiResult, string keyword, bool caseSensitive = false)
    {
        var words = FindWords(multiResult, keyword, caseSensitive);
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
        _engines.Clear();
    }
}

public class MultiOcrResult
{
    public Dictionary<string, OcrResult> Results { get; set; } = new();
    public OcrResult FusedResult { get; set; } = new();
    public string BestEngine { get; set; } = "";
}

public class WindowsMediaOcrEngine : IOcrEngine
{
    private readonly OcrService _service;

    public string Name => "Windows.Media.Ocr";
    public string DefaultLanguage => "zh-CN";

    public WindowsMediaOcrEngine(string language = "zh-CN")
    {
        _service = new OcrService(language, enablePreprocessing: false);
    }

    public OcrResult Recognize(Bitmap bitmap)
    {
        return _service.RecognizeBitmapAsync(bitmap).GetAwaiter().GetResult();
    }

    public OcrResult Recognize(string imagePath)
    {
        return _service.RecognizeImageAsync(imagePath).GetAwaiter().GetResult();
    }

    public List<OcrWord> FindWords(OcrResult result, string keyword, bool caseSensitive = false)
    {
        return _service.FindWords(result, keyword, caseSensitive);
    }

    public (int x, int y)? FindWordCenter(OcrResult result, string keyword, bool caseSensitive = false)
    {
        return _service.FindWordCenter(result, keyword, caseSensitive);
    }
}
