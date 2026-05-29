using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Perception;

public class ElementCandidate
{
    public string Text { get; set; } = "";
    public BoundingBox BBox { get; set; } = new();
    public double OcrConfidence { get; set; }
    public double SemanticScore { get; set; }
    public double LayoutScore { get; set; }
    public double ContextScore { get; set; }
    public double FinalGroundingScore { get; set; }
    public string Source { get; set; } = "";
    public UiElement? UiElement { get; set; }
    public OcrWord? OcrWord { get; set; }
}

public class CandidateRankRequest
{
    public string TargetText { get; set; } = "";
    public string? ExpectedType { get; set; }
    public string? PreferredRegion { get; set; }
    public string? PageContext { get; set; }
    public List<UiElement> UiaCandidates { get; set; } = new();
    public List<OcrWord> OcrCandidates { get; set; } = new();
    public BoundingBox? Viewport { get; set; }
}

public class CandidateRankResult
{
    public List<ElementCandidate> RankedCandidates { get; set; } = new();
    public ElementCandidate? BestCandidate => RankedCandidates.Count > 0 ? RankedCandidates[0] : null;
    public bool HasViableCandidate => BestCandidate != null && BestCandidate.FinalGroundingScore >= 0.5;
    public int TotalCandidates => RankedCandidates.Count;
    public string TargetText { get; set; } = "";
}

public class ElementCandidateRanker
{
    private const double WeightOcr = 0.25;
    private const double WeightSemantic = 0.30;
    private const double WeightLayout = 0.20;
    private const double WeightContext = 0.25;

    public CandidateRankResult Rank(CandidateRankRequest request)
    {
        var candidates = new List<ElementCandidate>();

        foreach (var uia in request.UiaCandidates)
        {
            candidates.Add(new ElementCandidate
            {
                Text = uia.Label ?? uia.Name ?? "",
                BBox = uia.BBox ?? new BoundingBox(),
                OcrConfidence = uia.Confidence,
                Source = "uia",
                UiElement = uia
            });
        }

        foreach (var ocr in request.OcrCandidates)
        {
            if (ocr.BoundingBox == null) continue;
            candidates.Add(new ElementCandidate
            {
                Text = ocr.Text ?? "",
                BBox = new BoundingBox
                {
                    X = (int)ocr.BoundingBox.X,
                    Y = (int)ocr.BoundingBox.Y,
                    Width = (int)ocr.BoundingBox.Width,
                    Height = (int)ocr.BoundingBox.Height
                },
                OcrConfidence = ocr.Confidence,
                Source = "ocr",
                OcrWord = ocr
            });
        }

        candidates = Deduplicate(candidates);

        foreach (var candidate in candidates)
        {
            ScoreCandidate(candidate, request);
        }

        candidates.Sort((a, b) => b.FinalGroundingScore.CompareTo(a.FinalGroundingScore));

        return new CandidateRankResult
        {
            RankedCandidates = candidates,
            TargetText = request.TargetText
        };
    }

    private ElementCandidate ScoreCandidate(ElementCandidate candidate, CandidateRankRequest request)
    {
        candidate.SemanticScore = ComputeSemanticScore(candidate, request.TargetText);
        candidate.LayoutScore = ComputeLayoutScore(candidate, request.Viewport, request.PreferredRegion);
        candidate.ContextScore = ComputeContextScore(candidate, request.PageContext);
        candidate.FinalGroundingScore = WeightOcr * candidate.OcrConfidence
            + WeightSemantic * candidate.SemanticScore
            + WeightLayout * candidate.LayoutScore
            + WeightContext * candidate.ContextScore;
        return candidate;
    }

    private double ComputeSemanticScore(ElementCandidate candidate, string targetText)
    {
        if (string.IsNullOrEmpty(targetText) || string.IsNullOrEmpty(candidate.Text))
            return 0.0;

        var candText = candidate.Text;
        var target = targetText;

        if (string.Equals(candText, target, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        if (candText.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0
            || target.IndexOf(candText, StringComparison.OrdinalIgnoreCase) >= 0)
            return 0.8;

        var dist = LevenshteinDistance(candText.ToLowerInvariant(), target.ToLowerInvariant());
        if (dist <= 2)
            return 0.6;

        return 0.0;
    }

    private double ComputeLayoutScore(ElementCandidate candidate, BoundingBox? viewport, string? preferredRegion)
    {
        if (viewport == null)
            return 0.5;

        var cx = candidate.BBox.CenterX;
        var cy = candidate.BBox.CenterY;

        bool inViewport = cx >= viewport.X && cx <= viewport.X + viewport.Width
            && cy >= viewport.Y && cy <= viewport.Y + viewport.Height;

        double score = inViewport ? 1.0 : 0.2;

        if (!string.IsNullOrEmpty(preferredRegion))
        {
            var regions = new Dictionary<string, (double xMin, double yMin, double xMax, double yMax)>
            {
                ["top"] = (0, 0, 1, 0.33),
                ["bottom"] = (0, 0.67, 1, 1),
                ["left"] = (0, 0, 0.33, 1),
                ["right"] = (0.67, 0, 1, 1),
                ["center"] = (0.25, 0.25, 0.75, 0.75)
            };

            if (regions.TryGetValue(preferredRegion.ToLowerInvariant(), out var region))
            {
                var normX = (cx - viewport.X) / viewport.Width;
                var normY = (cy - viewport.Y) / viewport.Height;

                if (normX >= region.xMin && normX <= region.xMax
                    && normY >= region.yMin && normY <= region.yMax)
                {
                    score += 0.3;
                }
            }
        }

        return Math.Min(score, 1.0);
    }

    private double ComputeContextScore(ElementCandidate candidate, string? pageContext)
    {
        if (string.IsNullOrEmpty(pageContext))
            return 0.5;

        if (string.IsNullOrEmpty(candidate.Text))
            return 0.3;

        var contextLower = pageContext.ToLowerInvariant();
        var candLower = candidate.Text.ToLowerInvariant();

        if (contextLower.Contains(candLower))
            return 0.9;

        var words = candLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int matchCount = 0;
        foreach (var word in words)
        {
            if (word.Length > 2 && contextLower.Contains(word))
                matchCount++;
        }

        if (words.Length > 0 && matchCount > 0)
            return 0.5 + 0.4 * ((double)matchCount / words.Length);

        return 0.3;
    }

    private List<ElementCandidate> Deduplicate(List<ElementCandidate> candidates)
    {
        var result = new List<ElementCandidate>();

        for (int i = 0; i < candidates.Count; i++)
        {
            bool merged = false;
            for (int j = 0; j < result.Count; j++)
            {
                if (string.Equals(candidates[i].Text, result[j].Text, StringComparison.OrdinalIgnoreCase)
                    && ComputeIoU(candidates[i].BBox, result[j].BBox) > 0.5)
                {
                    if (candidates[i].OcrConfidence > result[j].OcrConfidence)
                    {
                        result[j] = candidates[i];
                    }
                    merged = true;
                    break;
                }
            }

            if (!merged)
            {
                result.Add(candidates[i]);
            }
        }

        return result;
    }

    private static double ComputeIoU(BoundingBox a, BoundingBox b)
    {
        double x1 = Math.Max(a.X, b.X);
        double y1 = Math.Max(a.Y, b.Y);
        double x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        double y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        double intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        double areaA = a.Width * a.Height;
        double areaB = b.Width * b.Height;
        double union = areaA + areaB - intersection;

        if (union <= 0)
            return 0.0;

        return intersection / union;
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        int n = s1.Length;
        int m = s2.Length;

        if (n == 0) return m;
        if (m == 0) return n;

        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
