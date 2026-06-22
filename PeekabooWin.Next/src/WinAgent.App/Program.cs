using System.Runtime.InteropServices;
using System.Text.Json;
using WinAgent.Core.Actions;
using WinAgent.Core.Coordinate;
using WinAgent.Core.Grounding;
using WinAgent.Core.Models;
using WinAgent.Core.Observation;
using WinAgent.Core.Verification;
using WinAgent.Sensors.UIA;

namespace WinAgent.App;

/// <summary>
/// WinAgent CLI — Observe → Ground → Act → Verify
///
/// P1 阶段只支持:
///   observe --window notepad
///   ground  --snapshot snap.json --target btn_001
///   act     --snapshot snap.json --target btn_001 --action click [--force]
///   verify  --before before.png --after after.png
/// </summary>
class Program
{
    private static readonly ObservationService _observationService = new();
    private static readonly GroundingService _groundingService = new();
    private static readonly ActionExecutor _actionExecutor = new();
    private static readonly VerificationService _verificationService = new();

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        // 注册传感器
        _observationService.RegisterSensor(new UiaSensor());

        var command = args[0].ToLower();
        var opts = ParseArgs(args[1..]);

        try
        {
            return command switch
            {
                "observe" => CmdObserve(opts),
                "ground" => CmdGround(opts),
                "act" => CmdAct(opts),
                "verify" => CmdVerify(opts),
                "help" or "--help" or "-h" => CmdHelp(),
                _ => Fail($"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            return Fail($"Error: {ex.Message}");
        }
    }

    static int CmdObserve(Dictionary<string, string> opts)
    {
        var windowKeyword = GetOpt(opts, "window", "w", "");
        if (string.IsNullOrEmpty(windowKeyword))
            return Fail("--window is required");

        var hwnd = FindWindowByKeyword(windowKeyword);
        if (hwnd == IntPtr.Zero)
            return Fail($"Window not found: {windowKeyword}");

        var screenshotPath = GetOpt(opts, "out", "o", "");

        var result = _observationService.Observe(hwnd, string.IsNullOrEmpty(screenshotPath) ? null : screenshotPath);

        // 保存 snapshot JSON
        var runsDir = Path.Combine(Directory.GetCurrentDirectory(), "runs");
        Directory.CreateDirectory(runsDir);
        var snapshotFile = Path.Combine(runsDir, $"{result.SnapshotId}.json");
        File.WriteAllText(snapshotFile, JsonSerializer.Serialize(result, _jsonOpts));

        // 输出
        var output = new
        {
            snapshot_id = result.SnapshotId,
            snapshot_file = snapshotFile,
            window = new
            {
                title = result.ActiveWindow.Title,
                handle = $"0x{result.ActiveWindow.Handle:X8}",
                bbox = new[] { result.ActiveWindow.Bounds.X, result.ActiveWindow.Bounds.Y, result.ActiveWindow.Bounds.Right, result.ActiveWindow.Bounds.Bottom }
            },
            coordinate_space = "physical_screen_pixels",
            elements = result.Elements.Select(e => new
            {
                id = e.Id,
                role = e.Role.ToString().ToLower(),
                name = e.Name,
                automation_id = e.AutomationId,
                bbox = new[] { e.BBox.X, e.BBox.Y, e.BBox.Right, e.BBox.Bottom },
                source = e.Source.ToString().ToLower(),
                enabled = e.Enabled,
                visible = e.Visible,
                confidence = Math.Round(e.EstimatedConfidence, 2)
            }),
            warnings = result.Warnings
        };

        Console.WriteLine(JsonSerializer.Serialize(output, _jsonOpts));
        return 0;
    }

    static int CmdGround(Dictionary<string, string> opts)
    {
        var targetId = GetOpt(opts, "target", "t", "");
        if (string.IsNullOrEmpty(targetId))
            return Fail("--target is required");

        var snapshotFile = GetOpt(opts, "snapshot", "s", "");
        if (string.IsNullOrEmpty(snapshotFile) || !File.Exists(snapshotFile))
            return Fail("--snapshot is required (observe output file)");

        var observation = JsonSerializer.Deserialize<ObservationResult>(
            File.ReadAllText(snapshotFile), _jsonOpts);

        if (observation == null)
            return Fail("Failed to parse snapshot file");

        var query = new GroundingQuery { TargetId = targetId };
        var result = _groundingService.GroundById(observation, query);

        var output = new
        {
            ok = result.IsGrounded,
            target_id = result.TargetId,
            bbox = result.ResolvedElement != null
                ? new[] { result.ResolvedElement.BBox.X, result.ResolvedElement.BBox.Y, result.ResolvedElement.BBox.Right, result.ResolvedElement.BBox.Bottom }
                : null,
            click_point = result.ClickX.HasValue ? new[] { result.ClickX.Value, result.ClickY!.Value } : null,
            source = result.ResolvedElement?.Source.ToString().ToLower(),
            confidence = Math.Round(result.EstimatedScore, 2),
            risk = result.IsPotentiallyDangerous ? "dangerous" : "low",
            danger_warning = result.DangerWarning,
            error = result.Error
        };

        Console.WriteLine(JsonSerializer.Serialize(output, _jsonOpts));
        return result.IsGrounded ? 0 : 1;
    }

    static int CmdAct(Dictionary<string, string> opts)
    {
        var targetId = GetOpt(opts, "target", "t", "");
        if (string.IsNullOrEmpty(targetId))
            return Fail("--target is required");

        var actionType = GetOpt(opts, "action", "a", "click");
        var text = GetOpt(opts, "text", "", "");
        var keys = GetOpt(opts, "keys", "k", "");
        var force = opts.ContainsKey("force") || opts.ContainsKey("f");

        var snapshotFile = GetOpt(opts, "snapshot", "s", "");
        if (string.IsNullOrEmpty(snapshotFile) || !File.Exists(snapshotFile))
            return Fail("--snapshot is required (observe output file)");

        var observation = JsonSerializer.Deserialize<ObservationResult>(
            File.ReadAllText(snapshotFile), _jsonOpts);

        if (observation == null)
            return Fail("Failed to parse snapshot file");

        // Ground
        var grounding = _groundingService.GroundById(observation, new GroundingQuery { TargetId = targetId });

        if (!grounding.IsGrounded)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = grounding.Error }, _jsonOpts));
            return 1;
        }

        // 安全检查
        if (grounding.IsPotentiallyDangerous && !force)
        {
            var blocked = new
            {
                dry_run = true,
                action = actionType,
                target_id = targetId,
                click_point = new[] { grounding.ClickX, grounding.ClickY },
                blocked = true,
                reason = grounding.DangerWarning ?? "Dangerous element. Use --force to execute."
            };
            Console.WriteLine(JsonSerializer.Serialize(blocked, _jsonOpts));
            return 2;
        }

        // 默认 dry-run
        if (!force)
        {
            var dryRun = new
            {
                dry_run = true,
                action = actionType,
                target_id = targetId,
                click_point = new[] { grounding.ClickX, grounding.ClickY },
                reason = "Dry-run by default. Use --force to execute."
            };
            Console.WriteLine(JsonSerializer.Serialize(dryRun, _jsonOpts));
            return 0;
        }

        // 真正执行
        var request = new ActionRequest
        {
            Type = Enum.Parse<ActionType>(actionType, true),
            TargetId = targetId,
            Text = string.IsNullOrEmpty(text) ? null : text,
            Keys = string.IsNullOrEmpty(keys) ? null : keys,
            DryRun = false
        };

        // 截 before
        var runsDir = Path.Combine(Directory.GetCurrentDirectory(), "runs");
        Directory.CreateDirectory(runsDir);
        var beforePath = Path.Combine(runsDir, $"before_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        _verificationService.CaptureScreen(beforePath);

        var result = _actionExecutor.Execute(request, grounding, force: true);

        // 截 after + verify
        Thread.Sleep(500);
        var afterPath = Path.Combine(runsDir, $"after_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        _verificationService.CaptureScreen(afterPath);

        var verification = _verificationService.Compare(beforePath, afterPath);

        var output = new
        {
            ok = result.Success,
            action = actionType,
            target_id = targetId,
            click_point = new[] { grounding.ClickX, grounding.ClickY },
            dry_run = false,
            blocked = result.WasBlocked,
            verification = new
            {
                verified = verification.Changed,
                visual_change_score = Math.Round(verification.PixelDiffRatio, 4),
                before_screenshot = beforePath,
                after_screenshot = afterPath
            },
            error = result.Error
        };

        Console.WriteLine(JsonSerializer.Serialize(output, _jsonOpts));
        return result.Success ? 0 : 1;
    }

    static int CmdVerify(Dictionary<string, string> opts)
    {
        var beforePath = GetOpt(opts, "before", "b", "");
        var afterPath = GetOpt(opts, "after", "a", "");

        if (string.IsNullOrEmpty(beforePath) || string.IsNullOrEmpty(afterPath))
            return Fail("--before and --after are required");

        if (!File.Exists(beforePath))
            return Fail($"Before file not found: {beforePath}");
        if (!File.Exists(afterPath))
            return Fail($"After file not found: {afterPath}");

        var result = _verificationService.Compare(beforePath, afterPath);

        var output = new
        {
            verified = result.Changed,
            visual_change_score = Math.Round(result.PixelDiffRatio, 4),
            description = result.ChangeDescription
        };

        Console.WriteLine(JsonSerializer.Serialize(output, _jsonOpts));
        return 0;
    }

    static int CmdHelp()
    {
        PrintUsage();
        return 0;
    }

    static IntPtr FindWindowByKeyword(string keyword)
    {
        var hwnd = Windows.NativeMethods.FindWindow(null, keyword);
        if (hwnd != IntPtr.Zero) return hwnd;

        // 模糊搜索
        IntPtr found = IntPtr.Zero;
        Windows.NativeMethods.EnumWindows((h, _) =>
        {
            var sb = new System.Text.StringBuilder(256);
            Windows.NativeMethods.GetWindowText(h, sb, 256);
            if (sb.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                if (Windows.NativeMethods.IsWindowVisible(h))
                {
                    found = h;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"
WinAgent — Observe → Ground → Act → Verify

Commands:
  observe   Observe a window and produce element snapshot
  ground    Ground an element by target_id
  act       Execute an action on a grounded element
  verify    Compare before/after screenshots

Usage:
  winagent observe --window notepad [--out screenshot.png]
  winagent ground  --snapshot runs\snap_xxx.json --target btn_001
  winagent act     --snapshot runs\snap_xxx.json --target btn_001 --action click
  winagent act     --snapshot runs\snap_xxx.json --target btn_001 --action click --force
  winagent act     --snapshot runs\snap_xxx.json --target inp_001 --action type --text ""hello"" --force
  winagent verify  --before before.png --after after.png

Safety:
  - All actions are dry-run by default
  - Dangerous elements (delete/close/uninstall) blocked even with --force
  - Use --force to actually execute safe actions
  - All coordinates are physical screen pixels
");
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    static Dictionary<string, string> ParseArgs(string[] args)
    {
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var key = args[i][2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    opts[key] = args[++i];
                else
                    opts[key] = "true";
            }
            else if (args[i].StartsWith("-") && args[i].Length == 2)
            {
                var key = args[i][1..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    opts[key] = args[++i];
                else
                    opts[key] = "true";
            }
        }
        return opts;
    }

    static string GetOpt(Dictionary<string, string> opts, string longName, string shortName, string defaultValue)
    {
        if (opts.TryGetValue(longName, out var val)) return val;
        if (!string.IsNullOrEmpty(shortName) && opts.TryGetValue(shortName, out val)) return val;
        return defaultValue;
    }
}
