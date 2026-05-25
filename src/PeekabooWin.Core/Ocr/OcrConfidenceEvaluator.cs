using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Ocr;

public class OcrConfidenceEvaluator
{
    public OcrQualityAssessment AssessQuality(OcrResult result)
    {
        if (result == null)
            return new OcrQualityAssessment { IsValid = false, ErrorMessage = "Null result" };

        var assessment = new OcrQualityAssessment
        {
            IsValid = true,
            Text = result.Text,
            WordCount = result.Words.Count,
            Confidence = result.Confidence
        };

        if (result.Words.Count == 0)
        {
            assessment.IsValid = false;
            assessment.ErrorMessage = "No words detected";
            return assessment;
        }

        assessment.AverageWordConfidence = result.Words.Average(w => w.Confidence);
        assessment.MinWordConfidence = result.Words.Min(w => w.Confidence);
        assessment.MaxWordConfidence = result.Words.Max(w => w.Confidence);

        assessment.TextDensity = ComputeTextDensity(result);
        assessment.UnusualCharacterRatio = ComputeUnusualCharacterRatio(result);
        assessment.WordSpacingAnomaly = ComputeWordSpacingAnomaly(result);
        assessment.LineConsistency = ComputeLineConsistency(result);

        var scores = new List<double>
        {
            assessment.AverageWordConfidence * 0.3,
            (1.0 - Math.Abs(assessment.UnusualCharacterRatio - 0.1) / 0.5) * 0.2,
            assessment.TextDensity > 0 ? Math.Min(1.0, assessment.TextDensity) * 0.2 : 0.3,
            assessment.LineConsistency * 0.15,
            (1.0 - assessment.WordSpacingAnomaly) * 0.15
        };

        assessment.OverallScore = scores.Sum();
        assessment.QualityGrade = GetGrade(assessment.OverallScore);

        return assessment;
    }

    private double ComputeTextDensity(OcrResult result)
    {
        if (result.Words.Count == 0) return 0;

        var allBoxes = result.Words.Where(w => w.BoundingBox != null).ToList();
        if (allBoxes.Count == 0) return 0.1;

        var minX = allBoxes.Min(b => b.BoundingBox!.X);
        var maxX = allBoxes.Max(b => b.BoundingBox!.Right);
        var minY = allBoxes.Min(b => b.BoundingBox!.Y);
        var maxY = allBoxes.Max(b => b.BoundingBox!.Bottom);

        var area = (maxX - minX) * (maxY - minY);
        if (area <= 0) return 0;

        var totalCharCount = result.Text.Length;
        return totalCharCount / Math.Sqrt(area);
    }

    private double ComputeUnusualCharacterRatio(OcrResult result)
    {
        if (string.IsNullOrEmpty(result.Text)) return 0;

        var unusualCount = 0;
        foreach (char c in result.Text)
        {
            if (IsUnusualCharacter(c))
                unusualCount++;
        }

        return (double)unusualCount / result.Text.Length;
    }

    private bool IsUnusualCharacter(char c)
    {
        if (c >= 0x4E00 && c <= 0x9FFF) return false;
        if (c >= 0x0041 && c <= 0x005A) return false;
        if (c >= 0x0061 && c <= 0x007A) return false;
        if (c >= 0x0030 && c <= 0x0039) return false;
        if (c == ' ' || c == '\t' || c == '\n' || c == '\r') return false;
        if (c == '.' || c == ',' || c == '!' || c == '?' || c == ';' || c == ':') return false;
        if (c == '-' || c == '\'' || c == '"' || c == '(' || c == ')') return false;

        return true;
    }

    private double ComputeWordSpacingAnomaly(OcrResult result)
    {
        var wordsWithBoxes = result.Words.Where(w => w.BoundingBox != null).OrderBy(w => w.BoundingBox!.Y).ThenBy(w => w.BoundingBox!.X).ToList();
        if (wordsWithBoxes.Count < 2) return 0;

        var spaces = new List<double>();
        var currentY = wordsWithBoxes[0].BoundingBox!.Y;
        var currentLineWords = new List<OcrWord> { wordsWithBoxes[0] };

        for (int i = 1; i < wordsWithBoxes.Count; i++)
        {
            var word = wordsWithBoxes[i];
            var yDiff = Math.Abs(word.BoundingBox!.Y - currentY);

            if (yDiff < word.BoundingBox.Height / 2)
            {
                currentLineWords.Add(word);
            }
            else
            {
                if (currentLineWords.Count > 1)
                {
                    var sortedLine = currentLineWords.OrderBy(w => w.BoundingBox!.X).ToList();
                    for (int j = 1; j < sortedLine.Count; j++)
                    {
                        var space = sortedLine[j].BoundingBox!.X - sortedLine[j - 1].BoundingBox!.Right;
                        spaces.Add(space);
                    }
                }
                currentY = word.BoundingBox.Y;
                currentLineWords = new List<OcrWord> { word };
            }
        }

        if (spaces.Count < 2) return 0;

        var mean = spaces.Average();
        var variance = spaces.Sum(s => Math.Pow(s - mean, 2)) / spaces.Count;
        var stdDev = Math.Sqrt(variance);

        return Math.Min(1.0, stdDev / Math.Max(1, mean));
    }

    private double ComputeLineConsistency(OcrResult result)
    {
        var lines = result.Words
            .Where(w => w.BoundingBox != null)
            .GroupBy(w => (int)(w.BoundingBox!.Y / 10))
            .ToList();

        if (lines.Count < 2) return 1.0;

        var lineHeights = lines.Select(l =>
        {
            var heights = l.Select(w => w.BoundingBox!.Height).ToList();
            return heights.Max() - heights.Min();
        }).ToList();

        var avgHeight = lineHeights.Average();
        if (avgHeight < 1) return 1.0;

        var consistency = 1.0 - (lineHeights.Max() - lineHeights.Min()) / (avgHeight * 2);
        return Math.Max(0, Math.Min(1, consistency));
    }

    private string GetGrade(double score)
    {
        return score switch
        {
            >= 0.85 => "A (Excellent)",
            >= 0.70 => "B (Good)",
            >= 0.50 => "C (Fair)",
            >= 0.30 => "D (Poor)",
            _ => "F (Very Poor)"
        };
    }

    public double CalculateWordConfidence(string word, OcrRect? boundingBox)
    {
        var confidence = 0.5;

        if (string.IsNullOrWhiteSpace(word))
            return 0.1;

        confidence += 0.1 * Math.Min(1.0, word.Length / 10.0);

        if (ContainsLatinLetters(word))
            confidence += 0.1;
        if (ContainsChineseChars(word))
            confidence += 0.1;
        if (ContainsNumbers(word))
            confidence += 0.05;

        if (boundingBox != null)
        {
            var aspectRatio = boundingBox.Width / Math.Max(1, boundingBox.Height);
            if (aspectRatio > 1 && aspectRatio < 20)
                confidence += 0.1;

            if (boundingBox.Width > 20 && boundingBox.Height > 10)
                confidence += 0.05;
        }

        var unusualRatio = word.Count(c => IsUnusualCharacter(c)) / Math.Max(1, word.Length);
        confidence -= unusualRatio * 0.2;

        return Math.Max(0.1, Math.Min(1.0, confidence));
    }

    private bool ContainsLatinLetters(string s) => s.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
    private bool ContainsChineseChars(string s) => s.Any(c => c >= 0x4E00 && c <= 0x9FFF);
    private bool ContainsNumbers(string s) => s.Any(c => c >= '0' && c <= '9');

    public OcrResult ApplyConfidence(OcrResult result)
    {
        if (result.Words.Count == 0 || result.Words.All(w => w.Confidence == 0 || w.Confidence == 1.0))
        {
            foreach (var word in result.Words)
            {
                word.Confidence = CalculateWordConfidence(word.Text, word.BoundingBox);
            }
        }

        result.Confidence = result.Words.Count > 0
            ? result.Words.Average(w => w.Confidence)
            : 0;

        return result;
    }
}

public class OcrQualityAssessment
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string Text { get; set; } = "";
    public int WordCount { get; set; }
    public double Confidence { get; set; }
    public double AverageWordConfidence { get; set; }
    public double MinWordConfidence { get; set; }
    public double MaxWordConfidence { get; set; }
    public double TextDensity { get; set; }
    public double UnusualCharacterRatio { get; set; }
    public double WordSpacingAnomaly { get; set; }
    public double LineConsistency { get; set; }
    public double OverallScore { get; set; }
    public string QualityGrade { get; set; } = "";
}
