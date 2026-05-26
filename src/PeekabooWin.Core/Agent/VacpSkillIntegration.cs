using System;
using System.Collections.Generic;
using System.Linq;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Planning;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Core.Agent;
public class VacpSkillIntegration
{
    private readonly VisualSkillStore _store;
    private readonly VisualSkillExtractor _extractor;
    private readonly VisualSkillRetriever _retriever;
    private readonly SkillRetriever _skillRetriever;
    private readonly SkillExecutionPolicy _policy;
    public VacpSkillIntegration(VisualSkillStore? store = null)
    { _store = store ?? new VisualSkillStore(); _extractor = new VisualSkillExtractor(); _retriever = new VisualSkillRetriever(_store); _skillRetriever = new SkillRetriever(_store); _policy = new SkillExecutionPolicy(); }
    public List<SkillSearchResult> Search(string taskText, string? appPattern = null, string? visibleText = null, string? windowTitle = null) => _skillRetriever.Search(taskText, appPattern, visibleText, windowTitle);
    public SkillExecutionPolicy Policy => _policy;
    public WindowSignature BuildWindowSignature(string? windowTitle = null)
    {
        return BuildWindowSignatureAsync(windowTitle).GetAwaiter().GetResult();
    }

    public async Task<WindowSignature> BuildWindowSignatureAsync(string? windowTitle = null)
    {
        var sig = new WindowSignature { CapturedAt = DateTime.UtcNow };
        var windowService = new WindowService();
        var captureService = new CaptureService(windowService);
        var ocrService = new OcrService();
        var allWindows = windowService.ListWindows(null);
        var targetWin = string.IsNullOrEmpty(windowTitle) ? allWindows.FirstOrDefault() : allWindows.FirstOrDefault(w => w.Title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase));
        if (targetWin != null) { sig.ProcessName = targetWin.ProcessName; sig.WindowTitle = targetWin.Title; var (wt, im, rd) = ClassifyWindow(targetWin); sig.WindowType = wt; sig.InputMode = im; sig.RiskDomain = rd; }
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sig_" + Guid.NewGuid().ToString("N") + ".png");
        try { captureService.CaptureWindow(targetWin?.Title ?? "", tempPath); if (System.IO.File.Exists(tempPath)) { var ocrResult = await ocrService.RecognizeImageAsync(tempPath); sig.VisibleTexts = ocrResult.Words.Select(w => w.Text).Distinct().ToList(); } } finally { try { System.IO.File.Delete(tempPath); } catch (Exception ex) { PekaLogger.Warn("VacpSkillIntegration", "Failed to delete temp file: " + tempPath, ex); } }
        return sig;
    }
    public List<SkillSearchResult> SearchWithContext(string taskText, string? windowTitle = null)
    {
        var sig = BuildWindowSignature(windowTitle);
        var results = _skillRetriever.Search(taskText, sig.ProcessName, null, sig.WindowTitle);
        var validator = new SkillScopeValidator();
        return results.Where(r => { if (r.Skill.Scope == null) return true; var app = AppProfile.FromWindowSignature(sig); var scopeResult = validator.Validate(r.Skill, app); if (!scopeResult.IsValid) { r.Reason = "[BLOCKED] " + scopeResult.Reason; return false; } return true; }).ToList();
    }
    public void AfterSuccess(VacpTaskTrace taskTrace) { try { var skill = _extractor.Extract(taskTrace); if (skill != null) { EnrichSkillFromTrace(skill, taskTrace); _store.Add(skill); } } catch (Exception ex) { PekaLogger.Warn("VacpSkillIntegration", "AfterSuccess failed", ex); } }
    public SkillMatch? BeforePlanning(string appPattern, string screenType) { var skill = _retriever.Retrieve(appPattern, screenType, minConfidence: 0.75); if (skill == null) return null; return new SkillMatch { Skill = skill, Confidence = ComputeConfidence(skill), CanSkipVision = skill.SuccessRate >= 0.9 && skill.UsageCount >= 2 }; }
    public IReadOnlyList<VisualSkill> GetAllSkills() => _store.GetAll();
    public List<(VisualSkill skill, double confidence)> RankSkills(string appPattern, string screenType) => _retriever.Rank(appPattern, screenType, top: 5);
    private static (string wt, string im, string rd) ClassifyWindow(WindowInfo win) { var title = win.Title.ToLower(); var proc = win.ProcessName.ToLower(); string wt = proc.Contains("notepad") ? "editor" : proc.Contains("chrome") || proc.Contains("msedge") ? "browser" : title.Contains("dialog") || title.Contains("confirm") ? "dialog" : "unknown"; string im = wt == "editor" ? "edit_field" : wt == "browser" ? "web_textbox" : wt == "dialog" ? "dialog_input" : "unknown"; string rd = title.Contains("bank") || title.Contains("pay") || title.Contains("transfer") ? "payment" : title.Contains("doubao") || title.Contains("ai") ? "external_ai_chat" : title.Contains("admin") || title.Contains("setting") ? "admin" : "neutral"; return (wt, im, rd); }
    private static void EnrichSkillFromTrace(VisualSkill skill, VacpTaskTrace trace) { var labels = trace.StepTraces.Where(s => s.SelectedAction != null && !string.IsNullOrEmpty(s.SelectedAction?.TargetLabel)).Select(s => s.SelectedAction!.TargetLabel!).Distinct().ToList(); if (labels.Count > 0) skill.TriggerConditions.Add("element_labels=" + string.Join(",", labels)); }
    private static double ComputeConfidence(VisualSkill skill) => skill.SuccessRate * Math.Min(Math.Log(skill.UsageCount + 1) / Math.Log(10), 1.0);
}
public class SkillMatch { public VisualSkill Skill { get; set; } = null!; public double Confidence { get; set; } public bool CanSkipVision { get; set; } }
