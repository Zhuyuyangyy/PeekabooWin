using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Core.Agent;

public class ActionExecutor
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private readonly WindowService _windowService;
    private readonly CaptureService _captureService;
    private readonly InputService _inputService;
    private readonly OcrService _ocrService;
    private readonly UIAutomationService _uiaService;
    private readonly TempFileManager _tempFiles;

    public ActionExecutor(
        WindowService windowService,
        CaptureService captureService,
        InputService inputService,
        OcrService ocrService,
        UIAutomationService uiaService,
        TempFileManager tempFiles)
    {
        _windowService = windowService;
        _captureService = captureService;
        _inputService = inputService;
        _ocrService = ocrService;
        _uiaService = uiaService;
        _tempFiles = tempFiles;
    }

    public (bool success, string result) ExecuteAction(string action, Dictionary<string, string> args)
    {
        return ExecuteActionAsync(action, args).GetAwaiter().GetResult();
    }

    public async Task<(bool success, string result)> ExecuteActionAsync(string action, Dictionary<string, string> args)
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
                var window = args.GetValueOrDefault("window");
                var text = args["text"];
                var outPath = _tempFiles.CreateTempPath("ocr_find");

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

                var ocrResult = await _ocrService.RecognizeImageAsync(outPath);
                if (!string.IsNullOrEmpty(ocrResult.Error))
                    return (false, $"OCR error: {ocrResult.Error}");

                var center = _ocrService.FindWordCenter(ocrResult, text);
                if (center == null)
                    return (false, $"Text '{text}' not found. Recognized: {ocrResult.Text.Substring(0, Math.Min(100, ocrResult.Text.Length))}");

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

                _tempFiles.CleanupFile(outPath);
                return (true, $"Found '{text}' at screen({screenX}, {screenY}) [window-relative: ({center.Value.x}, {center.Value.y})]");
            }

            case "ocr-click":
            {
                var window = args.GetValueOrDefault("window");
                var text = args["text"];
                var outPath = _tempFiles.CreateTempPath("ocr_click");

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

                var ocrResult = await _ocrService.RecognizeImageAsync(outPath);
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
                _tempFiles.CleanupFile(outPath);
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

                var findResult = _uiaService.FindByName(window, name);
                if (!findResult.Success || findResult.Count == 0)
                    return (false, $"Element not found: {name}");

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

                var cap = _captureService.CaptureScreen(outPath);
                if (!cap.Success)
                    return (false, $"Screenshot failed: {cap.Error}");

                using var ocrService = new OcrService("chi_sim+eng");
                var result = await ocrService.RecognizeImageAsync(outPath);

                if (result.Error != null)
                    return (false, $"OCR failed: {result.Error}");

                var words = ocrService.FindWords(result, text);
                if (words.Count == 0)
                    return (false, $"Text not found: {text}");

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
                var result = await ocrService.RecognizeImageAsync(outPath);

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
}
