using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Windows;

public class WindowService
{
    #region Win32 API

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    #endregion

    /// <summary>
    /// 列出所有窗口，支持按标题关键字过滤
    /// </summary>
    public List<WindowInfo> ListWindows(string? keyword = null)
    {
        var results = new List<WindowInfo>();

        EnumWindows((hWnd, _) =>
        {
            // 跳过不可见窗口
            if (!IsWindowVisible(hWnd)) return true;

            // 获取标题
            int len = GetWindowTextLength(hWnd);
            if (len == 0) return true;

            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();

            // 跳过空标题
            if (string.IsNullOrWhiteSpace(title)) return true;

            // 关键字过滤
            if (keyword != null && !title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;

            // 获取类名
            var classSb = new StringBuilder(256);
            GetClassName(hWnd, classSb, classSb.Capacity);
            string className = classSb.ToString();

            // 获取进程ID
            GetWindowThreadProcessId(hWnd, out uint pid);

            // 获取位置
            GetWindowRect(hWnd, out RECT rect);

            // 跳过太小窗口（可能是隐藏控件）
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 50 || height < 50) return true;

            string processName = "";
            try
            {
                var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch { }

            results.Add(new WindowInfo
            {
                Handle = (long)hWnd,
                Title = title,
                ClassName = className,
                ProcessId = (int)pid,
                ProcessName = processName,
                IsVisible = true,
                IsEnabled = IsWindowEnabled(hWnd),
                Rect = new RectInfo
                {
                    X = rect.Left,
                    Y = rect.Top,
                    Width = width,
                    Height = height
                }
            });

            return true;
        }, IntPtr.Zero);

        return results;
    }

    /// <summary>
    /// 根据标题关键字找到第一个窗口
    /// </summary>
    public WindowInfo? FindWindow(string keyword)
    {
        var list = ListWindows(keyword);
        return list.FirstOrDefault();
    }

    /// <summary>
    /// 根据句柄获取窗口信息
    /// </summary>
    public WindowInfo? GetWindowByHandle(long handle)
    {
        var list = ListWindows();
        return list.FirstOrDefault(w => w.Handle == handle);
    }

    /// <summary>
    /// 聚焦窗口（激活到前台）
    /// </summary>
    public bool FocusWindow(IntPtr hWnd)
    {
        // 先恢复最小化窗口
        ShowWindow(hWnd, SW_RESTORE);
        return SetForegroundWindow(hWnd);
    }

    /// <summary>
    /// 根据标题聚焦窗口
    /// </summary>
    public bool FocusWindow(string keyword)
    {
        var win = FindWindow(keyword);
        if (win == null) return false;
        return FocusWindow(win.HandleIntPtr);
    }

    /// <summary>
    /// 获取窗口坐标
    /// </summary>
    public RectInfo? GetWindowRect(IntPtr hWnd)
    {
        if (!GetWindowRect(hWnd, out RECT rect))
            return null;

        return new RectInfo
        {
            X = rect.Left,
            Y = rect.Top,
            Width = rect.Right - rect.Left,
            Height = rect.Bottom - rect.Top
        };
    }
}