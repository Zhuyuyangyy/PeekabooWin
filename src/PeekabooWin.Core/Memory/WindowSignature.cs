using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekabooWin.Core.Memory;

public class WindowSignature
{
    public string WindowTitle { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string WindowType { get; set; } = "";
    public string InputMode { get; set; } = "";
    public string RiskDomain { get; set; } = "";
    public List<string> VisibleTexts { get; set; } = [];
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    public static WindowSignature FromProcessAndTitle(string processName, string title)
    {
        var p = processName.ToLower();
        var t = title.ToLower();
        return new WindowSignature
        {
            ProcessName = p,
            WindowTitle = title,
            WindowType = ClassifyWindowType(p, t),
            InputMode = ClassifyInputMode(p, t),
            RiskDomain = ClassifyRiskDomain(p, t)
        };
    }

    private static string ClassifyWindowType(string p, string t) =>
        p.Contains("msedge") || p.Contains("chrome") || p.Contains("firefox") ? "browser" :
        p.Contains("notepad") || p.Contains("wordpad") ? "editor" :
        t.Contains("dialog") || t.Contains("confirm") || t.Contains("弹窗") ? "dialog" :
        "unknown";

    private static string ClassifyInputMode(string p, string t) =>
        p.Contains("msedge") || p.Contains("chrome") ? "web_textbox" :
        p.Contains("notepad") ? "edit_field" :
        t.Contains("dialog") || t.Contains("弹窗") ? "dialog_input" :
        "unknown";

    private static string ClassifyRiskDomain(string p, string t) =>
        t.Contains("bank") || t.Contains("支付") || t.Contains("转账") ? "payment" :
        t.Contains("doubao") || t.Contains("豆包") || t.Contains("ai") ? "external_ai_chat" :
        t.Contains("admin") || t.Contains("管理") ? "admin" :
        "neutral";

    public double SimilarityTo(WindowSignature other)
    {
        double s = 0; int w = 0;
        if (WindowType == other.WindowType) { s += 0.4; } w += 4;
        if (InputMode == other.InputMode) { s += 0.3; } w += 3;
        if (RiskDomain == other.RiskDomain || RiskDomain == "neutral" || other.RiskDomain == "neutral") { s += 0.2; } w += 2;
        if (BelongsToSameFamily(other)) { s += 0.1; } w += 1;
        return w == 0 ? 0 : s / w;
    }

    private bool BelongsToSameFamily(WindowSignature o)
    {
        var browsers = new[] { "msedge", "chrome", "firefox" };
        return browsers.Any(b => ProcessName.Contains(b)) && browsers.Any(b => o.ProcessName.Contains(b));
    }
}