using System.Diagnostics;
using System.Windows.Automation;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Core.Perception;

/// <summary>
/// 感知路由器 — 协调 UIA 快速通道和 LLM 视觉 grounding
/// 
/// 路由策略:
/// 1. UIA 快速通道 (<50ms) — 直接拿到控件句柄
/// 2. LLM 视觉 grounding (1-3s) — 截屏发给多模态 LLM
/// 3. OCR 兜底 — 传统文字识别
/// </summary>
public class PerceptionRouter
{
    private readonly UIAutomationService _uiaService;
    private readonly LlmGroundingService _llmGrounding;
    private readonly OcrService _ocrService;
    private readonly CaptureService _captureService;
    private readonly InputService _inputService;
    private readonly WindowService _windowService;
    private readonly TempFileManager _tempFiles;
    private readonly PerceptionCache _cache;

    // Semantic keyword expansion for fuzzy UIA search
    private static readonly Dictionary<string, string[]> SemanticAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["确定"] = new[] { "OK", "确认", "Confirm", "确定" },
        ["取消"] = new[] { "Cancel", "取消", "放弃" },
        ["保存"] = new[] { "Save", "保存", "存储" },
        ["关闭"] = new[] { "Close", "关闭", "退出" },
        ["发送"] = new[] { "Send", "发送", "提交", "Submit" },
        ["搜索"] = new[] { "Search", "搜索", "查找", "Find" },
        ["删除"] = new[] { "Delete", "删除", "移除", "Remove" },
        ["打开"] = new[] { "Open", "打开", "开启" },
        ["是"] = new[] { "Yes", "是", "确认" },
        ["否"] = new[] { "No", "否", "不" },
    };

    public PerceptionRouter(
        UIAutomationService uiaService,
        LlmGroundingService llmGrounding,
        OcrService ocrService,
        CaptureService captureService,
        InputService inputService,
        WindowService windowService,
        TempFileManager tempFiles,
        PerceptionCache cache)
    {
        _uiaService = uiaService;
        _llmGrounding = llmGrounding;
        _ocrService = ocrService;
        _captureService = captureService;
        _inputService = inputService;
        _windowService = windowService;
        _tempFiles = tempFiles;
        _cache = cache;
    }

    /// <summary>
    /// 定位一个元素 — 主入口方法
    /// </summary>
    public async Task<PerceptionResult> GroundElementAsync(
        string? windowKeyword,
        string elementDescription,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // Phase 1: UIA exact match
        if (!string.IsNullOrEmpty(windowKeyword))
        {
            var uiaResult = TryUiaExact(windowKeyword, elementDescription);
            if (uiaResult.IsConfident)
            {
                sw.Stop();
                uiaResult.LatencyMs = sw.Elapsed.TotalMilliseconds;
                return uiaResult;
            }

            // Phase 1b: UIA semantic/fuzzy match
            var fuzzyResult = TryUiaFuzzy(windowKeyword, elementDescription);
            if (fuzzyResult.IsConfident)
            {
                sw.Stop();
                fuzzyResult.LatencyMs = sw.Elapsed.TotalMilliseconds;
                return fuzzyResult;
            }
        }

        // Phase 2: LLM visual grounding
        try
        {
            var llmResult = await TryLlmGroundingAsync(windowKeyword, elementDescription, ct);
            if (llmResult.IsConfident)
            {
                sw.Stop();
                llmResult.LatencyMs = sw.Elapsed.TotalMilliseconds;
                return llmResult;
            }
        }
        catch (Exception ex)
        {
            PekaLogger.Warn("PerceptionRouter", $"LLM grounding failed: {ex.Message}", ex);
        }

        // Phase 3: OCR fallback
        var ocrResult = await TryOcrFallbackAsync(windowKeyword, elementDescription, ct);
        sw.Stop();
        ocrResult.LatencyMs = sw.Elapsed.TotalMilliseconds;
        return ocrResult;
    }

    /// <summary>
    /// 全场景理解
    /// </summary>
    public async Task<ScreenUnderstanding?> UnderstandScreenAsync(
        string? windowKeyword,
        CancellationToken ct = default)
    {
        // Try LLM first for full understanding
        try
        {
            return await _llmGrounding.UnderstandScreenAsync(windowKeyword ?? "", ct);
        }
        catch (Exception ex)
        {
            PekaLogger.Warn("PerceptionRouter", $"LLM screen understanding failed: {ex.Message}", ex);
        }

        // Fallback: build from UIA + OCR
        return await BuildLocalUnderstandingAsync(windowKeyword, ct);
    }

    // --- Private methods ---

    private PerceptionResult TryUiaExact(string windowKeyword, string elementDescription)
    {
        try
        {
            var findResult = _uiaService.FindByName(windowKeyword, elementDescription);
            if (findResult.Success && findResult.Count > 0)
            {
                var el = findResult.Matches[0];
                return BuildUiaResult(el, windowKeyword, PerceptionSource.UIA, 0.95);
            }
        }
        catch (Exception ex)
        {
            PekaLogger.Debug("PerceptionRouter", $"UIA exact search failed: {ex.Message}");
        }
        return PerceptionResult.NotFound("UIA exact: no match");
    }

    private PerceptionResult TryUiaFuzzy(string windowKeyword, string elementDescription)
    {
        try
        {
            // Expand description with semantic aliases
            var aliases = GetSemanticAliases(elementDescription);
            int candidatesTried = 0;

            foreach (var alias in aliases)
            {
                candidatesTried++;
                var findResult = _uiaService.FindByName(windowKeyword, alias);
                if (findResult.Success && findResult.Count > 0)
                {
                    var result = BuildUiaResult(findResult.Matches[0], windowKeyword, PerceptionSource.UIA_Fuzzy, 0.8);
                    result.CandidatesTried = candidatesTried;
                    return result;
                }
            }

            // Also try control type matching based on description
            var controlType = InferControlType(elementDescription);
            if (controlType != null)
            {
                candidatesTried++;
                var typeResult = _uiaService.FindByControlType(windowKeyword, controlType);
                if (typeResult.Success && typeResult.Count > 0)
                {
                    // Filter by name similarity
                    var bestMatch = typeResult.Matches
                        .Where(m => m.Name != null && (
                            m.Name.Contains(elementDescription, StringComparison.OrdinalIgnoreCase) ||
                            elementDescription.Contains(m.Name, StringComparison.OrdinalIgnoreCase)))
                        .FirstOrDefault();

                    if (bestMatch != null)
                    {
                        var result = BuildUiaResult(bestMatch, windowKeyword, PerceptionSource.UIA_Fuzzy, 0.75);
                        result.CandidatesTried = candidatesTried;
                        return result;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PekaLogger.Debug("PerceptionRouter", $"UIA fuzzy search failed: {ex.Message}");
        }
        return PerceptionResult.NotFound("UIA fuzzy: no match");
    }

    private async Task<PerceptionResult> TryLlmGroundingAsync(string? windowKeyword, string elementDescription, CancellationToken ct)
    {
        var element = await _llmGrounding.FindElementAsync(windowKeyword ?? "", elementDescription, ct);
        if (element != null)
        {
            return new PerceptionResult
            {
                Element = element,
                Source = PerceptionSource.LLM_Vision,
                Confidence = element.Confidence,
                FallbackReason = null
            };
        }
        return PerceptionResult.NotFound("LLM: element not found");
    }

    private async Task<PerceptionResult> TryOcrFallbackAsync(string? windowKeyword, string elementDescription, CancellationToken ct)
    {
        try
        {
            var outPath = _tempFiles.CreateTempPath("perception_ocr");
            CaptureResult cap;
            double dpiScale = 1.0;

            if (!string.IsNullOrEmpty(windowKeyword))
            {
                var win = _windowService.FindWindow(windowKeyword);
                if (win != null)
                {
                    cap = _captureService.CaptureWindow(win.Handle.ToString(), outPath);
                    dpiScale = cap.ScaleFactor > 0 ? cap.ScaleFactor : DpiContext.Default.GetScaleFactor(win.HandleIntPtr);
                }
                else
                    cap = _captureService.CaptureScreen(outPath);
            }
            else
            {
                cap = _captureService.CaptureScreen(outPath);
            }

            if (!cap.Success)
                return PerceptionResult.NotFound($"OCR fallback: capture failed - {cap.Error}");

            var ocrResult = await _ocrService.RecognizeImageAsync(outPath);
            if (!string.IsNullOrEmpty(ocrResult.Error))
                return PerceptionResult.NotFound($"OCR fallback: OCR failed - {ocrResult.Error}");

            var center = _ocrService.FindWordCenter(ocrResult, elementDescription);
            if (center == null)
            {
                _tempFiles.CleanupFile(outPath);
                return PerceptionResult.NotFound($"OCR fallback: text '{elementDescription}' not found");
            }

            // OCR coordinates are in PHYSICAL pixels relative to the screenshot image.
            // For window captures, we need to add the window's PHYSICAL screen position.
            // WindowService returns LOGICAL (DPI-virtualized) coordinates, so we must
            // scale them by the DPI factor to get physical pixels.
            int screenX = center.Value.x;
            int screenY = center.Value.y;
            if (!string.IsNullOrEmpty(windowKeyword))
            {
                var win = _windowService.FindWindow(windowKeyword);
                if (win != null)
                {
                    screenX += (int)Math.Round(win.Rect.X * dpiScale);
                    screenY += (int)Math.Round(win.Rect.Y * dpiScale);
                }
            }

            _tempFiles.CleanupFile(outPath);
            return new PerceptionResult
            {
                Element = new GroundedElement
                {
                    Id = "ocr_" + Guid.NewGuid().ToString("N")[..6],
                    Type = "text_match",
                    Label = elementDescription,
                    BBox = new BoundingBox
                    {
                        X = screenX - 20,
                        Y = screenY - 10,
                        Width = 40,
                        Height = 20
                    },
                    ClickPoint = new ClickPoint { X = screenX, Y = screenY },
                    Confidence = 0.5,
                    Source = PerceptionSource.OCR_Fallback,
                    State = "normal"
                },
                Source = PerceptionSource.OCR_Fallback,
                Confidence = 0.5,
                FallbackReason = "UIA and LLM unavailable, using OCR fallback"
            };
        }
        catch (Exception ex)
        {
            return PerceptionResult.NotFound($"OCR fallback exception: {ex.Message}");
        }
    }

    private PerceptionResult BuildUiaResult(UIAElementInfo uiaEl, string windowKeyword, PerceptionSource source, double confidence)
    {
        // Try to get the raw AutomationElement for direct invocation
        AutomationElement? rawElement = TryGetRawUiaElement(windowKeyword, uiaEl.Name);

        var bbox = uiaEl.BoundingBox;
        var clickX = bbox != null ? bbox.X + bbox.Width / 2 : 0;
        var clickY = bbox != null ? bbox.Y + bbox.Height / 2 : 0;

        return new PerceptionResult
        {
            Element = new GroundedElement
            {
                Id = uiaEl.Id,
                Type = uiaEl.ControlType ?? "unknown",
                Label = uiaEl.Name ?? "",
                AutomationId = uiaEl.AutomationId,
                BBox = bbox != null ? new BoundingBox
                {
                    X = bbox.X,
                    Y = bbox.Y,
                    Width = bbox.Width,
                    Height = bbox.Height
                } : new BoundingBox(),
                ClickPoint = new ClickPoint { X = clickX, Y = clickY },
                Confidence = confidence,
                Source = source,
                State = uiaEl.IsEnabled ? "enabled" : "disabled",
                RawUiaElement = rawElement,
                SupportedPatterns = uiaEl.Patterns?.ToArray()
            },
            Source = source,
            Confidence = confidence
        };
    }

    private AutomationElement? TryGetRawUiaElement(string windowKeyword, string? name)
    {
        try
        {
            var win = _windowService.FindWindow(windowKeyword);
            if (win == null) return null;

            var root = AutomationElement.FromHandle((IntPtr)win.Handle);
            if (root == null) return null;

            if (!string.IsNullOrEmpty(name))
            {
                var condition = new PropertyCondition(AutomationElement.NameProperty, name);
                return root.FindFirst(TreeScope.Children | TreeScope.Descendants, condition);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string[] GetSemanticAliases(string description)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { description };

        foreach (var kvp in SemanticAliases)
        {
            if (kvp.Key.Contains(description, StringComparison.OrdinalIgnoreCase) ||
                description.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.Any(v => v.Contains(description, StringComparison.OrdinalIgnoreCase) ||
                                   description.Contains(v, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var alias in kvp.Value)
                    aliases.Add(alias);
            }
        }

        return aliases.ToArray();
    }

    private static string? InferControlType(string description)
    {
        var lower = description.ToLower();
        if (lower.Contains("按钮") || lower.Contains("button") || lower.Contains("btn"))
            return "button";
        if (lower.Contains("输入") || lower.Contains("textbox") || lower.Contains("input") || lower.Contains("编辑"))
            return "textbox";
        if (lower.Contains("checkbox") || lower.Contains("复选") || lower.Contains("勾选"))
            return "checkbox";
        if (lower.Contains("下拉") || lower.Contains("dropdown") || lower.Contains("combo"))
            return "combobox";
        if (lower.Contains("链接") || lower.Contains("link"))
            return "link";
        if (lower.Contains("菜单") || lower.Contains("menu"))
            return "menu";
        if (lower.Contains("tab") || lower.Contains("标签"))
            return "tab";
        return null;
    }

    private async Task<ScreenUnderstanding?> BuildLocalUnderstandingAsync(string? windowKeyword, CancellationToken ct)
    {
        try
        {
            var outPath = _tempFiles.CreateTempPath("local_understand");
            CaptureResult cap;
            string windowTitle = "";
            double dpiScale = 1.0;

            if (!string.IsNullOrEmpty(windowKeyword))
            {
                var win = _windowService.FindWindow(windowKeyword);
                if (win != null)
                {
                    cap = _captureService.CaptureWindow(win.Handle.ToString(), outPath);
                    windowTitle = win.Title;
                    dpiScale = cap.ScaleFactor > 0 ? cap.ScaleFactor : DpiContext.Default.GetScaleFactor(win.HandleIntPtr);
                }
                else
                {
                    cap = _captureService.CaptureScreen(outPath);
                }
            }
            else
            {
                cap = _captureService.CaptureScreen(outPath);
            }

            if (!cap.Success)
            {
                _tempFiles.CleanupFile(outPath);
                return null;
            }

            // Compute physical window origin for coordinate translation
            int winPhysX = 0, winPhysY = 0;
            if (!string.IsNullOrEmpty(windowKeyword))
            {
                var win = _windowService.FindWindow(windowKeyword);
                if (win != null)
                {
                    winPhysX = (int)Math.Round(win.Rect.X * dpiScale);
                    winPhysY = (int)Math.Round(win.Rect.Y * dpiScale);
                }
            }

            var ocrResult = await _ocrService.RecognizeImageAsync(outPath);
            _tempFiles.CleanupFile(outPath);

            var elements = new List<GroundedElement>();
            if (string.IsNullOrEmpty(ocrResult.Error) && ocrResult.Words.Count > 0)
            {
                int idx = 0;
                foreach (var word in ocrResult.Words.Where(w => w.BoundingBox != null))
                {
                    // Convert image-relative OCR coords to physical screen coords
                    int physX = (int)word.BoundingBox!.X + winPhysX;
                    int physY = (int)word.BoundingBox.Y + winPhysY;

                    elements.Add(new GroundedElement
                    {
                        Id = $"local_{idx++}",
                        Type = "text",
                        Label = word.Text,
                        BBox = new BoundingBox
                        {
                            X = physX,
                            Y = physY,
                            Width = (int)word.BoundingBox.Width,
                            Height = (int)word.BoundingBox.Height
                        },
                        ClickPoint = new ClickPoint
                        {
                            X = physX + (int)(word.BoundingBox.Width / 2),
                            Y = physY + (int)(word.BoundingBox.Height / 2)
                        },
                        Confidence = 0.5,
                        Source = PerceptionSource.OCR_Fallback,
                        State = "normal"
                    });
                }
            }

            return new ScreenUnderstanding
            {
                ScreenType = "unknown",
                WindowTitle = windowTitle,
                Elements = elements,
                Summary = $"Local OCR found {elements.Count} text elements (dpi_scale={dpiScale:F2})",
                Source = PerceptionSource.OCR_Fallback
            };
        }
        catch (Exception ex)
        {
            PekaLogger.Error("PerceptionRouter", $"Local understanding failed: {ex.Message}", ex);
            return null;
        }
    }
}
