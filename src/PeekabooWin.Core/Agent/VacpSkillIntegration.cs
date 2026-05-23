using System;
using System.Collections.Generic;
using System.Linq;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Planning;

namespace PeekabooWin.Core.Agent;

/// <summary>
/// V0.7 Visual Skill Memory integration for VacpPlanner.
/// 
/// After successful VACP execution:
///   → Extract VisualSkill → Store
///   
/// Before VACP planning:
///   → Check skill store for matching screen type
///   → If hit with high confidence → skip full vision, use skill procedure
///   → If miss → proceed with normal VACP vision pipeline
/// </summary>
public class VacpSkillIntegration
{
    private readonly VisualSkillStore _store;
    private readonly VisualSkillExtractor _extractor;
    private readonly VisualSkillRetriever _retriever;

    public VacpSkillIntegration(VisualSkillStore? store = null)
    {
        _store = store ?? new VisualSkillStore();
        _extractor = new VisualSkillExtractor();
        _retriever = new VisualSkillRetriever(_store);
    }

    /// <summary>
    /// Called after successful VACP execution to extract and store the skill.
    /// </summary>
    public void AfterSuccess(VacpTaskTrace taskTrace)
    {
        try
        {
            var skill = _extractor.Extract(taskTrace);
            if (skill == null) return;

            // Enrich skill with element labels from trace for replay guidance
            EnrichSkillFromTrace(skill, taskTrace);
            _store.Add(skill);
        }
        catch
        {
            // Silently fail — skill extraction should not block VACP
        }
    }

    /// <summary>
    /// Called before VACP planning to check if a skill can short-circuit vision.
    /// Returns null if no suitable skill found (proceed with normal VACP pipeline).
    /// </summary>
    public SkillMatch? BeforePlanning(string appPattern, string screenType)
    {
        var skill = _retriever.Retrieve(appPattern, screenType, minConfidence: 0.75);
        if (skill == null) return null;

        return new SkillMatch
        {
            Skill = skill,
            Confidence = ComputeConfidence(skill),
            CanSkipVision = skill.SuccessRate >= 0.9 && skill.UsageCount >= 2
        };
    }

    public IReadOnlyList<VisualSkill> GetAllSkills() => _store.GetAll();

    public List<(VisualSkill skill, double confidence)> RankSkills(string appPattern, string screenType)
        => _retriever.Rank(appPattern, screenType, top: 5);

    private static void EnrichSkillFromTrace(VisualSkill skill, VacpTaskTrace trace)
    {
        // Store element labels from successful steps for replay matching
        var labels = trace.StepTraces
            .Where(s => s.SelectedAction != null && !string.IsNullOrEmpty(s.SelectedAction?.TargetLabel))
            .Select(s => s.SelectedAction!.TargetLabel!)
            .Distinct()
            .ToList();

        if (labels.Count > 0)
            skill.TriggerConditions.Add($"element_labels={string.Join(",", labels)}");
    }

    private static double ComputeConfidence(VisualSkill skill)
    {
        var usageFactor = Math.Log(skill.UsageCount + 1) / Math.Log(10);
        return skill.SuccessRate * Math.Min(usageFactor, 1.0);
    }
}

public class SkillMatch
{
    public VisualSkill Skill { get; set; } = null!;
    public double Confidence { get; set; }
    public bool CanSkipVision { get; set; }
}