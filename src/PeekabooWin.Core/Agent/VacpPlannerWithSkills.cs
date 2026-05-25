using System;
using System.Collections.Generic;
using System.Linq;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Planning;

namespace PeekabooWin.Core.Agent;

/// <summary>
/// V0.8 VacpPlanner with Skill-Guided Execution.
///
/// Pipeline (V0.8):
///   STEP 0: SkillSearch → score all skills against task
///           Filter by execution policy (CanUseSkill)
///           If best score >= 0.7 → inject SkillHint into VacpRequest
///   STEP 1: Normal VACP execution (WITH skill hints if available)
///           VACP uses hints to prioritize candidate elements/actions
///           VACP is NEVER bypassed by skills in V0.8
///   STEP 2: On success → extract skill from trace, store
///
/// V0.7 had skill short-circuit (skip VACP on high-confidence hit).
/// V0.8 NEVER skips VACP — skill influences ranking, not execution.
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
    /// Plan with skill-guided execution (V0.8).
    /// Always runs VACP; skill influences planning via hints.
    /// </summary>
    public async Task<VacpSkillResult> PlanWithSkills(VacpRequest request, string? appPattern = null)
    {
        var result = new VacpSkillResult();

        // STEP 0: Skill-Guided Retrieval (NEW in V0.8)
        var screenType = InferScreenType(request.Task);
        var searchResults = _skills.Search(request.Task, appPattern, null, null);
        result.SkillSearchResults = searchResults;

        // Filter by execution policy
        var usable = searchResults.Where(r => _skills.Policy.CanUseSkill(r, request.Task)).ToList();
        result.UsableSkills = usable;

        // Get best candidate
        var best = usable.FirstOrDefault();
        if (best != null && best.Score.Total >= 0.7)
        {
            result.TopSkillCandidate = best.Skill;
            result.TopSkillScore = best.Score;
            // Inject skill as HINT into VACP — NOT a bypass
            request.SkillHint = CreateSkillHint(best.Skill);
        }

        // STEP 1: Normal VACP execution (WITH skill hints if available)
        var vacpResult = await _planner.Execute闭环(request);
        result.VacpResult = vacpResult;
        result.Successful = vacpResult.Success;
        result.SkippedBySkill = false; // V0.8: NEVER skip VACP
        result.FinalMessage = vacpResult.Success
            ? $"Executed with skill guidance (confidence: {best?.Score.Total:F2})"
            : vacpResult.FinalMessage;

        // STEP 2: On success, extract and store skill
        if (vacpResult.Success)
            _skills.AfterSuccess(ToTaskTrace(request.Task, vacpResult));

        return result;
    }

    // ===== V0.7 backward-compatible PlanWithSkills (legacy) =====
    public async Task<VacpSkillResult> PlanWithSkillsLegacy(VacpRequest request, string? appPattern = null)
    {
        var result = new VacpSkillResult();

        var screenType = InferScreenType(request.Task);
        var match = _skills.BeforePlanning(appPattern ?? "*", screenType);
        result.SkillMatch = match;

        if (match != null && match.CanSkipVision)
        {
            result.SkippedBySkill = true;
            result.SkillUsed = match.Skill;
            result.Successful = true;
            result.FinalMessage = $"Skill hit: {match.Skill.Name} (confidence: {match.Confidence:F2})";
            result.StepsExecuted = match.Skill.ProcedureSteps;
            result.SkillConfidence = match.Confidence;
            match.Skill.RecordUsage(true);
            _skills.AfterSuccess(ToTaskTrace(request.Task, match.Skill));
            return result;
        }

        var vacpResult = await _planner.Execute闭环(request);
        result.VacpResult = vacpResult;
        result.Successful = vacpResult.Success;
        result.FinalMessage = vacpResult.FinalMessage;

        if (vacpResult.Success)
        {
            var taskTrace = ToTaskTrace(request.Task, vacpResult);
            _skills.AfterSuccess(taskTrace);
            result.StepsExecuted = taskTrace.StepTraces
                .Where(s => s.SelectedAction != null)
                .Select(s => s.SelectedAction!.ActionType)
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

    // NEW: inject skill as planning hint, not execution bypass
    private static SkillHint CreateSkillHint(VisualSkill skill)
    {
        var elements = new List<string>();
        foreach (var cond in skill.TriggerConditions)
        {
            var keyVal = cond.Split('=', 2);
            if (keyVal.Length == 2 && keyVal[0] == "element_labels")
                elements.AddRange(keyVal[1].Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        return new SkillHint
        {
            SuggestedElements = elements,
            SuggestedActionTypes = skill.ProcedureSteps.ToList(),
            PreferredRiskLevel = skill.RiskLevel
        };
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

    // V0.8 Skill-Guided Execution
    public List<SkillSearchResult> SkillSearchResults { get; set; } = new();
    public List<SkillSearchResult> UsableSkills { get; set; } = new();
    public VisualSkill? TopSkillCandidate { get; set; }
    public SkillMatchScore? TopSkillScore { get; set; }
}