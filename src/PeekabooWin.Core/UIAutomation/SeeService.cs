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
        // Try by element_id first
        var el = catalog.Elements.FirstOrDefault(e => e.ElementId == elementQuery);
        if (el != null) return el;

        // Try by name (partial match)
        el = catalog.Elements.FirstOrDefault(e =>
            !string.IsNullOrEmpty(e.Name) &&
            e.Name.Contains(elementQuery, StringComparison.OrdinalIgnoreCase));
        return el;
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