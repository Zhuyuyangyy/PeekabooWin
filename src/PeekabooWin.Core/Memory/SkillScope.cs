using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekabooWin.Core.Memory;

public class SkillScope
{
    public List<string> SupportedApps { get; set; } = new() { "*" };
    public List<string> RequiredAnchors { get; set; } = [];
    public List<string> ForbiddenDomains { get; set; } = [];
    public string MinRiskLevel { get; set; } = "L0";
    public int MaxUseBeforeRevalidate { get; set; } = 10;

    public bool AllowsApp(AppProfile app)
    {
        if (SupportedApps.Count == 0 || SupportedApps.Contains("*")) return true;
        return SupportedApps.Any(a => app.AppId.Contains(a, StringComparison.OrdinalIgnoreCase) || app.ProcessName.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasRequiredAnchors(AppProfile app)
    {
        if (RequiredAnchors.Count == 0) return true;
        return RequiredAnchors.All(ra => app.KnownAnchors.Contains(ra, StringComparer.OrdinalIgnoreCase));
    }

    public bool IsDomainAllowed(AppProfile app)
    {
        if (ForbiddenDomains.Count == 0) return true;
        return !ForbiddenDomains.Any(fd => app.RiskDomain.Contains(fd, StringComparison.OrdinalIgnoreCase));
    }

    public string? Validate(AppProfile app)
    {
        if (!AllowsApp(app)) return $"APP_MISMATCH: skill not allowed on {app.AppId}";
        if (!HasRequiredAnchors(app)) return $"ANCHOR_MISSING: required anchors {string.Join(",", RequiredAnchors)} not found";
        if (!IsDomainAllowed(app)) return $"DOMAIN_FORBIDDEN: risk domain '{app.RiskDomain}' is in forbidden list";
        return null;
    }
}

public class SkillScopeValidator
{
    public SkillScopeResult Validate(VisualSkill skill, AppProfile app)
    {
        if (skill.Scope == null) return new SkillScopeResult { IsValid = true, Reason = "No scope restriction" };
        var error = skill.Scope.Validate(app);
        if (error != null) return new SkillScopeResult { IsValid = false, Reason = error, BlockedApp = app.AppId };
        return new SkillScopeResult { IsValid = true, Reason = "Scope validated", AllowedApp = app.AppId };
    }
}

public class SkillScopeResult
{
    public bool IsValid { get; set; }
    public string Reason { get; set; } = "";
    public string? BlockedApp { get; set; }
    public string? ForbiddenDomain { get; set; }
    public string? AllowedApp { get; set; }
}