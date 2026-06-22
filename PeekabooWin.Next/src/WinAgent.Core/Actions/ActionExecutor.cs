using System.Runtime.InteropServices;
using WinAgent.Core.Models;

namespace WinAgent.Core.Actions;

/// <summary>
/// Action 执行器 — 只接受 element_id，不接受坐标
///
/// 核心原则:
/// 1. LLM 选择 element_id → Grounding 解析坐标 → Action 执行
/// 2. 危险元素默认 dry-run
/// 3. 执行后强制 verify
/// </summary>
public class ActionExecutor
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_DOUBLECLICK = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBOARDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBOARDINPUT
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    /// <summary>
    /// 执行操作 — 必须通过 GroundingResult 传入
    /// </summary>
    public ActionResult Execute(ActionRequest request, GroundingResult grounding, bool force = false)
    {
        // 安全检查
        if (grounding.IsPotentiallyDangerous && !force)
        {
            return new ActionResult
            {
                Success = false,
                Type = request.Type,
                TargetId = request.TargetId,
                WasDryRun = true,
                WasBlocked = true,
                BlockReason = grounding.DangerWarning ?? "Dangerous element blocked"
            };
        }

        if (!grounding.IsGrounded)
        {
            return new ActionResult
            {
                Success = false,
                Type = request.Type,
                TargetId = request.TargetId,
                Error = grounding.Error ?? "Element not grounded"
            };
        }

        if (grounding.ClickX == null || grounding.ClickY == null)
        {
            return new ActionResult
            {
                Success = false,
                Type = request.Type,
                TargetId = request.TargetId,
                Error = "No click coordinates available"
            };
        }

        // Dry-run 模式
        if (request.DryRun)
        {
            return new ActionResult
            {
                Success = true,
                Type = request.Type,
                TargetId = request.TargetId,
                Description = $"[DRY-RUN] Would {request.Type} at ({grounding.ClickX}, {grounding.ClickY}) on element '{grounding.ResolvedElement?.Name}'",
                WasDryRun = true
            };
        }

        // 真正执行
        try
        {
            switch (request.Type)
            {
                case ActionType.Click:
                    Click(grounding.ClickX.Value, grounding.ClickY.Value);
                    return new ActionResult
                    {
                        Success = true,
                        Type = request.Type,
                        TargetId = request.TargetId,
                        Description = $"Clicked element '{grounding.ResolvedElement?.Name}' at ({grounding.ClickX}, {grounding.ClickY})"
                    };

                case ActionType.RightClick:
                    RightClick(grounding.ClickX.Value, grounding.ClickY.Value);
                    return new ActionResult
                    {
                        Success = true,
                        Type = request.Type,
                        TargetId = request.TargetId,
                        Description = $"Right-clicked element '{grounding.ResolvedElement?.Name}'"
                    };

                case ActionType.DoubleClick:
                    DoubleClick(grounding.ClickX.Value, grounding.ClickY.Value);
                    return new ActionResult
                    {
                        Success = true,
                        Type = request.Type,
                        TargetId = request.TargetId,
                        Description = $"Double-clicked element '{grounding.ResolvedElement?.Name}'"
                    };

                case ActionType.Type:
                    if (string.IsNullOrEmpty(request.Text))
                        return new ActionResult { Success = false, Type = request.Type, TargetId = request.TargetId, Error = "No text provided" };
                    Click(grounding.ClickX.Value, grounding.ClickY.Value);
                    Thread.Sleep(100);
                    TypeText(request.Text);
                    return new ActionResult
                    {
                        Success = true,
                        Type = request.Type,
                        TargetId = request.TargetId,
                        Description = $"Typed '{request.Text}' into element '{grounding.ResolvedElement?.Name}'"
                    };

                case ActionType.Hotkey:
                    if (string.IsNullOrEmpty(request.Keys))
                        return new ActionResult { Success = false, Type = request.Type, TargetId = request.TargetId, Error = "No keys provided" };
                    SendHotkey(request.Keys);
                    return new ActionResult
                    {
                        Success = true,
                        Type = request.Type,
                        TargetId = request.TargetId,
                        Description = $"Pressed hotkey: {request.Keys}"
                    };

                case ActionType.Hover:
                    SetCursorPos(grounding.ClickX.Value, grounding.ClickY.Value);
                    return new ActionResult
                    {
                        Success = true,
                        Type = request.Type,
                        TargetId = request.TargetId,
                        Description = $"Hovered on element '{grounding.ResolvedElement?.Name}'"
                    };

                default:
                    return new ActionResult
                    {
                        Success = false,
                        Type = request.Type,
                        TargetId = request.TargetId,
                        Error = $"Unknown action type: {request.Type}"
                    };
            }
        }
        catch (Exception ex)
        {
            return new ActionResult
            {
                Success = false,
                Type = request.Type,
                TargetId = request.TargetId,
                Error = ex.Message
            };
        }
    }

    private void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
    }

    private void RightClick(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero);
    }

    private void DoubleClick(int x, int y)
    {
        Click(x, y);
        Thread.Sleep(100);
        Click(x, y);
    }

    private void TypeText(string text)
    {
        foreach (char c in text)
        {
            SendChar(c);
            Thread.Sleep(30);
        }
    }

    private void SendChar(char c)
    {
        var inputs = new INPUT[2];

        inputs[0].Type = 1; // KEYBOARD
        inputs[0].Data.Keyboard.WVk = 0;
        inputs[0].Data.Keyboard.WScan = (ushort)c;
        inputs[0].Data.Keyboard.DwFlags = 0x0004; // KEYEVENTF_UNICODE

        inputs[1].Type = 1;
        inputs[1].Data.Keyboard.WVk = 0;
        inputs[1].Data.Keyboard.WScan = (ushort)c;
        inputs[1].Data.Keyboard.DwFlags = 0x0004 | 0x0002; // KEYEVENTF_UNICODE | KEYEVENTF_KEYUP

        SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    private void SendHotkey(string keys)
    {
        var parts = keys.Split('+', '-');
        var inputList = new List<INPUT>();

        foreach (var part in parts)
        {
            var vk = MapKeyToVk(part.Trim());
            if (vk == 0) continue;

            var down = new INPUT { Type = 1 };
            down.Data.Keyboard.WVk = vk;
            down.Data.Keyboard.DwFlags = 0;
            inputList.Add(down);
        }

        // Key up in reverse
        foreach (var part in parts.Reverse())
        {
            var vk = MapKeyToVk(part.Trim());
            if (vk == 0) continue;

            var up = new INPUT { Type = 1 };
            up.Data.Keyboard.WVk = vk;
            up.Data.Keyboard.DwFlags = 0x0002; // KEYEVENTF_KEYUP
            inputList.Add(up);
        }

        if (inputList.Count > 0)
        {
            SendInput((uint)inputList.Count, inputList.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }
    }

    private ushort MapKeyToVk(string key)
    {
        return key.ToLower() switch
        {
            "ctrl" or "control" => 0x11,
            "alt" => 0x12,
            "shift" => 0x10,
            "win" or "windows" => 0x5B,
            "enter" => 0x0D,
            "esc" or "escape" => 0x1B,
            "tab" => 0x09,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "space" => 0x20,
            "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
            "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
            "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
            _ => 0
        };
    }
}
