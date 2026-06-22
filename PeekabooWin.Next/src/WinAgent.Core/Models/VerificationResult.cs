namespace WinAgent.Core.Models;

/// <summary>
/// Verify 输出 — 操作前后对比验证
/// </summary>
public class VerificationResult
{
    public bool Changed { get; set; }
    public double PixelDiffRatio { get; set; }      // 像素变化比例
    public string? ChangeDescription { get; set; }  // 变化描述
    public string? BeforeScreenshot { get; set; }
    public string? AfterScreenshot { get; set; }
    public List<ElementChange> ElementChanges { get; set; } = new();
}

/// <summary>
/// 元素级变化
/// </summary>
public class ElementChange
{
    public string ElementId { get; set; } = "";
    public ChangeType Type { get; set; }
    public string? Detail { get; set; }
}

public enum ChangeType
{
    Appeared,
    Disappeared,
    TextChanged,
    StateChanged,
    Moved,
    Resized
}
