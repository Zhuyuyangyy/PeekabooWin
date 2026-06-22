namespace WinAgent.Core.Models;

/// <summary>
/// Ground 输出 — 元素定位结果
///
/// LLM 选择 element_id → Ground 解析为可执行坐标
/// LLM 不直接决定坐标，LLM 只选择元素
/// </summary>
public class GroundingResult
{
    public string SnapshotId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public ElementSnapshot? ResolvedElement { get; set; }
    public bool IsGrounded { get; set; }
    public double EstimatedScore { get; set; }
    public string MatchType { get; set; } = "";  // exact, fuzzy, semantic
    public bool IsPotentiallyDangerous { get; set; }
    public string? DangerWarning { get; set; }
    public string? Error { get; set; }

    // 执行坐标 — 只有 IsGrounded=true 时才有值
    public int? ClickX { get; set; }
    public int? ClickY { get; set; }
}

/// <summary>
/// Ground 查询 — LLM 发出的定位请求
/// </summary>
public class GroundingQuery
{
    public string SnapshotId { get; set; } = "";
    public string TargetId { get; set; } = "";    // 唯一合法的定位方式
    public string? FallbackText { get; set; }      // 兜底文本搜索 (仅 OCR 层使用)
    public bool Force { get; set; }                 // 强制执行危险操作
}
