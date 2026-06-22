namespace WinAgent.Core.Models;

/// <summary>
/// Observe 输出 — 屏幕快照
///
/// 这是整个系统的唯一感知入口。
/// LLM 只能看到这个结构，不能直接访问坐标或原始 API。
/// </summary>
public class ObservationResult
{
    public string SnapshotId { get; set; } = $"snap_{Guid.NewGuid():N}"[..16];
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public CoordinateSpace CoordinateSpace { get; set; } = CoordinateSpace.PhysicalScreenPixels;

    public WindowInfo ActiveWindow { get; set; } = new();
    public ScreenshotInfo? Screenshot { get; set; }
    public List<ElementSnapshot> Elements { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    // 统计
    public int TotalElements => Elements.Count;
    public int ClickableCount => Elements.Count(e => IsClickableRole(e.Role));
    public int EditableCount => Elements.Count(e => e.Role == ElementRole.Input);
    public int DangerousCount => Elements.Count(e => IsDangerousElement(e));

    private static bool IsClickableRole(ElementRole role)
        => role is ElementRole.Button or ElementRole.Link
            or ElementRole.MenuItem or ElementRole.Tab or ElementRole.Checkbox;

    private static bool IsDangerousElement(ElementSnapshot e)
    {
        if (string.IsNullOrEmpty(e.Name)) return false;
        var dangerous = new[] { "关闭", "删除", "卸载", "支付", "购买", "确认删除",
            "Close", "Delete", "Uninstall", "Pay", "Purchase" };
        return dangerous.Any(kw => e.Name.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// 窗口信息
/// </summary>
public class WindowInfo
{
    public long Handle { get; set; }
    public string Title { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
    public string ClassName { get; set; } = "";
    public BoundingBox Bounds { get; set; } = new();
}

/// <summary>
/// 截图信息
/// </summary>
public class ScreenshotInfo
{
    public string Path { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
}
