using System.Text.Json.Serialization;
using PeekabooWin.Core.Perception;

namespace PeekabooWin.Core.Planning;

/// <summary>
/// 动作候选 — GPT Vision 返回多个候选动作后，系统重新打分排序
/// </summary>
public class ActionCandidate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// 动作类型: click, type, scroll, drag, hotkey, wait, inspect
    /// </summary>
    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = "";

    /// <summary>
    /// 目标元素
    /// </summary>
    [JsonPropertyName("target_element")]
    public UiElement? TargetElement { get; set; }

    /// <summary>
    /// 原始坐标（当 Element 不存在时）
    /// </summary>
    public (int x, int y)? RawCoordinates { get; set; }

    /// <summary>
    /// 输入文本（type 动作）
    /// </summary>
    [JsonPropertyName("input_text")]
    public string? InputText { get; set; }

    /// <summary>
    /// GPT/Vision 模型返回的候选分数
    /// </summary>
    [JsonPropertyName("model_score")]
    public double ModelScore { get; set; } = 0.0;

    /// <summary>
    /// 元素定位置信度分数
    /// </summary>
    [JsonPropertyName("grounding_score")]
    public double GroundingScore { get; set; } = 0.0;

    /// <summary>
    /// 综合排序分数
    /// </summary>
    [JsonPropertyName("rank_score")]
    public double RankScore { get; set; } = 0.0;

    /// <summary>
    /// 是否自动执行
    /// </summary>
    [JsonPropertyName("auto_executable")]
    public bool AutoExecutable { get; set; } = false;

    /// <summary>
    /// 动作描述
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// 风险分数
    /// </summary>
    [JsonPropertyName("risk_score")]
    public double RiskScore { get; set; } = 0.0;
}

/// <summary>
/// 候选动作排序器
/// 
/// ActionScore = 0.35 × GoalRelevance
///              + 0.25 × ElementGrounding
///              + 0.20 × StateProgress
///              + 0.10 × SafetyScore
///              + 0.10 × HistoricalSuccess
/// </summary>
public class ActionRanker
{
    /// <summary>
    /// 对候选动作列表进行综合打分和排序
    /// </summary>
    public List<ActionCandidate> Rank(List<ActionCandidate> candidates, RankingContext context)
    {
        foreach (var candidate in candidates)
        {
            candidate.RankScore = ComputeRankScore(candidate, context);
        }

        return candidates
            .OrderByDescending(c => c.RankScore)
            .ToList();
    }

    private double ComputeRankScore(ActionCandidate candidate, RankingContext context)
    {
        var goalRelevance = ComputeGoalRelevance(candidate, context);
        var elementGrounding = candidate.GroundingScore;
        var stateProgress = ComputeStateProgress(candidate, context);
        var safetyScore = ComputeSafetyScore(candidate);
        var historicalSuccess = ComputeHistoricalSuccess(candidate, context);

        return 0.35 * goalRelevance
             + 0.25 * elementGrounding
             + 0.20 * stateProgress
             + 0.10 * safetyScore
             + 0.10 * historicalSuccess;
    }

    private double ComputeGoalRelevance(ActionCandidate candidate, RankingContext context)
    {
        if (string.IsNullOrEmpty(context.Goal)) return 0.5;

        var goalLower = context.Goal.ToLower();
        var actionLower = candidate.ActionType.ToLower();
        var descLower = candidate.Description.ToLower();

        double score = 0.5;

        // 类型匹配
        if (goalLower.Contains("click") && actionLower == "click") score += 0.3;
        if (goalLower.Contains("type") && (actionLower == "type" || actionLower == "click")) score += 0.3;
        if (goalLower.Contains("输入") && (actionLower == "type" || actionLower == "click")) score += 0.3;
        if (goalLower.Contains("scroll") && actionLower == "scroll") score += 0.3;

        // 目标关键词匹配
        var targetLabel = candidate.TargetElement?.Label ?? "";
        if (!string.IsNullOrEmpty(targetLabel) && goalLower.Contains(targetLabel.ToLower()))
            score += 0.2;

        return Math.Min(1.0, score);
    }

    private double ComputeStateProgress(ActionCandidate candidate, RankingContext context)
    {
        // 检查执行后是否能推进任务
        var screenBefore = context.ScreenBefore;

        // 如果有关键输入框还没填，优先点击输入框
        if (candidate.ActionType == "click")
        {
            var target = candidate.TargetElement;
            if (target?.Type == "input" && target.State == "empty")
                return 0.8; // 优先填充空输入框
            if (target?.Role == "primary_action")
                return 0.6; // 主操作按钮优先级中等
        }

        if (candidate.ActionType == "type")
            return 0.9; // 输入动作直接推进任务

        return 0.3;
    }

    private double ComputeSafetyScore(ActionCandidate candidate)
    {
        // 高风险动作降分
        var riskyKeywords = new[] { "删除", "delete", "remove", "转账", "transfer", "发送", "send", "提交", "submit", "取消", "cancel" };
        if (riskyKeywords.Any(k => candidate.Description.ToLower().Contains(k.ToLower())))
            return 0.3;

        return 1.0;
    }

    private double ComputeHistoricalSuccess(ActionCandidate candidate, RankingContext context)
    {
        // TODO: 查询 UI Pattern Memory
        // 暂时返回 0.7（中性值）
        return 0.7;
    }
}

/// <summary>
/// 排序上下文
/// </summary>
public class RankingContext
{
    public string Goal { get; set; } = "";
    public ScreenStateGraph? ScreenBefore { get; set; }
    public Dictionary<string, double>? HistoricalSuccessRates { get; set; }
}