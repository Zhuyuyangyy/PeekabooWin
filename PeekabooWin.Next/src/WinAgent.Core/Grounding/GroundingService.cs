using WinAgent.Core.Models;

namespace WinAgent.Core.Grounding;

/// <summary>
/// Grounding 服务 — LLM 选择 element_id，Ground 解析为可执行坐标
///
/// 核心原则:
/// 1. LLM 不直接决定坐标，LLM 只选择元素
/// 2. 危险元素默认 dry-run，必须 --force 才执行
/// 3. 找不到元素时返回结构化错误
/// </summary>
public class GroundingService
{
    private const double GroundingThreshold = 0.75;

    private static readonly string[] DangerousKeywords = new[]
    {
        "关闭", "删除", "卸载", "支付", "购买", "确认删除",
        "Close", "Delete", "Uninstall", "Pay", "Purchase",
        "取消", "Cancel"
    };

    /// <summary>
    /// 通过 element_id 定位元素
    /// </summary>
    public GroundingResult GroundById(ObservationResult observation, GroundingQuery query)
    {
        var element = observation.Elements.FirstOrDefault(e => e.Id == query.TargetId);

        if (element == null)
        {
            return new GroundingResult
            {
                SnapshotId = observation.SnapshotId,
                TargetId = query.TargetId,
                IsGrounded = false,
                Error = $"Element not found: {query.TargetId}",
                EstimatedScore = 0
            };
        }

        var score = element.EstimatedConfidence;
        var isDangerous = IsDangerous(element);
        var matchedKeywords = DangerousKeywords
            .Where(kw => element.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new GroundingResult
        {
            SnapshotId = observation.SnapshotId,
            TargetId = query.TargetId,
            ResolvedElement = element,
            IsGrounded = score >= GroundingThreshold,
            EstimatedScore = score,
            MatchType = "exact_id",
            IsPotentiallyDangerous = isDangerous,
            DangerWarning = isDangerous
                ? $"Dangerous element: matched keywords [{string.Join(", ", matchedKeywords)}]. Use --force to execute."
                : null,
            ClickX = element.BBox.CenterX,
            ClickY = element.BBox.CenterY
        };
    }

    /// <summary>
    /// 通过文本模糊匹配定位元素 (兜底方式)
    /// </summary>
    public GroundingResult GroundByText(ObservationResult observation, string text, bool force = false)
    {
        var candidates = new List<(ElementSnapshot Element, double Score, string MatchType)>();

        foreach (var el in observation.Elements)
        {
            var name = (el.Name ?? "").ToLower();
            var target = text.ToLower();

            double score = 0;
            string matchType = "";

            if (name == target)
            {
                score = 1.0;
                matchType = "exact";
            }
            else if (name.Contains(target))
            {
                score = 0.85;
                matchType = "contains";
            }
            else
            {
                var similarity = ComputeSimilarity(name, target);
                if (similarity > 0.6)
                {
                    score = similarity;
                    matchType = "fuzzy";
                }
                else if (SemanticMatch(name, target))
                {
                    score = 0.7;
                    matchType = "semantic";
                }
            }

            if (score >= 0.6)
            {
                candidates.Add((el, score, matchType));
            }
        }

        if (candidates.Count == 0)
        {
            return new GroundingResult
            {
                SnapshotId = observation.SnapshotId,
                TargetId = $"text:{text}",
                IsGrounded = false,
                Error = $"No element matching text: {text}",
                EstimatedScore = 0
            };
        }

        var best = candidates.OrderByDescending(c => c.Score).First();
        var isDangerous = IsDangerous(best.Element);
        var matchedKeywords = DangerousKeywords
            .Where(kw => best.Element.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new GroundingResult
        {
            SnapshotId = observation.SnapshotId,
            TargetId = best.Element.Id,
            ResolvedElement = best.Element,
            IsGrounded = best.Score >= GroundingThreshold,
            EstimatedScore = best.Score,
            MatchType = best.MatchType,
            IsPotentiallyDangerous = isDangerous,
            DangerWarning = isDangerous
                ? $"Dangerous element: matched keywords [{string.Join(", ", matchedKeywords)}]. Use --force to execute."
                : null,
            ClickX = best.Element.BBox.CenterX,
            ClickY = best.Element.BBox.CenterY
        };
    }

    private bool IsDangerous(ElementSnapshot element)
    {
        if (string.IsNullOrEmpty(element.Name)) return false;
        return DangerousKeywords.Any(kw => element.Name.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private double ComputeSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;
        var distance = LevenshteinDistance(s1, s2);
        var maxLen = Math.Max(s1.Length, s2.Length);
        return 1.0 - ((double)distance / maxLen);
    }

    private bool SemanticMatch(string source, string target)
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
