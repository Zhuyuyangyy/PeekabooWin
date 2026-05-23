using System.Runtime.InteropServices;

namespace PeekabooWin.Core.Input;

/// <summary>
/// Stable typer — simulates human typing rhythm to avoid app input buffer overflow.
/// Windows apps often drop characters sent too fast; TypeSlowly uses 30-50ms per character.
/// </summary>
public class StableTyper
{
    private readonly InputService _inputService;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    public int DefaultDelayMs { get; set; } = 40;
    public int LongTextDelayMs { get; set; } = 30;

    public StableTyper(InputService inputService)
    {
        _inputService = inputService;
    }

    /// <summary>
    /// Click target area, then type slowly with human-like cadence.
    /// </summary>
    public async Task TypeSlowly(string text, int x, int y)
    {
        if (string.IsNullOrEmpty(text)) return;

        // 1. Focus
        SetCursorPos(x, y);
        _inputService.Click(x, y);
        await Task.Delay(80);

        // 2. Clear existing content
        _inputService.Hotkey("ctrl+a");
        await Task.Delay(30);
        _inputService.PressKeyByName("backspace");
        await Task.Delay(30);

        // 3. Type character by character
        var delay = text.Length > 20 ? LongTextDelayMs : DefaultDelayMs;
        foreach (var c in text)
        {
            _inputService.TypeText(c.ToString());
            await Task.Delay(delay);
        }

        await Task.Delay(50);
    }

    /// <summary>
    /// Type directly into currently focused window.
    /// </summary>
    public async Task TypeSlowly(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var delay = text.Length > 20 ? LongTextDelayMs : DefaultDelayMs;
        foreach (var c in text)
        {
            _inputService.TypeText(c.ToString());
            await Task.Delay(delay);
        }
    }

    /// <summary>
    /// Type password with longer interval.
    /// </summary>
    public async Task TypePassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return;
        foreach (var c in password)
        {
            _inputService.TypeText(c.ToString());
            await Task.Delay(60);
        }
    }

    public void Confirm()
    {
        _inputService.PressKeyByName("enter");
    }

    public async Task FocusAndClear(int x, int y)
    {
        SetCursorPos(x, y);
        _inputService.Click(x, y);
        await Task.Delay(80);
        _inputService.Hotkey("ctrl+a");
        await Task.Delay(30);
        _inputService.PressKeyByName("backspace");
        await Task.Delay(30);
    }
}