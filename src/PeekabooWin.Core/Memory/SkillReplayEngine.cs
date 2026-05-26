using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.Safety;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Core.Memory;

public class SkillReplayEngine
{
    private readonly WindowService _windowService;
    private readonly CaptureService _captureService;
    private readonly OcrService _ocrService;
    private readonly InputService _inputService;
    private readonly UIAutomationService _uiaService;
    private readonly ActionRiskGate _riskGate;
    private readonly TempFileManager _tempFiles;

    public SkillReplayEngine(
        WindowService windowService,
        CaptureService captureService,
        OcrService ocrService,
        InputService inputService,
        UIAutomationService uiaService,
        ActionRiskGate riskGate,
        TempFileManager tempFiles)
    {
        _windowService = windowService;
        _captureService = captureService;
        _ocrService = ocrService;
        _inputService = inputService;
        _uiaService = uiaService;
        _riskGate = riskGate;
        _tempFiles = tempFiles;
    }

    public async Task<SkillReplayReport> ReplayAsync(VisualSkill skill, string? windowTitle, bool dryRun)
    {
        var report = new SkillReplayReport
        {
            SkillId = skill.SkillId,
            SkillName = skill.Name,
            DryRun = dryRun,
            StepsTotal = skill.ProcedureSteps.Count
        };

        if (!string.IsNullOrEmpty(windowTitle))
        {
            var win = _windowService.FindWindow(windowTitle);
            if (win == null)
            {
                PekaLogger.Warn("SkillReplayEngine", $"Window not found: {windowTitle}");
                report.StepRecords.Add(new StepReplayRecord
                {
                    StepIndex = 0,
                    StepDescription = "focus-window",
                    ParsedAction = "focus",
                    Target = windowTitle,
                    Executed = false,
                    Success = false,
                    Error = $"Window not found: {windowTitle}"
                });
                return report;
            }
            _windowService.FocusWindow(windowTitle);
            Thread.Sleep(300);
        }

        for (int i = 0; i < skill.ProcedureSteps.Count; i++)
        {
            var step = skill.ProcedureSteps[i];
            var record = new StepReplayRecord
            {
                StepIndex = i,
                StepDescription = step
            };

            var (action, target) = ParseStep(step);
            record.ParsedAction = action;
            record.Target = target;

            var beforePath = _tempFiles.CreateTempPath($"replay_{i}_before");
            _captureService.CaptureScreen(beforePath);
            record.BeforeScreenshot = beforePath;

            var riskContext = new ActionRiskContext
            {
                ActionType = action,
                TargetLabel = target,
                PageType = skill.ScreenType
            };
            var riskDecision = _riskGate.Evaluate(riskContext);
            record.RiskScore = riskDecision.RiskScore;

            if (riskDecision.Decision == RiskLevel.Block)
            {
                record.RiskBlocked = true;
                record.Executed = false;
                record.Error = riskDecision.BlockReason;
                report.StepsBlocked++;
                PekaLogger.Warn("SkillReplayEngine", $"Step {i} blocked: {riskDecision.BlockReason}");
                report.StepRecords.Add(record);
                continue;
            }

            if (dryRun)
            {
                record.DryRunSkipped = true;
                record.Executed = false;
                record.Success = true;
                PekaLogger.Info("SkillReplayEngine", $"Step {i} dry-run skipped: {step}");
                report.StepRecords.Add(record);
                continue;
            }

            try
            {
                var success = await ExecuteStepAsync(action, target, windowTitle);
                record.Executed = true;
                record.Success = success;
                if (success) report.StepsExecuted++;
                else record.Error = "Execution returned failure";
            }
            catch (Exception ex)
            {
                record.Executed = true;
                record.Success = false;
                record.Error = ex.Message;
                PekaLogger.Error("SkillReplayEngine", $"Step {i} execution failed", ex);
            }

            var afterPath = _tempFiles.CreateTempPath($"replay_{i}_after");
            _captureService.CaptureScreen(afterPath);
            record.AfterScreenshot = afterPath;

            Thread.Sleep(200);

            report.StepRecords.Add(record);
        }

        report.VerificationPassed = report.StepRecords.All(r => r.Success || r.DryRunSkipped);
        skill.RecordUsage(report.VerificationPassed);

        PekaLogger.Info("SkillReplayEngine",
            $"Replay completed: skill={skill.SkillId}, steps={report.StepsTotal}, " +
            $"executed={report.StepsExecuted}, blocked={report.StepsBlocked}, " +
            $"verified={report.VerificationPassed}");

        return report;
    }

    private static (string action, string? target) ParseStep(string step)
    {
        if (step.StartsWith("click_", StringComparison.OrdinalIgnoreCase))
            return ("click", step[6..]);
        if (step.StartsWith("type_", StringComparison.OrdinalIgnoreCase))
            return ("type", step[5..]);
        if (step.StartsWith("press_", StringComparison.OrdinalIgnoreCase))
            return ("press", step[6..]);
        if (step.StartsWith("confirm_", StringComparison.OrdinalIgnoreCase))
            return ("click", step[8..]);
        if (step.StartsWith("cancel_", StringComparison.OrdinalIgnoreCase))
            return ("click", step[7..]);
        if (step.StartsWith("hotkey_", StringComparison.OrdinalIgnoreCase))
            return ("hotkey", step[7..]);
        return ("click", step);
    }

    private async Task<bool> ExecuteStepAsync(string action, string? target, string? windowTitle)
    {
        switch (action)
        {
            case "click":
                return await ExecuteClickAsync(target, windowTitle);
            case "type":
                return ExecuteType(target);
            case "press":
                return ExecutePress(target);
            case "hotkey":
                return ExecuteHotkey(target);
            default:
                PekaLogger.Warn("SkillReplayEngine", $"Unknown action: {action}");
                return false;
        }
    }

    private async Task<bool> ExecuteClickAsync(string? target, string? windowTitle)
    {
        if (string.IsNullOrEmpty(target)) return false;

        if (!string.IsNullOrEmpty(windowTitle))
        {
            var findResult = _uiaService.FindByName(windowTitle, target);
            if (findResult.Success && findResult.Count > 0)
            {
                var el = findResult.Matches[0];
                if (el.BoundingBox != null)
                {
                    var cx = (int)(el.BoundingBox.X + el.BoundingBox.Width / 2);
                    var cy = (int)(el.BoundingBox.Y + el.BoundingBox.Height / 2);
                    var r = _inputService.Click(cx, cy);
                    return r.Success;
                }
            }
        }

        var outPath = _tempFiles.CreateTempPath("replay_click");
        _captureService.CaptureScreen(outPath);
        var ocrResult = await _ocrService.RecognizeImageAsync(outPath);
        var center = _ocrService.FindWordCenter(ocrResult, target);
        _tempFiles.CleanupFile(outPath);

        if (center != null)
        {
            var r = _inputService.Click(center.Value.x, center.Value.y);
            return r.Success;
        }

        PekaLogger.Warn("SkillReplayEngine", $"Click target not found: {target}");
        return false;
    }

    private bool ExecuteType(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var r = _inputService.TypeText(text);
        return r.Success;
    }

    private bool ExecutePress(string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        var r = _inputService.PressKeyByName(key.ToLower());
        return r.Success;
    }

    private bool ExecuteHotkey(string? hotkey)
    {
        if (string.IsNullOrEmpty(hotkey)) return false;
        var r = _inputService.Hotkey(hotkey);
        return r.Success;
    }
}
