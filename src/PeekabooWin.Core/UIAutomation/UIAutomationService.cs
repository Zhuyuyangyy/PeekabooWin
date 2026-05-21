using System.Runtime.InteropServices;
using System.Windows.Automation;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Core.UIAutomation;

public class UIAutomationService
{
    private readonly WindowService _windowService;
    private int _elementIdCounter = 0;

    public UIAutomationService(WindowService windowService)
    {
        _windowService = windowService;
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

            _elementIdCounter = 0;
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

            return InvokeElement(match);
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("click_element", ex.Message);
        }
    }

    /// <summary>
    /// 执行控件的 Invoke（按钮点击等）
    /// </summary>
    private CommandResult InvokeElement(AutomationElement el)
    {
        try
        {
            // 优先用 InvokePattern
            var invokePattern = el.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
            if (invokePattern != null)
            {
                invokePattern.Invoke();
                return CommandResult.Ok("click_element");
            }

            // 其次尝试 ValuePattern（输入框）
            var valuePattern = el.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
            if (valuePattern != null)
            {
                el.SetFocus();
                var box = el.Current.BoundingRectangle;
                if (box != System.Windows.Rect.Empty)
                {
                    var cx = (int)(box.X + box.Width / 2);
                    var cy = (int)(box.Y + box.Height / 2);
                    var input = new Input.InputService();
                    input.Click(cx, cy);
                    return CommandResult.Ok("click_element");
                }
            }

            // 最后用坐标点击
            el.SetFocus();
            var rect = el.Current.BoundingRectangle;
            if (rect != System.Windows.Rect.Empty)
            {
                var cx = (int)(rect.X + rect.Width / 2);
                var cy = (int)(rect.Y + rect.Height / 2);
                var input = new Input.InputService();
                input.Click(cx, cy);
                return CommandResult.Ok("click_element");
            }

            return CommandResult.Fail("click_element", "Element has no actionable pattern and no bounding box");
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("click_element", ex.Message);
        }
    }

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
        string id = $"e{++_elementIdCounter}";

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
        catch { }

        var patterns = new List<string>();
        try
        {
            var supported = el.GetSupportedPatterns();
            foreach (var p in supported)
            {
                patterns.Add(p.ProgrammaticName.Replace("Pattern.", ""));
            }
        }
        catch { }

        string? value = null;
        try
        {
            var vp = el.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
            if (vp != null) value = vp.Current.Value;
        }
        catch { }

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