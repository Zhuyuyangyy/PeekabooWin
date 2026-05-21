using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Input;
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

    // ==================== Helpers ====================

    static void PrintUsage()
    {
        Console.WriteLine(@"
PeekabooWin - Windows Desktop Automation CLI (V0.2.1)

Usage: peekaboo-win <command> [options]

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
  peekaboo-win list-windows --keyword 记事�?  peekaboo-win inspect --window notepad --max-depth 3
  peekaboo-win click-element --window notepad --name 文件 --dry-run
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
}