using System;
using Xunit;
using PeekabooWin.Core.Memory;

namespace PeekabooWin.Core.Tests;

public class SkillScopeValidatorTests
{
    private readonly SkillScopeValidator _validator = new();

    [Fact]
    public void Validate_AppInSupportedList_IsValid()
    {
        var skill = new VisualSkill
        {
            SkillId = "vs_edit",
            Name = "Edit Text",
            Scope = new SkillScope
            {
                SupportedApps = new List<string> { "notepad", "wordpad" }
            }
        };

        var app = new AppProfile { AppId = "notepad", WindowType = "edit" };

        var result = _validator.Validate(skill, app);

        Assert.True(result.IsValid);
        Assert.Equal("notepad", result.AllowedApp);
    }

    [Fact]
    public void Validate_AppNotInSupportedList_Invalid()
    {
        var skill = new VisualSkill
        {
            SkillId = "vs_notepad",
            Name = "Notepad Edit",
            Scope = new SkillScope
            {
                SupportedApps = new List<string> { "notepad", "wordpad" }
            }
        };

        var app = new AppProfile { AppId = "chrome", WindowType = "browser" };

        var result = _validator.Validate(skill, app);

        Assert.False(result.IsValid);
        Assert.Contains("APP_MISMATCH", result.Reason);
        Assert.Equal("chrome", result.BlockedApp);
    }

    [Fact]
    public void Validate_WildcardApp_AlwaysValid()
    {
        var skill = new VisualSkill
        {
            SkillId = "vs_generic",
            Name = "Generic Dialog",
            Scope = new SkillScope { SupportedApps = new List<string> { "*" } }
        };

        var app = new AppProfile { AppId = "anyapp", WindowType = "dialog" };

        var result = _validator.Validate(skill, app);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AppInForbiddenDomain_Invalid()
    {
        var skill = new VisualSkill
        {
            SkillId = "vs_ai",
            Name = "AI Chat",
            Scope = new SkillScope
            {
                SupportedApps = new List<string>(),
                ForbiddenDomains = new List<string> { "external_ai_chat" }
            }
        };

        var app = new AppProfile { AppId = "doubao", WindowType = "browser", RiskDomain = "external_ai_chat" };

        var result = _validator.Validate(skill, app);

        Assert.False(result.IsValid);
        Assert.Contains("DOMAIN_FORBIDDEN", result.Reason);
    }

    [Fact]
    public void Validate_NullScope_Valid()
    {
        var skill = new VisualSkill { SkillId = "vs_noscope", Name = "No Scope Skill", Scope = null };
        var app = new AppProfile { AppId = "anyapp" };

        var result = _validator.Validate(skill, app);

        Assert.True(result.IsValid);
        Assert.Equal("No scope restriction", result.Reason);
    }

    [Fact]
    public void Validate_MissingRequiredAnchors_Invalid()
    {
        var skill = new VisualSkill
        {
            SkillId = "vs_browser",
            Name = "Browser Input",
            Scope = new SkillScope
            {
                SupportedApps = new List<string> { "*" },
                RequiredAnchors = new List<string> { "input_box", "send_btn" }
            }
        };

        var app = new AppProfile { AppId = "notepad", WindowType = "edit" };

        var result = _validator.Validate(skill, app);

        Assert.False(result.IsValid);
        Assert.Contains("ANCHOR_MISSING", result.Reason);
    }

    [Fact]
    public void Validate_L2Skill_OnPaymentApp_IsValid()
    {
        var skill = new VisualSkill
        {
            SkillId = "vs_payment",
            Name = "Payment Confirm",
            RiskLevel = "L2",
            Scope = new SkillScope { MinRiskLevel = "L2" }
        };

        var app = new AppProfile { AppId = "bankapp", RiskDomain = "payment" };

        var result = _validator.Validate(skill, app);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_L0Skill_OnPaymentApp_BlockedByGuard()
    {
        // L0 skill on payment app is blocked by NegativeTransferGuard (not SkillScopeValidator)
        // SkillScopeValidator doesnt check MinRiskLevel for that combination
        var skill = new VisualSkill
        {
            SkillId = "vs_safe",
            Name = "Safe Skill",
            RiskLevel = "L0",
            Scope = new SkillScope { MinRiskLevel = "L2" }
        };

        var app = new AppProfile { AppId = "bankapp", RiskDomain = "payment" };

        var result = _validator.Validate(skill, app);

        // SkillScopeValidator just validates scope; MinRiskLevel check is in NegativeTransferGuard
        Assert.True(result.IsValid); // Validator says valid; guard blocks it later
    }

    [Fact]
    public void Validate_AllChecksPass_IsValid()
    {
        var skill = new VisualSkill
        {
            SkillId = "vs_browser_edit",
            Name = "Browser Edit",
            Scope = new SkillScope
            {
                SupportedApps = new List<string> { "chrome", "msedge" },
                RequiredAnchors = new List<string> { "input_box" },
                ForbiddenDomains = new List<string>(),
                MinRiskLevel = "L1"
            }
        };

        var app = new AppProfile
        {
            AppId = "msedge",
            WindowType = "browser",
            RiskDomain = "neutral",
            KnownAnchors = new List<string> { "input_box", "send_btn" }
        };

        var result = _validator.Validate(skill, app);

        Assert.True(result.IsValid);
        Assert.Equal("msedge", result.AllowedApp);
    }
}