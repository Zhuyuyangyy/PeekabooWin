using System.Net;
using System.Text;
using System.Text.Json;
using PeekabooWin.Core.Agent;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Cli;

/// <summary>
/// HTTP API server for PeekabooWin. Exposes PeekabooWin primitives as REST endpoints
/// so external agents (OpenClaw/Hermes) can drive Windows automation remotely.
/// </summary>
public class ApiServer
{
    private readonly HttpListener _listener;
    private readonly WindowService _windowService;
    private readonly CaptureService _captureService;
    private readonly InputService _inputService;
    private readonly UIAutomationService _uiaService;
    private readonly OcrService _ocrService;
    private readonly JsonSerializerOptions _jsonOptions;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public int Port { get; }
    public bool IsRunning => _listener.IsListening;

    public ApiServer(int port = 8080)
    {
        Port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");

        _windowService = new WindowService();
        _captureService = new CaptureService(_windowService);
        _inputService = new InputService();
        _uiaService = new UIAutomationService(_windowService);
        _ocrService = new OcrService();

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
        Console.WriteLine($"[ApiServer] Started on http://localhost:{Port}/");
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
            catch { /* ignore */ }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var response = ctx.Response;
        var request = ctx.Request;

        // Set CORS headers for cross-origin requests (OpenClaw/Hermes)
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

            // Route: GET /health
            if (path == "/health" && method == "GET")
            {
                await RespondJson(response, 200, new { status = "ok", version = "V0.5", timestamp = DateTime.UtcNow });
                return;
            }

            // Route: GET /windows
            if (path == "/windows" && method == "GET")
            {
                var keyword = request.QueryString["keyword"];
                var windows = _windowService.ListWindows(keyword);
                await RespondJson(response, 200, new { success = true, windows });
                return;
            }

            // Route: GET /inspect?window=X&max_depth=N
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

            // Route: POST /execute (generic command executor)
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

            // Route: POST /click
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

            // Route: POST /type
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

            // Route: POST /press
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

            // Route: POST /hotkey
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

            // Route: POST /screenshot
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

            // Route: POST /agent
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

                var agentService = new AgentService(_windowService, _captureService, _inputService, _uiaService, _ocrService);
                var agentReq = new AgentTaskRequest
                {
                    Task = req.task,
                    MaxSteps = req.max_steps > 0 ? req.max_steps : 5,
                    DryRun = req.dry_run,
                    Context = req.context
                };

                var result = agentService.ExecuteTask(agentReq);
                await RespondJson(response, 200, result);
                return;
            }

            // Route: POST /focus-window
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

            // Route: POST /ocr
            if (path == "/ocr" && method == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<ApiOcrRequest>(body, _jsonOptions);

                var imgPath = Path.Combine(Path.GetTempPath(), $"ocr_api_{Guid.NewGuid():N}.png");

                // Capture
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

                    // Optionally click
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

            // 404
            await RespondJson(response, 404, new { error = $"Route not found: {path}" });
        }
        catch (Exception ex)
        {
            await RespondJson(response, 500, new { error = ex.Message });
        }
    }

    private object ExecuteCommand(string command, Dictionary<string, string> args)
    {
        // Execute a CLI command via the API server's internal primitives
        // This proxies the CLI commands through the same services used by the CLI
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

    // Request DTOs

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
}