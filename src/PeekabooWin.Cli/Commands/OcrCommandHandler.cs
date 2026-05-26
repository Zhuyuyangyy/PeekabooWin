using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Cli.Commands;

public class OcrCommandHandler : ICommandHandler
{
    private readonly CaptureService _captureService;
    private readonly WindowService _windowService;
    private readonly OcrService _ocrService;
    private readonly InputService _inputService;
    private readonly TempFileManager _tempFiles;

    public string CommandName => "ocr";

    public OcrCommandHandler(CaptureService captureService, WindowService windowService, OcrService ocrService, InputService inputService, TempFileManager tempFiles)
    {
        _captureService = captureService;
        _windowService = windowService;
        _ocrService = ocrService;
        _inputService = inputService;
        _tempFiles = tempFiles;
    }

    public async Task<int> ExecuteAsync(string[] args)
    {
        var command = args[0].ToLower();
        return command switch
        {
            "ocr" => await HandleOcr(args),
            "find-on-screen" => await HandleFindOnScreen(args),
            "ocr-click" => await HandleOcrClick(args),
            _ => 1
        };
    }

    private async Task<int> HandleOcr(string[] args)
    {
        string? outPath = CliHelpers.GetFlag(args, "--out", "-o");
        string? window = CliHelpers.GetFlag(args, "--window", "-w");
        string? text = CliHelpers.GetFlag(args, "--text", "-t");
        bool screen = CliHelpers.HasFlag(args, "--screen", "-s");
        bool click = CliHelpers.HasFlag(args, "--click", "-c");
        string? lang = CliHelpers.GetFlag(args, "--lang", "-l") ?? "chi_sim+eng";

        using var ocrService = new OcrService(lang);

        string imgPath;
        if (!string.IsNullOrEmpty(window))
        {
            imgPath = outPath ?? _tempFiles.CreateTempPath("ocr_window");
            var cap = _captureService.CaptureWindow(window, imgPath);
            if (!cap.Success)
            {
                var r = CommandResult.Fail("ocr", $"Failed to capture window: {window}");
                CliHelpers.PrintJson(r);
                return 1;
            }
        }
        else
        {
            imgPath = outPath ?? _tempFiles.CreateTempPath("ocr_screen");
            var cap = _captureService.CaptureScreen(imgPath);
            if (!cap.Success)
            {
                var r = CommandResult.Fail("ocr", "Failed to capture screen");
                CliHelpers.PrintJson(r);
                return 1;
            }
        }

        var ocrResult = await ocrService.RecognizeImageAsync(imgPath);

        if (!string.IsNullOrEmpty(text))
        {
            var words = ocrService.FindWords(ocrResult, text);
            var center = ocrService.FindWordCenter(ocrResult, text);

            var findResult = new
            {
                search_query = text,
                recognized_text = ocrResult.Text,
                matches_count = words.Count,
                matches = words.Select(w => new { w.Text, w.BoundingBox }).ToList(),
                first_center = center.HasValue ? new { x = center.Value.x, y = center.Value.y } : null
            };

            var cmdResult = CommandResult.Ok("ocr find-text", findResult);
            CliHelpers.PrintJson(cmdResult);

            if (click && center.HasValue && center.Value.x > 0 && center.Value.y > 0)
            {
                var clickResult = _inputService.Click(center.Value.x, center.Value.y);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(CommandResult.Ok("ocr click", clickResult), CliHelpers.JsonOptions));
                return 0;
            }

            return words.Count > 0 ? 0 : 1;
        }
        else
        {
            var cmdResult = CommandResult.Ok("ocr", new
            {
                text = ocrResult.Text,
                words_count = ocrResult.Words.Count,
                confidence = ocrResult.Confidence,
                engine = ocrResult.Engine,
                language = ocrResult.Language,
                image = imgPath,
                error = ocrResult.Error
            });
            CliHelpers.PrintJson(cmdResult);
            return ocrResult.Words.Count > 0 ? 0 : 1;
        }
    }

    private async Task<int> HandleFindOnScreen(string[] args)
    {
        var window = CliHelpers.GetFlag(args, "--window", "-w");
        var text = CliHelpers.GetFlag(args, "--text", "-t");

        if (string.IsNullOrEmpty(text)) { CliHelpers.PrintError("find-on-screen", "Missing --text"); return 1; }

        var outPath = _tempFiles.CreateTempPath("fos");
        CaptureResult cap;
        if (!string.IsNullOrEmpty(window))
            cap = _captureService.CaptureWindow(window, outPath);
        else
            cap = _captureService.CaptureScreen(outPath);
        if (!cap.Success) { CliHelpers.PrintError("find-on-screen", $"Screenshot failed: {cap.Error}"); return 1; }

        var ocrResult = await _ocrService.RecognizeImageAsync(outPath);
        if (!string.IsNullOrEmpty(ocrResult.Error)) { CliHelpers.PrintError("find-on-screen", $"OCR error: {ocrResult.Error}"); return 1; }

        var center = _ocrService.FindWordCenter(ocrResult, text);
        if (center == null)
        {
            var r = CommandResult.Ok("find-on-screen", new { found = false, text, recognized_snippet = ocrResult.Text.Length > 200 ? ocrResult.Text.Substring(0, 200) : ocrResult.Text });
            CliHelpers.PrintJson(r);
            return 1;
        }

        int screenX = center.Value.x;
        int screenY = center.Value.y;
        if (!string.IsNullOrEmpty(window))
        {
            var win = _windowService.FindWindow(window);
            if (win != null) { screenX += win.Rect.X; screenY += win.Rect.Y; }
        }

        _tempFiles.CleanupFile(outPath);
        var result = CommandResult.Ok("find-on-screen", new { found = true, text, screen_x = screenX, screen_y = screenY, rel_x = center.Value.x, rel_y = center.Value.y });
        CliHelpers.PrintJson(result);
        return 0;
    }

    private async Task<int> HandleOcrClick(string[] args)
    {
        var window = CliHelpers.GetFlag(args, "--window", "-w");
        var text = CliHelpers.GetFlag(args, "--text", "-t");

        if (string.IsNullOrEmpty(text)) { CliHelpers.PrintError("ocr-click", "Missing --text"); return 1; }

        var outPath = _tempFiles.CreateTempPath("oc");
        CaptureResult cap;
        if (!string.IsNullOrEmpty(window))
            cap = _captureService.CaptureWindow(window, outPath);
        else
            cap = _captureService.CaptureScreen(outPath);
        if (!cap.Success) { CliHelpers.PrintError("ocr-click", $"Screenshot failed: {cap.Error}"); return 1; }

        var ocrResult = await _ocrService.RecognizeImageAsync(outPath);
        if (!string.IsNullOrEmpty(ocrResult.Error)) { CliHelpers.PrintError("ocr-click", $"OCR error: {ocrResult.Error}"); return 1; }

        var center = _ocrService.FindWordCenter(ocrResult, text);
        if (center == null) { CliHelpers.PrintError("ocr-click", $"Text '{text}' not found"); return 1; }

        int screenX = center.Value.x;
        int screenY = center.Value.y;
        if (!string.IsNullOrEmpty(window))
        {
            var win = _windowService.FindWindow(window);
            if (win != null) { screenX += win.Rect.X; screenY += win.Rect.Y; }
        }

        _inputService.Click(screenX, screenY);
        _tempFiles.CleanupFile(outPath);

        var result = CommandResult.Ok("ocr-click", new { text, clicked_x = screenX, clicked_y = screenY, rel_x = center.Value.x, rel_y = center.Value.y });
        CliHelpers.PrintJson(result);
        return 0;
    }
}
