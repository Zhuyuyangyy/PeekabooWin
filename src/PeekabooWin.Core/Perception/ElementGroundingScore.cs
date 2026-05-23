using PeekabooWin.Core.Perception;

namespace PeekabooWin.Core.Perception;

/// <summary>
/// 元素定位置信度评分器
/// 
/// Score(element) = 0.4 × VisionConfidence
///                + 0.2 × TextMatch
///                + 0.2 × PositionPrior
///                + 0.2 × TypeMatch
/// 
/// grounding_score < 0.75 → 不执行，要求重新截图或人工确认
/// </summary>
public class ElementGroundingScore
{
    private const double Threshold = 0.75;

    /// <summary>
    /// 计算元素的定位置信度
    /// </summary>
    public double Score(UiElement element, GroundingQuery query)
    {
        var visionConfidence = element.Confidence; // GPT Vision 返回的置信度
        var textMatch = ComputeTextMatch(element, query);
        var positionPrior = ComputePositionPrior(element, query);
        var typeMatch = ComputeTypeMatch(element, query);

        return 0.4 * Normalize(visionConfidence)
             + 0.2 * textMatch
             + 0.2 * positionPrior
             + 0.2 * typeMatch;
    }

    /// <summary>
    /// 判断元素是否可置信执行
    /// </summary>
    public GroundingDecision Evaluate(UiElement element, GroundingQuery query)
    {
        var score = Score(element, query);
        return new GroundingDecision
        {
            Element = element,
            Score = score,
            IsGrounded = score >= Threshold,
            Threshold = Threshold,
            Breakdown = new()
            {
                ["vision_confidence"] = Normalize(element.Confidence),
                ["text_match"] = ComputeTextMatch(element, query),
                ["position_prior"] = ComputePositionPrior(element, query),
                ["type_match"] = ComputeTypeMatch(element, query),
            }
        };
    }

    /// <summary>
    /// 在候选元素中选择最佳匹配
    /// </summary>
    public GroundingDecision? SelectBest(List<UiElement> candidates, GroundingQuery query)
    {
        GroundingDecision? best = null;
        foreach (var candidate in candidates)
        {
            var decision = Evaluate(candidate, query);
            if (best == null || decision.Score > best.Score)
                best = decision;
        }
        return best;
    }

    private double Normalize(double value) => Math.Clamp(value, 0.0, 1.0);

    private double ComputeTextMatch(UiElement element, GroundingQuery query)
    {
        if (string.IsNullOrEmpty(query.TargetText)) return 0.5;

        var target = query.TargetText.ToLower();
        var label = (element.Label ?? "").ToLower();
        var name = (element.Name ?? "").ToLower();
        var text = (element.TextContent ?? "").ToLower();

        // 精确匹配
        if (label == target || name == target || text == target) return 1.0;
        // 包含匹配
        if (label.Contains(target) || name.Contains(target) || text.Contains(target)) return 0.8;
        // 模糊匹配（简单编辑距离）
        if (LevenshteinDistance(label, target) <= 2) return 0.6;
        if (LevenshteinDistance(name, target) <= 2) return 0.6;

        return 0.0;
    }

    private double ComputePositionPrior(UiElement element, GroundingQuery query)
    {
        // 空间合理性：目标元素应该在预期的屏幕区域
        // 例如：主按钮通常在右下角或下方，输入框在上方
        // TODO: 可以引入更多先验知识

        if (string.IsNullOrEmpty(query.PreferredRegion)) return 0.5;

        var bbox = element.BBox;
        var region = query.PreferredRegion.ToLower();

        if (region.Contains("bottom") && bbox.CenterY > 400) return 0.8;
        if (region.Contains("top") && bbox.CenterY < 200) return 0.8;
        if (region.Contains("center") && bbox.CenterX > 300 && bbox.CenterX < 700) return 0.7;

        return 0.5;
    }

    private double ComputeTypeMatch(UiElement element, GroundingQuery query)
    {
        if (string.IsNullOrEmpty(query.ExpectedType)) return 0.5;

        var expected = query.ExpectedType.ToLower();
        var actual = element.Type.ToLower();

        // 精确匹配
        if (actual == expected) return 1.0;

        // 语义匹配
        var typeMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["button"] = new() { "button", "pushbutton", "submit", "link" },
            ["input"] = new() { "input", "edit", "textbox", "textarea", "combobox" },
            ["checkbox"] = new() { "checkbox", "check" },
            ["link"] = new() { "link", "hyperlink", "anchor" },
        };

        if (typeMap.TryGetValue(expected, out var aliases))
        {
            if (aliases.Contains(actual)) return 1.0;
            // 类型相近性
            if (expected == "button" && actual == "link") return 0.4;
        }

        return 0.0;
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
        if (string.IsNullOrEmpty(s2)) return s1.Length;

        var m = s1.Length;
        var n = s2.Length;
        var d = new int[m + 1, n + 1];

        for (int i = 0; i <= m; i++) d[i, 0] = i;
        for (int j = 0; j <= n; j++) d[0, j] = j;

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }
}

/// <summary>
/// 定位置信度查询
/// </summary>
public class GroundingQuery
{
    public string TargetText { get; set; } = "";
    public string? ExpectedType { get; set; }
    public string? PreferredRegion { get; set; } // e.g. "bottom-right", "center", "top"
    public string? PageContext { get; set; }
}

/// <summary>
/// 定位置信度决策
/// </summary>
public class GroundingDecision
{
    public UiElement Element { get; set; } = null!;
    public double Score { get; set; }
    public bool IsGrounded { get; set; }
    public double Threshold { get; set; }
    public Dictionary<string, double> Breakdown { get; set; } = new();
}