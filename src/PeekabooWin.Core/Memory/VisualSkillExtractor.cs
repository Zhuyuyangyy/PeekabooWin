using System;
using System.Collections.Generic;
using System.Linq;
using PeekabooWin.Core.Planning;
using PeekabooWin.Core.Perception;

namespace PeekabooWin.Core.Memory;

/// <summary>
/// Extracts a VisualSkill from a successful VACP trace.
/// V0.7 Visual Skill Memory.
/// </summary>
public class VisualSkillExtractor
{
    /// <summary>
    /// Extract a VisualSkill from a completed VacpTaskTrace.
    /// Returns null if the trace has no successful steps or is unsuitable.
    /// </summary>
    public VisualSkill? Extract(VacpTaskTrace taskTrace)
    {
        if (taskTrace == null || taskTrace.StepTraces.Count == 0)
            return null;

        // Only extract from overall-successful traces
        if (!taskTrace.OverallSuccess)
            return null;

        // Collect meaningful (non-verify) actions
        var actionSteps = taskTrace.StepTraces
            .Where(s => s.ExecutionResult != "BLOCKED" &&
                        s.VerificationOutcome != "FAILED")
            .ToList();

        if (actionSteps.Count == 0)
            return null;

        // Use first step as the trigger context
        var firstStep = taskTrace.StepTraces[0];
        var triggers = new List<string>();

        if (firstStep.CandidateActions != null && firstStep.CandidateActions.Count > 0)
            triggers.Add($"candidates={firstStep.CandidateActions.Count}");

        var firstAction = firstStep.SelectedAction?.ActionType ?? firstStep.ExecutionResult;
        if (!string.IsNullOrEmpty(firstAction))
            triggers.Add($"first_action={firstAction}");

        var procedureSteps = actionSteps
            .Select(s => s.SelectedAction?.ActionType ?? s.ExecutionResult ?? "unknown")
            .Where(a => !string.IsNullOrEmpty(a) && a != "unknown")
            .ToList();

        var screenType = InferScreenType(firstStep.ScreenStateGraph);
        var riskLevel = InferRiskLevel(taskTrace);

        var avgVerif = taskTrace.StepTraces
            .Where(s => s.VerificationScore > 0)
            .Select(s => s.VerificationScore)
            .DefaultIfEmpty(1.0)
            .Average();

        return new VisualSkill
        {
            SkillId = $"vs_{taskTrace.TaskId[..Math.Min(12, taskTrace.TaskId.Length)]}",
            Name = $"Skill: {taskTrace.TaskDescription}",
            AppPattern = "*",
            ScreenType = screenType,
            TriggerConditions = triggers,
            ProcedureSteps = procedureSteps,
            RiskLevel = riskLevel,
            SuccessRate = avgVerif >= 0.8 ? 1.0 : avgVerif,
            UsageCount = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static string InferScreenType(ScreenStateGraph? graph)
    {
        if (graph == null) return "generic";

        var type = graph.ScreenType ?? "";
        if (!string.IsNullOrEmpty(type)) return type.ToLower();

        var labels = graph.Elements?
            .Select(e => e.Label ?? "")
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList() ?? [];

        if (labels.Any(l => l.Contains("edit", StringComparison.OrdinalIgnoreCase) ||
                           l.Contains("text", StringComparison.OrdinalIgnoreCase)))
            return "edit";
        if (labels.Any(l => l.Contains("button", StringComparison.OrdinalIgnoreCase)))
            return "dialog";
        if (labels.Any(l => l.Contains("web", StringComparison.OrdinalIgnoreCase) ||
                           l.Contains("browser", StringComparison.OrdinalIgnoreCase)))
            return "web";

        return "generic";
    }

    private static string InferRiskLevel(VacpTaskTrace trace)
    {
        var hasBlocked = trace.StepTraces.Any(s => s.RiskGateDecision == "BLOCK");
        var hasConfirm = trace.StepTraces.Any(s => s.RiskGateDecision == "CONFIRM");

        if (hasBlocked) return "L2";
        if (hasConfirm) return "L1";
        return "L0";
    }
}