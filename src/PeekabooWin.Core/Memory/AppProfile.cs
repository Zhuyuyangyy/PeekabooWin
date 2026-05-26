using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekabooWin.Core.Memory;

public class AppProfile
{
    public string AppId { get; set; } = "";
    public string AppName { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string WindowType { get; set; } = "";
    public string InputMode { get; set; } = "";
    public string RiskDomain { get; set; } = "";
    public List<string> KnownAnchors { get; set; } = [];
    public List<string> SupportedActions { get; set; } = [];
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public int VisitCount { get; set; } = 1;

    public static AppProfile FromWindowSignature(WindowSignature sig)
    {
        var appId = sig.ProcessName.Replace(".exe", "").ToLower();
        var anchors = new List<string>();
        if (sig.WindowType == "browser" && sig.InputMode == "web_textbox")
            { anchors.Add("input_box"); anchors.Add("send_btn"); }
        else if (sig.InputMode == "edit_field")
            { anchors.Add("edit_region"); }
        else if (sig.WindowType == "dialog")
            { anchors.Add("ok_btn"); anchors.Add("cancel_btn"); }

        return new AppProfile
        {
            AppId = appId,
            AppName = sig.WindowTitle,
            ProcessName = sig.ProcessName,
            WindowType = sig.WindowType,
            InputMode = sig.InputMode,
            RiskDomain = sig.RiskDomain,
            KnownAnchors = anchors,
            SupportedActions = new List<string> { "click", "type", "hotkey" }
        };
    }

    public void Touch() { LastSeen = DateTime.UtcNow; VisitCount++; }

    public bool IsCompatibleWith(SkillScope? scope)
    {
        if (scope == null || scope.SupportedApps.Count == 0) return true;
        return scope.SupportedApps.Any(a => AppId.Contains(a) || a == "*");
    }
}