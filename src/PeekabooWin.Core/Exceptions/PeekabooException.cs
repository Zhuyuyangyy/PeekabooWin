namespace PeekabooWin.Core.Exceptions;

public abstract class PeekabooException : Exception
{
    public string ErrorCode { get; }
    public string? Hint { get; }

    protected PeekabooException(string errorCode, string message, string? hint = null)
        : base(message)
    {
        ErrorCode = errorCode;
        Hint = hint;
    }

    protected PeekabooException(string errorCode, string message, Exception inner, string? hint = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        Hint = hint;
    }
}

public class WindowNotFoundException : PeekabooException
{
    public WindowNotFoundException(string keyword)
        : base("WINDOW_NOT_FOUND", $"No window matched keyword: {keyword}", "Try list-windows first") { }
}

public class ElementNotFoundException : PeekabooException
{
    public ElementNotFoundException(string element)
        : base("ELEMENT_NOT_FOUND", $"Element not found: {element}", "Try inspect to browse the UI tree") { }
}

public class OcrUnavailableException : PeekabooException
{
    public OcrUnavailableException(string reason)
        : base("OCR_UNAVAILABLE", $"OCR unavailable: {reason}", "Check Windows.Media.Ocr language packs") { }
}

public class CaptureFailedException : PeekabooException
{
    public CaptureFailedException(string reason)
        : base("CAPTURE_FAILED", $"Screen capture failed: {reason}", "Check if the window is visible and not minimized") { }
}

public class RiskBlockedException : PeekabooException
{
    public double RiskScore { get; }
    public string BlockReason { get; }

    public RiskBlockedException(double riskScore, string blockReason)
        : base("RISK_BLOCKED", $"Action blocked by risk gate (score={riskScore:F2}): {blockReason}", "Use --force to override (not recommended)")
    {
        RiskScore = riskScore;
        BlockReason = blockReason;
    }
}

public class SkillReplayException : PeekabooException
{
    public SkillReplayException(string skillId, string reason)
        : base("SKILL_REPLAY_FAILED", $"Skill replay failed for '{skillId}': {reason}", "Try skill-search to find alternative skills") { }
}
