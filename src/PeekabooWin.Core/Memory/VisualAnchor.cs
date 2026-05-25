using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekabooWin.Core.Memory;

public class VisualAnchor
{
    public string AnchorType { get; set; } = "";
    public List<string> TextVariants { get; set; } = [];
    public List<string> AppModes { get; set; } = [];
    public double MatchConfidence { get; set; } = 0.8;

    public static List<VisualAnchor> StandardAnchors => new()
    {
        new VisualAnchor { AnchorType = "input_box", TextVariants = new() { "说点什么", "说点什么...", "Type a message", "placeholder", "输入", "搜索" }, AppModes = new() { "browser", "dialog" }, MatchConfidence = 0.88 },
        new VisualAnchor { AnchorType = "send_btn", TextVariants = new() { "发送", "Send", "submit", "提交", "Send»" }, AppModes = new() { "browser" }, MatchConfidence = 0.85 },
        new VisualAnchor { AnchorType = "ok_btn", TextVariants = new() { "确定", "OK", "好的", "确认", "Yes", "是" }, AppModes = new() { "dialog", "shell" }, MatchConfidence = 0.90 },
        new VisualAnchor { AnchorType = "cancel_btn", TextVariants = new() { "取消", "Cancel", "否", "关闭" }, AppModes = new() { "dialog", "shell" }, MatchConfidence = 0.90 },
        new VisualAnchor { AnchorType = "close_btn", TextVariants = new() { "关闭", "×", "Close", "X", "❌" }, AppModes = new() { "dialog", "browser" }, MatchConfidence = 0.82 },
        new VisualAnchor { AnchorType = "edit_region", TextVariants = new() { "" }, AppModes = new() { "editor" }, MatchConfidence = 0.75 },
    };
}

public class AnchorMatcher
{
    private readonly List<VisualAnchor> _anchors;
    public AnchorMatcher() { _anchors = VisualAnchor.StandardAnchors; }
    public AnchorMatcher(List<VisualAnchor> anchors) { _anchors = anchors.Count > 0 ? anchors : VisualAnchor.StandardAnchors; }

    public AnchorMatchResult? MatchAnchor(string anchorType, string appMode, List<string> visibleTexts)
    {
        var anchor = _anchors.FirstOrDefault(a => a.AnchorType == anchorType && a.AppModes.Contains(appMode));
        if (anchor == null) return null;
        foreach (var variant in anchor.TextVariants)
        {
            var matched = visibleTexts.FirstOrDefault(t => t.Contains(variant, StringComparison.OrdinalIgnoreCase));
            if (matched != null) return new AnchorMatchResult { AnchorType = anchorType, MatchedText = matched, Confidence = anchor.MatchConfidence, IsFound = true };
        }
        foreach (var variant in anchor.TextVariants)
        {
            if (variant.Length < 3) continue;
            var matched = visibleTexts.FirstOrDefault(t => t.Contains(variant.Substring(0, 2), StringComparison.OrdinalIgnoreCase));
            if (matched != null) return new AnchorMatchResult { AnchorType = anchorType, MatchedText = matched, Confidence = anchor.MatchConfidence * 0.7, IsFound = true };
        }
        return new AnchorMatchResult { AnchorType = anchorType, IsFound = false, Confidence = 0 };
    }

    public AnchorCoverageResult CheckCoverage(List<string> requiredAnchors, string appMode, List<string> visibleTexts)
    {
        var result = new AnchorCoverageResult();
        foreach (var ra in requiredAnchors)
        {
            var match = MatchAnchor(ra, appMode, visibleTexts);
            if (match?.IsFound == true) result.FoundAnchors.Add((ra, match));
            else result.MissingAnchors.Add(ra);
        }
        result.IsFullyCovered = result.MissingAnchors.Count == 0;
        result.CoverageScore = requiredAnchors.Count == 0 ? 1.0 : (double)result.FoundAnchors.Count / requiredAnchors.Count;
        return result;
    }
}

public class AnchorMatchResult
{
    public string AnchorType { get; set; } = "";
    public string MatchedText { get; set; } = "";
    public double Confidence { get; set; }
    public bool IsFound { get; set; }
}

public class AnchorCoverageResult
{
    public bool IsFullyCovered { get; set; }
    public double CoverageScore { get; set; }
    public List<(string anchorType, AnchorMatchResult match)> FoundAnchors { get; set; } = [];
    public List<string> MissingAnchors { get; set; } = [];
}