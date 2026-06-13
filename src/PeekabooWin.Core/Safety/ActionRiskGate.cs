using PeekabooWin.Core.Models;
using PeekabooWin.Core.Perception;

namespace PeekabooWin.Core.Safety;

public class ActionRiskGate
{
    private static readonly HashSet<string> SensitiveKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "密码", "password", "pwd",
        "身份证", "id_card", "idcard",
        "银行卡", "bank_card", "bankcard", "credit_card",
        "手机号", "phone", "tel",
        "邮箱", "email", "mail",
        "地址", "address", "addr",
        "金额", "money", "amount", "rmb", "yuan",
    };

    private static readonly HashSet<string> HighRiskPageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bank", "支付", "payment", "finance", "金融",
        "password", "密码", "login", "登录",
        "admin", "管理", "settings", "设置",
        "email", "邮件", "mail",
    };

    private static readonly HashSet<string> IrreversibleOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete", "remove", "trash",
        "transfer", "转账", "send", "发送",
        "format", "drop", "shutdown",
    };

    private static readonly string[][] DangerousPlanSequences = new[]
    {
        new[] { "hotkey", "type", "press" },
        new[] { "hotkey", "type", "click" },
        new[] { "focus-window", "type", "press" },
    };

    private static readonly HashSet<string> DangerousInputPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "bash",
        "rm ", "del ", "rmdir", "format ",
        "rd /s", "del /s", "del /f", "del /q",
        "shutdown", "taskkill",
        "reg delete", "reg add",
        "net user", "net localgroup",
        "takeown", "icacls",
        "winget uninstall", "appxpackage",
        "certutil", "bitsadmin",
        "wmic ", "mshta",
    };

    private static readonly HashSet<string> DangerousWindowTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "terminal", "命令提示符",
        "regedit", "注册表",
        "taskmgr", "任务管理器",
        "eventvwr", "事件查看器",
        "services.msc", "服务",
        "compmgmt", "计算机管理",
        "diskmgmt", "磁盘管理",
    };

    private static readonly string[] DangerousTaskIntents = new[]
    {
        "transfer money", "wire", "move funds", "汇款", "转账", "转到", "转出", "转帐",
        "bank transfer", "汇钱", "打款", "付款",
        "delete system", "delete all", "bulk delete", "删除系统", "批量删除", "清空", "清理系统",
        "send email with sensitive", "发送敏感", "send email with attachment",
        "execute system command", "execute powershell", "execute command",
        "run command", "run script", "run powershell",
        "modify admin", "修改管理",
        "delete file", "删除文件", "删文件",
        "format drive", "format disk",
        "shutdown", "restart", "关机", "重启",
        "send sensitive", "发送敏感数据", "密码发",
        "submit sensitive", "提交敏感",
        "close account", "close my account", "注销账户", "取消订阅", "取消我的",
        "rm -rf", "del /s", "清空回收站",
        "取消订单", "取消账户",
        "信用卡", "密码发给他", "密码发给",
        "terminate", "kill process",
    };

    private static readonly string[] NonOverridableIntents = new[]
    {
        "信用卡", "密码发给他", "密码发给",
        "转账", "转到", "转出", "汇款", "汇钱", "打款",
        "wire", "move funds",
        "rm -rf", "清空回收站", "清理系统",
        "close account", "close my account", "注销账户",
    };

    public TaskRiskDecision EvaluateTask(string taskDescription)
    {
        if (string.IsNullOrWhiteSpace(taskDescription))
            return new TaskRiskDecision { Decision = RiskLevel.Allow, RiskScore = 0, BlockReason = null };

        var taskLower = taskDescription.ToLower();

        foreach (var intent in DangerousTaskIntents)
        {
            if (taskLower.Contains(intent.ToLower()))
            {
                var isNonOverridable = NonOverridableIntents.Any(n => taskLower.Contains(n.ToLower()));

                if (isNonOverridable)
                {
                    return new TaskRiskDecision
                    {
                        Decision = RiskLevel.Block,
                        RiskScore = 1.0,
                        BlockReason = $"任务包含不可覆盖的高危意图: '{intent}'",
                        MatchedPattern = intent
                    };
                }

                return new TaskRiskDecision
                {
                    Decision = RiskLevel.Block,
                    RiskScore = 1.0,
                    BlockReason = $"任务包含高危意图: '{intent}'",
                    MatchedPattern = intent
                };
            }
        }

        if (MatchesFuzzyIntent(taskLower))
        {
            return new TaskRiskDecision
            {
                Decision = RiskLevel.Block,
                RiskScore = 0.9,
                BlockReason = "任务语义匹配高危意图（模糊匹配）",
                MatchedPattern = "fuzzy"
            };
        }

        var sensitiveHits = SensitiveKeywords.Where(k => taskLower.Contains(k.ToLower())).ToList();
        if (sensitiveHits.Count >= 2)
        {
            return new TaskRiskDecision
            {
                Decision = RiskLevel.Confirm,
                RiskScore = 0.5,
                BlockReason = $"任务涉及多个敏感关键词: {string.Join(", ", sensitiveHits)}",
                MatchedPattern = string.Join(",", sensitiveHits)
            };
        }

        return new TaskRiskDecision { Decision = RiskLevel.Allow, RiskScore = 0, BlockReason = null };
    }

    public PlanRiskDecision EvaluatePlan(List<AgentStep> steps, string taskDescription)
    {
        if (steps == null || steps.Count == 0)
            return new PlanRiskDecision { Decision = RiskLevel.Allow, RiskScore = 0, BlockReason = null };

        var actions = steps.Select(s => s.Action.ToLower()).ToList();
        var allArgs = steps.SelectMany(s => s.Args?.Values ?? Enumerable.Empty<string>()).ToList();
        var allArgsText = string.Join(" ", allArgs).ToLower();

        foreach (var input in allArgs)
        {
            if (string.IsNullOrEmpty(input)) continue;
            foreach (var pattern in DangerousInputPatterns)
            {
                if (input.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return new PlanRiskDecision
                    {
                        Decision = RiskLevel.Block,
                        RiskScore = 1.0,
                        BlockReason = $"计划包含危险输入: '{pattern}' → '{input}'",
                        MatchedStep = $"type '{input}'"
                    };
                }
            }
        }

        foreach (var arg in allArgs)
        {
            if (string.IsNullOrEmpty(arg)) continue;
            foreach (var target in DangerousWindowTargets)
            {
                if (arg.Contains(target, StringComparison.OrdinalIgnoreCase))
                {
                    var isSafeOpen = steps.Any(s =>
                        s.Action.Equals("focus-window", StringComparison.OrdinalIgnoreCase) &&
                        s.Args?.Values.Any(v => v.Contains(target, StringComparison.OrdinalIgnoreCase)) == true);

                    if (isSafeOpen && steps.Count <= 2)
                    {
                        continue;
                    }

                    return new PlanRiskDecision
                    {
                        Decision = RiskLevel.Block,
                        RiskScore = 0.9,
                        BlockReason = $"计划目标包含系统工具: '{target}'",
                        MatchedStep = $"focus '{target}'"
                    };
                }
            }
        }

        foreach (var seq in DangerousPlanSequences)
        {
            if (ContainsSubsequence(actions, seq.ToList()))
            {
                var hasDangerousInput = allArgs.Any(a =>
                    DangerousInputPatterns.Any(p => a.Contains(p, StringComparison.OrdinalIgnoreCase)));

                var hasDangerousTarget = allArgs.Any(a =>
                    DangerousWindowTargets.Any(t => a.Contains(t, StringComparison.OrdinalIgnoreCase)));

                if (hasDangerousInput || hasDangerousTarget)
                {
                    return new PlanRiskDecision
                    {
                        Decision = RiskLevel.Block,
                        RiskScore = 0.95,
                        BlockReason = $"计划包含 shell 执行序列 [{string.Join("→", seq)}] 且目标危险",
                        MatchedStep = string.Join("→", seq)
                    };
                }

                return new PlanRiskDecision
                {
                    Decision = RiskLevel.Confirm,
                    RiskScore = 0.5,
                    BlockReason = $"计划包含 shell 启动序列 [{string.Join("→", seq)}]，需确认意图",
                    MatchedStep = string.Join("→", seq)
                };
            }
        }

        var deleteSteps = steps.Where(s =>
            s.Action.Equals("type", StringComparison.OrdinalIgnoreCase) &&
            s.Args?.GetValueOrDefault("text")?.Contains("del", StringComparison.OrdinalIgnoreCase) == true).ToList();

        if (deleteSteps.Count > 0)
        {
            return new PlanRiskDecision
            {
                Decision = RiskLevel.Block,
                RiskScore = 0.9,
                BlockReason = "计划包含删除命令输入",
                MatchedStep = $"type '{deleteSteps[0].Args?["text"]}'"
            };
        }

        return new PlanRiskDecision { Decision = RiskLevel.Allow, RiskScore = 0, BlockReason = null };
    }

    private static bool MatchesFuzzyIntent(string taskLower)
    {
        var fillerWords = new[] { "一下", "的", "了", "把", "给", "些", "这", "那", "在", "去", "来" };
        var cleaned = taskLower;
        foreach (var filler in fillerWords)
            cleaned = cleaned.Replace(filler, "");

        foreach (var intent in DangerousTaskIntents)
        {
            if (cleaned.Contains(intent.ToLower()))
                return true;
        }

        return false;
    }

    private static bool ContainsSubsequence(List<string> source, List<string> pattern)
    {
        if (pattern.Count > source.Count) return false;
        for (int i = 0; i <= source.Count - pattern.Count; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Count; j++)
            {
                if (!source[i + j].Equals(pattern[j], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }

    public RiskDecision Evaluate(ActionRiskContext context)
    {
        var risk = ComputeRisk(context);
        return MakeDecision(risk, context);
    }

    public double ComputeRisk(ActionRiskContext context)
    {
        double operationRisk = ComputeOperationRisk(context);
        double pageRisk = ComputePageRisk(context);
        double irreversibility = ComputeIrreversibility(context);
        double dataSensitivity = ComputeDataSensitivity(context);
        double uncertainty = ComputeUncertainty(context);

        return 0.30 * operationRisk
             + 0.25 * pageRisk
             + 0.20 * irreversibility
             + 0.15 * dataSensitivity
             + 0.10 * uncertainty;
    }

    private double ComputeOperationRisk(ActionRiskContext context)
    {
        if (context.ActionType == "click") return 0.2;
        if (context.ActionType == "type") return 0.4;
        if (context.ActionType == "hotkey") return 0.5;
        if (context.ActionType == "press") return 0.4;
        if (context.ActionType == "scroll") return 0.1;
        return 0.3;
    }

    private double ComputePageRisk(ActionRiskContext context)
    {
        var pageLower = (context.PageType ?? "").ToLower();
        if (HighRiskPageTypes.Any(t => pageLower.Contains(t))) return 1.0;
        if (pageLower.Contains("dialog")) return 0.6;
        if (pageLower.Contains("browser")) return 0.3;
        return 0.1;
    }

    private double ComputeIrreversibility(ActionRiskContext context)
    {
        if (IrreversibleOperations.Contains(context.ActionType)) return 1.0;
        if (context.ActionType == "type" && context.InputText?.Length > 20) return 0.6;
        return 0.0;
    }

    private double ComputeDataSensitivity(ActionRiskContext context)
    {
        var text = (context.InputText ?? "") + " " + (context.TargetLabel ?? "");
        if (SensitiveKeywords.Any(k => text.Contains(k))) return 1.0;
        if (context.TargetElement?.Type == "password" || context.TargetElement?.Label.Contains("密码") == true)
            return 1.0;
        return 0.0;
    }

    private double ComputeUncertainty(ActionRiskContext context)
    {
        var grounding = context.GroundingScore;
        if (grounding < 0.5) return 0.8;
        if (grounding < 0.75) return 0.4;
        if (grounding < 0.85) return 0.2;
        return 0.0;
    }

    private RiskDecision MakeDecision(double risk, ActionRiskContext context)
    {
        var decision = new RiskDecision
        {
            RiskScore = risk,
            ActionType = context.ActionType,
            TargetLabel = context.TargetLabel ?? "",
            InputText = context.InputText,
        };

        if (risk < 0.3)
        {
            decision.Decision = RiskLevel.Allow;
            decision.Message = $"风险分数 {risk:F2} < 0.3，自动执行";
        }
        else if (risk < 0.6)
        {
            decision.Decision = RiskLevel.Confirm;
            decision.Message = $"风险分数 {risk:F2} ∈ [0.3, 0.6)，需要人工确认";
            decision.RequiredConfirmation = BuildConfirmationRequest(context, risk);
        }
        else
        {
            decision.Decision = RiskLevel.Block;
            decision.Message = $"风险分数 {risk:F2} ≥ 0.6，默认阻断";
            decision.BlockReason = GenerateBlockReason(context, risk);
        }

        return decision;
    }

    private string BuildConfirmationRequest(ActionRiskContext context, double risk)
    {
        return $"即将执行 [{context.ActionType}] 操作" +
               (string.IsNullOrEmpty(context.TargetLabel) ? "" : $" 于 [{context.TargetLabel}]") +
               (string.IsNullOrEmpty(context.InputText) ? "" : $"，输入文本: [{context.InputText}]") +
               $"，风险分数 {risk:F2}。是否确认执行？";
    }

    private string GenerateBlockReason(ActionRiskContext context, double risk)
    {
        var reasons = new List<string>();

        if (HighRiskPageTypes.Any(t => (context.PageType ?? "").ToLower().Contains(t.ToLower())))
            reasons.Add($"高风险页面: {context.PageType}");
        if (IrreversibleOperations.Contains(context.ActionType))
            reasons.Add("不可逆操作");
        if (!string.IsNullOrEmpty(context.InputText) && SensitiveKeywords.Any(k => context.InputText!.Contains(k)))
            reasons.Add("包含敏感数据");
        if (context.GroundingScore < 0.5)
            reasons.Add($"元素定位置信度过低: {context.GroundingScore:F2}");

        return string.Join("; ", reasons);
    }
}

public class ActionRiskContext
{
    public string ActionType { get; set; } = "";
    public string? TargetLabel { get; set; }
    public string? InputText { get; set; }
    public string? PageType { get; set; }
    public UiElement? TargetElement { get; set; }
    public double GroundingScore { get; set; } = 1.0;
}

public class TaskRiskDecision
{
    public RiskLevel Decision { get; set; } = RiskLevel.Allow;
    public double RiskScore { get; set; }
    public string? BlockReason { get; set; }
    public string? MatchedPattern { get; set; }
}

public class PlanRiskDecision
{
    public RiskLevel Decision { get; set; } = RiskLevel.Allow;
    public double RiskScore { get; set; }
    public string? BlockReason { get; set; }
    public string? MatchedStep { get; set; }
}

public class RiskDecision
{
    public RiskLevel Decision { get; set; } = RiskLevel.Allow;
    public double RiskScore { get; set; }
    public string Message { get; set; } = "";
    public string? RequiredConfirmation { get; set; }
    public string? BlockReason { get; set; }
    public string ActionType { get; set; } = "";
    public string? TargetLabel { get; set; }
    public string? InputText { get; set; }
}

public enum RiskLevel
{
    Allow,
    Confirm,
    Block
}
