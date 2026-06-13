using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.Perception;
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
    private readonly PerceptionRouter _perceptionRouter;

    public ActionExecutor(
        WindowService windowService,
        CaptureService captureService,
        InputService inputService,
        OcrService ocrService,
        UIAutomationService uiaService,
        TempFileManager tempFiles,
        PerceptionRouter perceptionRouter)
    {
        _windowService = windowService;
        _captureService = captureService;
        _inputService = inputService;
        _ocrService = ocrService;
        _uiaService = uiaService;
        _tempFiles = tempFiles;
        _perceptionRouter = perceptionRouter;
    }

    public async Task<(bool success, string result)> ExecuteActionAsync(string action, Dictionary<string, string> args, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (action)
        {
            case "click":
            {
                var x = int.Parse(args["x"]);
                var y = int.Parse(args["y"]);

                // Click coordinates are physical screen pixels.
                var (screenWidth, screenHeight) = DpiContext.Default.GetPhysicalScreenBounds();
                if (x < 0 || y < 0 || x >= screenWidth || y >= screenHeight)
                {
                    return (false, $"Click coordinates ({x}, {y}) are outside screen bounds ({screenWidth}x{screenHeight})");
                }

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

                // WindowService.Rect uses LOGICAL coords; InputService.Click expects PHYSICAL pixels.
                double scale = DpiContext.Default.GetScaleFactor(win.HandleIntPtr);
                var absX = (int)Math.Round(win.Rect.X * scale) + (int)Math.Round(relX * scale);
                var absY = (int)Math.Round(win.Rect.Y * scale) + (int)Math.Round(relY * scale);
                _inputService.Click(absX, absY);
                return (true, $"Clicked rel({relX}, {relY}) → abs({absX}, {absY}) in window '{win.Title}' (scale={scale:F2})");
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
                cancellationToken.ThrowIfCancellationRequested();
                var window = args.GetValueOrDefault("window");
                var text = args["text"];
                var outPath = _tempFiles.CreateTempPath("ocr_find");

                CaptureResult cap;
                double dpiScale = 1.0;
                if (!string.IsNullOrEmpty(window))
                {
                    var win = _windowService.FindWindow(window);
                    if (win == null) return (false, $"Window not found: {window}");
                    cap = _captureService.CaptureWindow(win.Handle.ToString(), outPath);
                    dpiScale = cap.ScaleFactor > 0 ? cap.ScaleFactor : DpiContext.Default.GetScaleFactor(win.HandleIntPtr);
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

                // OCR returns physical pixel coords relative to the screenshot image.
                // For window captures, add the window's PHYSICAL position on screen.
                // WindowService.Rect uses LOGICAL (DPI-virtualized) coords, so scale by DPI.
                int screenX = center.Value.x;
                int screenY = center.Value.y;
                if (!string.IsNullOrEmpty(window))
                {
                    var win = _windowService.FindWindow(window);
                    if (win != null)
                    {
                        screenX += (int)Math.Round(win.Rect.X * dpiScale);
                        screenY += (int)Math.Round(win.Rect.Y * dpiScale);
                    }
                }

                _tempFiles.CleanupFile(outPath);
                return (true, $"Found '{text}' at screen({screenX}, {screenY}) [image-relative: ({center.Value.x}, {center.Value.y}), dpi_scale={dpiScale:F2}]");
            }

            case "ocr-click":
            {
                cancellationToken.ThrowIfCancellationRequested();
                var window = args.GetValueOrDefault("window");
                var text = args["text"];

                // Use PerceptionRouter (UIA -> LLM -> OCR fallback pipeline)
                var perceptionResult = await _perceptionRouter.GroundElementAsync(window, text, cancellationToken);

                if (perceptionResult.Element == null || !perceptionResult.IsConfident)
                {
                    return (false, $"Text '{text}' not found via perception router. " +
                        $"Source: {perceptionResult.Source}, Reason: {perceptionResult.FallbackReason ?? "unknown"}");
                }

                PekaLogger.Info("ActionExecutor",
                    $"ocr-click: found '{text}' via {perceptionResult.Source} " +
                    $"(confidence={perceptionResult.Confidence:F2}, latency={perceptionResult.LatencyMs:F0}ms)");

                var groundedEl = perceptionResult.Element;

                // Use preferred click strategy from grounded element
                if (groundedEl.PreferredClickStrategy == ClickStrategy.UIA_Invoke && groundedEl.RawUiaElement != null)
                {
                    var invokeResult = _uiaService.InvokeElement(groundedEl.RawUiaElement);
                    if (invokeResult.Success)
                    {
                        return (true, $"OCR-click '{text}' via {invokeResult.Method} " +
                            $"(source={perceptionResult.Source}, confidence={perceptionResult.Confidence:F2})");
                    }
                }

                // Fallback to coordinate click using the grounded element's click point
                var clickX = (int)groundedEl.ClickPoint.X;
                var clickY = (int)groundedEl.ClickPoint.Y;
                _inputService.Click(clickX, clickY);
                return (true, $"OCR-click '{text}' at ({clickX}, {clickY}) " +
                    $"(source={perceptionResult.Source}, confidence={perceptionResult.Confidence:F2})");
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
                {
                    // Fixed: removed duplicate FindByControlType call (was called twice on lines 252-253)
                    result = _uiaService.FindByControlType(window, controlType);
                    if (result.Matches.Count == 0)
                        result = new UIAFindResult { Success = false, Error = "Not found" };
                }
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

                // Use PerceptionRouter for element grounding (UIA -> LLM -> OCR pipeline)
                var perceptionResult = await _perceptionRouter.GroundElementAsync(window, name, cancellationToken);

                if (perceptionResult.Element == null || !perceptionResult.IsConfident)
                {
                    return (false, $"Element not found: {name}. " +
                        $"Source: {perceptionResult.Source}, Reason: {perceptionResult.FallbackReason ?? "unknown"}");
                }

                PekaLogger.Info("ActionExecutor",
                    $"click-element: found '{name}' via {perceptionResult.Source} " +
                    $"(confidence={perceptionResult.Confidence:F2}, latency={perceptionResult.LatencyMs:F0}ms)");

                var groundedEl = perceptionResult.Element;

                // If UIA_Invoke strategy and raw element available, use InvokePattern directly
                if (groundedEl.PreferredClickStrategy == ClickStrategy.UIA_Invoke && groundedEl.RawUiaElement != null)
                {
                    var invokeResult = _uiaService.InvokeElement(groundedEl.RawUiaElement);
                    if (invokeResult.Success)
                    {
                        return (true, $"Clicked element '{name}' via {invokeResult.Method} " +
                            $"(source={perceptionResult.Source}, confidence={perceptionResult.Confidence:F2})");
                    }
                    // If invoke failed, fall through to coordinate click
                    PekaLogger.Warn("ActionExecutor",
                        $"UIA_Invoke failed for '{name}' ({invokeResult.ErrorDetail}), falling back to coordinate click");
                }

                // Coordinate click using the grounded element's click point
                var clickX = (int)groundedEl.ClickPoint.X;
                var clickY = (int)groundedEl.ClickPoint.Y;
                _inputService.Click(clickX, clickY);
                return (true, $"Clicked element '{name}' at ({clickX}, {clickY}) " +
                    $"(source={perceptionResult.Source}, confidence={perceptionResult.Confidence:F2})");
            }

            case "click-element-guess":
            {
                // Get the actual foreground window via P/Invoke
                var foregroundHwnd = GetForegroundWindow();
                var allWindows = _windowService.ListWindows(null);
                var activeWin = allWindows.FirstOrDefault(w => w.Handle == foregroundHwnd.ToInt64());

                if (activeWin == null)
                    return (false, "No active window found");

                var element = args["element"];

                // Use PerceptionRouter with the active window title for better element finding
                var perceptionResult = await _perceptionRouter.GroundElementAsync(activeWin.Title, element, cancellationToken);

                if (perceptionResult.Element != null && perceptionResult.IsConfident)
                {
                    PekaLogger.Info("ActionExecutor",
                        $"click-element-guess: found '{element}' via {perceptionResult.Source} " +
                        $"in window '{activeWin.Title}' (confidence={perceptionResult.Confidence:F2})");

                    var groundedEl = perceptionResult.Element;

                    // Use preferred click strategy
                    if (groundedEl.PreferredClickStrategy == ClickStrategy.UIA_Invoke && groundedEl.RawUiaElement != null)
                    {
                        var invokeResult = _uiaService.InvokeElement(groundedEl.RawUiaElement);
                        if (invokeResult.Success)
                        {
                            return (true, $"Clicked '{element}' via {invokeResult.Method} in '{activeWin.Title}' " +
                                $"(source={perceptionResult.Source}, confidence={perceptionResult.Confidence:F2})");
                        }
                    }

                    var clickX = (int)groundedEl.ClickPoint.X;
                    var clickY = (int)groundedEl.ClickPoint.Y;
                    _inputService.Click(clickX, clickY);
                    return (true, $"Clicked '{element}' at ({clickX}, {clickY}) in '{activeWin.Title}' " +
                        $"(source={perceptionResult.Source}, confidence={perceptionResult.Confidence:F2})");
                }

                // Fallback: search across all windows using PerceptionRouter
                foreach (var win in allWindows.Where(w => w.Handle != activeWin.Handle))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fallbackResult = await _perceptionRouter.GroundElementAsync(win.Title, element, cancellationToken);
                    if (fallbackResult.Element != null && fallbackResult.IsConfident)
                    {
                        var groundedEl = fallbackResult.Element;
                        var clickX = (int)groundedEl.ClickPoint.X;
                        var clickY = (int)groundedEl.ClickPoint.Y;
                        _inputService.Click(clickX, clickY);
                        return (true, $"Clicked '{element}' at ({clickX}, {clickY}) in '{win.Title}' " +
                            $"(source={fallbackResult.Source}, confidence={fallbackResult.Confidence:F2})");
                    }
                }

                return (false, $"Cannot find element: {element}");
            }

            case "ocr-find":
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = args["text"];
                var outPath = args["out"];

                var cap = _captureService.CaptureScreen(outPath);
                if (!cap.Success)
                    return (false, $"Screenshot failed: {cap.Error}");

                var result = await _ocrService.RecognizeImageAsync(outPath);

                if (!string.IsNullOrEmpty(result.Error))
                    return (false, $"OCR failed: {result.Error}");

                var words = _ocrService.FindWords(result, text);
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
                cancellationToken.ThrowIfCancellationRequested();
                var outPath = args["out"];
                var cap = _captureService.CaptureScreen(outPath);
                if (!cap.Success)
                    return (false, $"Screenshot failed: {cap.Error}");

                var result = await _ocrService.RecognizeImageAsync(outPath);

                if (!string.IsNullOrEmpty(result.Error))
                    return (false, $"OCR failed: {result.Error}");

                return (true, $"Recognized {result.Words.Count} words: {result.Text.Substring(0, Math.Min(200, result.Text.Length))}");
            }

            case "ocr-scan":
            {
                // Scan a window or full screen for ALL visible text elements with physical screen coordinates.
                // Works on ANY app type (Electron, UWP, Chromium, Win32) since it uses screenshot + OCR.
                cancellationToken.ThrowIfCancellationRequested();
                var window = args.GetValueOrDefault("window");
                var outPath = _tempFiles.CreateTempPath("ocr_scan");

                CaptureResult cap;
                double dpiScale = 1.0;
                int winPhysX = 0, winPhysY = 0;

                if (!string.IsNullOrEmpty(window))
                {
                    var win = _windowService.FindWindow(window);
                    if (win == null) return (false, $"Window not found: {window}");
                    cap = _captureService.CaptureWindow(win.Handle.ToString(), outPath);
                    dpiScale = cap.ScaleFactor > 0 ? cap.ScaleFactor : DpiContext.Default.GetScaleFactor(win.HandleIntPtr);
                    winPhysX = (int)Math.Round(win.Rect.X * dpiScale);
                    winPhysY = (int)Math.Round(win.Rect.Y * dpiScale);
                }
                else
                {
                    cap = _captureService.CaptureScreen(outPath);
                }

                if (!cap.Success) return (false, $"Screenshot failed: {cap.Error}");

                var ocrResult = await _ocrService.RecognizeImageAsync(outPath);
                _tempFiles.CleanupFile(outPath);

                if (!string.IsNullOrEmpty(ocrResult.Error))
                    return (false, $"OCR error: {ocrResult.Error}");

                var elements = new List<object>();
                foreach (var word in ocrResult.Words.Where(w => w.BoundingBox != null))
                {
                    int physX = (int)word.BoundingBox!.X + winPhysX;
                    int physY = (int)word.BoundingBox.Y + winPhysY;
                    int physW = (int)word.BoundingBox.Width;
                    int physH = (int)word.BoundingBox.Height;

                    elements.Add(new
                    {
                        text = word.Text,
                        screen_x = physX,
                        screen_y = physY,
                        screen_cx = physX + physW / 2,
                        screen_cy = physY + physH / 2,
                        width = physW,
                        height = physH
                    });
                }

                var scanResult = new
                {
                    window = window ?? "full_screen",
                    dpi_scale = dpiScale,
                    element_count = elements.Count,
                    total_text = ocrResult.Text.Length > 500 ? ocrResult.Text[..500] + "..." : ocrResult.Text,
                    elements
                };

                return (true, JsonSerializer.Serialize(scanResult, new JsonSerializerOptions { WriteIndented = true }));
            }

            case "error":
                return (false, args.GetValueOrDefault("message", "Unknown error"));

            default:
                return (false, $"Unknown action: {action}");
        }
    }
}
