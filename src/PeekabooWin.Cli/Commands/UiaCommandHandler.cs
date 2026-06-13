using System.Text;
using System.Text.Json;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.UIAutomation;

using PeekabooWin.Core.Windows;

namespace PeekabooWin.Cli.Commands;

public class UiaCommandHandler : ICommandHandler
{
    private readonly UIAutomationService _uiaService;
    private readonly InputService _inputService;
    private readonly CaptureService _captureService;
    private readonly OcrService _ocrService;
    private readonly WindowService _windowService;

    public string CommandName => "uia";

    public UiaCommandHandler(UIAutomationService uiaService, InputService inputService,
        CaptureService captureService, OcrService ocrService, WindowService windowService)
    {
        _uiaService = uiaService;
        _inputService = inputService;
        _captureService = captureService;
        _ocrService = ocrService;
        _windowService = windowService;
    }

    public async Task<int> ExecuteAsync(string[] args)
    {
        var command = args[0].ToLower();
        return command switch
        {
            "inspect" => HandleInspect(args),
            "find" => HandleFind(args),
            "click-element" => await HandleClickElementAsync(args),
            "find-by-control-type" => HandleFindByControlType(args),
            _ => 1
        };
    }

    private int HandleInspect(string[] args)
    {
        string? window = CliHelpers.GetFlag(args, "--window", "-w")
            ?? CliHelpers.GetFlag(args, "--title", "-t");

        if (string.IsNullOrEmpty(window))
        {
            CliHelpers.PrintError("inspect", "Missing --window flag");
            return 1;
        }

        string? depthStr = CliHelpers.GetFlag(args, "--max-depth", "-d");
        int depth = 10;
        if (!string.IsNullOrEmpty(depthStr) && int.TryParse(depthStr, out int d))
            depth = d;

        string? jsonOut = CliHelpers.GetFlag(args, "--json-out", "-j");

        var result = _uiaService.Inspect(window, depth);

        if (!string.IsNullOrEmpty(jsonOut))
        {
            var dir = Path.GetDirectoryName(jsonOut);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(jsonOut, JsonSerializer.Serialize(result, CliHelpers.JsonOptions), Encoding.UTF8);
        }

        var cmdResult = CommandResult.Ok("inspect", result);
        CliHelpers.PrintJson(cmdResult);
        return result.Success ? 0 : 1;
    }

    private int HandleFind(string[] args)
    {
        string? window = CliHelpers.GetFlag(args, "--window", "-w")
            ?? CliHelpers.GetFlag(args, "--title", "-t");

        if (string.IsNullOrEmpty(window))
        {
            CliHelpers.PrintError("find", "Missing --window flag");
            return 1;
        }

        string? name = CliHelpers.GetFlag(args, "--name", "-n");
        string? role = CliHelpers.GetFlag(args, "--control-type", "-r")
            ?? CliHelpers.GetFlag(args, "--role", "-r");
        string? aid = CliHelpers.GetFlag(args, "--automation-id", "-a");

        UIAFindResult result;

        if (!string.IsNullOrEmpty(name))
            result = _uiaService.FindByName(window, name);
        else if (!string.IsNullOrEmpty(role))
            result = _uiaService.FindByControlType(window, role);
        else if (!string.IsNullOrEmpty(aid))
            result = _uiaService.FindByAutomationId(window, aid);
        else
        {
            CliHelpers.PrintError("find", "Must specify --name, --control-type, or --automation-id");
            return 1;
        }

        var cmdResult = CommandResult.Ok("find", result);
        CliHelpers.PrintJson(cmdResult);
        return result.Success ? 0 : 1;
    }

    /// <summary>
    /// click-element: Try UIA first, fall back to OCR when UIA cannot find the element.
    /// This is critical for Chromium/Electron/UWP apps where UIA returns empty frames.
    /// </summary>
    private async Task<int> HandleClickElementAsync(string[] args)
    {
        string? window = CliHelpers.GetFlag(args, "--window", "-w")
            ?? CliHelpers.GetFlag(args, "--title", "-t");

        string? name = CliHelpers.GetFlag(args, "--name", "-n")
            ?? CliHelpers.GetFlag(args, "--text", "-t");

        string? controlType = CliHelpers.GetFlag(args, "--control-type", "-r");

        bool dryRun = CliHelpers.HasFlag(args, "--dry-run", "-d");

        if (string.IsNullOrEmpty(window))
        {
            CliHelpers.PrintError("click-element", "Missing --window flag");
            return 1;
        }

        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(controlType))
        {
            CliHelpers.PrintError("click-element", "Missing --name or --control-type flag");
            return 1;
        }

        // --- Phase 1: Try UIA ---
        UIAFindResult findResult;
        if (!string.IsNullOrEmpty(name))
            findResult = _uiaService.FindByName(window, name, recursive: true);
        else
            findResult = _uiaService.FindByControlType(window, controlType!);

        if (findResult.Success && findResult.Count > 0)
        {
            var target = findResult.Matches[0];

            var dryRunInfo = new
            {
                method = "uia",
                target_element = new
                {
                    name = target.Name,
                    automation_id = target.AutomationId,
                    control_type = target.ControlType,
                    bounding_box = target.BoundingBox,
                    click_point = target.BoundingBox != null
                        ? new { x = target.BoundingBox.X + target.BoundingBox.Width / 2, y = target.BoundingBox.Y + target.BoundingBox.Height / 2 }
                        : null
                }
            };

            if (dryRun)
            {
                var r = CommandResult.Ok("click-element (dry-run)", dryRunInfo);
                CliHelpers.PrintJson(r);
                return 0;
            }

            if (!string.IsNullOrEmpty(name))
            {
                var clickResult = _uiaService.ClickElementByName(window, name);
                CliHelpers.PrintJson(clickResult);
                return clickResult.Success ? 0 : 1;
            }
            else
            {
                var el = findResult.Matches[0];
                var rect = el.BoundingBox;
                if (rect != null)
                {
                    var cx = rect.X + rect.Width / 2;
                    var cy = rect.Y + rect.Height / 2;
                    var cr = _inputService.Click(cx, cy);
                    CliHelpers.PrintJson(cr);
                    return cr.Success ? 0 : 1;
                }
                var r2 = CommandResult.Fail("click-element", "Element has no bounding box");
                CliHelpers.PrintJson(r2);
                return 1;
            }
        }

        // --- Phase 2: OCR fallback (when UIA cannot find the element) ---
        // This handles Chromium, Electron, UWP and other apps where UIA returns empty frames.
        if (!string.IsNullOrEmpty(name))
        {
            PekaLogger.Info("click-element", $"UIA failed for '{name}' in '{window}', falling back to OCR");

            var tempPath = Path.Combine(Path.GetTempPath(), $"click_element_{Guid.NewGuid():N}.png");
            try
            {
                var cap = _captureService.CaptureWindow(window, tempPath);
                if (!cap.Success)
                {
                    var r = CommandResult.Fail("click-element",
                        $"Element not found via UIA and screenshot failed: {cap.Error}");
                    CliHelpers.PrintJson(r);
                    return 1;
                }

                var ocrResult = await _ocrService.RecognizeImageAsync(tempPath);
                if (!string.IsNullOrEmpty(ocrResult.Error))
                {
                    var r = CommandResult.Fail("click-element",
                        $"Element not found via UIA and OCR failed: {ocrResult.Error}");
                    CliHelpers.PrintJson(r);
                    return 1;
                }

                var center = _ocrService.FindWordCenter(ocrResult, name);
                if (center == null)
                {
                    var r = CommandResult.Fail("click-element",
                        $"Element not found via UIA or OCR in window: {window}");
                    CliHelpers.PrintJson(r);
                    return 1;
                }

                // Convert screenshot-relative coords to physical screen coords
                int screenX = center.Value.x;
                int screenY = center.Value.y;
                var win = _windowService.FindWindow(window);
                if (win != null)
                {
                    double scale = DpiContext.Default.GetScaleFactor(win.HandleIntPtr);
                    screenX += (int)Math.Round(win.Rect.X * scale);
                    screenY += (int)Math.Round(win.Rect.Y * scale);
                }

                var ocrDryRunInfo = new
                {
                    method = "ocr-fallback",
                    text = name,
                    screenshot_x = center.Value.x,
                    screenshot_y = center.Value.y,
                    screen_x = screenX,
                    screen_y = screenY
                };

                if (dryRun)
                {
                    var r = CommandResult.Ok("click-element (dry-run, ocr-fallback)", ocrDryRunInfo);
                    CliHelpers.PrintJson(r);
                    return 0;
                }

                var clickResult = _inputService.Click(screenX, screenY);
                var r3 = CommandResult.Ok("click-element (ocr-fallback)", new
                {
                    method = "ocr-fallback",
                    text = name,
                    clicked_x = screenX,
                    clicked_y = screenY,
                    click_result = clickResult
                });
                CliHelpers.PrintJson(r3);
                return clickResult.Success ? 0 : 1;
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        // No name to search via OCR, and UIA already failed
        {
            var r = CommandResult.Fail("click-element", $"Element not found in window: {window}");
            CliHelpers.PrintJson(r);
            return 1;
        }
    }

    private int HandleFindByControlType(string[] args)
    {
        string? window = CliHelpers.GetFlag(args, "--window", "-w")
            ?? CliHelpers.GetFlag(args, "--title", "-t");

        string? controlType = CliHelpers.GetFlag(args, "--control-type", "-r")
            ?? CliHelpers.GetFlag(args, "--type", "-t");

        if (string.IsNullOrEmpty(window) || string.IsNullOrEmpty(controlType))
        {
            CliHelpers.PrintError("find-by-control-type", "Missing --window or --control-type flag");
            return 1;
        }

        var result = _uiaService.FindByControlType(window, controlType);
        var cmdResult = CommandResult.Ok("find-by-control-type", result);
        CliHelpers.PrintJson(cmdResult);
        return result.Success ? 0 : 1;
    }
}
