using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekabooWin.Core.Memory;

/// <summary>
/// Retrieves relevant Visual Skills for a given context (app, screen).
/// V0.7 Visual Skill Memory.
/// </summary>
public class VisualSkillRetriever
{
    private readonly VisualSkillStore _store;

    public VisualSkillRetriever(VisualSkillStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Returns the best matching Visual Skill for the given context.
    /// Falls back to null if confidence is too low.
    /// </summary>
    public VisualSkill? Retrieve(string appPattern, string screenType, double minConfidence = 0.7)
    {
        var candidates = _store.Search(appPattern, screenType, top: 5);

        if (candidates.Count == 0)
            return null;

        // Confidence = success rate weighted by usage count (more used = more reliable)
        var best = candidates.First();
        var confidence = ComputeConfidence(best);

        return confidence >= minConfidence ? best : null;
    }

    public List<VisualSkill> RetrieveAll(string appPattern, string screenType, int top = 3)
    {
        return _store.Search(appPattern, screenType, top)
            .Where(s => ComputeConfidence(s) >= 0.6)
            .ToList();
    }

    /// <summary>
    /// Returns top-N skills sorted by confidence for a given context.
    /// </summary>
    public List<(VisualSkill skill, double confidence)> Rank(string appPattern, string screenType, int top = 5)
    {
        var candidates = _store.Search(appPattern, screenType, top: 10);
        return candidates
            .Select(s => (s, ComputeConfidence(s)))
            .Where(x => x.Item2 >= 0.5)
            .OrderByDescending(x => x.Item2)
            .Take(top)
            .ToList();
    }

    private static double ComputeConfidence(VisualSkill skill)
    {
        // Confidence = success_rate * log(usage_count + 1) / log(10)
        // More used skills are more reliable (logarithmic weight)
        var usageFactor = Math.Log(skill.UsageCount + 1) / Math.Log(10);
        return skill.SuccessRate * Math.Min(usageFactor, 1.0);
    }
}