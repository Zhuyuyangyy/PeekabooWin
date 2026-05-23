using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Windows;
using WinRT;

namespace PeekabooWin.Core.Agent;

/// <summary>
/// V0.4 LLM Agent Runtime
/// 
/// Takes natural language tasks and executes them using available tools:
/// - Window management (list, focus, info)
/// - Screenshot capture
/// - Mouse/keyboard input
/// - OCR + find-text
/// - UI Automation (inspect, find, click-element)
/// 
/// Simple prompt-based approach with MiniMax API call.
/// Falls back to rule-based parsing when LLM is unavailable.
/// </summary>
public class AgentService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private readonly WindowService _windowService;
    private readonly CaptureService _captureService;
    private readonly InputService _inputService;
    private readonly UIAutomationService _uiaService;
    private readonly OcrService _ocrService;
    private readonly HttpClient _httpClient;

    private const string MINIMAX_API = "https://api.minimax.chat/v1/chat/completions";
    private const string MINIMAX_MODEL = "MiniMax-M2.7";

    // Tools available to the agent
    private static readonly List<ToolDescriptor> AvailableTools = new()
    {
        new ToolDescriptor { Name = "list-windows", Description = "List all visible windows. Optionally filter by keyword.", Parameters = new() { ["keyword"] = "optional window title keyword" } },
        new ToolDescriptor { Name = "focus-window", Description = "Bring a window to foreground by title keyword.", Parameters = new() { ["title"] = "window title keyword (partial match)" } },
        new ToolDescriptor { Name = "screenshot", Description = "Capture screenshot of full screen or a specific window.", Parameters = new() { ["out"] = "output PNG path", ["window"] = "optional window title keyword" } },
        new ToolDescriptor { Name = "click", Description = "Click at screen coordinates.", Parameters = new() { ["x"] = "X coordinate", ["y"] = "Y coordinate" } },
        new ToolDescriptor { Name = "click-rel", Description = "Click at window-relative coordinates.", Parameters = new() { ["window"] = "window title keyword", ["x"] = "relative X from window left", ["y"] = "relative Y from window top" } },
        new ToolDescriptor { Name = "is-focused", Description = "Check if a window has keyboard focus.", Parameters = new() { ["window"] = "window title keyword to check" } },
        new ToolDescriptor { Name = "find-on-screen", Description = "Use OCR to find text and return screen coordinates.", Parameters = new() { ["window"] = "optional window title", ["text"] = "text to find" } },
        new ToolDescriptor { Name = "ocr-click", Description = "OCR find text then click it in one step.", Parameters = new() { ["window"] = "optional window title", ["text"] = "text to find and click" } },
        new ToolDescriptor { Name = "type", Description = "Type text into the focused window.", Parameters = new() { ["text"] = "text to type" } },
        new ToolDescriptor { Name = "press", Description = "Press a keyboard key (enter/esc/tab/backspace/delete).", Parameters = new() { ["key"] = "key name" } },
        new ToolDescriptor { Name = "hotkey", Description = "Execute keyboard hotkey combination.", Parameters = new() { ["keys"] = "e.g. ctrl+c, alt+f4, win+r" } },
        new ToolDescriptor { Name = "inspect", Description = "Inspect UI Automation tree of a window.", Parameters = new() { ["window"] = "window title keyword", ["max_depth"] = "optional max depth (default 5)" } },
        new ToolDescriptor { Name = "find", Description = "Find UI element by name, control-type or automation-id.", Parameters = new() { ["window"] = "window title keyword", ["name"] = "element name", ["control-type"] = "e.g. button, edit", ["automation-id"] = "element ID" } },
        new ToolDescriptor { Name = "click-element", Description = "Click a UI element by name (uses UIA, not coordinates).", Parameters = new() { ["window"] = "window title keyword", ["name"] = "element name to click" } },
        new ToolDescriptor { Name = "ocr", Description = "Recognize text in a screenshot. Can search for text and click.", Parameters = new() { ["window"] = "optional window title keyword", ["text"] = "optional text to search for and click" } },
    };

    public AgentService(WindowService windowService, CaptureService captureService, InputService inputService, UIAutomationService uiaService, OcrService ocrService)
    {
        _windowService = windowService;
        _captureService = captureService;
        _inputService = inputService;
        _uiaService = uiaService;
        _ocrService = ocrService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Execute a natural language task
    /// </summary>
    public AgentTaskResponse ExecuteTask(AgentTaskRequest request)
    {
        var response = new AgentTaskResponse
        {
            Task = request.Task,
            LlmModel = MINIMAX_MODEL
        };

        try
        {
            // Step 1: Parse task into action plan
            var plan = ParseTask(request.Task, request.Context);
            var steps = new List<AgentStep>();

            // Step 2: Execute each step
            for (int i = 0; i < Math.Min(plan.Count, request.MaxSteps); i++)
            {
                var step = plan[i];
                var stepResult = new AgentStep
                {
                    Step = i + 1,
                    Thought = step.Thought,
                    Action = step.Action,
                    Args = step.Args
                };

                try
                {
                    var (success, result) = ExecuteAction(step.Action, step.Args);
                    stepResult.Success = success;
                    stepResult.Result = result;
                    steps.Add(stepResult);

                    if (!success && !request.DryRun)
                    {
                        // Abort on failure unless dry-run
                        break;
                    }
                }
                catch (Exception ex)
                {
                    stepResult.Success = false;
                    stepResult.Error = ex.Message;
                    steps.Add(stepResult);
                    break;
                }
            }

            response.Steps = steps;
            response.Success = steps.All(s => s.Success);
            response.FinalResult = BuildFinalResult(steps);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = ex.Message;
        }

        return response;
    }

    /// <summary>
    /// Parse natural language task into action steps.
    /// First tries rule-based parsing, then falls back to LLM.
    /// </summary>
    private List<AgentStep> ParseTask(string task, string? context = null)
    {
        var lowerTask = task.ToLower().Trim();

        // Rule-based parsing for common patterns
        var steps = TryRuleBasedParse(lowerTask, task);
        if (steps.Count > 0)
            return steps;

        // Fall back to LLM parsing
        return TryLLMParse(task, context);
    }

    private List<AgentStep> TryRuleBasedParse(string lowerTask, string originalTask)
    {
        var steps = new List<AgentStep>();

        // Pattern: "click [something]" or "click on [something]"
        if (lowerTask.StartsWith("click ") || lowerTask.StartsWith("click on "))
        {
            var target = lowerTask.Replace("click on ", "").Replace("click ", "").Trim();

            // Check if it's a coordinate click "click 100 200"
            var coordParts = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (coordParts.Length == 2 && int.TryParse(coordParts[0], out _) && int.TryParse(coordParts[1], out _))
            {
                steps.Add(new AgentStep
                {
                    Thought = $"Clicking at coordinates ({coordParts[0]}, {coordParts[1]})",
                    Action = "click",
                    Args = new() { ["x"] = coordParts[0], ["y"] = coordParts[1] }
                });
                return steps;
            }

            // Check if it matches a UI element
            if (target.Length > 1)
            {
                // Try to find the window containing this element, then click it
                // e.g. "click the save button" -> find save button in focused window
                steps.Add(new AgentStep
                {
                    Thought = $"Finding and clicking element: {target}",
                    Action = "click-element-guess",
                    Args = new() { ["element"] = target }
                });
                return steps;
            }
        }

        // Pattern: "type [text]" or "enter [text]"
        if (lowerTask.StartsWith("type ") || lowerTask.StartsWith("enter ") || lowerTask.StartsWith("input "))
        {
            var text = originalTask;
            foreach (var prefix in new[] { "type ", "enter ", "input " })
            {
                if (lowerTask.StartsWith(prefix))
                {
                    text = originalTask.Substring(prefix.Length).Trim().Trim('"', '\'');
                    break;
                }
            }

            if (!string.IsNullOrEmpty(text))
            {
                steps.Add(new AgentStep
                {
                    Thought = $"Typing text: {text}",
                    Action = "type",
                    Args = new() { ["text"] = text }
                });
            }
            return steps;
        }

        // Pattern: "press [key]" (hotkey like ctrl+a, alt+f4, etc)
        if (lowerTask.StartsWith("press "))
        {
            // Extract the hotkey: take text after "press ", stop at common sentence endings
            var key = originalTask.Substring(6).Trim(); // skip "press "
            // Stop at sentence boundaries
            var punctIdx = key.IndexOfAny(new[] { ' ', ',', '.' });
            if (punctIdx > 0)
                key = key.Substring(0, punctIdx);
            key = key.Trim();

            // Hotkey (e.g. ctrl+a, alt+f4) uses the "hotkey" action
            if (key.Contains("+") || key.Contains("-"))
            {
                steps.Add(new AgentStep
                {
                    Thought = $"Executing hotkey: {key}",
                    Action = "hotkey",
                    Args = new() { ["keys"] = key }
                });
            }
            else
            {
                steps.Add(new AgentStep
                {
                    Thought = $"Pressing key: {key}",
                    Action = "press",
                    Args = new() { ["key"] = key }
                });
            }
            return steps;
        }

        // Pattern: "open [app]" or "launch [app]" or "focus [window]"
        if (lowerTask.StartsWith("open ") || lowerTask.StartsWith("launch ") || lowerTask.StartsWith("start ") || lowerTask.StartsWith("focus ") || lowerTask.StartsWith("bring "))
        {
            var app = lowerTask;
            foreach (var prefix in new[] { "open ", "launch ", "start ", "focus ", "bring " })
            {
                if (lowerTask.StartsWith(prefix))
                {
                    app = originalTask.Substring(prefix.Length).Trim();
                    break;
                }
            }

            // Try to focus existing window first
            steps.Add(new AgentStep
            {
                Thought = $"Looking for window: {app}",
                Action = "focus-window",
                Args = new() { ["title"] = app }
            });
            return steps;
        }

        // Pattern: "take a screenshot" or "screenshot"
        if (lowerTask.Contains("screenshot") || lowerTask.Contains("截图"))
        {
            var outPath = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            steps.Add(new AgentStep
            {
                Thought = "Taking screenshot",
                Action = "screenshot",
                Args = new() { ["out"] = outPath }
            });
            return steps;
        }

        // Pattern: "list windows" or "show windows"
        if (lowerTask.Contains("list window") || lowerTask.Contains("show window") || lowerTask == "windows")
        {
            steps.Add(new AgentStep
            {
                Thought = "Listing all windows",
                Action = "list-windows",
                Args = new()
            });
            return steps;
        }

        // Pattern: OCR task
        if (lowerTask.Contains("read ") || lowerTask.Contains("ocr ") || lowerTask.Contains("recognize ") ||
            lowerTask.Contains("读取") || lowerTask.Contains("识别"))
        {
            var textToFind = ExtractQuotedText(originalTask);
            var outPath = $"ocr_{DateTime.Now:yyyyMMdd_HHmmss}.png";

            if (!string.IsNullOrEmpty(textToFind))
            {
                steps.Add(new AgentStep
                {
                    Thought = $"OCR and find text: {textToFind}",
                    Action = "ocr-find",
                    Args = new() { ["text"] = textToFind, ["out"] = outPath }
                });
            }
            else
            {
                steps.Add(new AgentStep
                {
                    Thought = "OCR screenshot",
                    Action = "ocr",
                    Args = new() { ["out"] = outPath }
                });
            }
            return steps;
        }

        // Pattern: "inspect [window]" or "look at [window]"
        if (lowerTask.StartsWith("inspect ") || lowerTask.StartsWith("look at ") || lowerTask.StartsWith("check "))
        {
            var window = "";
            foreach (var prefix in new[] { "inspect ", "look at ", "check " })
            {
                if (lowerTask.StartsWith(prefix))
                {
                    window = originalTask.Substring(prefix.Length).Trim();
                    break;
                }
            }
            // Clean up window name
            // Remove leading "the " and trailing descriptors
            window = window.Replace("the ", "")
                     .Replace(" window", "")
                     .Replace(" ui", "")
                     .Replace(" controls", "")
                     .Replace(" application", "")
                     .Trim();

            if (string.IsNullOrEmpty(window))
                window = "notepad"; // default

            steps.Add(new AgentStep
            {
                Thought = $"Inspecting window: {window}",
                Action = "inspect",
                Args = new() { ["window"] = window, ["max_depth"] = "5" }
            });
            return steps;
        }

        // Pattern: "find [element] in [window]" or "click [element]"
        if (lowerTask.StartsWith("find ") || lowerTask.StartsWith("click ") || lowerTask.StartsWith("search for "))
        {
            var prefix = "";
            foreach (var p in new[] { "find ", "click ", "search for " })
            {
                if (lowerTask.StartsWith(p))
                { prefix = p; break; }
            }
            var text = originalTask.Substring(prefix.Length).Trim();
            // Extract window name if "in [window]" pattern
            string? window = null;
            var inIdx = text.LastIndexOf(" in ", StringComparison.OrdinalIgnoreCase);
            if (inIdx > 0)
            {
                window = text.Substring(inIdx + 4).Trim();
                text = text.Substring(0, inIdx).Trim();
            }

            if (prefix == "click " || lowerTask.Contains("click element"))
            {
                var args = new Dictionary<string, string> { ["element"] = text };
                if (!string.IsNullOrEmpty(window))
                    args["window"] = window;
                steps.Add(new AgentStep
                {
                    Thought = $"Clicking element: {text}",
                    Action = "click-element-guess",
                    Args = args
                });
            }
            else
            {
                var args = new Dictionary<string, string> { ["name"] = text };
                if (!string.IsNullOrEmpty(window))
                    args["window"] = window;
                steps.Add(new AgentStep
                {
                    Thought = $"Finding element: {text}",
                    Action = "find",
                    Args = args
                });
            }
            return steps;
        }

        return steps; // No rule matched
    }

    private List<AgentStep> TryLLMParse(string task, string? context = null)
    {
        // Build prompt for LLM
        var toolsJson = JsonSerializer.Serialize(AvailableTools, new JsonSerializerOptions { WriteIndented = false });

        var systemPrompt = $@"You are a Windows desktop automation agent. Given a task in natural language, output a JSON array of action steps.

Available tools:
{toolsJson}

Rules:
- Each step has: thought (string), action (string), args (dict of string→string)
- Use the exact tool names from the list above
- For click-element, pass window title in args.window and element name in args.name
- Output ONLY valid JSON, no markdown, no explanation
- For multi-step tasks, output multiple steps in order
- If task is ambiguous, pick the most reasonable interpretation

Example:
Task: ""open notepad and type hello""
Output: [
  {{""thought"":""Open notepad window"",""action"":""focus-window"",""args"":{{""title"":""notepad""}}}},
  {{""thought"":""Type hello"",""action"":""type"",""args"":{{""text"":""hello""}}}}
]";

        var userPrompt = $@"Task: ""{task}"""
            + (string.IsNullOrEmpty(context) ? "" : $"\nContext: {context}");

        // Try to call MiniMax API
        var apiKey = Environment.GetEnvironmentVariable("MINIMAX_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // No API key - return a single inspect step as fallback to get window list
            return new List<AgentStep>
            {
                new AgentStep
                {
                    Thought = $"Cannot parse task without LLM: {task}",
                    Action = "error",
                    Args = new() { ["message"] = "No MINIMAX_API_KEY set, cannot parse complex tasks. Try simpler commands like 'click 100 200' or 'type hello'." }
                }
            };
        }

        try
        {
            var response = CallMiniMax(systemPrompt, userPrompt, apiKey);
            var steps = ParseStepsFromLLMResponse(response);
            if (steps.Count > 0)
                return steps;
        }
        catch
        {
            // LLM failed, fall through
        }

        // Last resort: return error step
        return new List<AgentStep>
        {
            new AgentStep
            {
                Thought = $"Could not parse task: {task}",
                Action = "error",
                Args = new() { ["message"] = "Task parsing failed. Try commands like 'click 100 200', 'type hello', 'press enter'." }
            }
        };
    }

    private (bool success, string result) ExecuteAction(string action, Dictionary<string, string> args)
    {
        switch (action)
        {
            case "click":
            {
                var x = int.Parse(args["x"]);
                var y = int.Parse(args["y"]);
                _inputService.Click(x, y);
                return (true, $"Clicked at ({x}, {y})");
            }

            case "click-rel":
            {
                // Click at window-relative coordinates
                var window = args["window"];
                var relX = int.Parse(args["x"]);
                var relY = int.Parse(args["y"]);
                var win = _windowService.FindWindow(window);
                if (win == null)
                    return (false, $"Window not found: {window}");
                var absX = win.Rect.X + relX;
                var absY = win.Rect.Y + relY;
                _inputService.Click(absX, absY);
                return (true, $"Clicked rel({relX}, {relY}) → abs({absX}, {absY}) in window '{win.Title}'");
            }

            case "is-focused":
            {
                // Check if the specified window is currently focused
                var windowKeyword = args.GetValueOrDefault("window", "");
                var foregroundHwnd = GetForegroundWindow();
                var allWindows = _windowService.ListWindows(null);
                var focusedWin = allWindows.FirstOrDefault(w => w.Handle == foregroundHwnd.ToInt64());
                if (focusedWin == null)
                    return (true, $"Foreground window handle: {foregroundHwnd}, not tracked");
                var isMatch = string.IsNullOrEmpty(windowKeyword)
                    || focusedWin.Title.Contains(windowKeyword, StringComparison.OrdinalIgnoreCase);
                return (true, $"Focused: '{focusedWin.Title}' | matches '{windowKeyword}': {isMatch}");
            }

            case "find-on-screen":
            {
                // Use OCR to find text and return screen absolute coordinates
                var window = args.GetValueOrDefault("window");
                var text = args["text"];
                var outPath = Path.Combine(Path.GetTempPath(), $"ocr_find_{Guid.NewGuid()}.png");

                CaptureResult cap;
                if (!string.IsNullOrEmpty(window))
                {
                    var win = _windowService.FindWindow(window);
                    if (win == null) return (false, $"Window not found: {window}");
                    cap = _captureService.CaptureWindow(win.Handle.ToString(), outPath);
                }
                else
                {
                    cap = _captureService.CaptureScreen(outPath);
                }
                if (!cap.Success) return (false, $"Screenshot failed: {cap.Error}");

                var ocrResult = _ocrService.RecognizeImageAsync(outPath).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(ocrResult.Error))
                    return (false, $"OCR error: {ocrResult.Error}");

                var center = _ocrService.FindWordCenter(ocrResult, text);
                if (center == null)
                    return (false, $"Text '{text}' not found. Recognized: {ocrResult.Text.Substring(0, Math.Min(100, ocrResult.Text.Length))}");

                // If captured from window, add window offset
                int screenX = center.Value.x;
                int screenY = center.Value.y;
                if (!string.IsNullOrEmpty(window))
                {
                    var win = _windowService.FindWindow(window);
                    if (win != null)
                    {
                        screenX += win.Rect.X;
                        screenY += win.Rect.Y;
                    }
                }

                try { File.Delete(outPath); } catch { }
                return (true, $"Found '{text}' at screen({screenX}, {screenY}) [window-relative: ({center.Value.x}, {center.Value.y})]");
            }

            case "ocr-click":
            {
                // OCR find text, then click it — two-step as one
                var window = args.GetValueOrDefault("window");
                var text = args["text"];
                var outPath = Path.Combine(Path.GetTempPath(), $"ocr_click_{Guid.NewGuid()}.png");

                CaptureResult cap;
                if (!string.IsNullOrEmpty(window))
                {
                    var win = _windowService.FindWindow(window);
                    if (win == null) return (false, $"Window not found: {window}");
                    cap = _captureService.CaptureWindow(win.Handle.ToString(), outPath);
                }
                else
                {
                    cap = _captureService.CaptureScreen(outPath);
                }
                if (!cap.Success) return (false, $"Screenshot failed: {cap.Error}");

                var ocrResult = _ocrService.RecognizeImageAsync(outPath).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(ocrResult.Error))
                    return (false, $"OCR error: {ocrResult.Error}");

                var center = _ocrService.FindWordCenter(ocrResult, text);
                if (center == null)
                    return (false, $"Text '{text}' not found on screen");

                int screenX = center.Value.x;
                int screenY = center.Value.y;
                if (!string.IsNullOrEmpty(window))
                {
                    var win = _windowService.FindWindow(window);
                    if (win != null)
                    {
                        screenX += win.Rect.X;
                        screenY += win.Rect.Y;
                    }
                }

                _inputService.Click(screenX, screenY);
                try { File.Delete(outPath); } catch { }
                return (true, $"OCR-click '{text}' at screen({screenX}, {screenY})");
            }

            case "type":
            {
                var text = args["text"];
                _inputService.TypeText(text);
                return (true, $"Typed: {text}");
            }

            case "press":
            {
                var key = args["key"];
                _inputService.PressKeyByName(key);
                return (true, $"Pressed: {key}");
            }

            case "hotkey":
            {
                var keys = args["keys"];
                _inputService.Hotkey(keys);
                return (true, $"Executed hotkey: {keys}");
            }

            case "list-windows":
            {
                var windows = _windowService.ListWindows(null);
                var result = string.Join("\n", windows.Select(w => $"[{w.Handle}] {w.Title}"));
                return (true, result);
            }

            case "focus-window":
            {
                var title = args["title"];
                var win = _windowService.FindWindow(title);
                if (win != null)
                {
                    _windowService.FocusWindow(win.HandleIntPtr);
                    return (true, $"Focused window: {win.Title}");
                }
                return (false, $"Window not found: {title}");
            }

            case "screenshot":
            {
                var outPath = args["out"];
                var window = args.GetValueOrDefault("window");
                CaptureResult cap;
                if (!string.IsNullOrEmpty(window))
                {
                    var win = _windowService.FindWindow(window);
                    if (win == null)
                        return (false, $"Window not found: {window}");
                    cap = _captureService.CaptureWindow(win.Handle.ToString(), outPath);
                }
                else
                {
                    cap = _captureService.CaptureScreen(outPath);
                }
                return (cap.Success, cap.Success ? $"Screenshot saved: {outPath}" : $"Screenshot failed: {cap.Error}");
            }

            case "inspect":
            {
                var window = args["window"];
                var maxDepth = int.Parse(args.GetValueOrDefault("max_depth", "5"));
                var result = _uiaService.Inspect(window, maxDepth);
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
                return (result.Success, json);
            }

            case "find":
            {
                var window = args["window"];
                var name = args.GetValueOrDefault("name");
                var controlType = args.GetValueOrDefault("control-type");
                var autoId = args.GetValueOrDefault("automation-id");

                UIAFindResult result;
                if (!string.IsNullOrEmpty(name))
                    result = _uiaService.FindByName(window, name);
                else if (!string.IsNullOrEmpty(controlType))
                    result = _uiaService.FindByControlType(window, controlType).Matches.Count > 0
                        ? new UIAFindResult { Success = true, Matches = _uiaService.FindByControlType(window, controlType).Matches }
                        : new UIAFindResult { Success = false, Error = "Not found" };
                else if (!string.IsNullOrEmpty(autoId))
                    result = _uiaService.FindByAutomationId(window, autoId);
                else
                    return (false, "Must specify --name, --control-type, or --automation-id");

                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
                return (result.Success, json);
            }

            case "click-element":
            {
                var window = args["window"];
                var name = args["name"];

                // Find the element first
                var findResult = _uiaService.FindByName(window, name);
                if (!findResult.Success || findResult.Count == 0)
                    return (false, $"Element not found: {name}");

                // Get bounding box and click center
                var el = findResult.Matches[0];
                if (el.BoundingBox != null)
                {
                    var cx = (int)(el.BoundingBox.X + el.BoundingBox.Width / 2);
                    var cy = (int)(el.BoundingBox.Y + el.BoundingBox.Height / 2);
                    _inputService.Click(cx, cy);
                    return (true, $"Clicked element '{name}' at ({cx}, {cy})");
                }
                return (false, $"Element '{name}' has no bounding box");
            }

            case "click-element-guess":
            {
                // Try to find element in foreground window
                // Get the currently active window title
                var windows = _windowService.ListWindows(null);
                var activeWin = windows.OrderByDescending(w => w.Handle).FirstOrDefault();
                if (activeWin == null)
                    return (false, "No active window found");

                var element = args["element"];
                var findResult = _uiaService.FindByName(activeWin.Title, element);
                if (findResult.Success && findResult.Count > 0)
                {
                    var el = findResult.Matches[0];
                    if (el.BoundingBox != null)
                    {
                        var cx = (int)(el.BoundingBox.X + el.BoundingBox.Width / 2);
                        var cy = (int)(el.BoundingBox.Y + el.BoundingBox.Height / 2);
                        _inputService.Click(cx, cy);
                        return (true, $"Clicked '{element}' at ({cx}, {cy}) in '{activeWin.Title}'");
                    }
                }

                // Fallback: try to find window with element name as keyword
                foreach (var win in windows.Where(w => w.Title.Contains(element)))
                {
                    var r = _uiaService.FindByName(win.Title, element);
                    if (r.Success && r.Count > 0)
                    {
                        var el = r.Matches[0];
                        var cx = (int)(el.BoundingBox!.X + el.BoundingBox.Width / 2);
                        var cy = (int)(el.BoundingBox.Y + el.BoundingBox.Height / 2);
                        _inputService.Click(cx, cy);
                        return (true, $"Clicked '{element}' at ({cx}, {cy})");
                    }
                }

                return (false, $"Cannot find element: {element}");
            }

            case "ocr-find":
            {
                var text = args["text"];
                var outPath = args["out"];

                // Capture the window or screen and do OCR
                // For now, do full screen OCR
                var cap = _captureService.CaptureScreen(outPath);
                if (!cap.Success)
                    return (false, $"Screenshot failed: {cap.Error}");

                // Use OcrService to recognize and find text
                using var ocrService = new OcrService("chi_sim+eng");
                var result = ocrService.RecognizeImageAsync(outPath).GetAwaiter().GetResult();

                if (result.Error != null)
                    return (false, $"OCR failed: {result.Error}");

                // Find the text
                var words = ocrService.FindWords(result, text);
                if (words.Count == 0)
                    return (false, $"Text not found: {text}");

                // Click the first match
                var word = words[0];
                if (word.BoundingBox != null)
                {
                    var cx = (int)(word.BoundingBox.X + word.BoundingBox.Width / 2);
                    var cy = (int)(word.BoundingBox.Y + word.BoundingBox.Height / 2);
                    _inputService.Click(cx, cy);
                    return (true, $"Found '{text}' at ({cx}, {cy}), clicked");
                }
                return (false, $"Text found but no bounding box: {text}");
            }

            case "ocr":
            {
                var outPath = args["out"];
                var cap = _captureService.CaptureScreen(outPath);
                if (!cap.Success)
                    return (false, $"Screenshot failed: {cap.Error}");

                using var ocrService = new OcrService("chi_sim+eng");
                var result = ocrService.RecognizeImageAsync(outPath).GetAwaiter().GetResult();

                if (result.Error != null)
                    return (false, $"OCR failed: {result.Error}");

                return (true, $"Recognized {result.Words.Count} words: {result.Text.Substring(0, Math.Min(200, result.Text.Length))}");
            }

            case "error":
                return (false, args.GetValueOrDefault("message", "Unknown error"));

            default:
                return (false, $"Unknown action: {action}");
        }
    }

    private string CallMiniMax(string systemPrompt, string userPrompt, string apiKey)
    {
        var requestBody = new
        {
            model = MINIMAX_MODEL,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.1,
            max_tokens = 1024
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, MINIMAX_API);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = content;

        var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);

        return responseObj.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "[]";
    }

    private List<AgentStep> ParseStepsFromLLMResponse(string response)
    {
        try
        {
            // Try to parse as JSON array
            var steps = JsonSerializer.Deserialize<List<AgentStep>>(response);
            return steps ?? new List<AgentStep>();
        }
        catch
        {
            return new List<AgentStep>();
        }
    }

    private string BuildFinalResult(List<AgentStep> steps)
    {
        if (steps.Count == 0)
            return "No steps executed";
        if (steps.All(s => s.Success))
            return $"Completed {steps.Count} step(s)";
        var failed = steps.FirstOrDefault(s => !s.Success);
        return $"Failed at step {failed?.Step}: {failed?.Error ?? "unknown error"}";
    }

    private static string ExtractQuotedText(string text)
    {
        // Extract text in quotes
        var match = System.Text.RegularExpressions.Regex.Match(text, "\"([^\"]+)\"");
        if (match.Success)
            return match.Groups[1].Value;
        match = System.Text.RegularExpressions.Regex.Match(text, "'([^']+)'");
        if (match.Success)
            return match.Groups[1].Value;
        return "";
    }
}