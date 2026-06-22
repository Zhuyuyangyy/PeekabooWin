namespace WinAgent.Core.Models;

/// <summary>
/// 元素来源标记 — 所有 bbox 必须标注来源
/// </summary>
public enum ElementSource
{
    UIA,        // Windows UI Automation (最可靠)
    OCR,        // OCR 文字识别 (兜底)
    Vision,     // 视觉模型解析 (兜底)
    CDP,        // Chrome DevTools Protocol (浏览器)
    Heuristic   // 启发式推断 (最低优先级)
}

/// <summary>
/// 元素角色 — 语义化分类
/// </summary>
public enum ElementRole
{
    Button,
    Link,
    Input,
    Checkbox,
    Radio,
    Select,
    Tab,
    MenuItem,
    Text,
    Image,
    Dialog,
    Window,
    Unknown
}

/// <summary>
/// 坐标空间 — 统一为 physical screen pixels
/// </summary>
public enum CoordinateSpace
{
    PhysicalScreenPixels  // 唯一合法的坐标空间
}

/// <summary>
/// 元素快照 — observe 的输出单元
///
/// 关键设计:
/// - id 是唯一交互入口，LLM 只能选择 id，不能直接指定坐标
/// - bbox 必须标注来源 (source)
/// - confidence 是估计值，不是 ground truth
/// - coordinate_space 固定为 PhysicalScreenPixels
/// </summary>
public class ElementSnapshot
{
    public string Id { get; set; } = "";           // e.g. "btn_12", "txt_03"
    public ElementRole Role { get; set; }          // 语义角色
    public string Name { get; set; } = "";         // 可见文本
    public string? AutomationId { get; set; }      // UIA AutomationId
    public string? ClassName { get; set; }         // 窗口类名
    public BoundingBox BBox { get; set; } = new(); // physical screen pixels
    public ElementSource Source { get; set; }       // bbox 来源
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public double EstimatedConfidence { get; set; } // 估计置信度，非 ground truth
    public Dictionary<string, string> Properties { get; set; } = new();
}

/// <summary>
/// 边界框 — 统一为 physical screen pixels
/// </summary>
public class BoundingBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(int px, int py)
        => px >= X && px <= Right && py >= Y && py <= Bottom;

    public double IoU(BoundingBox other)
    {
        var x1 = Math.Max(X, other.X);
        var y1 = Math.Max(Y, other.Y);
        var x2 = Math.Min(Right, other.Right);
        var y2 = Math.Min(Bottom, other.Bottom);

        var intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        var union = Width * Height + other.Width * other.Height - intersection;

        return union > 0 ? (double)intersection / union : 0;
    }
}
