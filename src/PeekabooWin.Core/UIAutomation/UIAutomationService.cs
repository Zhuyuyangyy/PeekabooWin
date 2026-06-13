using System.Runtime.InteropServices;
using System.Windows.Automation;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Core.UIAutomation;

public class UIAutomationService
{
    private readonly WindowService _windowService;
    private readonly InputService _inputService;

    // Static counter for thread-safe element ID generation
    private static int _elementIdCounter = 0;

    // Semantic keyword expansion for fuzzy UIA search (mirrors PerceptionRouter concept)
    private static readonly Dictionary<string, string[]> SemanticAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["确定"] = new[] { "OK", "确认", "Confirm", "确定" },
        ["取消"] = new[] { "Cancel", "取消", "放弃" },
        ["保存"] = new[] { "Save", "保存", "存储" },
        ["关闭"] = new[] { "Close", "关闭", "退出" },
        ["发送"] = new[] { "Send", "发送", "提交", "Submit" },
        ["搜索"] = new[] { "Search", "搜索", "查找", "Find" },
        ["删除"] = new[] { "Delete", "删除", "移除", "Remove" },
        ["打开"] = new[] { "Open", "打开", "开启" },
        ["是"] = new[] { "Yes", "是", "确认" },
        ["否"] = new[] { "No", "否", "不" },
        ["提交"] = new[] { "Submit", "提交", "确定", "Confirm" },
        ["应用"] = new[] { "Apply", "应用", "确定" },
        ["重试"] = new[] { "Retry", "重试", "再来" },
        ["忽略"] = new[] { "Ignore", "忽略", "跳过" },
    };

    public UIAutomationService(WindowService windowService, InputService inputService)
    {
        _windowService = windowService;
        _inputService = inputService;
    }

    /// <summary>
    /// 遍历窗口的 UIA 控件树
    /// </summary>
    public UIAInspectResult Inspect(string windowKeyword, int maxDepth = 10)
    {
        try
        {
            var win = _windowService.FindWindow(windowKeyword);
            if (win == null)
                return new UIAInspectResult { Success = false, Error = $"Window not found: {windowKeyword}" };

            var root = AutomationElement.FromHandle((IntPtr)win.Handle);
            if (root == null)
                return new UIAInspectResult { Success = false, Error = "Cannot get UIA element from handle" };

            // Reset counter for this inspect session (thread-safe)
            Interlocked.Exchange(ref _elementIdCounter, 0);
            var elements = new List<UIAElementInfo>();
            var rootInfo = BuildElementTree(root, 0, maxDepth, elements);

            return new UIAInspectResult
            {
                Success = true,
                WindowTitle = win.Title,
                WindowHandle = win.Handle,
                ElementCount = elements.Count,
                RootElements = new List<UIAElementInfo> { rootInfo }
            };
        }
        catch (Exception ex)
        {
            return new UIAInspectResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 按名称查找控件
    /// </summary>
    public UIAFindResult FindByName(string windowKeyword, string name, bool recursive = true)
    {
        try
        {
            var win = _windowService.FindWindow(windowKeyword);
            if (win == null)
                return new UIAFindResult { Success = false, Error = $"Window not found: {windowKeyword}" };

            var root = AutomationElement.FromHandle((IntPtr)win.Handle);
            if (root == null)
                return new UIAFindResult { Success = false, Error = "Cannot get UIA element" };

            var condition = new PropertyCondition(AutomationElement.NameProperty, name);
            var matches = root.FindAll(recursive ? TreeScope.Children | TreeScope.Descendants : TreeScope.Children, condition);

            var results = new List<UIAElementInfo>();
            foreach (AutomationElement el in matches)
            {
                results.Add(BuildElementInfo(el, null));
            }

            return new UIAFindResult { Success = true, Matches = results, Count = results.Count };
        }
        catch (Exception ex)
        {
            return new UIAFindResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 按控件类型查找控件
    /// </summary>
    public UIAFindResult FindByControlType(string windowKeyword, string controlType, bool recursive = true)
    {
        try
        {
            var win = _windowService.FindWindow(windowKeyword);
            if (win == null)
                return new UIAFindResult { Success = false, Error = $"Window not found: {windowKeyword}" };

            var root = AutomationElement.FromHandle((IntPtr)win.Handle);
            if (root == null)
                return new UIAFindResult { Success = false, Error = "Cannot get UIA element" };

            var ct = controlType.ToLower() switch
            {
                "button" => ControlType.Button,
                "edit" or "textbox" or "text field" => ControlType.Edit,
                "checkbox" => ControlType.CheckBox,
                "combobox" or "dropdown" => ControlType.ComboBox,
                "list" => ControlType.List,
                "listitem" => ControlType.ListItem,
                "menu" => ControlType.Menu,
                "menuitem" => ControlType.MenuItem,
                "tab" => ControlType.Tab,
                "tabitem" => ControlType.TabItem,
                "tree" => ControlType.Tree,
                "treeitem" => ControlType.TreeItem,
                "group" => ControlType.Group,
                "window" => ControlType.Window,
                "dialog" => ControlType.Window,
                "link" or "hyperlink" => ControlType.Hyperlink,
                "image" => ControlType.Image,
                "slider" => ControlType.Slider,
                "scrollbar" => ControlType.ScrollBar,
                "progressbar" => ControlType.ProgressBar,
                "document" => ControlType.Document,
                _ => ControlType.Custom
            };

            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ct);
            var matches = root.FindAll(recursive ? TreeScope.Children | TreeScope.Descendants : TreeScope.Children, condition);

            var results = new List<UIAElementInfo>();
            foreach (AutomationElement el in matches)
            {
                results.Add(BuildElementInfo(el, null));
            }

            return new UIAFindResult { Success = true, Matches = results, Count = results.Count };
        }
        catch (Exception ex)
        {
            return new UIAFindResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 按 AutomationId 精确查找
    /// </summary>
    public UIAFindResult FindByAutomationId(string windowKeyword, string automationId)
    {
        try
        {
            var win = _windowService.FindWindow(windowKeyword);
            if (win == null)
                return new UIAFindResult { Success = false, Error = $"Window not found: {windowKeyword}" };

            var root = AutomationElement.FromHandle((IntPtr)win.Handle);
            if (root == null)
                return new UIAFindResult { Success = false, Error = "Cannot get UIA element" };

            var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
            var match = root.FindFirst(TreeScope.Children | TreeScope.Descendants, condition);

            if (match != null)
            {
                return new UIAFindResult
                {
                    Success = true,
                    Matches = new List<UIAElementInfo> { BuildElementInfo(match, null) },
                    Count = 1
                };
            }

            return new UIAFindResult { Success = true, Matches = new List<UIAElementInfo>(), Count = 0 };
        }
        catch (Exception ex)
        {
            return new UIAFindResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Semantic element search: expands the description using keyword aliases,
    /// then tries FindByName for each alias. Falls back to control-type inference.
    /// </summary>
    public UIAFindResult FindBySemantic(string windowKeyword, string description)
    {
        try
        {
            // Expand description with semantic aliases
            var aliases = GetSemanticAliases(description);
            int candidatesTried = 0;

            // Try each alias via FindByName, return first match
            foreach (var alias in aliases)
            {
                candidatesTried++;
                var findResult = FindByName(windowKeyword, alias);
                if (findResult.Success && findResult.Count > 0)
                {
                    PekaLogger.Debug("UIAutomationService",
                        $"FindBySemantic: matched '{alias}' for description '{description}' after {candidatesTried} candidates");
                    return findResult;
                }
            }

            // Fallback: try control-type inference + name similarity
            var controlType = InferControlType(description);
            if (controlType != null)
            {
                candidatesTried++;
                var typeResult = FindByControlType(windowKeyword, controlType);
                if (typeResult.Success && typeResult.Count > 0)
                {
                    // Filter by name similarity
                    var bestMatch = typeResult.Matches
                        .Where(m => m.Name != null && (
                            m.Name.Contains(description, StringComparison.OrdinalIgnoreCase) ||
                            description.Contains(m.Name, StringComparison.OrdinalIgnoreCase)))
                        .FirstOrDefault();

                    if (bestMatch != null)
                    {
                        PekaLogger.Debug("UIAutomationService",
                            $"FindBySemantic: control-type fallback matched '{bestMatch.Name}' for '{description}'");
                        return new UIAFindResult
                        {
                            Success = true,
                            Matches = new List<UIAElementInfo> { bestMatch },
                            Count = 1
                        };
                    }
                }
            }

            PekaLogger.Debug("UIAutomationService",
                $"FindBySemantic: no match found for '{description}' after {candidatesTried} candidates");
            return new UIAFindResult { Success = false, Error = $"Semantic search found no match for: {description}" };
        }
        catch (Exception ex)
        {
            PekaLogger.Warn("UIAutomationService", $"FindBySemantic failed: {ex.Message}", ex);
            return new UIAFindResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 点击控件（通过名称查找，然后点击）
    /// </summary>
    public CommandResult ClickElementByName(string windowKeyword, string name)
    {
        try
        {
            var win = _windowService.FindWindow(windowKeyword);
            if (win == null)
                return CommandResult.Fail("click_element", $"Window not found: {windowKeyword}");

            var root = AutomationElement.FromHandle((IntPtr)win.Handle);
            if (root == null)
                return CommandResult.Fail("click_element", "Cannot get UIA element");

            var condition = new PropertyCondition(AutomationElement.NameProperty, name);
            var match = root.FindFirst(TreeScope.Children | TreeScope.Descendants, condition);

            if (match == null)
                return CommandResult.Fail("click_element", $"Element not found: {name}");

            var invokeResult = InvokeElement(match);
            return invokeResult.Success
                ? CommandResult.Ok("click_element", new { method = invokeResult.Method })
                : CommandResult.Fail("click_element", invokeResult.ErrorDetail ?? "Invoke failed");
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("click_element", ex.Message);
        }
    }

    /// <summary>
    /// 执行控件的 Invoke（按钮点击等）— 公开方法，返回 InvokeResult
    /// </summary>
    public InvokeResult InvokeElement(AutomationElement el)
    {
        try
        {
            // Check supported patterns once to avoid spurious "不支持的模式" exceptions
            AutomationPattern[] supportedPatterns;
            try { supportedPatterns = el.GetSupportedPatterns(); }
            catch { supportedPatterns = Array.Empty<AutomationPattern>(); }

            bool supportsInvoke = Array.Exists(supportedPatterns, p => p == InvokePattern.Pattern);
            bool supportsValue = Array.Exists(supportedPatterns, p => p == ValuePattern.Pattern);

            // 优先用 InvokePattern
            if (supportsInvoke)
            {
                var invokePattern = el.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                if (invokePattern != null)
                {
                    invokePattern.Invoke();
                    return new InvokeResult { Success = true, Method = "InvokePattern" };
                }
            }

            // 其次尝试 ValuePattern（输入框）— focus + click center
            if (supportsValue)
            {
                var valuePattern = el.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                if (valuePattern != null)
                {
                    el.SetFocus();
                    var clickPoint = GetClickablePoint(el);
                    if (clickPoint.HasValue)
                    {
                        _inputService.Click(clickPoint.Value.X, clickPoint.Value.Y);
                        return new InvokeResult { Success = true, Method = "ValuePattern" };
                    }
                }
            }

            // 最后用坐标点击
            try { el.SetFocus(); } catch { /* SetFocus may fail on some elements */ }
            var fallbackPoint = GetClickablePoint(el);
            if (fallbackPoint.HasValue)
            {
                _inputService.Click(fallbackPoint.Value.X, fallbackPoint.Value.Y);
                return new InvokeResult { Success = true, Method = "CoordinateClick" };
            }

            return new InvokeResult
            {
                Success = false,
                Method = "NoAction",
                ErrorDetail = "Element has no actionable pattern and no bounding box"
            };
        }
        catch (Exception ex)
        {
            return new InvokeResult
            {
                Success = false,
                Method = "Error",
                ErrorDetail = ex.Message
            };
        }
    }

    /// <summary>
    /// Gets the clickable point for an AutomationElement.
    /// Tries GetClickablePoint first, then falls back to bounding box center.
    /// </summary>
    public (int X, int Y)? GetClickablePoint(AutomationElement el)
    {
        try
        {
            // Try GetClickablePoint first
            System.Windows.Point point;
            if (el.TryGetClickablePoint(out point))
            {
                return ((int)point.X, (int)point.Y);
            }
        }
        catch (Exception ex)
        {
            PekaLogger.Debug("UIAutomationService", $"TryGetClickablePoint failed: {ex.Message}");
        }

        // Fallback: bounding box center
        try
        {
            var rect = el.Current.BoundingRectangle;
            if (rect != System.Windows.Rect.Empty && rect.Width > 0 && rect.Height > 0)
            {
                var cx = (int)(rect.X + rect.Width / 2);
                var cy = (int)(rect.Y + rect.Height / 2);
                return (cx, cy);
            }
        }
        catch (Exception ex)
        {
            PekaLogger.Debug("UIAutomationService", $"BoundingRectangle fallback failed: {ex.Message}");
        }

        return null;
    }

    #region Semantic Helpers

    private static string[] GetSemanticAliases(string description)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { description };

        foreach (var kvp in SemanticAliases)
        {
            if (kvp.Key.Contains(description, StringComparison.OrdinalIgnoreCase) ||
                description.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.Any(v => v.Contains(description, StringComparison.OrdinalIgnoreCase) ||
                                   description.Contains(v, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var alias in kvp.Value)
                    aliases.Add(alias);
            }
        }

        return aliases.ToArray();
    }

    private static string? InferControlType(string description)
    {
        var lower = description.ToLower();
        if (lower.Contains("按钮") || lower.Contains("button") || lower.Contains("btn"))
            return "button";
        if (lower.Contains("输入") || lower.Contains("textbox") || lower.Contains("input") || lower.Contains("编辑"))
            return "textbox";
        if (lower.Contains("checkbox") || lower.Contains("复选") || lower.Contains("勾选"))
            return "checkbox";
        if (lower.Contains("下拉") || lower.Contains("dropdown") || lower.Contains("combo"))
            return "combobox";
        if (lower.Contains("链接") || lower.Contains("link"))
            return "link";
        if (lower.Contains("菜单") || lower.Contains("menu"))
            return "menu";
        if (lower.Contains("tab") || lower.Contains("标签"))
            return "tab";
        return null;
    }

    #endregion

    #region Element Tree Building

    private UIAElementInfo BuildElementTree(AutomationElement el, int depth, int maxDepth, List<UIAElementInfo> allElements)
    {
        if (depth > maxDepth) return BuildElementInfo(el, null);

        var info = BuildElementInfo(el, null);
        allElements.Add(info);

        try
        {
            var children = el.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                var childInfo = BuildElementTree(child, depth + 1, maxDepth, allElements);
                info.Children.Add(childInfo);
            }
        }
        catch
        {
            // 忽略子节点访问错误
        }

        return info;
    }

    private UIAElementInfo BuildElementInfo(AutomationElement el, string? parentId)
    {
        string id = $"e{Interlocked.Increment(ref _elementIdCounter)}";

        string controlType = "";
        try { controlType = el.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); }
        catch { controlType = "Unknown"; }

        RectInfo? box = null;
        try
        {
            var r = el.Current.BoundingRectangle;
            if (r != System.Windows.Rect.Empty)
            {
                box = new RectInfo
                {
                    X = (int)r.X,
                    Y = (int)r.Y,
                    Width = (int)r.Width,
                    Height = (int)r.Height
                };
            }
        }
        catch { /* BoundingRectangle may fail on certain elements — safe to skip */ }

        var patterns = new List<string>();
        bool supportsValuePattern = false;
        try
        {
            var supported = el.GetSupportedPatterns();
            foreach (var p in supported)
            {
                var name = p.ProgrammaticName.Replace("Pattern.", "");
                patterns.Add(name);
                if (p == ValuePattern.Pattern)
                    supportsValuePattern = true;
            }
        }
        catch { /* GetSupportedPatterns may throw on certain elements — safe to ignore */ }

        // Only query ValuePattern if the element explicitly supports it,
        // avoiding spurious "不支持的模式" warnings in the log.
        string? value = null;
        if (supportsValuePattern)
        {
            try
            {
                var vp = el.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                if (vp != null) value = vp.Current.Value;
            }
            catch { /* Race condition: pattern may have been removed between check and access */ }
        }

        return new UIAElementInfo
        {
            Id = id,
            Name = TryGet<string>(el, AutomationElement.NameProperty),
            AutomationId = TryGet<string>(el, AutomationElement.AutomationIdProperty),
            ControlType = controlType,
            ClassName = TryGet<string>(el, AutomationElement.ClassNameProperty),
            BoundingBox = box,
            IsEnabled = TryGet<bool>(el, AutomationElement.IsEnabledProperty),
            IsVisible = TryGet<bool>(el, AutomationElement.IsOffscreenProperty) == false,
            Patterns = patterns,
            Value = value,
            Children = new List<UIAElementInfo>()
        };
    }

    private static T? TryGet<T>(AutomationElement el, AutomationProperty property)
    {
        try { return (T?)el.GetCurrentPropertyValue(property); }
        catch { return default; }
    }

    #endregion
}
