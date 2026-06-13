using System.Runtime.InteropServices;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Input;

public class InputService
{
    private readonly DpiContext _dpiContext;

    /// <summary>
    /// Creates a new InputService instance.
    /// </summary>
    /// <param name="dpiContext">DPI context for scale-aware coordinate conversion. Defaults to DpiContext.Default.</param>
    public InputService(DpiContext? dpiContext = null)
    {
        _dpiContext = dpiContext ?? DpiContext.Default;
    }

    #region Win32 API

    [DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern IntPtr GetMessageExtraInfo();

    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;

    const int MOUSEEVENTF_MOVE = 0x0001;
    const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    const int MOUSEEVENTF_LEFTUP = 0x0004;
    const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const int MOUSEEVENTF_RIGHTUP = 0x0010;
    const int MOUSEEVENTF_ABSOLUTE = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public int dwFlags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public short wVk;
        public short wScan;
        public int dwFlags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUT_UNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    struct INPUT
    {
        public int type;
        public INPUT_UNION u;
    }

    const int KEYEVENTF_KEYDOWN = 0x0000;
    const int KEYEVENTF_KEYUP = 0x0002;
    const int KEYEVENTF_UNICODE = 0x0004;
    const int VK_SHIFT = 0x10;
    const int VK_CTRL = 0x11;
    const int VK_ALT = 0x12;

    #endregion

    /// <summary>
    /// Gets the current cursor position in physical pixel coordinates.
    /// </summary>
    /// <returns>The current cursor X and Y coordinates.</returns>
    public (int X, int Y) GetCursorPos()
    {
        GetCursorPos(out POINT p);
        return (p.X, p.Y);
    }

    /// <summary>
    /// Moves the mouse to the specified screen pixel coordinates and performs a left click.
    /// Coordinates from Win32, UI Automation, OCR over screenshots, and MCP clients are already
    /// in physical screen space, so no DPI scaling is applied here.
    /// </summary>
    /// <param name="x">Physical screen X coordinate in pixels.</param>
    /// <param name="y">Physical screen Y coordinate in pixels.</param>
    /// <returns>A CommandResult indicating success or failure.</returns>
    public CommandResult Click(int x, int y)
    {
        return ClickPhysicalCore(x, y, "click", "Click");
    }

    /// <summary>
    /// Performs a left click at the current cursor position.
    /// </summary>
    /// <returns>A CommandResult indicating success or failure.</returns>
    public CommandResult ClickCurrent()
    {
        var (x, y) = GetCursorPos();
        // ClickCurrent operates on physical coordinates directly, use ClickPhysical
        return ClickPhysical(x, y);
    }

    /// <summary>
    /// Moves the mouse to the specified screen pixel coordinates and performs a right click.
    /// No DPI scaling is applied.
    /// </summary>
    /// <param name="x">Physical screen X coordinate in pixels.</param>
    /// <param name="y">Physical screen Y coordinate in pixels.</param>
    /// <returns>A CommandResult indicating success or failure.</returns>
    public CommandResult RightClick(int x, int y)
    {
        return RightClickPhysicalCore(x, y, "right_click", "RightClick");
    }

    /// <summary>
    /// Performs a left click from logical (DPI-independent) coordinates.
    /// Use this only when the caller explicitly has logical coordinates.
    /// </summary>
    public CommandResult ClickLogical(int x, int y)
    {
        try
        {
            double scale = _dpiContext.GetPrimaryScale();
            var (physicalX, physicalY) = _dpiContext.LogicalToPhysical(x, y, scale);

            if (!_dpiContext.IsWithinScreenBounds(physicalX, physicalY))
            {
                PekaLogger.Warn("InputService",
                    $"ClickLogical position ({physicalX},{physicalY}) is outside screen bounds. " +
                    $"Logical=({x},{y}), scale={scale:F2}");
                return CommandResult.Fail("click_logical",
                    $"Position ({physicalX},{physicalY}) is outside screen bounds",
                    hint: $"Logical coords ({x},{y}) at scale {scale:F2} resulted in physical ({physicalX},{physicalY})");
            }

            var result = ClickPhysicalCore(physicalX, physicalY, "click_logical", "ClickLogical");
            return result.Success
                ? CommandResult.Ok("click_logical", new { logicalX = x, logicalY = y, physicalX, physicalY, scale })
                : result;
        }
        catch (Exception ex)
        {
            PekaLogger.Error("InputService", $"ClickLogical failed at ({x},{y}): {ex.Message}", ex);
            return CommandResult.Fail("click_logical", ex.Message);
        }
    }

    /// <summary>
    /// Performs a right click from logical (DPI-independent) coordinates.
    /// Use this only when the caller explicitly has logical coordinates.
    /// </summary>
    public CommandResult RightClickLogical(int x, int y)
    {
        try
        {
            double scale = _dpiContext.GetPrimaryScale();
            var (physicalX, physicalY) = _dpiContext.LogicalToPhysical(x, y, scale);

            if (!_dpiContext.IsWithinScreenBounds(physicalX, physicalY))
            {
                PekaLogger.Warn("InputService",
                    $"RightClickLogical position ({physicalX},{physicalY}) is outside screen bounds. " +
                    $"Logical=({x},{y}), scale={scale:F2}");
                return CommandResult.Fail("right_click_logical",
                    $"Position ({physicalX},{physicalY}) is outside screen bounds",
                    hint: $"Logical coords ({x},{y}) at scale {scale:F2} resulted in physical ({physicalX},{physicalY})");
            }

            var result = RightClickPhysicalCore(physicalX, physicalY, "right_click_logical", "RightClickLogical");
            return result.Success
                ? CommandResult.Ok("right_click_logical", new { logicalX = x, logicalY = y, physicalX, physicalY, scale })
                : result;
        }
        catch (Exception ex)
        {
            PekaLogger.Error("InputService", $"RightClickLogical failed at ({x},{y}): {ex.Message}", ex);
            return CommandResult.Fail("right_click_logical", ex.Message);
        }
    }

    /// <summary>
    /// Verifies that the cursor is currently at the expected physical pixel coordinates,
    /// within the specified tolerance (in pixels).
    /// </summary>
    /// <param name="expectedX">Expected X coordinate in physical pixels.</param>
    /// <param name="expectedY">Expected Y coordinate in physical pixels.</param>
    /// <param name="tolerance">Maximum allowed deviation in pixels. Defaults to 5.</param>
    /// <returns>True if the cursor is within tolerance of the expected position; false otherwise.</returns>
    public bool VerifyClickPosition(int expectedX, int expectedY, int tolerance = 5)
    {
        try
        {
            var (actualX, actualY) = GetCursorPos();
            int deltaX = Math.Abs(actualX - expectedX);
            int deltaY = Math.Abs(actualY - expectedY);

            bool withinTolerance = deltaX <= tolerance && deltaY <= tolerance;

            PekaLogger.Debug("InputService",
                $"VerifyClickPosition: expected=({expectedX},{expectedY}), actual=({actualX},{actualY}), " +
                $"delta=({deltaX},{deltaY}), tolerance={tolerance}, result={withinTolerance}");

            return withinTolerance;
        }
        catch (Exception ex)
        {
            PekaLogger.Warn("InputService", $"VerifyClickPosition failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Performs a left click using physical (already-scaled) pixel coordinates.
    /// No DPI conversion is applied; the coordinates are used directly.
    /// Use this when coordinates are already in physical pixel space.
    /// </summary>
    /// <param name="x">Physical X coordinate in pixels.</param>
    /// <param name="y">Physical Y coordinate in pixels.</param>
    /// <returns>A CommandResult indicating success or failure.</returns>
    public CommandResult ClickPhysical(int x, int y)
    {
        return ClickPhysicalCore(x, y, "click_physical", "ClickPhysical");
    }

    private CommandResult ClickPhysicalCore(int x, int y, string commandName, string logName)
    {
        try
        {
            // Validate screen bounds
            if (!_dpiContext.IsWithinScreenBounds(x, y))
            {
                PekaLogger.Warn("InputService",
                    $"{logName} position ({x},{y}) is outside screen bounds");
                return CommandResult.Fail(commandName,
                    $"Position ({x},{y}) is outside screen bounds");
            }

            SetCursorPos(x, y);
            Thread.Sleep(50);

            var inputs = new INPUT[2];
            inputs[0].type = (int)INPUT_MOUSE;
            inputs[0].u.mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_LEFTDOWN };
            inputs[1].type = (int)INPUT_MOUSE;
            inputs[1].u.mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_LEFTUP };

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());

            PekaLogger.Debug("InputService", $"{logName}: physical=({x},{y})");

            return CommandResult.Ok(commandName, new { physicalX = x, physicalY = y, coordinateSpace = "physical" });
        }
        catch (Exception ex)
        {
            PekaLogger.Error("InputService", $"{logName} failed at ({x},{y}): {ex.Message}", ex);
            return CommandResult.Fail(commandName, ex.Message);
        }
    }

    private CommandResult RightClickPhysicalCore(int x, int y, string commandName, string logName)
    {
        try
        {
            if (!_dpiContext.IsWithinScreenBounds(x, y))
            {
                PekaLogger.Warn("InputService",
                    $"{logName} position ({x},{y}) is outside screen bounds");
                return CommandResult.Fail(commandName,
                    $"Position ({x},{y}) is outside screen bounds");
            }

            SetCursorPos(x, y);
            Thread.Sleep(50);

            var inputs = new INPUT[2];
            inputs[0].type = (int)INPUT_MOUSE;
            inputs[0].u.mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_RIGHTDOWN };
            inputs[1].type = (int)INPUT_MOUSE;
            inputs[1].u.mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_RIGHTUP };

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());

            PekaLogger.Debug("InputService", $"{logName}: physical=({x},{y})");

            return CommandResult.Ok(commandName, new { physicalX = x, physicalY = y, coordinateSpace = "physical" });
        }
        catch (Exception ex)
        {
            PekaLogger.Error("InputService", $"{logName} failed at ({x},{y}): {ex.Message}", ex);
            return CommandResult.Fail(commandName, ex.Message);
        }
    }

    /// <summary>
    /// Types the specified text by simulating keyboard input (Unicode characters).
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <returns>A CommandResult indicating success or failure.</returns>
    public CommandResult TypeText(string text)
    {
        try
        {
            var inputs = new List<INPUT>();

            foreach (char c in text)
            {
                // Send Unicode character
                var down = new INPUT
                {
                    type = (int)INPUT_KEYBOARD,
                    u = new INPUT_UNION
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = (short)c,
                            dwFlags = KEYEVENTF_UNICODE,
                            dwExtraInfo = GetMessageExtraInfo()
                        }
                    }
                };
                var up = new INPUT
                {
                    type = (int)INPUT_KEYBOARD,
                    u = new INPUT_UNION
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = (short)c,
                            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                            dwExtraInfo = GetMessageExtraInfo()
                        }
                    }
                };

                inputs.Add(down);
                inputs.Add(up);
            }

            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            return CommandResult.Ok("type_text");
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("type_text", ex.Message);
        }
    }

    /// <summary>
    /// Presses the specified virtual key code (key down followed by key up).
    /// </summary>
    /// <param name="vkCode">The virtual key code to press.</param>
    /// <returns>A CommandResult indicating success or failure.</returns>
    public CommandResult PressKey(short vkCode)
    {
        try
        {
            var inputs = new INPUT[2];
            inputs[0].type = (int)INPUT_KEYBOARD;
            inputs[0].u.ki = new KEYBDINPUT { wVk = vkCode, wScan = 0, dwFlags = KEYEVENTF_KEYDOWN };
            inputs[1].type = (int)INPUT_KEYBOARD;
            inputs[1].u.ki = new KEYBDINPUT { wVk = vkCode, wScan = 0, dwFlags = KEYEVENTF_KEYUP };

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            return CommandResult.Ok("press_key");
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("press_key", ex.Message);
        }
    }

    /// <summary>
    /// Presses a key by name (supports: esc/escape, enter/return, tab, backspace, delete).
    /// </summary>
    /// <param name="keyName">The name of the key to press.</param>
    /// <returns>A CommandResult indicating success or failure.</returns>
    public CommandResult PressKeyByName(string keyName)
    {
        var vk = keyName.ToLower() switch
        {
            "esc" or "escape" => (short)0x1B,
            "enter" or "return" => (short)0x0D,
            "tab" => (short)0x09,
            "backspace" => (short)0x08,
            "delete" => (short)0x2E,
            _ => (short)0
        };

        if (vk == 0)
            return CommandResult.Fail("press_key", $"Unknown key: {keyName}");

        return PressKey(vk);
    }

    /// <summary>
    /// Executes a hotkey combination (e.g., "ctrl+l", "alt+f4", "ctrl+shift+a").
    /// </summary>
    /// <param name="hotkey">The hotkey string in "modifier+key" format.</param>
    /// <returns>A CommandResult indicating success or failure.</returns>
    public CommandResult Hotkey(string hotkey)
    {
        try
        {
            var parts = hotkey.ToLower().Split('+');
            var vkCodes = new List<int>();

            foreach (var part in parts)
            {
                var vk = part.Trim() switch
                {
                    "ctrl" or "control" => 0x11,
                    "shift" => 0x10,
                    "alt" => 0x12,
                    "enter" or "return" => 0x0D,
                    "tab" => 0x09,
                    "esc" or "escape" => 0x1B,
                    "win" or "windows" => 0x5B,
                    "backspace" => 0x08,
                    "delete" => 0x2E,
                    "up" => 0x26,
                    "down" => 0x28,
                    "left" => 0x25,
                    "right" => 0x27,
                    "f1" => 0x70,
                    "f2" => 0x71,
                    "f3" => 0x72,
                    "f4" => 0x73,
                    _ => part.Length == 1 ? char.ToUpper(part[0]) : 0
                };
                vkCodes.Add(vk);
            }

            if (vkCodes.Count == 0)
                return CommandResult.Fail("hotkey", "Invalid hotkey: " + hotkey);

            // Press all modifier keys, then the main key, then release modifiers in reverse
            var inputs = new List<INPUT>();

            foreach (var vk in vkCodes.Take(vkCodes.Count - 1))
            {
                inputs.Add(new INPUT
                {
                    type = (int)INPUT_KEYBOARD,
                    u = new INPUT_UNION { ki = new KEYBDINPUT { wVk = (short)vk, wScan = 0, dwFlags = KEYEVENTF_KEYDOWN } }
                });
            }

            // Main key press
            inputs.Add(new INPUT
            {
                type = (int)INPUT_KEYBOARD,
                u = new INPUT_UNION { ki = new KEYBDINPUT { wVk = (short)vkCodes.Last(), wScan = 0, dwFlags = KEYEVENTF_KEYDOWN } }
            });

            // Main key release
            inputs.Add(new INPUT
            {
                type = (int)INPUT_KEYBOARD,
                u = new INPUT_UNION { ki = new KEYBDINPUT { wVk = (short)vkCodes.Last(), wScan = 0, dwFlags = KEYEVENTF_KEYUP } }
            });

            // Release modifier keys (reverse order)
            foreach (var vk in vkCodes.Take(vkCodes.Count - 1).Reverse())
            {
                inputs.Add(new INPUT
                {
                    type = (int)INPUT_KEYBOARD,
                    u = new INPUT_UNION { ki = new KEYBDINPUT { wVk = (short)vk, wScan = 0, dwFlags = KEYEVENTF_KEYUP } }
                });
            }

            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            return CommandResult.Ok("hotkey");
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("hotkey", ex.Message);
        }
    }

    [DllImport("user32.dll")]
    static extern short VkKeyScan(char ch);
}
