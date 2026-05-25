using System;

namespace PeekabooWin.Core.Memory;

/// <summary>
/// Scoring components for a skill-vs-task match evaluation.
/// V0.8 Skill-Guided Execution.
/// </summary>
public class SkillMatchScore
{
    /// <summary>
    /// 0.0–1.0 wildcard pattern match between task app and skill AppPattern.
    /// </summary>
    public double AppMatch { get; set; }

    /// <summary>
    /// 0.0–1.0 — OCR visible text vs skill TriggerConditions.
    /// </summary>
    public double TextMatch { get; set; }

    /// <summary>
    /// 0.0–1.0 — task action verbs vs skill ProcedureSteps alignment.
    /// </summary>
    public double ActionSequenceMatch { get; set; }

    /// <summary>
    /// 0.0–1.0 — skill RiskLevel compatibility with inferred task risk.
    /// </summary>
    public double RiskMatch { get; set; }

    /// <summary>
    /// Log-scaled usage count factor: log(usageCount+1)/log(10).
    /// </summary>
    public double RecencyFactor { get; set; }

    /// <summary>
    /// Weighted composite: 0.30*App + 0.25*Text + 0.20*Action + 0.15*Risk + 0.10*Recency.
    /// </summary>
    public double Total { get; set; }

    /// <summary>
    /// True when Total >= 0.6 and RiskMatch >= 0.5.
    /// </summary>
    public bool IsUsable => Total >= 0.6 && RiskMatch >= 0.5;
}

/// <summary>
/// Result of a skill search operation.
/// V0.8 Skill-Guided Execution.
/// </summary>
public class SkillSearchResult
{
    public VisualSkill Skill { get; set; } = null!;
    public SkillMatchScore Score { get; set; } = null!;
    public string Reason { get; set; } = "";
}