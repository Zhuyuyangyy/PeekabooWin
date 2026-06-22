using System.Text.Json;
using WinAgent.Core.Actions;
using WinAgent.Core.Grounding;
using WinAgent.Core.Models;
using WinAgent.Core.Observation;
using WinAgent.Core.Verification;
using WinAgent.Sensors.UIA;

namespace WinAgent.App;

/// <summary>
/// WinAgent CLI — Observe → Ground → Act → Verify
///
/// 用法:
///   winagent observe --window notepad
///   winagent ground --snapshot snap_xxx --target btn_012
///   winagent act --snapshot snap_xxx --target btn_012 --action click [--force]
///   winagent verify --before before.png --after after.png
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
                "help" => CmdHelp(),
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
        var windowKeyword = GetValue(opts, "window", "w", "");
        if (string.IsNullOrEmpty(windowKeyword))
            return Fail("--window is required");

        var hwnd = FindWindowHandle(windowKeyword);
        if (hwnd == IntPtr.Zero)
            return Fail($"Window not found: {windowKeyword}");

        var screenshotPath = GetValue(opts, "out", "o", "");
        var maxDepth = int.Parse(GetValue(opts, "max-depth", "d", "6"));

        // 注册传感器
        _observationService.RegisterSensor(new UiaSensor());

        var result = _observationService.Observe(hwnd, screenshotPath);

        Console.WriteLine(JsonSerializer.Serialize(result, _jsonOpts));
        return 0;
    }

    static int CmdGround(Dictionary<string, string> opts)
    {
        var targetId = GetValue(opts, "target", "t", "");
        var text = GetValue(opts, "text", "", "");

        if (string.IsNullOrEmpty(targetId) && string.IsNullOrEmpty(text))
            return Fail("--target or --text is required");

        // 需要先 observe
        var snapshotFile = GetValue(opts, "snapshot", "s", "");
        if (string.IsNullOrEmpty(snapshotFile) || !File.Exists(snapshotFile))
            return Fail("--snapshot is required (observe output file)");

        var observation = JsonSerializer.Deserialize<ObservationResult>(
            File.ReadAllText(snapshotFile), _jsonOpts);

        if (observation == null)
            return Fail("Failed to parse snapshot file");

        GroundingResult result;
        if (!string.IsNullOrEmpty(targetId))
        {
            var query = new GroundingQuery { TargetId = targetId };
            result = _groundingService.GroundById(observation, query);
        }
        else
        {
            result = _groundingService.GroundByText(observation, text);
        }

        Console.WriteLine(JsonSerializer.Serialize(result, _jsonOpts));
        return result.IsGrounded ? 0 : 1;
    }

    static int CmdAct(Dictionary<string, string> opts)
    {
        var targetId = GetValue(opts, "target", "t", "");
        if (string.IsNullOrEmpty(targetId))
            return Fail("--target is required");

        var actionType = GetValue(opts, "action", "a", "click");
        var text = GetValue(opts, "text", "", "");
        var keys = GetValue(opts, "keys", "k", "");
        var dryRun = !opts.ContainsKey("force") && !opts.ContainsKey("f");

        // 需要先 ground
        var snapshotFile = GetValue(opts, "snapshot", "s", "");
        if (string.IsNullOrEmpty(snapshotFile) || !File.Exists(snapshotFile))
            return Fail("--snapshot is required (observe output file)");

        var observation = JsonSerializer.Deserialize<ObservationResult>(
            File.ReadAllText(snapshotFile), _jsonOpts);

        if (observation == null)
            return Fail("Failed to parse snapshot file");

        // Ground
        var groundingQuery = new GroundingQuery { TargetId = targetId };
        var grounding = _groundingService.GroundById(observation, groundingQuery);

        if (!grounding.IsGrounded)
        {
            Console.WriteLine(JsonSerializer.Serialize(grounding, _jsonOpts));
            return 1;
        }

        // 安全检查
        if (grounding.IsPotentiallyDangerous && dryRun)
        {
            var blockedResult = new ActionResult
            {
                Success = false,
                Type = Enum.Parse<ActionType>(actionType, true),
                TargetId = targetId,
                WasDryRun = true,
                WasBlocked = true,
                BlockReason = grounding.DangerWarning
            };
            Console.WriteLine(JsonSerializer.Serialize(blockedResult, _jsonOpts));
            return 2;
        }

        // Act
        var request = new ActionRequest
        {
            Type = Enum.Parse<ActionType>(actionType, true),
            TargetId = targetId,
            Text = text,
            Keys = keys,
            DryRun = dryRun
        };

        var result = _actionExecutor.Execute(request, grounding, !dryRun);

        // Verify (如果不是 dry-run)
        if (!result.WasDryRun && result.Success)
        {
            Thread.Sleep(500);
            var afterPath = _verificationService.CaptureScreen();
            result.Verification = new VerificationResult
            {
                AfterScreenshot = afterPath,
                Changed = true // 简化，实际需要 before 截图对比
            };
        }

        Console.WriteLine(JsonSerializer.Serialize(result, _jsonOpts));
        return result.Success ? 0 : 1;
    }

    static int CmdVerify(Dictionary<string, string> opts)
    {
        var beforePath = GetValue(opts, "before", "b", "");
        var afterPath = GetValue(opts, "after", "a", "");

        if (string.IsNullOrEmpty(beforePath) || string.IsNullOrEmpty(afterPath))
            return Fail("--before and --after are required");

        if (!File.Exists(beforePath))
            return Fail($"Before file not found: {beforePath}");
        if (!File.Exists(afterPath))
            return Fail($"After file not found: {afterPath}");

        var result = _verificationService.Compare(beforePath, afterPath);
        Console.WriteLine(JsonSerializer.Serialize(result, _jsonOpts));
        return 0;
    }

    static int CmdHelp()
    {
        PrintUsage();
        return 0;
    }

    static IntPtr FindWindowHandle(string keyword)
    {
        // 简化实现，复用原项目的 WindowService 逻辑
        var hwnd = WinAgent.Core.Windows.NativeMethods.FindWindow(null, keyword);
        return hwnd;
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"
WinAgent — Observe → Ground → Act → Verify

Commands:
  observe   Observe a window and produce element snapshot
  ground    Ground an element by ID or text
  act       Execute an action on a grounded element
  verify    Compare before/after screenshots

Usage:
  winagent observe --window notepad [--out screenshot.png]
  winagent ground --snapshot snap.json --target btn_012
  winagent ground --snapshot snap.json --text ""保存""
  winagent act --snapshot snap.json --target btn_012 --action click [--force]
  winagent act --snapshot snap.json --target inp_001 --action type --text ""hello""
  winagent verify --before before.png --after after.png

Safety:
  - Dangerous elements (delete/close/uninstall) require --force
  - Without --force, actions are dry-run only
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
                {
                    opts[key] = args[++i];
                }
                else
                {
                    opts[key] = "true";
                }
            }
            else if (args[i].StartsWith("-") && args[i].Length == 2)
            {
                var key = args[i][1..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    opts[key] = args[++i];
                }
                else
                {
                    opts[key] = "true";
                }
            }
        }
        return opts;
    }

    static string GetValue(Dictionary<string, string> opts, string longName, string shortName, string defaultValue)
    {
        if (opts.TryGetValue(longName, out var val)) return val;
        if (!string.IsNullOrEmpty(shortName) && opts.TryGetValue(shortName, out val)) return val;
        return defaultValue;
    }
}
