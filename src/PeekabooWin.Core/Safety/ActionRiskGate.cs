using PeekabooWin.Core.Perception;

namespace PeekabooWin.Core.Safety;

/// <summary>
/// 风险感知动作门控 — 继承 AgentShield 思路的可信桌面 Agent 核心
/// 
/// Risk = 0.30 × OperationRisk
///       + 0.25 × PageRisk
///       + 0.20 × Irreversibility
///       + 0.15 × DataSensitivity
///       + 0.10 × Uncertainty
/// 
/// Risk < 0.3  → 自动执行
/// 0.3 ≤ R < 0.6 → 人工确认
/// R ≥ 0.6 → 默认阻断
/// </summary>
public class ActionRiskGate
{
    /// <summary>
    /// 敏感关键词 — 出现时提高 DataSensitivity
    /// </summary>
    private static readonly HashSet<string> SensitiveKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "密码", "password", "pwd",
        "身份证", "id_card", "idcard",
        "银行卡", "bank_card", "bankcard", "credit_card",
        "手机号", "phone", "tel",
        "邮箱", "email", "mail",
        "地址", "address", "addr",
        "金额", "money", "amount", "rmb", "yuan",
        "转账", "transfer", "payment",
        "删除", "delete", "remove",
        "发送", "send", "submit",
        "取消", "cancel",
    };

    /// <summary>
    /// 高风险页面类型
    /// </summary>
    private static readonly HashSet<string> HighRiskPageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bank", "支付", "payment", "finance", "金融",
        "password", "密码", "login", "登录",
        "admin", "管理", "settings", "设置",
        "email", "邮件", "mail",
    };

    /// <summary>
    /// 高风险操作类型
    /// </summary>
    private static readonly HashSet<string> HighRiskOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete", "remove", "trash",
        "transfer", "转账", "payment", "支付",
        "send", "发送", "submit", "提交",
        "cancel", "取消", "close_account",
        "exec", "run", "cmd", "powershell",
    };

    /// <summary>
    /// 可逆操作列表
    /// </summary>
    private static readonly HashSet<string> IrreversibleOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete", "remove", "trash",
        "transfer", "转账", "send", "发送",
        "format", "drop", "shutdown",
    };

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
        if (HighRiskOperations.Contains(context.ActionType)) return 1.0;
        if (context.ActionType == "click") return 0.2;
        if (context.ActionType == "type") return 0.4;
        if (context.ActionType == "hotkey") return 0.5;
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

        // 检查是否是密码字段
        if (context.TargetElement?.Type == "password" || context.TargetElement?.Label.Contains("密码") == true)
            return 1.0;

        return 0.0;
    }

    private double ComputeUncertainty(ActionRiskContext context)
    {
        // 定位置信度低时增加不确定性
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

        if (HighRiskOperations.Contains(context.ActionType))
            reasons.Add($"高危操作类型: {context.ActionType}");
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

/// <summary>
/// 风险评估上下文
/// </summary>
public class ActionRiskContext
{
    public string ActionType { get; set; } = "";
    public string? TargetLabel { get; set; }
    public string? InputText { get; set; }
    public string? PageType { get; set; }
    public UiElement? TargetElement { get; set; }
    public double GroundingScore { get; set; } = 1.0;
}

/// <summary>
/// 风险决策结果
/// </summary>
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