using System;
using System.Collections.Generic;

namespace PeekabooWin.Core.Memory;

/// <summary>
/// A reusable UI operation skill extracted from a successful VACP trace.
/// V0.7 Visual Skill Memory.
/// </summary>
public class VisualSkill
{
    public string SkillId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "";
    public string AppPattern { get; set; } = "";   // e.g. "notepad.exe", "*doubao*"
    public string ScreenType { get; set; } = "";  // e.g. "edit", "dialog", "web-form"
    public List<string> TriggerConditions { get; set; } = [];
    public List<string> ProcedureSteps { get; set; } = [];  // serialized action sequence
    public string RiskLevel { get; set; } = "L0";
    public string RiskDomain { get; set; } = "neutral";
    public List<string> ContextAnchors { get; set; } = [];
    public double SuccessRate { get; set; } = 1.0;
    public int UsageCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // V0.9 SkillScope: cross-app migration metadata
    public SkillScope? Scope { get; set; }

    public void RecordUsage(bool success)
    {
        UsageCount++;
        // Incremental success rate
        SuccessRate = (SuccessRate * (UsageCount - 1) + (success ? 1.0 : 0.0)) / UsageCount;
        UpdatedAt = DateTime.UtcNow;
    }
}
