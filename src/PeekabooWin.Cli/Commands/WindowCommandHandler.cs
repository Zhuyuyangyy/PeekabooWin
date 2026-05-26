using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Cli.Commands;

public class WindowCommandHandler : ICommandHandler
{
    private readonly WindowService _windowService;
    private readonly CaptureService _captureService;
    private readonly InputService _inputService;

    public string CommandName => "window";

    public WindowCommandHandler(WindowService windowService, CaptureService captureService, InputService inputService)
    {
        _windowService = windowService;
        _captureService = captureService;
        _inputService = inputService;
    }

    public Task<int> ExecuteAsync(string[] args)
    {
        var command = args[0].ToLower();
        var result = command switch
        {
            "list-windows" => HandleListWindows(args),
            "focus-window" => HandleFocusWindow(args),
            "screenshot" => HandleScreenshot(args),
            "click" => HandleClick(args),
            "type" => HandleType(args),
            "press" => HandlePress(args),
            "hotkey" => HandleHotkey(args),
            "window-info" => HandleWindowInfo(args),
            "click-rel" => HandleClickRel(args),
            "is-focused" => HandleIsFocused(args),
            _ => 1
        };
        return Task.FromResult(result);
    }

    private int HandleListWindows(string[] args)
    {
        string? keyword = CliHelpers.GetFlag(args, "--keyword", "-k");
        var windows = _windowService.ListWindows(keyword);
        var result = CommandResult.Ok("list-windows", new { windows });
        CliHelpers.PrintJson(result);
        return 0;
    }

    private int HandleFocusWindow(string[] args)
    {
        string? keyword = CliHelpers.GetFlag(args, "--window", "-w")
            ?? CliHelpers.GetFlag(args, "--title", "-t");

        if (string.IsNullOrEmpty(keyword))
        {
            CliHelpers.PrintError("focus-window", "Missing --window flag");
            return 1;
        }

        var ok = _windowService.FocusWindow(keyword);
        var result = CommandResult.Ok("focus-window", new { success = ok, focused = keyword });
        CliHelpers.PrintJson(result);
        return ok ? 0 : 1;
    }

    private int HandleScreenshot(string[] args)
    {
        string? outPath = CliHelpers.GetFlag(args, "--out", "-o");
        string? window = CliHelpers.GetFlag(args, "--window", "-w");
        bool screen = CliHelpers.HasFlag(args, "--screen", "-s");

        if (string.IsNullOrEmpty(outPath))
        {
            outPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"peekaboo_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        }

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        CaptureResult capResult;
        if (screen || string.IsNullOrEmpty(window))
            capResult = _captureService.CaptureScreen(outPath);
        else
            capResult = _captureService.CaptureWindow(window, outPath);

        var result = CommandResult.Ok("screenshot", capResult);
        CliHelpers.PrintJson(result);
        return capResult.Success ? 0 : 1;
    }

    private int HandleClick(string[] args)
    {
        string? xStr = CliHelpers.GetFlag(args, "--x", "-x");
        string? yStr = CliHelpers.GetFlag(args, "--y", "-y");

        if (!string.IsNullOrEmpty(xStr) && !string.IsNullOrEmpty(yStr))
        {
            if (int.TryParse(xStr, out int x) && int.TryParse(yStr, out int y))
            {
                var r = _inputService.Click(x, y);
                CliHelpers.PrintJson(r);
                return r.Success ? 0 : 1;
            }
        }

        var r2 = _inputService.ClickCurrent();
        CliHelpers.PrintJson(r2);
        return r2.Success ? 0 : 1;
    }

    private int HandleType(string[] args)
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
            CliHelpers.PrintError("type", "Missing text to type");
            return 1;
        }

        var r = _inputService.TypeText(text);
        CliHelpers.PrintJson(r);
        return r.Success ? 0 : 1;
    }

    private int HandlePress(string[] args)
    {
        string? key = CliHelpers.GetFlag(args, "--key", "-k")
            ?? CliHelpers.GetFlag(args, "--keys", "-k");

        if (string.IsNullOrEmpty(key))
        {
            CliHelpers.PrintError("press", "Missing --key flag. Supported: esc, enter, tab, backspace, delete");
            return 1;
        }

        var r = _inputService.PressKeyByName(key.ToLower());
        CliHelpers.PrintJson(r);
        return r.Success ? 0 : 1;
    }

    private int HandleHotkey(string[] args)
    {
        string? hotkey = CliHelpers.GetFlag(args, "--keys", "-k")
            ?? CliHelpers.GetFlag(args, "--hotkey", "-h");

        if (string.IsNullOrEmpty(hotkey))
        {
            CliHelpers.PrintError("hotkey", "Missing --keys flag");
            return 1;
        }

        var r = _inputService.Hotkey(hotkey);
        CliHelpers.PrintJson(r);
        return r.Success ? 0 : 1;
    }

    private int HandleWindowInfo(string[] args)
    {
        var windows = _windowService.ListWindows();
        var result = CommandResult.Ok("window-info", new { count = windows.Count, windows });
        CliHelpers.PrintJson(result);
        return 0;
    }

    private int HandleClickRel(string[] args)
    {
        var window = CliHelpers.GetFlag(args, "--window", "-w") ?? CliHelpers.GetFlag(args, "--win", "-W");
        var xStr = CliHelpers.GetFlag(args, "--x", "-x");
        var yStr = CliHelpers.GetFlag(args, "--y", "-y");

        if (string.IsNullOrEmpty(window)) { CliHelpers.PrintError("click-rel", "Missing --window"); return 1; }
        if (string.IsNullOrEmpty(xStr) || string.IsNullOrEmpty(yStr)) { CliHelpers.PrintError("click-rel", "Missing --x or --y"); return 1; }
        if (!int.TryParse(xStr, out int relX) || !int.TryParse(yStr, out int relY)) { CliHelpers.PrintError("click-rel", "--x and --y must be integers"); return 1; }

        var win = _windowService.FindWindow(window);
        if (win == null) { CliHelpers.PrintError("click-rel", $"Window not found: {window}"); return 1; }

        int absX = win.Rect.X + relX;
        int absY = win.Rect.Y + relY;
        var r = _inputService.Click(absX, absY);
        var result = CommandResult.Ok("click-rel", new { abs_x = absX, abs_y = absY, rel_x = relX, rel_y = relY, window = win.Title, rect = win.Rect, success = r.Success, error = r.Error });
        CliHelpers.PrintJson(result);
        return r.Success ? 0 : 1;
    }

    private int HandleIsFocused(string[] args)
    {
        var window = CliHelpers.GetFlag(args, "--window", "-w") ?? "";
        var foregroundHwnd = GetForegroundWindow();
        var allWindows = _windowService.ListWindows(null);
        var focusedWin = allWindows.FirstOrDefault(w => w.Handle == foregroundHwnd.ToInt64());

        if (focusedWin == null)
        {
            var result = CommandResult.Ok("is-focused", new { foreground_handle = foregroundHwnd.ToInt64(), tracked = false });
            CliHelpers.PrintJson(result);
            return 0;
        }

        var isMatch = string.IsNullOrEmpty(window) || focusedWin.Title.Contains(window, StringComparison.OrdinalIgnoreCase);
        var r = CommandResult.Ok("is-focused", new { focused_window = focusedWin.Title, focused_pid = focusedWin.ProcessId, matches_query = isMatch, query = window });
        CliHelpers.PrintJson(r);
        return isMatch ? 0 : 1;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
