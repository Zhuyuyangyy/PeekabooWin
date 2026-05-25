using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekabooWin.Core.Memory;

public class NegativeTransferGuard
{
    private static readonly HashSet<string> HighRiskVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "transfer","转账","payment","支付","delete","删除","destroy","销毁","drop",
        "send_external","发送外部","submit","提交","login","登录","password","密码",
        "admin","管理","settings","设置"
    };

    private static readonly HashSet<string> HighRiskTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "bank","银行","account","账户","card","卡","money","金额","funds","资金",
        "file","文件","folder","文件夹","database","数据库","table","表"
    };

    private static readonly HashSet<(string, string)> ForbiddenTransfers = new()
    {
        ("neutral","payment"),("external_ai_chat","payment"),("external_ai_chat","admin"),("neutral","admin"),
    };

    public GuardResult Evaluate(GuardContext ctx)
    {
        var result = new GuardResult { IsAllowed = true };
        if (ctx.TaskText == null) return result;

        var verb = DetectHighRiskVerb(ctx.TaskText);
        if (verb != null && (ctx.SkillRiskLevel == "L0" || ctx.SkillRiskLevel == "L1"))
        {
            result.IsAllowed = false;
            result.BlockReason = $"HIGH_RISK_VERB_DETECTED: '{verb}' in task, skill is {ctx.SkillRiskLevel}";
            result.BlockedBecause = "skill_too_weak_for_risk";
            result.SuggestedAction = "BLOCK";
            return result;
        }
        result.HighRiskVerbDetected = verb;

        var target = DetectHighRiskTarget(ctx.TaskText);
        if (target != null && ctx.SkillRiskLevel == "L0")
        {
            result.IsAllowed = false;
            result.BlockReason = $"HIGH_RISK_TARGET_DETECTED: '{target}' in task, L0 skill insufficient";
            result.BlockedBecause = "skill_too_weak_for_target";
            result.SuggestedAction = "HUMAN_REVIEW";
            return result;
        }
        result.HighRiskTargetDetected = target;

        if (!string.IsNullOrEmpty(ctx.SkillRiskDomain) && !string.IsNullOrEmpty(ctx.AppRiskDomain))
        {
            if (ForbiddenTransfers.Contains((ctx.SkillRiskDomain, ctx.AppRiskDomain)))
            {
                result.IsAllowed = false;
                result.BlockReason = $"DOMAIN_TRANSFER_FORBIDDEN: '{ctx.SkillRiskDomain}' cannot transfer to app domain '{ctx.AppRiskDomain}'";
                result.BlockedBecause = "forbidden_domain_transfer";
                result.SuggestedAction = "BLOCK";
                return result;
            }
        }

        if (ctx.SkillRiskDomain == "external_ai_chat" && (ctx.AppRiskDomain == "payment" || ctx.AppRiskDomain == "admin"))
        {
            result.IsAllowed = false;
            result.BlockReason = $"CROSS_DOMAIN_BLOCKED: external_ai_chat skill cannot operate on {ctx.AppRiskDomain} app";
            result.BlockedBecause = "cross_domain_risk";
            result.SuggestedAction = "BLOCK";
            return result;
        }

        if ((ctx.AppRiskDomain == "payment" || ctx.AppRiskDomain == "admin") && ctx.SkillRiskLevel != "L2")
        {
            result.IsAllowed = false;
            result.BlockReason = $"HIGH_RISK_APP requires L2 skill, but provided skill is {ctx.SkillRiskLevel}";
            result.BlockedBecause = "insufficient_skill_level";
            result.SuggestedAction = "HUMAN_REVIEW";
            return result;
        }

        return result;
    }

    private string? DetectHighRiskVerb(string text) =>
        HighRiskVerbs.FirstOrDefault(v => text.Contains(v, StringComparison.OrdinalIgnoreCase));

    private string? DetectHighRiskTarget(string text) =>
        HighRiskTargets.FirstOrDefault(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}

public class GuardContext
{
    public string? TaskText { get; set; }
    public string SkillRiskLevel { get; set; } = "L0";
    public string SkillRiskDomain { get; set; } = "neutral";
    public string AppRiskDomain { get; set; } = "neutral";
    public string? SkillId { get; set; }
    public string? AppId { get; set; }
}

public class GuardResult
{
    public bool IsAllowed { get; set; }
    public string BlockReason { get; set; } = "";
    public string? BlockedBecause { get; set; }
    public string SuggestedAction { get; set; } = "ALLOW";
    public string? HighRiskVerbDetected { get; set; }
    public string? HighRiskTargetDetected { get; set; }
}