using System.Runtime.InteropServices;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Input;

public class InputService
{
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
    /// 获取当前鼠标坐标
    /// </summary>
    public (int X, int Y) GetCursorPos()
    {
        GetCursorPos(out POINT p);
        return (p.X, p.Y);
    }

    /// <summary>
    /// 移动鼠标到指定坐标并点击
    /// </summary>
    public CommandResult Click(int x, int y)
    {
        try
        {
            SetCursorPos(x, y);
            Thread.Sleep(50);

            var inputs = new INPUT[2];
            inputs[0].type = (int)INPUT_MOUSE;
            inputs[0].u.mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_LEFTDOWN };
            inputs[1].type = (int)INPUT_MOUSE;
            inputs[1].u.mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_LEFTUP };

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            return CommandResult.Ok("click");
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("click", ex.Message);
        }
    }

    /// <summary>
    /// 在当前鼠标位置点击
    /// </summary>
    public CommandResult ClickCurrent()
    {
        var (x, y) = GetCursorPos();
        return Click(x, y);
    }

    /// <summary>
    /// 右键点击
    /// </summary>
    public CommandResult RightClick(int x, int y)
    {
        try
        {
            SetCursorPos(x, y);
            Thread.Sleep(50);

            var inputs = new INPUT[2];
            inputs[0].type = (int)INPUT_MOUSE;
            inputs[0].u.mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_RIGHTDOWN };
            inputs[1].type = (int)INPUT_MOUSE;
            inputs[1].u.mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_RIGHTUP };

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            return CommandResult.Ok("right_click");
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("right_click", ex.Message);
        }
    }

    /// <summary>
    /// 输入文本（模拟键盘）
    /// </summary>
    public CommandResult TypeText(string text)
    {
        try
        {
            var inputs = new List<INPUT>();

            foreach (char c in text)
            {
                // 发送 Unicode 字符
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
    /// 按下指定虚拟键码
    /// </summary>
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
    /// 按名称按下按键 (esc/enter/tab/backspace/delete)
    /// </summary>
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
    /// 执行快捷键，如 "ctrl+l", "alt+f4", "ctrl+shift+a"
    /// </summary>
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

            // 按下所有修饰键，然后按下主键，然后释放
            var inputs = new List<INPUT>();

            foreach (var vk in vkCodes.Take(vkCodes.Count - 1))
            {
                inputs.Add(new INPUT
                {
                    type = (int)INPUT_KEYBOARD,
                    u = new INPUT_UNION { ki = new KEYBDINPUT { wVk = (short)vk, wScan = 0, dwFlags = KEYEVENTF_KEYDOWN } }
                });
            }

            // 主键按下
            inputs.Add(new INPUT
            {
                type = (int)INPUT_KEYBOARD,
                u = new INPUT_UNION { ki = new KEYBDINPUT { wVk = (short)vkCodes.Last(), wScan = 0, dwFlags = KEYEVENTF_KEYDOWN } }
            });

            // 主键释放
            inputs.Add(new INPUT
            {
                type = (int)INPUT_KEYBOARD,
                u = new INPUT_UNION { ki = new KEYBDINPUT { wVk = (short)vkCodes.Last(), wScan = 0, dwFlags = KEYEVENTF_KEYUP } }
            });

            // 释放修饰键（反向）
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