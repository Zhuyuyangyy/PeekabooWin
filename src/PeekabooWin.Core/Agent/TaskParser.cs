using System.Linq;
using System.Text.Json;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Agent;

public class TaskParser
{
    private readonly ILlmClient? _llmClient;

    public string LastFallbackReason { get; private set; } = "";
    public bool LastLlmEnabled { get; private set; } = true;
    public string LastParserMode { get; private set; } = "none";
    public string LastLlmErrorCode { get; private set; } = "";

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

    public TaskParser(ILlmClient? llmClient = null)
    {
        _llmClient = llmClient;
    }

    public async Task<List<AgentStep>> ParseTaskAsync(string task, string? context = null, CancellationToken cancellationToken = default)
    {
        var lowerTask = task.ToLower().Trim();

        var steps = TryRuleBasedParse(lowerTask, task);
        if (steps.Count > 0)
        {
            LastParserMode = "rule_based";
            LastFallbackReason = "";
            LastLlmEnabled = true;
            LastLlmErrorCode = "";
            return steps;
        }

        return await TryLLMParseAsync(task, context, cancellationToken);
    }

    public ParseTaskMetadata GetLastParseMetadata()
    {
        return new ParseTaskMetadata
        {
            FallbackReason = LastFallbackReason,
            LlmEnabled = LastLlmEnabled,
            ParserMode = LastParserMode,
            LlmErrorCode = LastLlmErrorCode
        };
    }

    private List<AgentStep> TryRuleBasedParse(string lowerTask, string originalTask)
    {
        var steps = new List<AgentStep>();

        if (lowerTask.StartsWith("click ") || lowerTask.StartsWith("click on "))
        {
            var target = lowerTask.Replace("click on ", "").Replace("click ", "").Trim();

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

            if (target.Length > 1)
            {
                steps.Add(new AgentStep
                {
                    Thought = $"Finding and clicking element: {target}",
                    Action = "click-element-guess",
                    Args = new() { ["element"] = target }
                });
                return steps;
            }
        }

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

        if (lowerTask.StartsWith("press "))
        {
            var key = originalTask.Substring(6).Trim();
            var punctIdx = key.IndexOfAny(new[] { ' ', ',', '.' });
            if (punctIdx > 0)
                key = key.Substring(0, punctIdx);
            key = key.Trim();

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

            steps.Add(new AgentStep
            {
                Thought = $"Looking for window: {app}",
                Action = "focus-window",
                Args = new() { ["title"] = app }
            });
            return steps;
        }

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
            window = window.Replace("the ", "")
                     .Replace(" window", "")
                     .Replace(" ui", "")
                     .Replace(" controls", "")
                     .Replace(" application", "")
                     .Trim();

            if (string.IsNullOrEmpty(window))
                window = "notepad";

            steps.Add(new AgentStep
            {
                Thought = $"Inspecting window: {window}",
                Action = "inspect",
                Args = new() { ["window"] = window, ["max_depth"] = "5" }
            });
            return steps;
        }

        if (lowerTask.StartsWith("find ") || lowerTask.StartsWith("click ") || lowerTask.StartsWith("search for "))
        {
            var prefix = "";
            foreach (var p in new[] { "find ", "click ", "search for " })
            {
                if (lowerTask.StartsWith(p))
                { prefix = p; break; }
            }
            var text = originalTask.Substring(prefix.Length).Trim();
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

        return steps;
    }

    private async Task<List<AgentStep>> TryLLMParseAsync(string task, string? context, CancellationToken cancellationToken)
    {
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

        var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY")
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? Environment.GetEnvironmentVariable("MINIMAX_API_KEY");

        if (_llmClient == null || !_llmClient.IsAvailable)
        {
            LastParserMode = "regex_fallback";
            LastFallbackReason = string.IsNullOrEmpty(apiKey) ? "LLM_API_KEY not set" : "LLM client not available";
            LastLlmEnabled = false;
            LastLlmErrorCode = "MISSING_API_KEY";
            PekaLogger.Warn("TaskParser", $"LLM fallback: {LastFallbackReason}, using regex-only parsing");

            return new List<AgentStep>
            {
                new AgentStep
                {
                    Thought = $"Cannot parse task without LLM: {task}",
                    Action = "error",
                    Args = new() { ["message"] = "No LLM API key set, cannot parse complex tasks. Try simpler commands like 'click 100 200' or 'type hello'." }
                }
            };
        }

        try
        {
            var response = await _llmClient.ChatAsync(systemPrompt, userPrompt, cancellationToken);
            var steps = ParseStepsFromLLMResponse(response);
            if (steps.Count > 0)
            {
                LastParserMode = "llm";
                LastFallbackReason = "";
                LastLlmEnabled = true;
                LastLlmErrorCode = "";
                return steps;
            }

            LastParserMode = "llm_failed";
            LastFallbackReason = "LLM returned unparseable response";
            LastLlmEnabled = true;
            LastLlmErrorCode = "LLM_UNPARSEABLE_RESPONSE";
            PekaLogger.Warn("TaskParser", "LLM fallback: LLM response could not be parsed");
        }
        catch (OperationCanceledException)
        {
            LastParserMode = "llm_timeout";
            LastFallbackReason = "LLM call was cancelled or timed out";
            LastLlmEnabled = true;
            LastLlmErrorCode = "LLM_TIMEOUT";
            PekaLogger.Warn("TaskParser", "LLM fallback: LLM call cancelled/timed out");
        }
        catch (HttpRequestException ex)
        {
            LastParserMode = "llm_error";
            LastFallbackReason = $"LLM HTTP error: {ex.Message}";
            LastLlmEnabled = true;
            LastLlmErrorCode = "LLM_HTTP_ERROR";
            PekaLogger.Warn("TaskParser", $"LLM fallback: HTTP error - {ex.Message}");
        }
        catch (Exception ex)
        {
            LastParserMode = "llm_error";
            LastFallbackReason = $"LLM call failed: {ex.Message}";
            LastLlmEnabled = true;
            LastLlmErrorCode = "LLM_UNKNOWN_ERROR";
            PekaLogger.Warn("TaskParser", $"LLM fallback: LLM call failed - {ex.Message}");
        }

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

    private List<AgentStep> ParseStepsFromLLMResponse(string response)
    {
        try
        {
            var steps = JsonSerializer.Deserialize<List<AgentStep>>(response);
            return steps ?? new List<AgentStep>();
        }
        catch
        {
            return new List<AgentStep>();
        }
    }

    private static string ExtractQuotedText(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, "\"([^\"]+)\"");
        if (match.Success)
            return match.Groups[1].Value;
        match = System.Text.RegularExpressions.Regex.Match(text, "'([^']+)'");
        if (match.Success)
            return match.Groups[1].Value;
        return "";
    }
}

public class ParseTaskMetadata
{
    public string FallbackReason { get; set; } = "";
    public bool LlmEnabled { get; set; }
    public string ParserMode { get; set; } = "none";
    public string LlmErrorCode { get; set; } = "";
}