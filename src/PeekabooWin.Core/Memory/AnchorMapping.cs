using System;
using System.Collections.Generic;
using System.Linq;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Memory;

public static class AnchorMapping
{
    private static readonly Dictionary<(WindowType, string), List<string>> AnchorTextMap = new()
    {
        { (WindowType.Edit, "input_box"),      new[] { "Notepad", "Edit", "file", "edit", "type here" }.ToList() },
        { (WindowType.Edit, "save_btn"),       new[] { "Save", "save", "storage" }.ToList() },
        { (WindowType.Edit, "close_btn"),      new[] { "Close", "close", "x" }.ToList() },
        { (WindowType.WebBrowser, "input_box"),  new[] { "Compose", "type", "search", "input" }.ToList() },
        { (WindowType.WebBrowser, "send_btn"),  new[] { "Send", "send", "submit", "post" }.ToList() },
        { (WindowType.WebBrowser, "close_btn"), new[] { "Close", "close", "x" }.ToList() },
        { (WindowType.Dialog, "confirm_btn"),   new[] { "OK", "ok", "yes", "confirm", "confirm" }.ToList() },
        { (WindowType.Dialog, "cancel_btn"),    new[] { "Cancel", "cancel", "no", "close" }.ToList() },
        { (WindowType.Dialog, "close_btn"),     new[] { "Close", "close", "x" }.ToList() },
        { (WindowType.Dialog, "save_btn"),      new[] { "Save", "save", "storage" }.ToList() },
    };

    public static List<string> GetSearchTexts(WindowType appType, string anchorName)
    {
        var key = (appType, anchorName);
        return AnchorTextMap.TryGetValue(key, out var texts) ? texts : new List<string> { anchorName };
    }

    public static double ScoreAnchorMatch(string foundText, WindowType appType, string anchorName)
    {
        if (string.IsNullOrEmpty(foundText)) return 0.0;
        var expected = GetSearchTexts(appType, anchorName);
        if (expected.Any(e => foundText.Contains(e, StringComparison.OrdinalIgnoreCase))) return 1.0;
        var foundLower = foundText.ToLower();
        int matchedChars = expected.SelectMany(e => e.ToLower().Intersect(foundLower)).Count();
        return Math.Min(1.0, (double)matchedChars / Math.Max(foundText.Length, 1));
    }
}