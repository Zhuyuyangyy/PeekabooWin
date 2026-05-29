using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.UIAutomation;

namespace PeekabooWin.Core.Verification;

public enum VerificationStatus
{
    Passed,
    Failed,
    Inconclusive
}

public class VerificationResult
{
    public VerificationStatus Status { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public double Confidence { get; set; }
    public string? BeforeScreenshot { get; set; }
    public string? AfterScreenshot { get; set; }
    public string? BeforeText { get; set; }
    public string? AfterText { get; set; }
    public int BeforeElementCount { get; set; }
    public int AfterElementCount { get; set; }
}

public class VerificationRequest
{
    public string Action { get; set; } = "";
    public Dictionary<string, string>? Args { get; set; }
    public string? BeforeScreenshotPath { get; set; }
    public string? BeforeOcrText { get; set; }
    public int? BeforeElementCount { get; set; }
}

public class ActionVerifier
{
    private readonly CaptureService _captureService;
    private readonly OcrService _ocrService;
    private readonly UIAutomationService _uiaService;
    private readonly TempFileManager _tempFiles;

    public ActionVerifier(CaptureService captureService, OcrService ocrService, UIAutomationService uiaService, TempFileManager tempFiles)
    {
        _captureService = captureService;
        _ocrService = ocrService;
        _uiaService = uiaService;
        _tempFiles = tempFiles;
    }

    public async Task<VerificationResult> VerifyAsync(VerificationRequest request, CancellationToken cancellationToken = default)
    {
        var afterScreenshotPath = _tempFiles.CreateTempPath("verify_after");
        var captureResult = _captureService.CaptureScreen(afterScreenshotPath);

        string afterOcrText = "";
        int afterElementCount = 0;

        if (captureResult.Success)
        {
            var ocrResult = await _ocrService.RecognizeImageAsync(afterScreenshotPath);
            if (ocrResult.Success)
            {
                afterOcrText = ocrResult.Text;
            }
        }

        var windowKeyword = request.Args?.GetValueOrDefault("window") ?? request.Args?.GetValueOrDefault("title");
        if (!string.IsNullOrEmpty(windowKeyword))
        {
            try
            {
                var inspectResult = _uiaService.Inspect(windowKeyword);
                if (inspectResult.Success)
                {
                    afterElementCount = inspectResult.ElementCount;
                }
            }
            catch { }
        }

        var beforeText = request.BeforeOcrText ?? "";
        var beforeElementCount = request.BeforeElementCount ?? 0;

        return request.Action.ToLower() switch
        {
            "type" => VerifyType(request, beforeText, afterOcrText, beforeElementCount, afterElementCount, request.BeforeScreenshotPath, afterScreenshotPath),
            "click" => VerifyStateChange("click", beforeText, afterOcrText, beforeElementCount, afterElementCount, request.BeforeScreenshotPath, afterScreenshotPath),
            "hotkey" => VerifyStateChange("hotkey", beforeText, afterOcrText, beforeElementCount, afterElementCount, request.BeforeScreenshotPath, afterScreenshotPath),
            "press" => VerifyStateChange("press", beforeText, afterOcrText, beforeElementCount, afterElementCount, request.BeforeScreenshotPath, afterScreenshotPath),
            "focus-window" => BuildResult(VerificationStatus.Passed, "focus-window", "Focus-window action always considered passed", 1.0, request.BeforeScreenshotPath, afterScreenshotPath, beforeText, afterOcrText, beforeElementCount, afterElementCount),
            "screenshot" => BuildResult(VerificationStatus.Passed, "screenshot", "Screenshot action always considered passed", 1.0, request.BeforeScreenshotPath, afterScreenshotPath, beforeText, afterOcrText, beforeElementCount, afterElementCount),
            _ => BuildResult(VerificationStatus.Inconclusive, request.Action, $"Unknown action type '{request.Action}', cannot verify", 0.3, request.BeforeScreenshotPath, afterScreenshotPath, beforeText, afterOcrText, beforeElementCount, afterElementCount)
        };
    }

    private VerificationResult VerifyType(VerificationRequest request, string beforeText, string afterText, int beforeElementCount, int afterElementCount, string? beforeScreenshot, string afterScreenshot)
    {
        var typedText = request.Args?.GetValueOrDefault("text") ?? "";

        if (beforeText != afterText && afterText.Contains(typedText, StringComparison.OrdinalIgnoreCase))
        {
            return BuildResult(VerificationStatus.Passed, request.Action, $"Typed text '{typedText}' found in after-state OCR", 0.9, beforeScreenshot, afterScreenshot, beforeText, afterText, beforeElementCount, afterElementCount);
        }

        if (!afterText.Contains(typedText, StringComparison.OrdinalIgnoreCase))
        {
            return BuildResult(VerificationStatus.Failed, request.Action, $"Typed text '{typedText}' not found in after-state OCR", 0.8, beforeScreenshot, afterScreenshot, beforeText, afterText, beforeElementCount, afterElementCount);
        }

        return BuildResult(VerificationStatus.Inconclusive, request.Action, "OCR text unchanged after type action", 0.5, beforeScreenshot, afterScreenshot, beforeText, afterText, beforeElementCount, afterElementCount);
    }

    private VerificationResult VerifyStateChange(string action, string beforeText, string afterText, int beforeElementCount, int afterElementCount, string? beforeScreenshot, string afterScreenshot)
    {
        bool textChanged = beforeText != afterText;
        bool elementCountChanged = beforeElementCount != afterElementCount;

        if (textChanged || elementCountChanged)
        {
            double confidence = textChanged && elementCountChanged ? 0.9 : 0.7;
            return BuildResult(VerificationStatus.Passed, action, $"State changed after {action}: text changed={textChanged}, element count changed={elementCountChanged}", confidence, beforeScreenshot, afterScreenshot, beforeText, afterText, beforeElementCount, afterElementCount);
        }

        return BuildResult(VerificationStatus.Inconclusive, action, $"No visible state change after {action} (action may not have visible effect)", 0.4, beforeScreenshot, afterScreenshot, beforeText, afterText, beforeElementCount, afterElementCount);
    }

    private static VerificationResult BuildResult(VerificationStatus status, string action, string reason, double confidence, string? beforeScreenshot, string afterScreenshot, string beforeText, string afterText, int beforeElementCount, int afterElementCount)
    {
        return new VerificationResult
        {
            Status = status,
            Action = action,
            Reason = reason,
            Confidence = confidence,
            BeforeScreenshot = beforeScreenshot,
            AfterScreenshot = afterScreenshot,
            BeforeText = beforeText,
            AfterText = afterText,
            BeforeElementCount = beforeElementCount,
            AfterElementCount = afterElementCount
        };
    }
}
