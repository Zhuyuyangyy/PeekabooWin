using System.Net;
using System.Text;
using System.Text.Json;
using PeekabooWin.Core.Agent;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.Safety;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Cli;

public class ApiServer
{
    private readonly HttpListener _listener;
    private readonly WindowService _windowService;
    private readonly CaptureService _captureService;
    private readonly InputService _inputService;
    private readonly UIAutomationService _uiaService;
    private readonly OcrService _ocrService;
    private readonly AgentService _agentService;
    private readonly VacpSkillIntegration _skillIntegration;
    private readonly SkillReplayEngine _skillReplayEngine;
    private readonly ActionRiskGate _riskGate;
    private readonly VisualSkillStore _skillStore;
    private readonly JsonSerializerOptions _jsonOptions;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public int Port { get; }
    public bool IsRunning => _listener.IsListening;

    public ApiServer(int port = 8025)
    {
        Port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");

        _windowService = new WindowService();
        _captureService = new CaptureService(_windowService);
        _inputService = new InputService();
        _uiaService = new UIAutomationService(_windowService, _inputService);
        _ocrService = new OcrService();
        _agentService = new AgentService(_windowService, _captureService, _inputService, _uiaService, _ocrService);

        _skillStore = new VisualSkillStore();
        _skillIntegration = new VacpSkillIntegration(_skillStore);
        _riskGate = new ActionRiskGate();
        _skillReplayEngine = new SkillReplayEngine(
            _windowService, _captureService, _ocrService,
            _inputService, _uiaService, _riskGate, new TempFileManager());

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public void Start()
    {
        _listener.Start();
        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoop(_cts.Token));
        Console.WriteLine($"[ApiServer] V0.14 Agent Runtime started on http://localhost:{Port}/");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
        Console.WriteLine("[ApiServer] Stopped");
    }

    private async Task ServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(ctx));
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch { }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var response = ctx.Response;
        var request = ctx.Request;

        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        try
        {
            string path = request.Url?.AbsolutePath ?? "/";
            string method = request.HttpMethod;

            if (path == "/api/v1/health" && method == "GET")
            {
                await RespondJson(response, 200, BuildRuntimeResponse(true,
                    decision: "ALLOW",
                    riskLevel: "L0",
                    actions: [],
                    verification: new { passed = true, reason = "runtime healthy" }));
                return;
            }

            if (path == "/api/v1/task/preview" && method == "POST")
            {
                await HandleV1TaskPreview(request, response);
                return;
            }

            if (path == "/api/v1/task/run" && method == "POST")
            {
                await HandleV1TaskRun(request, response);
                return;
            }

            if (path == "/api/v1/skill/search" && method == "POST")
            {
                await HandleV1SkillSearch(request, response);
                return;
            }

            if (path == "/api/v1/skill/replay" && method == "POST")
            {
                await HandleV1SkillReplay(request, response);
                return;
            }

            if (path == "/api/v1/risk/evaluate" && method == "POST")
            {
                await HandleV1RiskEvaluate(request, response);
                return;
            }

            if (path.StartsWith("/api/v1/trace/") && method == "GET")
            {
                var traceId = path["/api/v1/trace/".Length..];
                await RespondJson(response, 200, BuildRuntimeResponse(true,
                    traceId: traceId,
                    decision: "ALLOW",
                    riskLevel: "L0",
                    error: "Trace storage not yet implemented"));
                return;
            }

            if (path == "/health" && method == "GET")
            {
                await RespondJson(response, 200, new { status = "ok", version = "V0.14", timestamp = DateTime.UtcNow });
                return;
            }

            if (path == "/windows" && method == "GET")
            {
                var keyword = request.QueryString["keyword"];
                var windows = _windowService.ListWindows(keyword);
                await RespondJson(response, 200, new { success = true, windows });
                return;
            }

            if (path == "/inspect" && method == "GET")
            {
                var window = request.QueryString["window"];
                var depthStr = request.QueryString["max_depth"];
                int depth = int.TryParse(depthStr, out var d) ? d : 5;

                if (string.IsNullOrEmpty(window))
                {
                    await RespondJson(response, 400, new { success = false, error = "Missing 'window' query param" });
                    return;
                }

                var result = _uiaService.Inspect(window, depth);
                await RespondJson(response, result.Success ? 200 : 404, result);
                return;
            }

            if (path == "/execute" && method == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var execReq = JsonSerializer.Deserialize<ApiExecuteRequest>(body, _jsonOptions);

                if (execReq == null || string.IsNullOrEmpty(execReq.command))
                {
                    await RespondJson(response, 400, new { success = false, error = "Missing 'command' field" });
                    return;
                }

                var result = ExecuteCommand(execReq.command, execReq.args ?? new Dictionary<string, string>());
                await RespondJson(response, 200, result);
                return;
            }

            if (path == "/click" && method == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<ApiClickRequest>(body, _jsonOptions);

                if (req == null || (req.x <= 0 && req.y <= 0))
                {
                    await RespondJson(response, 400, new { success = false, error = "Missing x/y coordinates" });
                    return;
                }

                var result = _inputService.Click(req.x, req.y);
                await RespondJson(response, 200, result);
                return;
            }

            if (path == "/type" && method == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<ApiTypeRequest>(body, _jsonOptions);

                if (req == null || string.IsNullOrEmpty(req.text))
                {
                    await RespondJson(response, 400, new { success = false, error = "Missing 'text' field" });
                    return;
                }

                var result = _inputService.TypeText(req.text);
                await RespondJson(response, 200, result);
                return;
            }

            if (path == "/press" && method == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<ApiPressRequest>(body, _jsonOptions);

                if (req == null || string.IsNullOrEmpty(req.key))
                {
                    await RespondJson(response, 400, new { success = false, error = "Missing 'key' field" });
                    return;
                }

                var result = _inputService.PressKeyByName(req.key.ToLower());
                await RespondJson(response, 200, result);
                return;
            }

            if (path == "/hotkey" && method == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<ApiHotkeyRequest>(body, _jsonOptions);

                if (req == null || string.IsNullOrEmpty(req.keys))
                {
                    await RespondJson(response, 400, new { success = false, error = "Missing 'keys' field" });
                    return;
                }

                var result = _inputService.Hotkey(req.keys);
                await RespondJson(response, 200, result);
                return;
            }

            if (path == "/screenshot" && method == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<ApiScreenshotRequest>(body, _jsonOptions);

                var outPath = req?.out_path ?? Path.Combine(Path.GetTempPath(), $"peekaboo_api_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                CaptureResult capResult;

                if (!string.IsNullOrEmpty(req?.window))
                {
                    capResult = _captureService.CaptureWindow(req.window, outPath);
                }
                else
                {
                    capResult = _captureService.CaptureScreen(outPath);
                }

                await RespondJson(response, capResult.Success ? 200 : 500, new
                {
                    success = capResult.Success,
                    path = outPath,
                    error = capResult.Error
                });
                return;
            }

            if (path == "/agent" && method == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<ApiAgentRequest>(body, _jsonOptions);

                if (req == null || string.IsNullOrEmpty(req.task))
                {
                    await RespondJson(response, 400, new { success = false, error = "Missing 'task' field" });
                    return;
                }

                var agentReq = new AgentTaskRequest
                {
                    Task = req.task,
                    MaxSteps = req.max_steps > 0 ? req.max_steps : 5,
                    DryRun = req.dry_run,
                    Context = req.context
                };

                var result = await _agentService.ExecuteTaskAsync(agentReq);
                await RespondJson(response, 200, result);
                return;
            }

            if (path == "/focus-window" && method == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<ApiFocusWindowRequest>(body, _jsonOptions);

                if (req == null || string.IsNullOrEmpty(req.title))
                {
                    await RespondJson(response, 400, new { success = false, error = "Missing 'title' field" });
                    return;
                }

                var ok = _windowService.FocusWindow(req.title);
                await RespondJson(response, 200, new { success = ok, focused = req.title });
                return;
            }

            if (path == "/ocr" && method == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<ApiOcrRequest>(body, _jsonOptions);

                var imgPath = Path.Combine(Path.GetTempPath(), $"ocr_api_{Guid.NewGuid():N}.png");

                if (!string.IsNullOrEmpty(req?.window))
                {
                    var cap = _captureService.CaptureWindow(req.window, imgPath);
                    if (!cap.Success)
                    {
                        await RespondJson(response, 500, new { success = false, error = cap.Error });
                        return;
                    }
                }
                else
                {
                    var cap = _captureService.CaptureScreen(imgPath);
                    if (!cap.Success)
                    {
                        await RespondJson(response, 500, new { success = false, error = cap.Error });
                        return;
                    }
                }

                var lang = req?.lang ?? "chi_sim+eng";
                using var ocrService = new OcrService(lang);
                var ocrResult = await ocrService.RecognizeImageAsync(imgPath);

                if (!string.IsNullOrEmpty(req?.text))
                {
                    var words = ocrService.FindWords(ocrResult, req.text);
                    var center = ocrService.FindWordCenter(ocrResult, req.text);

                    var findResult = new
                    {
                        search_query = req.text,
                        recognized_text = ocrResult.Text,
                        matches_count = words.Count,
                        first_center = center.HasValue ? new { x = center.Value.x, y = center.Value.y } : null
                    };

                    await RespondJson(response, 200, findResult);

                    if (req.click == true && center.HasValue && center.Value.x > 0 && center.Value.y > 0)
                    {
                        var clickResult = _inputService.Click(center.Value.x, center.Value.y);
                    }
                    return;
                }
                else
                {
                    await RespondJson(response, 200, new
                    {
                        success = true,
                        text = ocrResult.Text,
                        words_count = ocrResult.Words.Count,
                        confidence = ocrResult.Confidence,
                        engine = ocrResult.Engine
                    });
                    return;
                }
            }

            await RespondJson(response, 404, new { error = $"Route not found: {path}" });
        }
        catch (Exception ex)
        {
            await RespondJson(response, 500, new { error = ex.Message });
        }
    }

    private async Task HandleV1TaskPreview(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var req = JsonSerializer.Deserialize<ApiV1TaskRequest>(body, _jsonOptions);

        if (req == null || string.IsNullOrEmpty(req.task))
        {
            await RespondJson(response, 400, BuildRuntimeResponse(false, error: "Missing 'task' field"));
            return;
        }

        var traceId = GenerateTraceId();
        var agentReq = new AgentTaskRequest
        {
            Task = req.task,
            MaxSteps = req.max_steps > 0 ? req.max_steps : 5,
            DryRun = true,
            Context = req.context,
            TimeoutMs = req.timeout_ms > 0 ? req.timeout_ms : 30000
        };

        var result = await _agentService.ExecuteTaskAsync(agentReq);

        var actions = result.Steps.Select(s => (object)new
        {
            step = s.Step,
            action = s.Action,
            args = s.Args,
            thought = s.Thought,
            success = s.Success,
            result = s.Result
        }).ToList();

        await RespondJson(response, 200, BuildRuntimeResponse(
            ok: result.Success,
            traceId: traceId,
            decision: "PREVIEW",
            riskLevel: "L0",
            parserMode: result.ParserMode,
            actions: actions,
            verification: new { passed = result.Success, reason = result.Success ? "dry-run preview completed" : result.Error },
            error: result.Error));
    }

    private async Task HandleV1TaskRun(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var req = JsonSerializer.Deserialize<ApiV1TaskRequest>(body, _jsonOptions);

        if (req == null || string.IsNullOrEmpty(req.task))
        {
            await RespondJson(response, 400, BuildRuntimeResponse(false, error: "Missing 'task' field"));
            return;
        }

        var traceId = GenerateTraceId();
        var agentReq = new AgentTaskRequest
        {
            Task = req.task,
            MaxSteps = req.max_steps > 0 ? req.max_steps : 5,
            DryRun = req.dry_run,
            Context = req.context,
            TimeoutMs = req.timeout_ms > 0 ? req.timeout_ms : 30000
        };

        var result = await _agentService.ExecuteTaskAsync(agentReq);

        var actions = result.Steps.Select(s => (object)new
        {
            step = s.Step,
            action = s.Action,
            args = s.Args,
            thought = s.Thought,
            success = s.Success,
            result = s.Result
        }).ToList();

        string decision = "ALLOW";
        string riskLevel = "L0";

        if (result.Steps.Any(s => !s.Success))
        {
            decision = "CONFIRM";
            riskLevel = "L1";
        }

        if (!result.Success && result.Error != null)
        {
            decision = "BLOCK";
            riskLevel = "L2";
        }

        if (req.dry_run)
        {
            decision = "PREVIEW";
            riskLevel = "L0";
        }

        await RespondJson(response, 200, BuildRuntimeResponse(
            ok: result.Success,
            traceId: traceId,
            decision: decision,
            riskLevel: riskLevel,
            parserMode: result.ParserMode,
            actions: actions,
            verification: new { passed = result.Success, reason = result.Success ? "task completed" : (result.Error ?? "task failed") },
            error: result.Error));
    }

    private async Task HandleV1SkillSearch(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var req = JsonSerializer.Deserialize<ApiV1SkillSearchRequest>(body, _jsonOptions);

        if (req == null || string.IsNullOrEmpty(req.task))
        {
            await RespondJson(response, 400, BuildRuntimeResponse(false, error: "Missing 'task' field"));
            return;
        }

        var traceId = GenerateTraceId();
        var searchResults = _skillIntegration.Search(req.task, windowTitle: req.window_title);

        var actions = searchResults.Select(r => (object)new
        {
            skill_id = r.Skill.SkillId,
            name = r.Skill.Name,
            app_pattern = r.Skill.AppPattern,
            screen_type = r.Skill.ScreenType,
            risk_level = r.Skill.RiskLevel,
            score = r.Score.Total,
            is_usable = r.Score.IsUsable,
            reason = r.Reason
        }).ToList();

        await RespondJson(response, 200, BuildRuntimeResponse(
            ok: true,
            traceId: traceId,
            decision: "ALLOW",
            riskLevel: "L0",
            actions: actions));
    }

    private async Task HandleV1SkillReplay(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var req = JsonSerializer.Deserialize<ApiV1SkillReplayRequest>(body, _jsonOptions);

        if (req == null || string.IsNullOrEmpty(req.skill_id))
        {
            await RespondJson(response, 400, BuildRuntimeResponse(false, error: "Missing 'skill_id' field"));
            return;
        }

        var traceId = GenerateTraceId();
        var skill = _skillStore.Get(req.skill_id);

        if (skill == null)
        {
            await RespondJson(response, 404, BuildRuntimeResponse(false,
                traceId: traceId,
                decision: "BLOCK",
                riskLevel: "L2",
                error: $"Skill not found: {req.skill_id}"));
            return;
        }

        var report = await _skillReplayEngine.ReplayAsync(skill, req.window_title, req.dry_run);

        var actions = report.StepRecords.Select(r => (object)new
        {
            step_index = r.StepIndex,
            description = r.StepDescription,
            action = r.ParsedAction,
            target = r.Target,
            executed = r.Executed,
            success = r.Success,
            dry_run_skipped = r.DryRunSkipped,
            risk_blocked = r.RiskBlocked,
            risk_score = r.RiskScore,
            error = r.Error
        }).ToList();

        string decision = "ALLOW";
        string riskLevel = "L0";

        if (report.StepsBlocked > 0)
        {
            decision = "BLOCK";
            riskLevel = "L2";
        }
        else if (req.dry_run)
        {
            decision = "PREVIEW";
            riskLevel = "L0";
        }

        await RespondJson(response, 200, BuildRuntimeResponse(
            ok: report.VerificationPassed,
            traceId: traceId,
            decision: decision,
            riskLevel: riskLevel,
            actions: actions,
            verification: new { passed = report.VerificationPassed, reason = report.VerificationPassed ? "skill replay verified" : "skill replay verification failed" }));
    }

    private async Task HandleV1RiskEvaluate(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var req = JsonSerializer.Deserialize<ApiV1RiskEvaluateRequest>(body, _jsonOptions);

        if (req == null || string.IsNullOrEmpty(req.action_type))
        {
            await RespondJson(response, 400, BuildRuntimeResponse(false, error: "Missing 'action_type' field"));
            return;
        }

        var traceId = GenerateTraceId();
        var riskContext = new ActionRiskContext
        {
            ActionType = req.action_type,
            TargetLabel = req.target_label,
            InputText = req.input_text,
            PageType = req.page_type
        };

        var riskDecision = _riskGate.Evaluate(riskContext);

        string decision = riskDecision.Decision switch
        {
            RiskLevel.Allow => "ALLOW",
            RiskLevel.Confirm => "CONFIRM",
            RiskLevel.Block => "BLOCK",
            _ => "ALLOW"
        };

        string riskLevel = riskDecision.RiskScore switch
        {
            < 0.3 => "L0",
            < 0.6 => "L1",
            _ => "L2"
        };

        await RespondJson(response, 200, BuildRuntimeResponse(
            ok: riskDecision.Decision != RiskLevel.Block,
            traceId: traceId,
            decision: decision,
            riskLevel: riskLevel,
            groundingScore: riskContext.GroundingScore,
            verification: new
            {
                passed = riskDecision.Decision != RiskLevel.Block,
                reason = riskDecision.Message,
                block_reason = riskDecision.BlockReason,
                required_confirmation = riskDecision.RequiredConfirmation
            }));
    }

    private static string GenerateTraceId()
    {
        return "trace_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6];
    }

    private object BuildRuntimeResponse(
        bool ok,
        string? traceId = null,
        string decision = "ALLOW",
        string riskLevel = "L0",
        string? parserMode = null,
        double? groundingScore = null,
        List<object>? actions = null,
        object? verification = null,
        string? error = null)
    {
        return new
        {
            ok,
            trace_id = traceId ?? GenerateTraceId(),
            decision,
            risk_level = riskLevel,
            parser_mode = parserMode,
            grounding_score = groundingScore,
            actions = actions ?? [],
            verification,
            error,
            version = "V0.14",
            runtime = "PeekabooWin Agent Runtime",
            timestamp = DateTime.UtcNow
        };
    }

    private object ExecuteCommand(string command, Dictionary<string, string> args)
    {
        switch (command.ToLower())
        {
            case "list-windows":
                return new { success = true, output = JsonSerializer.Serialize(_windowService.ListWindows(args.GetValueOrDefault("--keyword") ?? args.GetValueOrDefault("-k"))) };

            case "screenshot": {
                var outPath = args.GetValueOrDefault("--out") ?? Path.Combine(Path.GetTempPath(), $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                var cap = string.IsNullOrEmpty(args.GetValueOrDefault("--window"))
                    ? _captureService.CaptureScreen(outPath)
                    : _captureService.CaptureWindow(args.GetValueOrDefault("--window")!, outPath);
                return new { success = cap.Success, path = outPath, error = cap.Error };
            }

            case "click": {
                var x = int.TryParse(args.GetValueOrDefault("--x") ?? args.GetValueOrDefault("-x") ?? "0", out var xi) ? xi : 0;
                var y = int.TryParse(args.GetValueOrDefault("--y") ?? args.GetValueOrDefault("-y") ?? "0", out var yi) ? yi : 0;
                var r = _inputService.Click(x, y);
                return (object)r;
            }

            case "type": {
                var text = args.GetValueOrDefault("text") ?? args.GetValueOrDefault("\"") ?? "";
                if (text.StartsWith("\"") && text.EndsWith("\""))
                    text = text.Substring(1, text.Length - 2);
                var r = _inputService.TypeText(text);
                return (object)r;
            }

            case "hotkey": {
                var keys = args.GetValueOrDefault("--keys") ?? args.GetValueOrDefault("-k") ?? "";
                var r = _inputService.Hotkey(keys);
                return (object)r;
            }

            case "inspect": {
                var window = args.GetValueOrDefault("--window") ?? args.GetValueOrDefault("-w");
                if (string.IsNullOrEmpty(window))
                    return new { success = false, error = "Missing --window" };
                var depth = int.TryParse(args.GetValueOrDefault("--max-depth") ?? "5", out var d) ? d : 5;
                var result = _uiaService.Inspect(window, depth);
                return (object)result;
            }

            case "find": {
                var window = args.GetValueOrDefault("--window") ?? args.GetValueOrDefault("-w");
                var name = args.GetValueOrDefault("--name") ?? args.GetValueOrDefault("-n");
                if (string.IsNullOrEmpty(window))
                    return new { success = false, error = "Missing --window" };
                if (string.IsNullOrEmpty(name))
                    return new { success = false, error = "Missing --name" };
                var result = _uiaService.FindByName(window, name);
                return (object)result;
            }

            default:
                return new { success = false, error = $"Unknown command: {command}" };
        }
    }

    private async Task RespondJson(HttpListenerResponse response, int statusCode, object data)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    private class ApiExecuteRequest
    {
        public string command { get; set; } = "";
        public Dictionary<string, string> args { get; set; } = new();
    }

    private class ApiClickRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("x")]
        public int x { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("y")]
        public int y { get; set; }
    }

    private class ApiTypeRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("text")]
        public string text { get; set; } = "";
    }

    private class ApiPressRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("key")]
        public string key { get; set; } = "";
    }

    private class ApiHotkeyRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("keys")]
        public string keys { get; set; } = "";
    }

    private class ApiScreenshotRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("window")]
        public string? window { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("out_path")]
        public string? out_path { get; set; }
    }

    private class ApiAgentRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("task")]
        public string task { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("max_steps")]
        public int max_steps { get; set; } = 5;
        [System.Text.Json.Serialization.JsonPropertyName("dry_run")]
        public bool dry_run { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("context")]
        public string? context { get; set; }
    }

    private class ApiFocusWindowRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string title { get; set; } = "";
    }

    private class ApiOcrRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("window")]
        public string? window { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("text")]
        public string? text { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("lang")]
        public string? lang { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("click")]
        public bool click { get; set; } = false;
    }

    private class ApiV1TaskRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("task")]
        public string task { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("context")]
        public string? context { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("max_steps")]
        public int max_steps { get; set; } = 5;
        [System.Text.Json.Serialization.JsonPropertyName("dry_run")]
        public bool dry_run { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("timeout_ms")]
        public int timeout_ms { get; set; } = 30000;
    }

    private class ApiV1SkillSearchRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("task")]
        public string task { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("window_title")]
        public string? window_title { get; set; }
    }

    private class ApiV1SkillReplayRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("skill_id")]
        public string skill_id { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("window_title")]
        public string? window_title { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("dry_run")]
        public bool dry_run { get; set; } = true;
    }

    private class ApiV1RiskEvaluateRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("action_type")]
        public string action_type { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("target_label")]
        public string? target_label { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("input_text")]
        public string? input_text { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("page_type")]
        public string? page_type { get; set; }
    }
}
