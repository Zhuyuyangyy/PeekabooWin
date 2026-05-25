using PeekabooWin.Core.Perception;

namespace PeekabooWin.Core.Perception;

/// <summary>
/// 元素定位置信度评分器 V2
///
/// Score(element) = w1 × VisionConfidence
///                + w2 × TextMatch
///                + w3 × PositionPrior
///                + w4 × TypeMatch
///                + w5 × ElementRelations
///
/// 权重可根据场景自适应调整
/// grounding_score < 0.75 → 不执行，要求重新截图或人工确认
/// </summary>
public class ElementGroundingScore
{
    private double _visionWeight = 0.25;
    private double _textWeight = 0.25;
    private double _positionWeight = 0.2;
    private double _typeWeight = 0.15;
    private double _relationWeight = 0.15;
    private const double Threshold = 0.75;

    public double VisionWeight
    {
        get => _visionWeight;
        set => _visionWeight = Math.Clamp(value, 0, 1);
    }

    public double TextWeight
    {
        get => _textWeight;
        set => _textWeight = Math.Clamp(value, 0, 1);
    }

    public double PositionWeight
    {
        get => _positionWeight;
        set => _positionWeight = Math.Clamp(value, 0, 1);
    }

    public double TypeWeight
    {
        get => _typeWeight;
        set => _typeWeight = Math.Clamp(value, 0, 1);
    }

    public double RelationWeight
    {
        get => _relationWeight;
        set => _relationWeight = Math.Clamp(value, 0, 1);
    }

    public void SetContextWeights(string context)
    {
        var ctx = (context ?? "").ToLower();

        if (ctx.Contains("vision") || ctx.Contains("screenshot"))
        {
            _visionWeight = 0.35;
            _textWeight = 0.2;
        }
        else if (ctx.Contains("form") || ctx.Contains("input"))
        {
            _visionWeight = 0.2;
            _textWeight = 0.35;
            _typeWeight = 0.2;
        }
        else if (ctx.Contains("dialog") || ctx.Contains("popup"))
        {
            _positionWeight = 0.3;
            _visionWeight = 0.3;
        }
        else
        {
            _visionWeight = 0.25;
            _textWeight = 0.25;
            _positionWeight = 0.2;
            _typeWeight = 0.15;
            _relationWeight = 0.15;
        }

        NormalizeWeights();
    }

    private void NormalizeWeights()
    {
        var total = _visionWeight + _textWeight + _positionWeight + _typeWeight + _relationWeight;
        if (total > 0)
        {
            _visionWeight /= total;
            _textWeight /= total;
            _positionWeight /= total;
            _typeWeight /= total;
            _relationWeight /= total;
        }
    }

    /// <summary>
    /// 计算元素的定位置信度
    /// </summary>
    public double Score(UiElement element, GroundingQuery query, ScreenStateGraph? screenGraph = null)
    {
        var visionConfidence = element.Confidence;
        var textMatch = ComputeTextMatch(element, query);
        var positionPrior = ComputePositionPrior(element, query);
        var typeMatch = ComputeTypeMatch(element, query);
        var relationScore = ComputeRelationScore(element, query, screenGraph);

        return _visionWeight * Normalize(visionConfidence)
             + _textWeight * textMatch
             + _positionWeight * positionPrior
             + _typeWeight * typeMatch
             + _relationWeight * relationScore;
    }

    /// <summary>
    /// 判断元素是否可置信执行
    /// </summary>
    public GroundingDecision Evaluate(UiElement element, GroundingQuery query, ScreenStateGraph? screenGraph = null)
    {
        var score = Score(element, query, screenGraph);
        var breakdown = ComputeBreakdown(element, query, screenGraph);

        return new GroundingDecision
        {
            Element = element,
            Score = score,
            IsGrounded = score >= Threshold,
            Threshold = Threshold,
            Breakdown = breakdown,
            Weights = new()
            {
                ["vision"] = _visionWeight,
                ["text"] = _textWeight,
                ["position"] = _positionWeight,
                ["type"] = _typeWeight,
                ["relation"] = _relationWeight
            }
        };
    }

    private Dictionary<string, double> ComputeBreakdown(UiElement element, GroundingQuery query, ScreenStateGraph? screenGraph)
    {
        return new Dictionary<string, double>
        {
            ["vision_confidence"] = Normalize(element.Confidence),
            ["text_match"] = ComputeTextMatch(element, query),
            ["position_prior"] = ComputePositionPrior(element, query),
            ["type_match"] = ComputeTypeMatch(element, query),
            ["relation_score"] = ComputeRelationScore(element, query, screenGraph),
        };
    }

    /// <summary>
    /// 在候选元素中选择最佳匹配
    /// </summary>
    public GroundingDecision? SelectBest(List<UiElement> candidates, GroundingQuery query, ScreenStateGraph? screenGraph = null)
    {
        GroundingDecision? best = null;
        foreach (var candidate in candidates)
        {
            var decision = Evaluate(candidate, query, screenGraph);
            if (best == null || decision.Score > best.Score)
                best = decision;
        }
        return best;
    }

    /// <summary>
    /// 评分排序所有候选元素
    /// </summary>
    public List<GroundingDecision> RankCandidates(List<UiElement> candidates, GroundingQuery query, ScreenStateGraph? screenGraph = null)
    {
        var decisions = candidates
            .Select(c => Evaluate(c, query, screenGraph))
            .OrderByDescending(d => d.Score)
            .ToList();
        return decisions;
    }

    private double Normalize(double value) => Math.Clamp(value, 0.0, 1.0);

    private double ComputeTextMatch(UiElement element, GroundingQuery query)
    {
        if (string.IsNullOrEmpty(query.TargetText)) return 0.5;

        var target = query.TargetText.ToLower();
        var label = (element.Label ?? "").ToLower();
        var name = (element.Name ?? "").ToLower();
        var text = (element.TextContent ?? "").ToLower();

        if (label == target || name == target || text == target) return 1.0;
        if (label.Contains(target) || name.Contains(target) || text.Contains(target)) return 0.8;

        var labelSim = 1.0 - ((double)LevenshteinDistance(label, target) / Math.Max(label.Length, target.Length, 1));
        var nameSim = 1.0 - ((double)LevenshteinDistance(name, target) / Math.Max(name.Length, target.Length, 1));

        if (labelSim > 0.6) return labelSim;
        if (nameSim > 0.6) return nameSim;

        if (SemanticTextMatch(label, target) || SemanticTextMatch(name, target))
            return 0.7;

        return 0.0;
    }

    private bool SemanticTextMatch(string source, string target)
    {
        var synonyms = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["save"] = new() { "保存", "存储", "另存" },
            ["cancel"] = new() { "取消", "关闭" },
            ["ok"] = new() { "确定", "确认" },
            ["delete"] = new() { "删除", "移除" },
            ["edit"] = new() { "编辑", "修改" },
            ["close"] = new() { "关闭", "退出" },
            ["submit"] = new() { "提交", "发送" },
            ["login"] = new() { "登录", "登陆" },
            ["settings"] = new() { "设置", "选项", "偏好" }
        };

        foreach (var kvp in synonyms)
        {
            if ((source.Contains(kvp.Key) && kvp.Value.Any(v => target.Contains(v))) ||
                (target.Contains(kvp.Key) && kvp.Value.Any(v => source.Contains(v))))
            {
                return true;
            }
        }

        return false;
    }

    private double ComputePositionPrior(UiElement element, GroundingQuery query)
    {
        if (string.IsNullOrEmpty(query.PreferredRegion)) return 0.5;

        var bbox = element.BBox;
        var region = query.PreferredRegion.ToLower();

        var screenWidth = query.ScreenWidth > 0 ? query.ScreenWidth : 1920;
        var screenHeight = query.ScreenHeight > 0 ? query.ScreenHeight : 1080;

        var centerX = bbox.CenterX;
        var centerY = bbox.CenterY;

        double score = 0.5;

        if (region.Contains("bottom"))
        {
            if (centerY > screenHeight * 0.6) score = 0.9;
            else if (centerY > screenHeight * 0.4) score = 0.6;
        }
        else if (region.Contains("top"))
        {
            if (centerY < screenHeight * 0.4) score = 0.9;
            else if (centerY < screenHeight * 0.6) score = 0.6;
        }

        if (region.Contains("left"))
        {
            if (centerX < screenWidth * 0.4) score = Math.Max(score, 0.8);
        }
        else if (region.Contains("right"))
        {
            if (centerX > screenWidth * 0.6) score = Math.Max(score, 0.8);
        }
        else if (region.Contains("center"))
        {
            if (centerX > screenWidth * 0.3 && centerX < screenWidth * 0.7)
                score = Math.Max(score, 0.7);
        }

        return score;
    }

    private double ComputeTypeMatch(UiElement element, GroundingQuery query)
    {
        if (string.IsNullOrEmpty(query.ExpectedType)) return 0.5;

        var expected = query.ExpectedType.ToLower();
        var actual = element.Type.ToLower();

        if (actual == expected) return 1.0;

        var typeMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["button"] = new() { "button", "pushbutton", "submit", "link", "tabitem" },
            ["input"] = new() { "input", "edit", "textbox", "textarea", "combobox", "document" },
            ["checkbox"] = new() { "checkbox", "check" },
            ["link"] = new() { "link", "hyperlink", "anchor" },
            ["menu"] = new() { "menu", "menuitem" },
            ["image"] = new() { "image", "icon", "picture" },
        };

        if (typeMap.TryGetValue(expected, out var aliases))
        {
            if (aliases.Contains(actual)) return 1.0;

            var closeTypes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["button"] = new() { "link", "menuitem" },
                ["input"] = new() { "combobox", "document" },
                ["link"] = new() { "button", "menuitem" }
            };

            if (closeTypes.TryGetValue(expected, out var closeAliases) && closeAliases.Contains(actual))
                return 0.4;
        }

        return 0.0;
    }

    private double ComputeRelationScore(UiElement element, GroundingQuery query, ScreenStateGraph? screenGraph)
    {
        if (screenGraph == null || screenGraph.Elements.Count == 0) return 0.5;
        if (string.IsNullOrEmpty(query.ContextElementId)) return 0.5;

        var contextElement = screenGraph.Elements.FirstOrDefault(e => e.Id == query.ContextElementId);
        if (contextElement == null) return 0.5;

        var relations = screenGraph.Relations
            .Where(r => r.FromId == element.Id || r.ToId == element.Id)
            .ToList();

        double score = 0.5;

        foreach (var relation in relations)
        {
            var relatedId = relation.FromId == element.Id ? relation.ToId : relation.FromId;
            if (relatedId == query.ContextElementId)
            {
                if (relation.RelationType == query.ExpectedRelation)
                    score = Math.Max(score, 0.9);
                else if (!string.IsNullOrEmpty(query.ExpectedRelation))
                    score = Math.Min(score, 0.3);
            }
        }

        var spatialRelation = InferSpatialRelation(element, contextElement);
        if (!string.IsNullOrEmpty(query.ExpectedRelation))
        {
            if (spatialRelation == query.ExpectedRelation)
                score = Math.Max(score, 0.7);
        }

        return score;
    }

    private string InferSpatialRelation(UiElement source, UiElement target)
    {
        var srcBox = source.BBox;
        var tgtBox = target.BBox;

        var srcCenterX = srcBox.CenterX;
        var srcCenterY = srcBox.CenterY;
        var tgtCenterX = tgtBox.CenterX;
        var tgtCenterY = tgtBox.CenterY;

        var dx = tgtCenterX - srcCenterX;
        var dy = tgtCenterY - srcCenterY;

        var threshold = Math.Max(srcBox.Width, srcBox.Height) / 2;

        if (Math.Abs(dy) > Math.Abs(dx))
        {
            if (dy > threshold) return "below";
            if (dy < -threshold) return "above";
        }
        else
        {
            if (dx > threshold) return "right_of";
            if (dx < -threshold) return "left_of";
        }

        return "near";
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
    public string? PreferredRegion { get; set; }
    public string? PageContext { get; set; }
    public string? ContextElementId { get; set; }
    public string? ExpectedRelation { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
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
    public Dictionary<string, double> Weights { get; set; } = new();
}