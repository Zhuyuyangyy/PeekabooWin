using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PeekabooWin.Core.Memory;

/// <summary>
/// Skill retrieval with multi-dimensional scoring for V0.8 skill-guided execution.
/// V0.8 Skill-Guided Execution.
/// </summary>
public class SkillRetriever
{
    private readonly VisualSkillStore _store;

    public SkillRetriever(VisualSkillStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Full-text skill search with multi-dimensional scoring.
    /// Returns top-3 ranked SkillSearchResults.
    /// </summary>
    public List<SkillSearchResult> Search(string taskText, string? appPattern = null,
        string? visibleText = null, string? windowTitle = null)
    {
        var all = _store.GetAll();
        var results = new List<SkillSearchResult>();

        foreach (var skill in all)
        {
            var score = ComputeMatchScore(skill, taskText, appPattern, visibleText, windowTitle);
            results.Add(new SkillSearchResult { Skill = skill, Score = score, Reason = Explain(score) });
        }

        return results
            .OrderByDescending(r => r.Score.Total)
            .Take(3)
            .ToList();
    }

    private SkillMatchScore ComputeMatchScore(VisualSkill skill, string taskText, string? app,
        string? visibleText, string? windowTitle)
    {
        // App match: wildcard pattern
        double appMatch = SkillExecutionPolicy.MatchesAppPattern(skill.AppPattern, app ?? "")
            ? 1.0 : 0.0;

        // Text match: visible OCR text vs trigger conditions
        double textMatch = ComputeTextMatch(skill, visibleText ?? "", taskText);

        // Action sequence match: task verbs vs skill procedure steps
        double actionMatch = ComputeActionMatch(skill, taskText);

        // Risk match: skill riskLevel vs task risk inference
        double riskMatch = ComputeRiskMatch(skill, taskText);

        // Recency: log-scaled usage count
        double recency = Math.Log(skill.UsageCount + 1) / Math.Log(10);

        double total = 0.30 * appMatch + 0.25 * textMatch + 0.20 * actionMatch
                     + 0.15 * riskMatch + 0.10 * recency;

        return new SkillMatchScore
        {
            AppMatch = appMatch,
            TextMatch = textMatch,
            ActionSequenceMatch = actionMatch,
            RiskMatch = riskMatch,
            RecencyFactor = recency,
            Total = total
        };
    }

    private double ComputeTextMatch(VisualSkill skill, string visibleText, string taskText)
    {
        if (string.IsNullOrEmpty(visibleText) || skill.TriggerConditions.Count == 0)
            return 0.5; // neutral — no info

        var vt = visibleText.ToLower();
        int hits = 0;
        foreach (var cond in skill.TriggerConditions)
        {
            // cond may be "candidates=5" or "element_labels=Edit,Text Editor"
            var keyVal = cond.Split('=', 2);
            if (keyVal.Length < 2) continue;
            var val = keyVal[1].ToLower();

            // Check if any keyword in the value appears in visible text
            var keywords = val.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (keywords.Any(k => vt.Contains(k)))
                hits++;
        }

        return skill.TriggerConditions.Count > 0
            ? (double)hits / skill.TriggerConditions.Count
            : 0.0;
    }

    private double ComputeActionMatch(VisualSkill skill, string taskText)
    {
        var taskLower = taskText.ToLower();

        // Extract action verbs from task
        var taskVerbs = new List<string>();
        if (taskLower.Contains("click")) taskVerbs.Add("click");
        if (taskLower.Contains("type") || taskLower.Contains("input") || taskLower.Contains("fill"))
            taskVerbs.Add("type");
        if (taskLower.Contains("press")) taskVerbs.Add("press");
        if (taskLower.Contains("open")) taskVerbs.Add("open");
        if (taskLower.Contains("close")) taskVerbs.Add("close");
        if (taskLower.Contains("save")) taskVerbs.Add("save");
        if (taskLower.Contains("delete")) taskVerbs.Add("delete");

        if (taskVerbs.Count == 0) return 0.5; // neutral

        var skillActions = skill.ProcedureSteps
            .Select(a => a.ToLower())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int matches = taskVerbs.Count(v => skillActions.Contains(v));
        return (double)matches / taskVerbs.Count;
    }

    private double ComputeRiskMatch(VisualSkill skill, string taskText)
    {
        bool isHighRisk = SkillExecutionPolicy.IsHighRiskTask(taskText);
        var skillRisk = skill.RiskLevel;

        if (!isHighRisk) return 1.0; // low-risk task, any skill is fine

        // High-risk task: prefer higher-risk-rated skills
        return skillRisk switch
        {
            "L2" => 1.0,
            "L1" => 0.7,
            "L0" => 0.0, // L0 skill insufficient for high-risk task
            _ => 0.0
        };
    }

    private static string Explain(SkillMatchScore score)
    {
        var parts = new List<string>();
        if (score.AppMatch > 0) parts.Add($"app={score.AppMatch:F2}");
        if (score.TextMatch > 0) parts.Add($"text={score.TextMatch:F2}");
        if (score.ActionSequenceMatch > 0) parts.Add($"action={score.ActionSequenceMatch:F2}");
        if (score.RiskMatch > 0) parts.Add($"risk={score.RiskMatch:F2}");
        parts.Add($"recency={score.RecencyFactor:F2}");
        return string.Join(" ", parts);
    }
}