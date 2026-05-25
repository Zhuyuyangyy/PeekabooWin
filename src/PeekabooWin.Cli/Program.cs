using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PeekabooWin.Core.Agent;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Cli;

class Program
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    static int Main(string[] args)
    {
        // Set UTF-8 encoding for console
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var windowService = new WindowService();
        var captureService = new CaptureService(windowService);
        var inputService = new InputService();
        var uiaService = new UIAutomationService(windowService);
        var ocrService = new OcrService();

        string command = args[0].ToLower();

        try
        {
            switch (command)
            {
                case "list-windows":
                    return HandleListWindows(args, windowService);

                case "focus-window":
                    return HandleFocusWindow(args, windowService);

                case "screenshot":
                    return HandleScreenshot(args, captureService);

                case "click":
                    return HandleClick(args, inputService);

                case "type":
                    return HandleType(args, inputService);

                case "press":
                    return HandlePress(args, inputService);

                case "hotkey":
                    return HandleHotkey(args, inputService);

                case "window-info":
                    return HandleWindowInfo(args, windowService);

                // V0.2 UIA commands
                case "inspect":
                    return HandleInspect(args, uiaService);

                case "find":
                    return HandleFind(args, uiaService);

                case "click-element":
                    return HandleClickElement(args, uiaService, inputService);

                case "find-by-control-type":
                    return HandleFindByControlType(args, uiaService);

                case "ocr":
                    return HandleOcr(args, captureService, windowService);

                case "click-rel":
                    return HandleClickRel(args, windowService);

                case "is-focused":
                    return HandleIsFocused(args, windowService);

                case "find-on-screen":
                    return HandleFindOnScreen(args, captureService, windowService, ocrService);

                case "ocr-click":
                    return HandleOcrClick(args, captureService, windowService, ocrService);

                case "skill-list":
                    return HandleSkillList(args);

                case "skill-replay":
                    return HandleSkillReplay(args, windowService, captureService, inputService);

                case "skill-seed":
                    return HandleSkillSeed(args);

                case "skill-search":
                    return HandleSkillSearch(args);

                case "skill-search-context":
                    return HandleSkillSearchContext(args);

                case "skill-use-preview":
                    return HandleSkillUsePreview(args);

                case "skill-execute-guided":
                    return HandleSkillExecuteGuided(args, windowService, captureService, inputService, uiaService, ocrService);

                case "agent":
                    return HandleAgent(args, windowService, captureService, inputService, uiaService, ocrService);

                case "server":
                    return HandleServer(args);

                case "--help" or "-h" or "help":
                    PrintUsage();
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            PrintError(command, ex.Message);
            return 1;
        }
    }

    // ==================== V0.1 Handlers ====================

    static int HandleListWindows(string[] args, WindowService svc)
    {
        string? keyword = GetFlag(args, "--keyword", "-k");

        var windows = svc.ListWindows(keyword);
        var result = CommandResult.Ok("list-windows", new { windows });
        PrintJson(result);
        return 0;
    }

    static int HandleFocusWindow(string[] args, WindowService svc)
    {
        string? keyword = GetFlag(args, "--window", "-w")
            ?? GetFlag(args, "--title", "-t");

        if (string.IsNullOrEmpty(keyword))
        {
            PrintError("focus-window", "Missing --window flag");
            return 1;
        }

        var ok = svc.FocusWindow(keyword);
        var result = CommandResult.Ok("focus-window", new { success = ok, focused = keyword });
        PrintJson(result);
        return ok ? 0 : 1;
    }

    static int HandleScreenshot(string[] args, CaptureService svc)
    {
        string? outPath = GetFlag(args, "--out", "-o");
        string? window = GetFlag(args, "--window", "-w");
        bool screen = HasFlag(args, "--screen", "-s");

        if (string.IsNullOrEmpty(outPath))
        {
            outPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"peekaboo_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        }

        // Ensure output directory exists
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        CaptureResult capResult;
        if (screen || string.IsNullOrEmpty(window))
        {
            capResult = svc.CaptureScreen(outPath);
        }
        else
        {
            capResult = svc.CaptureWindow(window, outPath);
        }

        var result = CommandResult.Ok("screenshot", capResult);
        PrintJson(result);
        return capResult.Success ? 0 : 1;
    }

    static int HandleClick(string[] args, InputService svc)
    {
        string? xStr = GetFlag(args, "--x", "-x");
        string? yStr = GetFlag(args, "--y", "-y");

        if (!string.IsNullOrEmpty(xStr) && !string.IsNullOrEmpty(yStr))
        {
            if (int.TryParse(xStr, out int x) && int.TryParse(yStr, out int y))
            {
                var r = svc.Click(x, y);
                PrintJson(r);
                return r.Success ? 0 : 1;
            }
        }

        var r2 = svc.ClickCurrent();
        PrintJson(r2);
        return r2.Success ? 0 : 1;
    }

    static int HandleType(string[] args, InputService svc)
    {
        string? text = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
            {
                text = args[i];
                break;
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            PrintError("type", "Missing text to type");
            return 1;
        }

        var r = svc.TypeText(text);
        PrintJson(r);
        return r.Success ? 0 : 1;
    }

    static int HandlePress(string[] args, InputService svc)
    {
        string? key = GetFlag(args, "--key", "-k")
            ?? GetFlag(args, "--keys", "-k");

        if (string.IsNullOrEmpty(key))
        {
            PrintError("press", "Missing --key flag. Supported: esc, enter, tab, backspace, delete");
            return 1;
        }

        var r = svc.PressKeyByName(key.ToLower());
        PrintJson(r);
        return r.Success ? 0 : 1;
    }

    static int HandleHotkey(string[] args, InputService svc)
    {
        string? hotkey = GetFlag(args, "--keys", "-k")
            ?? GetFlag(args, "--hotkey", "-h");

        if (string.IsNullOrEmpty(hotkey))
        {
            PrintError("hotkey", "Missing --keys flag");
            return 1;
        }

        var r = svc.Hotkey(hotkey);
        PrintJson(r);
        return r.Success ? 0 : 1;
    }

    static int HandleWindowInfo(string[] args, WindowService svc)
    {
        var windows = svc.ListWindows();
        var result = CommandResult.Ok("window-info", new { count = windows.Count, windows });
        PrintJson(result);
        return 0;
    }

    // ==================== V0.2 UIA Handlers ====================

    static int HandleInspect(string[] args, UIAutomationService svc)
    {
        string? window = GetFlag(args, "--window", "-w")
            ?? GetFlag(args, "--title", "-t");

        if (string.IsNullOrEmpty(window))
        {
            PrintError("inspect", "Missing --window flag");
            return 1;
        }

        string? depthStr = GetFlag(args, "--max-depth", "-d");
        int depth = 10;
        if (!string.IsNullOrEmpty(depthStr) && int.TryParse(depthStr, out int d))
            depth = d;

        string? jsonOut = GetFlag(args, "--json-out", "-j");

        var result = svc.Inspect(window, depth);

        if (!string.IsNullOrEmpty(jsonOut))
        {
            var dir = Path.GetDirectoryName(jsonOut);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(jsonOut, JsonSerializer.Serialize(result, JsonOptions), Encoding.UTF8);
        }

        var cmdResult = CommandResult.Ok("inspect", result);
        PrintJson(cmdResult);
        return result.Success ? 0 : 1;
    }

    static int HandleFind(string[] args, UIAutomationService svc)
    {
        string? window = GetFlag(args, "--window", "-w")
            ?? GetFlag(args, "--title", "-t");

        if (string.IsNullOrEmpty(window))
        {
            PrintError("find", "Missing --window flag");
            return 1;
        }

        string? name = GetFlag(args, "--name", "-n");
        string? role = GetFlag(args, "--control-type", "-r")
            ?? GetFlag(args, "--role", "-r");
        string? aid = GetFlag(args, "--automation-id", "-a");

        UIAFindResult result;

        if (!string.IsNullOrEmpty(name))
        {
            result = svc.FindByName(window, name);
        }
        else if (!string.IsNullOrEmpty(role))
        {
            result = svc.FindByControlType(window, role);
        }
        else if (!string.IsNullOrEmpty(aid))
        {
            result = svc.FindByAutomationId(window, aid);
        }
        else
        {
            PrintError("find", "Must specify --name, --control-type, or --automation-id");
            return 1;
        }

        var cmdResult = CommandResult.Ok("find", result);
        PrintJson(cmdResult);
        return result.Success ? 0 : 1;
    }

    static int HandleFindByControlType(string[] args, UIAutomationService svc)
    {
        string? window = GetFlag(args, "--window", "-w")
            ?? GetFlag(args, "--title", "-t");

        string? controlType = GetFlag(args, "--control-type", "-r")
            ?? GetFlag(args, "--type", "-t");

        if (string.IsNullOrEmpty(window) || string.IsNullOrEmpty(controlType))
        {
            PrintError("find-by-control-type", "Missing --window or --control-type flag");
            return 1;
        }

        var result = svc.FindByControlType(window, controlType);
        var cmdResult = CommandResult.Ok("find-by-control-type", result);
        PrintJson(cmdResult);
        return result.Success ? 0 : 1;
    }

    static int HandleClickElement(string[] args, UIAutomationService svc, InputService inputService)
    {
        string? window = GetFlag(args, "--window", "-w")
            ?? GetFlag(args, "--title", "-t");

        string? name = GetFlag(args, "--name", "-n")
            ?? GetFlag(args, "--text", "-t");

        string? controlType = GetFlag(args, "--control-type", "-r");

        bool dryRun = HasFlag(args, "--dry-run", "-d");

        if (string.IsNullOrEmpty(window))
        {
            PrintError("click-element", "Missing --window flag");
            return 1;
        }

        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(controlType))
        {
            PrintError("click-element", "Missing --name or --control-type flag");
            return 1;
        }

        UIAFindResult findResult;
        if (!string.IsNullOrEmpty(name))
        {
            findResult = svc.FindByName(window, name, recursive: true);
        }
        else
        {
            findResult = svc.FindByControlType(window, controlType!);
        }

        if (!findResult.Success || findResult.Count == 0)
        {
            var r = CommandResult.Fail("click-element", $"Element not found in window: {window}");
            PrintJson(r);
            return 1;
        }

        // Take first match
        var target = findResult.Matches[0];

        var dryRunInfo = new
        {
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
            PrintJson(r);
            return 0;
        }

        // Click based on how we found the element
        if (!string.IsNullOrEmpty(name))
        {
            // Click by name - use the service method
            var clickResult = svc.ClickElementByName(window, name);
            PrintJson(clickResult);
            return clickResult.Success ? 0 : 1;
        }
        else
        {
            // Click by control type - click the center of the first match bounding box
            var el = findResult.Matches[0];
            var rect = el.BoundingBox;
            if (rect != null)
            {
                var cx = rect.X + rect.Width / 2;
                var cy = rect.Y + rect.Height / 2;
                var cr = inputService.Click(cx, cy);
                PrintJson(cr);
                return cr.Success ? 0 : 1;
            }
            var r2 = CommandResult.Fail("click-element", "Element has no bounding box");
            PrintJson(r2);
            return 1;
        }
    }

    // ==================== V0.2 UIA Handlers ====================

    static int HandleOcr(string[] args, CaptureService captureService, WindowService windowService)
    {
        string? sub = args.Length > 1 ? args[1].ToLower() : null;
        string? outPath = GetFlag(args, "--out", "-o");
        string? window = GetFlag(args, "--window", "-w");
        string? text = GetFlag(args, "--text", "-t");
        bool screen = HasFlag(args, "--screen", "-s");
        bool click = HasFlag(args, "--click", "-c");
        string? lang = GetFlag(args, "--lang", "-l") ?? "chi_sim+eng";

        using var ocrService = new OcrService(lang);

        string imgPath;
        if (!string.IsNullOrEmpty(window))
        {
            // Capture specific window
            imgPath = outPath ?? Path.Combine(Path.GetTempPath(), $"ocr_window_{Guid.NewGuid():N}.png");
            var cap = captureService.CaptureWindow(window, imgPath);
            if (!cap.Success)
            {
                var r = CommandResult.Fail("ocr", $"Failed to capture window: {window}");
                PrintJson(r);
                return 1;
            }
        }
        else
        {
            // Capture full screen
            imgPath = outPath ?? Path.Combine(Path.GetTempPath(), $"ocr_screen_{Guid.NewGuid():N}.png");
            var cap = captureService.CaptureScreen(imgPath);
            if (!cap.Success)
            {
                var r = CommandResult.Fail("ocr", "Failed to capture screen");
                PrintJson(r);
                return 1;
            }
        }

        var ocrResult = ocrService.RecognizeImageAsync(imgPath).GetAwaiter().GetResult();

        if (!string.IsNullOrEmpty(text))
        {
            // Find specific text and optionally click
            var words = ocrService.FindWords(ocrResult, text);
            var center = ocrService.FindWordCenter(ocrResult, text);

            var findResult = new
            {
                search_query = text,
                recognized_text = ocrResult.Text,
                matches_count = words.Count,
                matches = words.Select(w => new { w.Text, w.BoundingBox }).ToList(),
                first_center = center.HasValue ? new { x = center.Value.x, y = center.Value.y } : null
            };

            var cmdResult = CommandResult.Ok("ocr find-text", findResult);
            PrintJson(cmdResult);

            if (click && center.HasValue && center.Value.x > 0 && center.Value.y > 0)
            {
                var inputService = new InputService();
                var clickResult = inputService.Click(center.Value.x, center.Value.y);
                Console.WriteLine(JsonSerializer.Serialize(CommandResult.Ok("ocr click", clickResult), JsonOptions));
                return 0;
            }

            return words.Count > 0 ? 0 : 1;
        }
        else
        {
            // Just recognize and return text
            var cmdResult = CommandResult.Ok("ocr", new
            {
                text = ocrResult.Text,
                words_count = ocrResult.Words.Count,
                confidence = ocrResult.Confidence,
                engine = ocrResult.Engine,
                language = ocrResult.Language,
                image = imgPath
            });
            PrintJson(cmdResult);
            return ocrResult.Words.Count > 0 ? 0 : 1;
        }
    }

    // ==================== V0.6 Enhanced Actions ====================
    // click-rel, is-focused, find-on-screen, ocr-click

    static int HandleClickRel(string[] args, WindowService windowService)
    {
        var window = GetFlag(args, "--window", "-w") ?? GetFlag(args, "--win", "-W");
        var xStr = GetFlag(args, "--x", "-x");
        var yStr = GetFlag(args, "--y", "-y");

        if (string.IsNullOrEmpty(window)) { PrintError("click-rel", "Missing --window"); return 1; }
        if (string.IsNullOrEmpty(xStr) || string.IsNullOrEmpty(yStr)) { PrintError("click-rel", "Missing --x or --y"); return 1; }
        if (!int.TryParse(xStr, out int relX) || !int.TryParse(yStr, out int relY)) { PrintError("click-rel", "--x and --y must be integers"); return 1; }

        var win = windowService.FindWindow(window);
        if (win == null) { PrintError("click-rel", $"Window not found: {window}"); return 1; }

        int absX = win.Rect.X + relX;
        int absY = win.Rect.Y + relY;
        var inputService = new InputService();
        var r = inputService.Click(absX, absY);
        var result = CommandResult.Ok("click-rel", new { abs_x = absX, abs_y = absY, rel_x = relX, rel_y = relY, window = win.Title, rect = win.Rect, success = r.Success, error = r.Error });
        PrintJson(result);
        return r.Success ? 0 : 1;
    }

    static int HandleIsFocused(string[] args, WindowService windowService)
    {
        var window = GetFlag(args, "--window", "-w") ?? "";
        var foregroundHwnd = GetForegroundWindow();
        var allWindows = windowService.ListWindows(null);
        var focusedWin = allWindows.FirstOrDefault(w => w.Handle == foregroundHwnd.ToInt64());

        if (focusedWin == null)
        {
            var result = CommandResult.Ok("is-focused", new { foreground_handle = foregroundHwnd.ToInt64(), tracked = false });
            PrintJson(result);
            return 0;
        }

        var isMatch = string.IsNullOrEmpty(window) || focusedWin.Title.Contains(window, StringComparison.OrdinalIgnoreCase);
        var r = CommandResult.Ok("is-focused", new { focused_window = focusedWin.Title, focused_pid = focusedWin.ProcessId, matches_query = isMatch, query = window });
        PrintJson(r);
        return isMatch ? 0 : 1;
    }

    static int HandleFindOnScreen(string[] args, CaptureService captureService, WindowService windowService, OcrService ocrService)
    {
        var window = GetFlag(args, "--window", "-w");
        var text = GetFlag(args, "--text", "-t");

        if (string.IsNullOrEmpty(text)) { PrintError("find-on-screen", "Missing --text"); return 1; }

        var outPath = Path.Combine(Path.GetTempPath(), $"fos_{Guid.NewGuid():N}.png");
        CaptureResult cap;
        if (!string.IsNullOrEmpty(window))
        {
            cap = captureService.CaptureWindow(window, outPath);
        }
        else
        {
            cap = captureService.CaptureScreen(outPath);
        }
        if (!cap.Success) { PrintError("find-on-screen", $"Screenshot failed: {cap.Error}"); return 1; }

        var ocrResult = ocrService.RecognizeImageAsync(outPath).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(ocrResult.Error)) { PrintError("find-on-screen", $"OCR error: {ocrResult.Error}"); return 1; }

        var center = ocrService.FindWordCenter(ocrResult, text);
        if (center == null)
        {
            var r = CommandResult.Ok("find-on-screen", new { found = false, text, recognized_snippet = ocrResult.Text.Length > 200 ? ocrResult.Text.Substring(0, 200) : ocrResult.Text });
            PrintJson(r);
            return 1;
        }

        int screenX = center.Value.x;
        int screenY = center.Value.y;
        if (!string.IsNullOrEmpty(window))
        {
            var win = windowService.FindWindow(window);
            if (win != null) { screenX += win.Rect.X; screenY += win.Rect.Y; }
        }

        try { File.Delete(outPath); } catch { }
        var result = CommandResult.Ok("find-on-screen", new { found = true, text, screen_x = screenX, screen_y = screenY, rel_x = center.Value.x, rel_y = center.Value.y });
        PrintJson(result);
        return 0;
    }

    static int HandleOcrClick(string[] args, CaptureService captureService, WindowService windowService, OcrService ocrService)
    {
        var window = GetFlag(args, "--window", "-w");
        var text = GetFlag(args, "--text", "-t");

        if (string.IsNullOrEmpty(text)) { PrintError("ocr-click", "Missing --text"); return 1; }

        var outPath = Path.Combine(Path.GetTempPath(), $"oc_{Guid.NewGuid():N}.png");
        CaptureResult cap;
        if (!string.IsNullOrEmpty(window))
        {
            cap = captureService.CaptureWindow(window, outPath);
        }
        else
        {
            cap = captureService.CaptureScreen(outPath);
        }
        if (!cap.Success) { PrintError("ocr-click", $"Screenshot failed: {cap.Error}"); return 1; }

        var ocrResult = ocrService.RecognizeImageAsync(outPath).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(ocrResult.Error)) { PrintError("ocr-click", $"OCR error: {ocrResult.Error}"); return 1; }

        var center = ocrService.FindWordCenter(ocrResult, text);
        if (center == null) { PrintError("ocr-click", $"Text '{text}' not found"); return 1; }

        int screenX = center.Value.x;
        int screenY = center.Value.y;
        if (!string.IsNullOrEmpty(window))
        {
            var win = windowService.FindWindow(window);
            if (win != null) { screenX += win.Rect.X; screenY += win.Rect.Y; }
        }

        var inputService = new InputService();
        inputService.Click(screenX, screenY);
        try { File.Delete(outPath); } catch { }

        var result = CommandResult.Ok("ocr-click", new { text, clicked_x = screenX, clicked_y = screenY, rel_x = center.Value.x, rel_y = center.Value.y });
        PrintJson(result);
        return 0;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // ==================== V0.4 Agent Handler ====================

    static int HandleAgent(string[] args, WindowService windowService, CaptureService captureService, InputService inputService, UIAutomationService uiaService, OcrService ocrService)
    {
        string? task = GetFlag(args, "--task", "-t");
        int maxSteps = int.TryParse(GetFlag(args, "--max-steps", "-m") ?? "5", out var ms) ? ms : 5;
        bool dryRun = HasFlag(args, "--dry-run", "-d");
        string? context = GetFlag(args, "--context", "-c");

        if (string.IsNullOrEmpty(task))
        {
            PrintError("agent", "Missing --task flag");
            return 1;
        }

        var agentService = new AgentService(windowService, captureService, inputService, uiaService, ocrService);
        var request = new AgentTaskRequest
        {
            Task = task,
            Context = context,
            MaxSteps = maxSteps,
            DryRun = dryRun
        };

        var result = agentService.ExecuteTask(request);
        var cmdResult = CommandResult.Ok("agent", result);
        PrintJson(cmdResult);
        return result.Success ? 0 : 1;
    }

    // ==================== V0.5 HTTP API Server ====================

    static int HandleServer(string[] args)
    {
        string? portStr = GetFlag(args, "--port", "-p");
        int port = int.TryParse(portStr, out var p) ? p : 8080;

        Console.WriteLine($"[PeekabooWin] Starting HTTP API server on port {port}...");
        var server = new ApiServer(port);
        server.Start();

        // Wait for Ctrl+C to stop
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            server.Stop();
        };

        Console.WriteLine("[PeekabooWin] API server running. Press Ctrl+C to stop.");
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    // ==================== Helpers ====================

    static void PrintUsage()
    {
        Console.WriteLine(@"
PeekabooWin - Windows Desktop Automation CLI (V0.7)

Usage: peekaboo-win <command> [options]

V0.6 - VACP Trusted Execution:
  click-rel --window K --x X --y Y  Click relative to window
  is-focused [--window K]          Check focus state
  find-on-screen TEXT              OCR find text on screen
  ocr-click --text TEXT            Find text and click

V0.7 - Visual Skill Memory:
  skill-list                       List extracted visual skills
  skill-replay --id ID [--window K]  Replay a saved skill
  skill-seed                       Seed demo skills (Notepad + Dialog)

V0.1 - Core:
  list-windows [--keyword K]    List all visible windows
  focus-window --window K       Focus window by title keyword
  screenshot --out PATH [--screen | --window K]
  click --x X --y Y            Click at coordinates
  type ""text""                 Type text
  press --key K                Press key (esc/enter/tab/backspace/delete)
  hotkey --keys CTRL+L         Execute hotkey
  window-info                  Show all windows detail

V0.2 - UIA Automation:
  inspect --window K [--max-depth N] [--json-out PATH]
  find --window K --name N
  find --window K --control-type TYPE  (button/edit/document/menu...)
  find --window K --automation-id ID
  click-element --window K --name N [--dry-run]
  find-by-control-type --window K --control-type TYPE

Examples:
  peekaboo-win list-windows --keyword 璁颁簨锟?  peekaboo-win inspect --window notepad --max-depth 3
  peekaboo-win click-element --window notepad --name 鏂囦欢 --dry-run
  peekaboo-win press --key esc
  peekaboo-win screenshot --screen --out artifacts/screen.png
");
    }

    static string? GetFlag(string[] args, string name, string shortName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals(shortName, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    return args[i + 1];
            }
        }
        return null;
    }

    static bool HasFlag(string[] args, string name, string shortName)
    {
        return args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                             a.Equals(shortName, StringComparison.OrdinalIgnoreCase));
    }

    static void PrintJson(object obj)
    {
        Console.WriteLine(JsonSerializer.Serialize(obj, JsonOptions));
    }

    static void PrintError(string command, string message)
    {
        var r = CommandResult.Fail(command, message);
        Console.Error.WriteLine(JsonSerializer.Serialize(r, JsonOptions));
    }

    // ==================== V0.7 Visual Skill Memory ====================

    static int HandleSkillList(string[] args)
    {
        var store = new PeekabooWin.Core.Memory.VisualSkillStore();
        var integration = new PeekabooWin.Core.Agent.VacpSkillIntegration(store);
        var skills = integration.GetAllSkills();

        var result = CommandResult.Ok("skill-list", new {
            count = skills.Count,
            skills = skills.Select(s => new {
                s.SkillId,
                s.Name,
                s.AppPattern,
                s.ScreenType,
                s.RiskLevel,
                s.SuccessRate,
                s.UsageCount,
                s.CreatedAt
            })
        });
        PrintJson(result);
        return 0;
    }

    static int HandleSkillReplay(string[] args, WindowService windowService, CaptureService captureService, InputService inputService)
    {
        var skillId = GetFlag(args, "--id", "-i");
        string? window = GetFlag(args, "--window", "-w");

        if (string.IsNullOrEmpty(skillId))
        {
            PrintError("skill-replay", "Missing --id flag");
            return 1;
        }

        var store = new PeekabooWin.Core.Memory.VisualSkillStore();
        var skill = store.Get(skillId);

        if (skill == null)
        {
            PrintError("skill-replay", $"Skill not found: {skillId}");
            return 1;
        }

        // Focus window if specified
        if (!string.IsNullOrEmpty(window))
        {
            var win = windowService.FindWindow(window);
            if (win == null)
            {
                PrintError("skill-replay", $"Window not found: {window}");
                return 1;
            }
            windowService.FocusWindow(window);
            Thread.Sleep(200);
        }

        // Replay each procedure step
        var results = new List<object>();
        foreach (var step in skill.ProcedureSteps)
        {
            try
            {
                // Simple step execution: try to find element by label via UIA
                // In V0.7 MVP: just log the step — actual element-level replay needs UIA integration
                results.Add(new { step, status = "played", skill_id = skillId });
            }
            catch (Exception ex)
            {
                results.Add(new { step, status = "error", error = ex.Message });
            }
        }

        // Record usage
        skill.RecordUsage(results.All(r => ((dynamic)r).status == "played"));
        store.Add(skill);

        var cmdResult = CommandResult.Ok("skill-replay", new {
            skill_id = skillId,
            skill_name = skill.Name,
            steps = results
        });
        PrintJson(cmdResult);
        return 0;
    }

    static int HandleSkillSeed(string[] args)
    {
        var store = new PeekabooWin.Core.Memory.VisualSkillStore();
        store.SeedDemo();
        var result = CommandResult.Ok("skill-seed", new {
            message = "Demo skills seeded (Notepad Text Entry + Dialog Confirm)",
            count = store.GetAll().Count
        });
        PrintJson(result);
        return 0;
    }

    // ==================== V0.8 Skill-Guided Execution CLI ====================

    static int HandleSkillSearch(string[] args)
    {
        var task = GetFlag(args, "--task", "-t") ?? GetFlag(args, "--text", "-x");
        var app = GetFlag(args, "--app", "-a");
        var text = GetFlag(args, "--visible-text", "-v");
        var title = GetFlag(args, "--window", "-w");

        if (string.IsNullOrEmpty(task))
        {
            PrintError("skill-search", "Missing --task flag");
            return 1;
        }

        var store = new PeekabooWin.Core.Memory.VisualSkillStore();
        var integration = new PeekabooWin.Core.Agent.VacpSkillIntegration(store);
        var searchResults = integration.Search(task, app, text, title);

                var output = new
        {
            query = task,
            app_pattern = app,
            results = searchResults.Select(r => new
            {
                r.Skill.SkillId,
                r.Skill.Name,
                r.Skill.AppPattern,
                r.Skill.ScreenType,
                r.Skill.RiskLevel,
                r.Skill.UsageCount,
                scope = r.Skill.Scope == null ? null : new
                {
                    r.Skill.Scope.SupportedApps,

                    r.Skill.Scope.RequiredAnchors,
                    r.Skill.Scope.ForbiddenDomains,
                    r.Skill.Scope.MinRiskLevel
                },
                score = new
                {
                    r.Score.AppMatch,
                    r.Score.TextMatch,
                    r.Score.ActionSequenceMatch,
                    r.Score.RiskMatch,
                    r.Score.RecencyFactor,
                    r.Score.Total,
                    r.Score.IsUsable
                },
                r.Reason
            }).ToList()
        };

        var cmdResult = CommandResult.Ok("skill-search", output);
        PrintJson(cmdResult);
        return 0;
    }

    static int HandleSkillUsePreview(string[] args)
    {
        var task = GetFlag(args, "--task", "-t");
        var app = GetFlag(args, "--app", "-a");

        if (string.IsNullOrEmpty(task))
        {
            PrintError("skill-use-preview", "Missing --task flag");
            return 1;
        }

        var store = new PeekabooWin.Core.Memory.VisualSkillStore();
        var integration = new PeekabooWin.Core.Agent.VacpSkillIntegration(store);
        var searchResults = integration.Search(task, app, null, null);
        var usable = searchResults.Where(r => integration.Policy.CanUseSkill(r, task)).ToList();
        var best = usable.FirstOrDefault();

        var output = new
        {
            query = task,
            app_pattern = app,
            all_results_count = searchResults.Count,
            usable_count = usable.Count,
            top_candidate = best != null ? new
            {
                best.Skill.SkillId,
                best.Skill.Name,
                best.Skill.RiskLevel,
                best.Score.Total,
                best.Score.IsUsable,
                would_use_skill_hint = best.Score.Total >= 0.7
            } : null,
            usable_skills = usable.Select(r => new
            {
                r.Skill.SkillId,
                r.Skill.Name,
                r.Score.Total,
                r.Score.IsUsable
            }).ToList()
        };

        var cmdResult = CommandResult.Ok("skill-use-preview", output);
        PrintJson(cmdResult);
        return 0;
    }

    static int HandleSkillExecuteGuided(string[] args, WindowService windowService,
        CaptureService captureService, InputService inputService,
        UIAutomationService uiaService, OcrService ocrService)
    {
        var task = GetFlag(args, "--task", "-t");
        var app = GetFlag(args, "--app", "-a");

        if (string.IsNullOrEmpty(task))
        {
            PrintError("skill-execute-guided", "Missing --task flag");
            return 1;
        }

        var store = new PeekabooWin.Core.Memory.VisualSkillStore();
        var integration = new PeekabooWin.Core.Agent.VacpSkillIntegration(store);

        // V0.8: Search first
        var searchResults = integration.Search(task, app, null, null);
        var usable = searchResults.Where(r => integration.Policy.CanUseSkill(r, task)).ToList();
        var best = usable.FirstOrDefault();

        var preview = new
        {
            query = task,
            app_pattern = app,
            search_count = searchResults.Count,
            usable_count = usable.Count,
            top_skill = best?.Skill.Name,
            top_score = best?.Score.Total,
            skill_hint_injected = best != null && best.Score.Total >= 0.7
        };

        // In V0.8 MVP the actual VACP execution is deferred to the AgentService path
        // which calls VacpPlannerWithSkills.PlanWithSkills internally.
        // Here we return the preview with skill search results.
        var cmdResult = CommandResult.Ok("skill-execute-guided", new
        {
            preview,
            search_results = searchResults.Select(r => new
            {
                r.Skill.SkillId,
                r.Skill.Name,
                r.Score.Total,
                r.Score.IsUsable
            }),
            note = "V0.8: skill-execute-guided shows search preview. Use 'agent --task ...' for full guided execution."
        });
        PrintJson(cmdResult);
        return 0;
    }

    // ==================== V0.9 skill-search-context ====================


    static int HandleSkillSearchContext(string[] args)
    {
        var task = GetFlag(args, "--task", "-t") ?? GetFlag(args, "--text", "-x");
        var windowTitle = GetFlag(args, "--window", "-w");

        if (string.IsNullOrEmpty(task))
        {
            PrintError("skill-search-context", "Missing --task flag");
            return 1;
        }

        var store = new PeekabooWin.Core.Memory.VisualSkillStore();
        var integration = new PeekabooWin.Core.Agent.VacpSkillIntegration(store);

        // V0.9: Build WindowSignature from current window, then search with SkillScope validation
        var sig = integration.BuildWindowSignature(windowTitle);
        var searchResults = integration.SearchWithContext(task, windowTitle);
        var visibleHints = sig.VisibleTexts;
        var anchors = sig.AnchorCandidates;
        var profile = sig.Profile;

        var output = new
        {
            query = task,
            window_title = windowTitle ?? "(foreground window)",
            app_profile = profile == null ? null : new
            {
                profile.AppName,
                profile.ProcessName,
                profile.AppId,
                profile.WindowType,
                profile.InputMode,
                profile.RiskDomain,
                visibleTextHints = visibleHints
            },
            anchor_candidates = anchors,
            window_signature = new
            {
                sig.WindowTitle,
                sig.ProcessName,
                sig.WindowType,
                sig.InputMode,
                sig.RiskDomain,
                sig.CapturedAt
            },
            results = searchResults.Select(r => new
            {
                r.Skill.SkillId,
                r.Skill.Name,
                r.Skill.AppPattern,
                r.Skill.ScreenType,
                r.Skill.RiskLevel,
                scope = r.Skill.Scope == null ? null : new
                {
                    r.Skill.Scope.SupportedApps,
                    r.Skill.Scope.RequiredAnchors,
                    r.Skill.Scope.ForbiddenDomains,
                    r.Skill.Scope.MinRiskLevel
                },
                score = new
                {
                    r.Score.Total,
                    r.Score.IsUsable
                },
                r.Reason
            }).ToList()
        };

        var cmdResult = CommandResult.Ok("skill-search-context", output);
        PrintJson(cmdResult);
        return 0;
    }
}
