using System;
using System.Collections.Generic;

namespace PeekabooWin.Core.Memory;

public class SkillTransferController
{
    private readonly SkillScopeValidator _scopeValidator;
    private readonly NegativeTransferGuard _guard;
    private readonly AnchorMatcher _anchorMatcher;

    public SkillTransferController()
    {
        _scopeValidator = new SkillScopeValidator();
        _guard = new NegativeTransferGuard();
        _anchorMatcher = new AnchorMatcher();
    }

    /// <summary>
    /// V0.9 core: decide whether a VisualSkill can be applied on the current window.
    /// Returns a TransferDecision with action (inject/block/human_review) and reason.
    /// </summary>
    public TransferDecision Decide(TransferContext ctx)
    {
        // Step 1: Scope validation
        var scopeResult = _scopeValidator.Validate(ctx.Skill, ctx.CurrentApp);
        if (!scopeResult.IsValid)
            return new TransferDecision { Action = TransferAction.BLOCK, Reason = scopeResult.Reason, BlockReason = scopeResult.Reason };

        // Step 2: Negative transfer guard
        var guardCtx = new GuardContext
        {
            TaskText = ctx.TaskText,
            SkillRiskLevel = ctx.Skill.RiskLevel ?? "L0",
            SkillRiskDomain = ctx.Skill.RiskDomain ?? "neutral",
            AppRiskDomain = ctx.CurrentApp.RiskDomain,
            SkillId = ctx.Skill.SkillId,
            AppId = ctx.CurrentApp.AppId
        };
        var guardResult = _guard.Evaluate(guardCtx);
        if (!guardResult.IsAllowed)
            return new TransferDecision
            {
                Action = guardResult.SuggestedAction == "HUMAN_REVIEW" ? TransferAction.HUMAN_REVIEW : TransferAction.BLOCK,
                Reason = guardResult.BlockReason,
                BlockReason = guardResult.BlockedBecause
            };

        // Step 3: Anchor coverage check
        if (ctx.Skill.Scope?.RequiredAnchors.Count > 0)
        {
            var coverage = _anchorMatcher.CheckCoverage(
                ctx.Skill.Scope.RequiredAnchors,
                ctx.CurrentApp.WindowType,
                ctx.VisibleTexts);
            if (!coverage.IsFullyCovered)
                return new TransferDecision
                {
                    Action = TransferAction.BLOCK,
                    Reason = $"ANCHORS_MISSING: {string.Join(",", coverage.MissingAnchors)}",
                    BlockReason = "missing_required_anchors",
                    CoverageScore = coverage.CoverageScore
                };
        }

        // Step 4: SkillMatchScore weighting (from V0.8)
        double score = ctx.SkillMatchScore;
        if (ctx.CurrentApp.WindowType == "browser" && ctx.Skill.ContextAnchors.Contains("browser_input"))
            score += 0.15;
        if (ctx.CurrentApp.RiskDomain != "neutral" && ctx.Skill.RiskLevel != "L2")
            score -= 0.20;

        if (score >= 0.75) return new TransferDecision { Action = TransferAction.INJECT, Reason = $"APPROVED score={score:F3}", SkillMatchScore = score };
        if (score >= 0.50) return new TransferDecision { Action = TransferAction.HUMAN_REVIEW, Reason = $"SCORE_LOW score={score:F3}", SkillMatchScore = score };
        return new TransferDecision { Action = TransferAction.BLOCK, Reason = $"SCORE_TOO_LOW score={score:F3}", SkillMatchScore = score };
    }
}

public class TransferContext
{
    public VisualSkill Skill { get; set; } = null!;
    public AppProfile CurrentApp { get; set; } = null!;
    public string? TaskText { get; set; }
    public double SkillMatchScore { get; set; }
    public List<string> VisibleTexts { get; set; } = [];
}

public class TransferDecision
{
    public TransferAction Action { get; set; }
    public string Reason { get; set; } = "";
    public string? BlockReason { get; set; }
    public double SkillMatchScore { get; set; }
    public double CoverageScore { get; set; } = 1.0;
}

public enum TransferAction { INJECT, BLOCK, HUMAN_REVIEW }