using System.Windows.Automation;
using WinAgent.Core.Coordinate;
using WinAgent.Core.Models;

namespace WinAgent.Sensors.UIA;

/// <summary>
/// UIA 传感器 — Windows UI Automation，第一优先级感知源
///
/// 原则:
/// - 所有坐标通过 CoordinateMapper 转换为 physical screen pixels
/// - 每个元素必须标记 Source = ElementSource.UIA
/// - confidence 基于 UIA 属性完整性评估，不伪造
/// </summary>
public class UiaSensor : IObservationSensor
{
    private readonly CoordinateMapper _coordMapper = new();

    public string Name => "UIA";
    public ElementSource Source => ElementSource.UIA;
    public int Priority => 1;

    public bool IsAvailable() => true;

    public List<ElementSnapshot> Observe(IntPtr windowHandle)
    {
        var elements = new List<ElementSnapshot>();

        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            if (root == null) return elements;

            CollectElements(root, 0, 6, elements);
        }
        catch
        {
            // UIA 不可用时返回空列表，让 OCR 兜底
        }

        return elements;
    }

    private void CollectElements(AutomationElement parent, int depth, int maxDepth, List<ElementSnapshot> elements)
    {
        if (depth > maxDepth) return;

        try
        {
            var children = parent.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                var snapshot = BuildSnapshot(child);
                if (snapshot != null && IsRelevant(snapshot))
                {
                    elements.Add(snapshot);
                }

                CollectElements(child, depth + 1, maxDepth, elements);
            }
        }
        catch { }
    }

    private ElementSnapshot? BuildSnapshot(AutomationElement el)
    {
        try
        {
            var role = MapControlType(el.Current.ControlType.ProgrammaticName);
            var name = SafeGet(() => el.Current.Name);
            var autoId = SafeGet(() => el.Current.AutomationId);
            var className = SafeGet(() => el.Current.ClassName);
            var enabled = SafeGet(() => el.Current.IsEnabled, true);
            var offscreen = SafeGet(() => el.Current.IsOffscreen, false);

            BoundingBox? bbox = null;
            try
            {
                var rect = el.Current.BoundingRectangle;
                if (rect != System.Windows.Rect.Empty && rect.Width > 0 && rect.Height > 0)
                {
                    var logicalBox = new BoundingBox
                    {
                        X = (int)rect.X,
                        Y = (int)rect.Y,
                        Width = (int)rect.Width,
                        Height = (int)rect.Height
                    };
                    bbox = _coordMapper.UiaToPhysical(logicalBox);
                }
            }
            catch { }

            if (bbox == null) return null;

            var confidence = EstimateConfidence(name, autoId, bbox);

            return new ElementSnapshot
            {
                Role = role,
                Name = name ?? "",
                AutomationId = autoId,
                ClassName = className,
                BBox = bbox,
                Source = ElementSource.UIA,
                Enabled = enabled,
                Visible = !offscreen,
                EstimatedConfidence = confidence,
                Properties = BuildProperties(el)
            };
        }
        catch
        {
            return null;
        }
    }

    private double EstimateConfidence(string? name, string? autoId, BoundingBox bbox)
    {
        var score = 0.5;

        if (!string.IsNullOrEmpty(name)) score += 0.15;
        if (!string.IsNullOrEmpty(autoId)) score += 0.15;
        if (bbox.Width > 10 && bbox.Height > 5) score += 0.1;
        if (bbox.Width > 0 && bbox.Height > 0 && _coordMapper.IsOnScreen(bbox)) score += 0.1;

        return Math.Min(1.0, score);
    }

    private bool IsRelevant(ElementSnapshot el)
    {
        if (el.Role == ElementRole.Unknown && string.IsNullOrEmpty(el.Name))
            return false;
        if (el.BBox.Width <= 0 || el.BBox.Height <= 0)
            return false;
        return true;
    }

    private ElementRole MapControlType(string programmaticName)
    {
        return programmaticName switch
        {
            "ControlType.Button" => ElementRole.Button,
            "ControlType.Hyperlink" => ElementRole.Link,
            "ControlType.Edit" => ElementRole.Input,
            "ControlType.Document" => ElementRole.Input,
            "ControlType.CheckBox" => ElementRole.Checkbox,
            "ControlType.RadioButton" => ElementRole.Radio,
            "ControlType.ComboBox" => ElementRole.Select,
            "ControlType.TabItem" => ElementRole.Tab,
            "ControlType.MenuItem" => ElementRole.MenuItem,
            "ControlType.Menu" => ElementRole.MenuItem,
            "ControlType.Text" => ElementRole.Text,
            "ControlType.Image" => ElementRole.Image,
            "ControlType.Window" => ElementRole.Window,
            "ControlType.Pane" => ElementRole.Window,
            "ControlType.Dialog" => ElementRole.Dialog,
            _ => ElementRole.Unknown
        };
    }

    private Dictionary<string, string> BuildProperties(AutomationElement el)
    {
        var props = new Dictionary<string, string>();
        try
        {
            foreach (var p in el.GetSupportedPatterns())
            {
                props[$"pattern_{p.ProgrammaticName}"] = "supported";
            }
        }
        catch { }

        try
        {
            var vp = el.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
            if (vp != null) props["value"] = vp.Current.Value ?? "";
        }
        catch { }

        return props;
    }

    private static T SafeGet<T>(Func<T> getter, T defaultValue = default!)
    {
        try { return getter(); }
        catch { return defaultValue; }
    }
}
