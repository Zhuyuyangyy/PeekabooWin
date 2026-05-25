using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekabooWin.Core.Memory;

/// <summary>
/// Hard-filter execution policy for skill selection.
/// Returns false (block) if any guard condition is met.
/// V0.8 Skill-Guided Execution.
/// </summary>
public class SkillExecutionPolicy
{
    /// <summary>
    /// Returns false if the candidate skill should NOT be used for this task.
    /// Guards: null candidate, unusable score, app mismatch, high-risk task with L0 skill.
    /// </summary>
    public bool CanUseSkill(SkillSearchResult? candidate, string? taskText = null)
    {
        if (candidate == null) return false;
        if (!candidate.Score.IsUsable) return false;

        // App pattern must match
        if (!MatchesAppPattern(candidate.Skill.AppPattern, candidate.Skill.AppPattern))
            return false;

        // High-risk tasks (delete/transfer/send) need skill with riskLevel >= L1
        if (IsHighRiskTask(taskText) && candidate.Skill.RiskLevel == "L0")
            return false;

        return true;
    }

    /// <summary>
    /// Returns true if taskText contains dangerous-operation keywords.
    /// </summary>
    public static bool IsHighRiskTask(string? taskText)
    {
        if (string.IsNullOrEmpty(taskText)) return false;
        var t = taskText.ToLower();
        return t.Contains("delete") || t.Contains("transfer") ||
               t.Contains("send") || t.Contains("remove") ||
               t.Contains("destroy") || t.Contains("drop");
    }

    /// <summary>
    /// Wildcard match: skill.AppPattern is the pattern, app is the actual app name.
    /// </summary>
    public static bool MatchesAppPattern(string pattern, string app)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
        if (string.IsNullOrEmpty(app)) return false;

        if (!pattern.Contains('*'))
            return pattern.Equals(app, StringComparison.OrdinalIgnoreCase);

        var parts = pattern.Split('*');
        var idx = 0;
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            var found = app.IndexOf(part, idx, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            idx = found + part.Length;
        }
        return true;
    }
}