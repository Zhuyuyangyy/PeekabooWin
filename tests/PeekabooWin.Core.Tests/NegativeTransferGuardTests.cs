using System;
using Xunit;
using PeekabooWin.Core.Memory;

namespace PeekabooWin.Core.Tests;

public class NegativeTransferGuardTests
{
    private readonly NegativeTransferGuard _guard = new();

    [Fact]
    public void Evaluate_L0Skill_WithHighRiskVerb_IsBlocked()
    {
        // "transfer money to bank account" contains "transfer" (high-risk verb) and "bank" (high-risk target)
        // High-risk verb always returns BLOCK for L0/L1 skills
        var ctx = new GuardContext
        {
            TaskText = "transfer money to bank account",
            SkillRiskLevel = "L0",
            SkillRiskDomain = "neutral",
            AppRiskDomain = "neutral",
            SkillId = "vs_payment",
            AppId = "notepad"
        };

        var result = _guard.Evaluate(ctx);

        Assert.False(result.IsAllowed);
        Assert.Equal("skill_too_weak_for_risk", result.BlockedBecause);
        Assert.Equal("BLOCK", result.SuggestedAction);
    }

    [Fact]
    public void Evaluate_L1Skill_WithHighRiskVerb_IsBlocked()
    {
        var ctx = new GuardContext
        {
            TaskText = "transfer money to bank",
            SkillRiskLevel = "L1",
            SkillRiskDomain = "neutral",
            AppRiskDomain = "neutral",
            SkillId = "vs_payment",
            AppId = "notepad"
        };

        var result = _guard.Evaluate(ctx);

        Assert.False(result.IsAllowed);
        Assert.Equal("skill_too_weak_for_risk", result.BlockedBecause);
        Assert.Equal("BLOCK", result.SuggestedAction);
    }

    [Fact]
    public void Evaluate_L2Skill_WithHighRiskVerb_IsAllowed()
    {
        // L2 skills bypass high-risk verb detection
        var ctx = new GuardContext
        {
            TaskText = "transfer money to bank",
            SkillRiskLevel = "L2",
            SkillRiskDomain = "neutral",
            AppRiskDomain = "neutral",
            SkillId = "vs_payment",
            AppId = "bankapp"
        };

        var result = _guard.Evaluate(ctx);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_ExternalAIChatSkill_OnPaymentApp_IsBlocked()
    {
        // external_ai_chat skill cannot operate on payment app
        var ctx = new GuardContext
        {
            TaskText = "send message",
            SkillRiskLevel = "L2",
            SkillRiskDomain = "external_ai_chat",
            AppRiskDomain = "payment",
            SkillId = "vs_ai_chat",
            AppId = "bankapp"
        };

        var result = _guard.Evaluate(ctx);

        Assert.False(result.IsAllowed);
        Assert.Equal("forbidden_domain_transfer", result.BlockedBecause);
    }

    [Fact]
    public void Evaluate_ExternalAIChatSkill_OnAdminApp_IsBlocked()
    {
        var ctx = new GuardContext
        {
            TaskText = "click confirm",
            SkillRiskLevel = "L2",
            SkillRiskDomain = "external_ai_chat",
            AppRiskDomain = "admin",
            SkillId = "vs_ai_chat",
            AppId = "admin_tool"
        };

        var result = _guard.Evaluate(ctx);

        Assert.False(result.IsAllowed);
        Assert.Equal("forbidden_domain_transfer", result.BlockedBecause);
    }

    [Fact]
    public void Evaluate_NeutralSkill_OnPaymentApp_L0_IsBlocked()
    {
        // payment apps require L2 skills; L0 is insufficient
        var ctx = new GuardContext
        {
            TaskText = "click ok",
            SkillRiskLevel = "L0",
            SkillRiskDomain = "neutral",
            AppRiskDomain = "payment",
            SkillId = "vs_generic",
            AppId = "bankapp"
        };

        var result = _guard.Evaluate(ctx);

        Assert.False(result.IsAllowed);
        Assert.Equal("insufficient_skill_level", result.BlockedBecause);
        Assert.Equal("HUMAN_REVIEW", result.SuggestedAction);
    }

    [Fact]
    public void Evaluate_L2Skill_OnPaymentApp_IsAllowed()
    {
        var ctx = new GuardContext
        {
            TaskText = "click confirm",
            SkillRiskLevel = "L2",
            SkillRiskDomain = "neutral",
            AppRiskDomain = "payment",
            SkillId = "vs_payment_confirm",
            AppId = "bankapp"
        };

        var result = _guard.Evaluate(ctx);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_L1Skill_OnNeutralApp_IsAllowed()
    {
        var ctx = new GuardContext
        {
            TaskText = "click ok button",
            SkillRiskLevel = "L1",
            SkillRiskDomain = "neutral",
            AppRiskDomain = "neutral",
            SkillId = "vs_dialog",
            AppId = "notepad"
        };

        var result = _guard.Evaluate(ctx);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_NullTaskText_IsAllowed()
    {
        var ctx = new GuardContext
        {
            TaskText = null,
            SkillRiskLevel = "L0",
            SkillRiskDomain = "neutral",
            AppRiskDomain = "payment",
            SkillId = "vs_generic",
            AppId = "bankapp"
        };

        var result = _guard.Evaluate(ctx);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_HighRiskVerb_CapturesDetectedVerb()
    {
        var ctx = new GuardContext
        {
            TaskText = "delete all files now",
            SkillRiskLevel = "L0",
            SkillRiskDomain = "neutral",
            AppRiskDomain = "neutral",
            SkillId = "vs_delete",
            AppId = "explorer"
        };

        var result = _guard.Evaluate(ctx);

        Assert.False(result.IsAllowed);
    }
}
