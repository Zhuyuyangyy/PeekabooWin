using System;
using System.Collections.Generic;
using System.Linq;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Planning;

namespace PeekabooWin.Core.Agent;

/// <summary>
/// V0.7 VacpPlanner with Visual Skill Memory integration.
///
/// Before planning: checks skill store for matching (app+screen).
///   → Hit with high confidence: return SkillPlanResult (skip full VACP)
///   → Miss: proceed with normal VACP pipeline
///
/// After successful execution:
///   → Extract skill → Store
/// </summary>
public class VacpPlannerWithSkills
{
    private readonly VacpPlanner _planner;
    private readonly VacpSkillIntegration _skills;

    public VacpPlannerWithSkills(VacpPlanner planner, VisualSkillStore? store = null)
    {
        _planner = planner;
        _skills = new VacpSkillIntegration(store);
    }

    /// <summary>
    /// Plan with skill memory — checks store before VACP, extracts after success.
    /// </summary>
    public async Task<VacpSkillResult> PlanWithSkills(VacpRequest request, string? appPattern = null)
    {
        var result = new VacpSkillResult();

        // Step 0: Skill lookup before planning
        var screenType = InferScreenType(request.Task);
        var match = _skills.BeforePlanning(appPattern ?? "*", screenType);
        result.SkillMatch = match;

        if (match != null && match.CanSkipVision)
        {
            // Skill hit: short-circuit VACP vision pipeline
            result.SkippedBySkill = true;
            result.SkillUsed = match.Skill;
            result.Successful = true;
            result.FinalMessage = $"Skill hit: {match.Skill.Name} (confidence: {match.Confidence:F2})";
            result.StepsExecuted = match.Skill.ProcedureSteps;
            result.SkillConfidence = match.Confidence;

            // Record skill usage
            match.Skill.RecordUsage(true);
            _skills.AfterSuccess(ToTaskTrace(request.Task, match.Skill));
            return result;
        }

        // Step 1: Normal VACP execution
        var vacpResult = await _planner.Execute闭环(request);
        result.VacpResult = vacpResult;
        result.Successful = vacpResult.Success;
        result.FinalMessage = vacpResult.FinalMessage;

        // Step 2: On success, extract and store skill
        if (vacpResult.Success)
        {
            var taskTrace = ToTaskTrace(request.Task, vacpResult);
            _skills.AfterSuccess(taskTrace);
            result.StepsExecuted = taskTrace.StepTraces
                .Where(s => s.SelectedAction != null)
                .Select(s => s.SelectedAction.ActionType)
                .Where(a => !string.IsNullOrEmpty(a))
                .ToList();
        }

        return result;
    }

    public IReadOnlyList<VisualSkill> GetSkills() => _skills.GetAllSkills();

    public List<(VisualSkill skill, double confidence)> RankSkills(string appPattern, string screenType)
        => _skills.RankSkills(appPattern, screenType);

    private static string InferScreenType(string task)
    {
        var t = task.ToLower();
        if (t.Contains("notepad") || t.Contains("edit") || t.Contains("text")) return "edit";
        if (t.Contains("web") || t.Contains("browser") || t.Contains("http")) return "web";
        if (t.Contains("dialog") || t.Contains("popup") || t.Contains("confirm")) return "dialog";
        if (t.Contains("file") || t.Contains("explorer") || t.Contains("folder")) return "file-explorer";
        return "generic";
    }

    private static VacpTaskTrace ToTaskTrace(string taskDescription, VisualSkill skill)
    {
        var steps = skill.ProcedureSteps.Select((action, i) => new VacpTraceRecord
        {
            StepIndex = i,
            ExecutionResult = "SUCCESS",
            VerificationScore = skill.SuccessRate,
            VerificationOutcome = "SUCCESS",
            SelectedAction = new SelectedActionRecord { ActionType = action }
        }).ToList();

        return new VacpTaskTrace
        {
            TaskId = Guid.NewGuid().ToString("N")[..12],
            TaskDescription = skill.Name,
            OverallSuccess = true,
            StepTraces = steps
        };
    }

    private static VacpTaskTrace ToTaskTrace(string taskDescription, VacpResult vacpResult)
    {
        var steps = new List<VacpTraceRecord>();

        if (vacpResult.SelectedAction != null)
        {
            steps.Add(new VacpTraceRecord
            {
                StepIndex = 0,
                SelectedAction = new SelectedActionRecord
                {
                    ActionType = vacpResult.SelectedAction.ActionType,
                    TargetLabel = vacpResult.SelectedAction.TargetElement?.Label ?? "",
                    TargetCoordinates = new CoordinateRecord
                    {
                        X = vacpResult.SelectedAction.TargetElement?.BBox?.CenterX ?? 0,
                        Y = vacpResult.SelectedAction.TargetElement?.BBox?.CenterY ?? 0
                    }
                },
                ExecutionResult = vacpResult.Success ? "SUCCESS" : "FAILED",
            VerificationScore = vacpResult.VerificationResult?.VerificationScore ?? 0,
                VerificationOutcome = vacpResult.VerificationResult?.Outcome.ToString() ?? ""
            });
        }

        return new VacpTaskTrace
        {
            TaskId = Guid.NewGuid().ToString("N")[..12],
            TaskDescription = taskDescription,
            OverallSuccess = vacpResult.Success,
            StepTraces = steps
        };
    }
}

public class VacpSkillResult
{
    public bool Successful { get; set; }
    public string FinalMessage { get; set; } = "";
    public bool SkippedBySkill { get; set; }
    public SkillMatch? SkillMatch { get; set; }
    public VisualSkill? SkillUsed { get; set; }
    public double SkillConfidence { get; set; }
    public VacpResult? VacpResult { get; set; }
    public List<string> StepsExecuted { get; set; } = [];
}