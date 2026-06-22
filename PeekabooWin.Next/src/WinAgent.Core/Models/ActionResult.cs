namespace WinAgent.Core.Models;

/// <summary>
/// Act 输入 — LLM 只能通过 element_id 发起操作
/// </summary>
public class ActionRequest
{
    public ActionType Type { get; set; }
    public string TargetId { get; set; } = "";     // 元素 ID (click/type/hover)
    public string? Text { get; set; }              // type 操作的文本
    public string? Keys { get; set; }              // hotkey 操作的组合键
    public bool DryRun { get; set; }               // 默认 dry-run，--force 才真执行
}

/// <summary>
/// Act 输出 — 操作执行结果
/// </summary>
public class ActionResult
{
    public bool Success { get; set; }
    public ActionType Type { get; set; }
    public string TargetId { get; set; } = "";
    public string? Description { get; set; }
    public string? Error { get; set; }
    public bool WasDryRun { get; set; }
    public bool WasBlocked { get; set; }           // 被安全门控拦截
    public string? BlockReason { get; set; }
    public VerificationResult? Verification { get; set; }
}

/// <summary>
/// 操作类型
/// </summary>
public enum ActionType
{
    Click,
    RightClick,
    DoubleClick,
    Type,
    Hotkey,
    Scroll,
    Hover,
    Focus
}
