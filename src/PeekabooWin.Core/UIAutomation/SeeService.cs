using System.Runtime.InteropServices;
using System.Windows.Automation;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Core.UIAutomation;

/// <summary>
/// V0.3: Unified see command - captures window info, screenshot, and UIA tree in one shot
/// </summary>
public class SeeService
{
    private readonly WindowService _windowService;
    private readonly CaptureService _captureService;

    private static readonly string[] DangerousKeywords = new[]
    {
        "关闭", "删除", "卸载", "支付", "购买", "确认删除",
        "Close", "Delete", "Uninstall", "Pay", "Purchase",
        "取消", "Cancel"  // added for safety
    };

    public SeeService(WindowService windowService, CaptureService captureService)
    {
        _windowService = windowService;
        _captureService = captureService;
    }

    /// <summary>
    /// Execute the full see command:
    /// Resolve window → Capture screenshot → Inspect UIA → Extract elements
    /// </summary>
    public SeeResult Execute(string windowKeyword, long? handle, string screenshotOutPath, int maxDepth = 4)
    {
        var result = new SeeResult { Command = "see" };
        var warnings = new List<string>();

        // 1. Resolve target window
        WindowInfo? win;
        if (handle.HasValue)
        {
            win = _windowService.GetWindowByHandle(handle.Value);
            if (win == null)
            {
                result.Success = false;
                result.Error = $"Window with handle {handle} not found";
                return result;
            }
        }
        else
        {
            win = _windowService.FindWindow(windowKeyword);
            if (win == null)
            {
                result.Success = false;
                result.Error = $"Window not found: {windowKeyword}";
                return result;
            }
        }

        result.ActiveWindow = new SeeWindowInfo
        {
            Handle = win.Handle,
            Title = win.Title,
            ProcessName = win.ProcessName ?? "",
            ProcessId = win.ProcessId,
            ClassName = win.ClassName ?? "",
            Rect = win.Rect
        };

        // 2. Capture screenshot
        if (string.IsNullOrEmpty(screenshotOutPath))
        {
            screenshotOutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"see_{win.Handle}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        }

        var dir = Path.GetDirectoryName(screenshotOutPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var cap = _captureService.CaptureWindow(win.Title, screenshotOutPath);
        if (cap.Success)
        {
            result.Screenshot = new SeeScreenshot
            {
                Path = cap.Path ?? screenshotOutPath,
                Width = cap.Width,
                Height = cap.Height
            };
        }
        else
        {
            warnings.Add($"Screenshot failed: {cap.Error}");
        }

        // 3. Inspect UIA tree
        var uiRoot = AutomationElement.FromHandle((IntPtr)win.Handle);
        if (uiRoot == null)
        {
            warnings.Add("Could not get UIA root element");
        }
        else
        {
            var allElements = new List<AutomationElement>();
            int elementIdCounter = 0;

            // Collect all elements with depth limit
            CollectElements(uiRoot, 0, maxDepth, allElements);

            // Build tree and extract summary
            var treeSummary = new UiTreeSummary { Depth = maxDepth };
            var clickable = new List<SeeElement>();
            var editable = new List<SeeElement>();
            var textElements = new List<SeeElement>();

            foreach (var el in allElements)
            {
                elementIdCounter++;
                var seeEl = BuildSeeElement(el, $"el_{elementIdCounter:D3}");

                // Count control types
                var ct = seeEl.ControlType;
                treeSummary.ControlTypeCounts[ct] = treeSummary.ControlTypeCounts.GetValueOrDefault(ct) + 1;
                treeSummary.TotalElements++;

                // Categorize element
                if (IsClickable(el, seeEl))
                    clickable.Add(seeEl);

                if (IsEditable(el, seeEl))
                    editable.Add(seeEl);

                if (IsTextElement(el, seeEl))
                    textElements.Add(seeEl);
            }

            result.ClickableElements = clickable;
            result.EditableElements = editable;
            result.TextElements = textElements;
            result.UiTreeSummary = treeSummary;
        }

        result.Success = true;
        result.Warnings = warnings;
        return result;
    }

    /// <summary>
    /// Load a see JSON file and return the element catalog
    /// </summary>
    public SeeElementCatalog? LoadCatalog(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return null;

        var json = File.ReadAllText(jsonPath);
        var seeResult = System.Text.Json.JsonSerializer.Deserialize<SeeResult>(json);
        if (seeResult == null)
            return null;

        // Merge all element lists into a single catalog with deduplicated element_ids
        var catalog = new SeeElementCatalog
        {
            ActiveWindow = seeResult.ActiveWindow,
            Elements = new List<SeeElement>()
        };

        // Re-index all elements with consistent element_ids
        int idx = 1;
        foreach (var el in seeResult.ClickableElements)
        {
            el.ElementId = $"el_{idx:D3}";
            catalog.Elements.Add(el);
            idx++;
        }
        foreach (var el in seeResult.EditableElements)
        {
            if (!catalog.Elements.Any(e => e.BoundingBox == el.BoundingBox && e.Name == el.Name))
            {
                el.ElementId = $"el_{idx:D3}";
                catalog.Elements.Add(el);
                idx++;
            }
        }
        foreach (var el in seeResult.TextElements)
        {
            if (!catalog.Elements.Any(e => e.BoundingBox == el.BoundingBox && e.Name == el.Name))
            {
                el.ElementId = $"el_{idx:D3}";
                catalog.Elements.Add(el);
                idx++;
            }
        }

        return catalog;
    }

    /// <summary>
    /// Find element in catalog by element_id or by name
    /// </summary>
    public SeeElement? FindElement(SeeElementCatalog catalog, string elementQuery)
    {
        return FindBestMatch(catalog, elementQuery)?.Element;
    }

    /// <summary>
    /// Find element with fuzzy matching and semantic understanding
    /// </summary>
    public FuzzyMatchResult FindBestMatch(SeeElementCatalog catalog, string elementQuery, double threshold = 0.6)
    {
        var query = elementQuery.ToLower().Trim();
        var candidates = new List<(SeeElement Element, double Score, string MatchType)>();

        foreach (var el in catalog.Elements)
        {
            var name = (el.Name ?? "").ToLower();
            var autoId = (el.AutomationId ?? "").ToLower();
            var className = (el.ClassName ?? "").ToLower();

            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(autoId))
                continue;

            double score = 0;
            string matchType = "";

            if (name == query || autoId == query)
            {
                score = 1.0;
                matchType = "exact";
            }
            else if (name.Contains(query) || autoId.Contains(query))
            {
                score = 0.9;
                matchType = "contains";
            }
            else if (FuzzyMatch(name, query, out var fuzzyScore) && fuzzyScore >= threshold)
            {
                score = fuzzyScore;
                matchType = "fuzzy";
            }
            else if (ContainsAnyWord(name, query.Split(' ')))
            {
                score = 0.7;
                matchType = "word_match";
            }
            else if (SemanticMatch(name, query, out var semanticScore))
            {
                score = semanticScore;
                matchType = "semantic";
            }

            if (score >= threshold)
            {
                candidates.Add((el, score, matchType));
            }
        }

        if (candidates.Count == 0)
            return null;

        var best = candidates.OrderByDescending(c => c.score).First();
        var isDangerous = IsDangerousElement(best.Element);
        var matchedKeywords = DangerousKeywords
            .Where(kw => best.Element.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new FuzzyMatchResult
        {
            Element = best.Element,
            Score = best.Score,
            MatchType = best.MatchType,
            IsPotentiallyDangerous = isDangerous,
            DangerWarning = isDangerous ? $"Matched dangerous keywords: {string.Join(", ", matchedKeywords)}" : null,
            AllCandidates = candidates.OrderByDescending(c => c.score).ToList()
        };
    }

    private bool FuzzyMatch(string text, string pattern, out double score)
    {
        score = 0;

        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
            return false;

        if (text.Length > pattern.Length * 3 || pattern.Length > text.Length * 3)
            return false;

        var distance = LevenshteinDistance(text, pattern);
        var maxLen = Math.Max(text.Length, pattern.Length);
        var similarity = 1.0 - ((double)distance / maxLen);

        if (similarity >= 0.6)
        {
            score = similarity;
            return true;
        }

        if (pattern.Length <= 3)
        {
            if (text.StartsWith(pattern))
            {
                score = 0.7;
                return true;
            }
        }

        if (text.Length >= pattern.Length)
        {
            var substrings = GetSubstrings(text, pattern.Length);
            if (substrings.Any(s => s == pattern))
            {
                score = 0.85;
                return true;
            }
        }

        return false;
    }

    private List<string> GetSubstrings(string text, int length)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text) || length <= 0 || length > text.Length)
            return result;

        for (int i = 0; i <= text.Length - length; i++)
        {
            result.Add(text.Substring(i, length));
        }
        return result;
    }

    private bool ContainsAnyWord(string text, string[] words)
    {
        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word)) continue;
            if (text.Contains(word)) return true;
        }
        return false;
    }

    private bool SemanticMatch(string elementName, string query, out double score)
    {
        score = 0;

        var synonyms = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["save"] = new() { "保存", "save", "存储", "另存" },
            ["cancel"] = new() { "取消", "cancel", "关闭", "back" },
            ["ok"] = new() { "确定", "ok", "yes", "confirm", "yes" },
            ["delete"] = new() { "删除", "delete", "remove", "remove" },
            ["edit"] = new() { "编辑", "edit", "modify", "change" },
            ["search"] = new() { "搜索", "search", "find", "查找" },
            ["close"] = new() { "关闭", "close", "quit", "exit" },
            ["back"] = new() { "返回", "back", "previous", "last" },
            ["next"] = new() { "下一步", "next", "forward", "continue" },
            ["submit"] = new() { "提交", "submit", "send", "post" },
            ["login"] = new() { "登录", "login", "signin", "log in", "sign in" },
            ["logout"] = new() { "退出", "logout", "signout", "sign out", "log out" },
            ["settings"] = new() { "设置", "settings", "options", "preferences" }
        };

        var queryWords = query.Split(new[] { ' ', '_', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var kvp in synonyms)
        {
            foreach (var qw in queryWords)
            {
                if (kvp.Value.Contains(qw))
                {
                    foreach (var syn in kvp.Value)
                    {
                        if (elementName.Contains(syn) || elementName.Contains(kvp.Key))
                        {
                            score = 0.75;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private int LevenshteinDistance(string s1, string s2)
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

    public class FuzzyMatchResult
    {
        public SeeElement? Element { get; set; }
        public double Score { get; set; }
        public string MatchType { get; set; } = "";
        public bool IsPotentiallyDangerous { get; set; }
        public string? DangerWarning { get; set; }
        public List<(SeeElement Element, double Score, string MatchType)> AllCandidates { get; set; } = new();
    }

    /// <summary>
    /// Check if element name contains dangerous keywords
    /// </summary>
    public bool IsDangerousElement(SeeElement el)
    {
        if (string.IsNullOrEmpty(el.Name)) return false;
        var name = el.Name;
        return DangerousKeywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private void CollectElements(AutomationElement el, int depth, int maxDepth, List<AutomationElement> all)
    {
        if (depth > maxDepth) return;
        all.Add(el);
        try
        {
            var children = el.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                CollectElements(child, depth + 1, maxDepth, all);
            }
        }
        catch { }
    }

    private SeeElement BuildSeeElement(AutomationElement el, string elementId)
    {
        string controlType = "";
        try { controlType = el.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); }
        catch { controlType = "Unknown"; }

        RectInfo? box = null;
        int clickX = 0, clickY = 0;
        try
        {
            var r = el.Current.BoundingRectangle;
            if (r != System.Windows.Rect.Empty)
            {
                box = new RectInfo { X = (int)r.X, Y = (int)r.Y, Width = (int)r.Width, Height = (int)r.Height };
                clickX = (int)(r.X + r.Width / 2);
                clickY = (int)(r.Y + r.Height / 2);
            }
        }
        catch { }

        var patterns = new List<string>();
        try
        {
            foreach (var p in el.GetSupportedPatterns())
                patterns.Add(p.ProgrammaticName.Replace("Pattern.", ""));
        }
        catch { }

        string? value = null;
        try
        {
            var vp = el.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
            if (vp != null) value = vp.Current.Value;
        }
        catch { }

        bool isOffscreen = false;
        try { isOffscreen = el.Current.IsOffscreen; }
        catch { }

        bool isEnabled = false;
        try { isEnabled = el.Current.IsEnabled; }
        catch { }

        var seeEl = new SeeElement
        {
            ElementId = elementId,
            Name = TryGet<string>(el, AutomationElement.NameProperty),
            AutomationId = TryGet<string>(el, AutomationElement.AutomationIdProperty),
            ControlType = controlType,
            ClassName = TryGet<string>(el, AutomationElement.ClassNameProperty),
            BoundingBox = box,
            ClickPoint = box != null ? new ClickPoint { X = clickX, Y = clickY } : null,
            Enabled = isEnabled,
            IsOffscreen = isOffscreen,
            SupportedPatterns = patterns,
            Source = "uia",
            Value = value
        };

        seeEl.IsDangerous = IsDangerousElement(seeEl);
        return seeEl;
    }

    private static bool IsClickable(AutomationElement el, SeeElement seeEl)
    {
        var ct = seeEl.ControlType;
        if (ct == "Button" || ct == "MenuItem" || ct == "Hyperlink" || ct == "TabItem")
            return true;

        // Has Invoke pattern
        try { return el.GetCurrentPattern(InvokePattern.Pattern) != null; }
        catch { return false; }
    }

    private static bool IsEditable(AutomationElement el, SeeElement seeEl)
    {
        var ct = seeEl.ControlType;
        if (ct == "Edit" || ct == "Document")
            return true;

        // Supports Value or Text pattern
        try
        {
            return el.GetCurrentPattern(ValuePattern.Pattern) != null ||
                   el.GetCurrentPattern(TextPattern.Pattern) != null;
        }
        catch { return false; }
    }

    private static bool IsTextElement(AutomationElement el, SeeElement seeEl)
    {
        var ct = seeEl.ControlType;
        if (ct == "Text" || ct == "MenuItem" || ct == "Button")
            return !string.IsNullOrEmpty(seeEl.Name);

        return false;
    }

    private static T? TryGet<T>(AutomationElement el, AutomationProperty property)
    {
        try { return (T?)el.GetCurrentPropertyValue(property); }
        catch { return default; }
    }
}