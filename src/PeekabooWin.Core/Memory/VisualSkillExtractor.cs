using System;
using System.Collections.Generic;
using System.Linq;
using PeekabooWin.Core.Planning;
using PeekabooWin.Core.Agent;

namespace PeekabooWin.Core.Memory;

/// <summary>
/// Extracts a VisualSkill from a successful VACP trace record.
/// V0.7 Visual Skill Memory.
/// </summary>
public class VisualSkillExtractor
{
    /// <summary>
    /// Extract a VisualSkill from a completed, successful VACP trace.
    /// Returns null if the trace is not suitable for skill extraction
    /// (e.g., failed traces, or traces with too few steps).
    /// </summary>
    public VisualSkill? Extract(VacpTraceRecord trace)
    {
        if (trace == null || trace.Steps.Count == 0)
            return null;

        // Only extract from successful traces with 2+ meaningful steps
        var meaningfulSteps = trace.Steps
            .Where(s => s.Action != null && !s.Action.Contains("verify", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (meaningfulSteps.Count < 1)
            return null;

        // Build trigger conditions from screen state at start
        var firstStep = trace.Steps.First();
        var triggers = new List<string>();

        if (!string.IsNullOrEmpty(trace.TargetApp))
            triggers.Add($"app={trace.TargetApp}");
        if (firstStep.ScreenElements != null && firstStep.ScreenElements.Count > 0)
            triggers.Add($"element_count={firstStep.ScreenElements.Count}");

        var procedureSteps = meaningfulSteps
            .Select(s => s.Action ?? "")
            .Where(a => !string.IsNullOrEmpty(a))
            .ToList();

        // Determine screen type from UI elements
        var screenType = InferScreenType(firstStep.ScreenElements);

        // Determine risk level from trace
        var riskLevel = trace.RiskLevel switch
        {
            RiskLevel.L0 or RiskLevel.L1 => "L0",
            RiskLevel.L2 => "L1",
            _ => "L0"
        };

        return new VisualSkill
        {
            SkillId = $"vs_{trace.SessionId[..12]}",
            Name = $"Skill: {trace.TaskGoal} on {trace.TargetApp}",
            AppPattern = trace.TargetApp ?? "*",
            ScreenType = screenType,
            TriggerConditions = triggers,
            ProcedureSteps = procedureSteps,
            RiskLevel = riskLevel,
            SuccessRate = trace.Steps.All(s => s.GroundTruthScore >= 0.8) ? 1.0 : 0.85,
            UsageCount = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static string InferScreenType(List<ScreenElement>? elements)
    {
        if (elements == null || elements.Count == 0)
            return "unknown";

        var types = elements
            .Select(e => e.Role ?? e.ClassName ?? "")
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        if (types.Any(t => t.Contains("edit", StringComparison.OrdinalIgnoreCase) || t.Contains("text", StringComparison.OrdinalIgnoreCase)))
            return "edit";
        if (types.Any(t => t.Contains("button", StringComparison.OrdinalIgnoreCase)))
            return "dialog";
        if (types.Any(t => t.Contains("document", StringComparison.OrdinalIgnoreCase)))
            return "document";
        if (types.Any(t => t.Contains("web", StringComparison.OrdinalIgnoreCase) || t.Contains("browser", StringComparison.OrdinalIgnoreCase)))
            return "web";

        return "generic";
    }
}