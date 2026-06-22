using WinAgent.Core.Models;

namespace WinAgent.Core.Observation;

/// <summary>
/// 观察器接口 — 感知层抽象
/// 放在 Core 中，Sensors 实现此接口
/// </summary>
public interface IObservationSensor
{
    string Name { get; }
    ElementSource Source { get; }
    int Priority { get; }  // 越小越优先: UIA=1, CDP=2, OCR=3, Vision=4
    bool IsAvailable();
    List<ElementSnapshot> Observe(IntPtr windowHandle);
}

/// <summary>
/// 观察服务 — 聚合多个传感器，按优先级合并元素
///
/// 核心原则:
/// 1. UIA 优先，OCR 兜底
/// 2. 同一区域元素去重 (IoU > 0.5 时取高优先级)
/// 3. 所有坐标统一为 physical screen pixels
/// 4. 所有元素必须有 source 标记
/// 5. element id 稳定生成: {role_prefix}_{index}
/// </summary>
public class ObservationService
{
    private readonly List<IObservationSensor> _sensors = new();
    private readonly ElementDeduplicator _deduplicator = new();

    public void RegisterSensor(IObservationSensor sensor)
    {
        _sensors.Add(sensor);
        _sensors.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    public ObservationResult Observe(IntPtr windowHandle, string? screenshotPath = null)
    {
        var result = new ObservationResult
        {
            Timestamp = DateTime.Now,
            CoordinateSpace = CoordinateSpace.PhysicalScreenPixels
        };

        // 获取窗口信息
        var windowBounds = Coordinate.CoordinateMapper.GetWindowPhysicalBoundsStatic(windowHandle);
        var title = Coordinate.CoordinateMapper.GetWindowTitleStatic(windowHandle);

        result.ActiveWindow = new WindowInfo
        {
            Handle = windowHandle.ToInt64(),
            Title = title,
            Bounds = windowBounds
        };

        // 按优先级收集元素
        var allElements = new List<ElementSnapshot>();
        var idCounter = new Dictionary<string, int>
        {
            ["btn"] = 0, ["txt"] = 0, ["inp"] = 0,
            ["lnk"] = 0, ["mnu"] = 0, ["chk"] = 0,
            ["tab"] = 0, ["img"] = 0, ["dlg"] = 0, ["el"] = 0
        };

        foreach (var sensor in _sensors)
        {
            if (!sensor.IsAvailable())
            {
                result.Warnings.Add($"Sensor {sensor.Name} unavailable, skipped");
                continue;
            }

            try
            {
                var elements = sensor.Observe(windowHandle);

                foreach (var el in elements)
                {
                    el.Source = sensor.Source;
                    el.Id = AssignElementId(el.Role, idCounter);
                }

                allElements.AddRange(elements);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Sensor {sensor.Name} error: {ex.Message}");
            }
        }

        // 去重
        result.Elements = _deduplicator.Deduplicate(allElements);

        // 截图
        if (!string.IsNullOrEmpty(screenshotPath))
        {
            result.Screenshot = new ScreenshotInfo { Path = screenshotPath };
        }

        return result;
    }

    private string AssignElementId(ElementRole role, Dictionary<string, int> counter)
    {
        var prefix = role switch
        {
            ElementRole.Button => "btn",
            ElementRole.Input => "inp",
            ElementRole.Text => "txt",
            ElementRole.Link => "lnk",
            ElementRole.MenuItem => "mnu",
            ElementRole.Checkbox => "chk",
            ElementRole.Tab => "tab",
            ElementRole.Image => "img",
            ElementRole.Dialog => "dlg",
            _ => "el"
        };

        counter[prefix]++;
        return $"{prefix}_{counter[prefix]:D3}";
    }
}

/// <summary>
/// 元素去重器 — 同一区域 IoU > 0.5 时取高优先级来源
/// </summary>
public class ElementDeduplicator
{
    private const double IoUThreshold = 0.5;

    public List<ElementSnapshot> Deduplicate(List<ElementSnapshot> elements)
    {
        var result = new List<ElementSnapshot>();
        var removed = new HashSet<int>();

        for (int i = 0; i < elements.Count; i++)
        {
            if (removed.Contains(i)) continue;

            for (int j = i + 1; j < elements.Count; j++)
            {
                if (removed.Contains(j)) continue;

                var iou = elements[i].BBox.IoU(elements[j].BBox);
                if (iou > IoUThreshold)
                {
                    var priorityI = GetSourcePriority(elements[i].Source);
                    var priorityJ = GetSourcePriority(elements[j].Source);

                    if (priorityI <= priorityJ)
                        removed.Add(j);
                    else
                        removed.Add(i);
                }
            }
        }

        for (int i = 0; i < elements.Count; i++)
        {
            if (!removed.Contains(i))
                result.Add(elements[i]);
        }

        return result;
    }

    private int GetSourcePriority(ElementSource source)
        => source switch
        {
            ElementSource.UIA => 1,
            ElementSource.CDP => 2,
            ElementSource.OCR => 3,
            ElementSource.Vision => 4,
            ElementSource.Heuristic => 5,
            _ => 6
        };
}
