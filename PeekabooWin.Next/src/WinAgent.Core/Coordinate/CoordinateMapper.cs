using WinAgent.Core.Models;

#if WINDOWS
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace WinAgent.Core.Coordinate;

/// <summary>
/// 坐标统一层 — 所有坐标必须转换为 physical screen pixels
///
/// 核心原则:
/// 1. 系统内部只使用 physical screen pixels
/// 2. UIA 返回的逻辑坐标必须经过 DPI 转换
/// 3. OCR 返回的像素坐标基于截图分辨率，需要映射到屏幕坐标
/// 4. 任何进入 ActionExecutor 的坐标必须是 physical screen pixels
/// </summary>
public class CoordinateMapper
{
#if WINDOWS
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    private const int LOGPIXELSX = 88;
    private const int LOGPIXELSY = 90;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>
    /// 获取系统 DPI 缩放因子
    /// </summary>
    public double GetDpiScale()
    {
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            var dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
            return dpiX / 96.0;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>
    /// 将 UIA 逻辑坐标转换为 physical screen pixels
    /// </summary>
    public BoundingBox UiaToPhysical(BoundingBox logicalBox)
    {
        var scale = GetDpiScale();
        if (Math.Abs(scale - 1.0) < 0.01)
            return logicalBox;

        return new BoundingBox
        {
            X = (int)(logicalBox.X * scale),
            Y = (int)(logicalBox.Y * scale),
            Width = (int)(logicalBox.Width * scale),
            Height = (int)(logicalBox.Height * scale)
        };
    }
#endif

    /// <summary>
    /// 将截图内的 OCR 坐标映射到 physical screen pixels
    /// </summary>
    public BoundingBox OcrToPhysical(BoundingBox ocrBox, BoundingBox windowBounds, int screenshotWidth, int screenshotHeight)
    {
        var scaleX = (double)windowBounds.Width / screenshotWidth;
        var scaleY = (double)windowBounds.Height / screenshotHeight;

        return new BoundingBox
        {
            X = windowBounds.X + (int)(ocrBox.X * scaleX),
            Y = windowBounds.Y + (int)(ocrBox.Y * scaleY),
            Width = (int)(ocrBox.Width * scaleX),
            Height = (int)(ocrBox.Height * scaleY)
        };
    }

#if WINDOWS
    /// <summary>
    /// 将截图内的 OCR 坐标映射到全屏 physical screen pixels
    /// </summary>
    public BoundingBox OcrToPhysicalFullScreen(BoundingBox ocrBox, int screenshotWidth, int screenshotHeight)
    {
        var screenRect = GetPrimaryScreenPhysicalBounds();
        var scaleX = (double)screenRect.Width / screenshotWidth;
        var scaleY = (double)screenRect.Height / screenshotHeight;

        return new BoundingBox
        {
            X = screenRect.X + (int)(ocrBox.X * scaleX),
            Y = screenRect.Y + (int)(ocrBox.Y * scaleY),
            Width = (int)(ocrBox.Width * scaleX),
            Height = (int)(ocrBox.Height * scaleY)
        };
    }

    /// <summary>
    /// 获取主显示器物理边界
    /// </summary>
    public BoundingBox GetPrimaryScreenPhysicalBounds()
    {
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            var width = GetDeviceCaps(hdc, 118); // DESKTOPHORZRES
            var height = GetDeviceCaps(hdc, 117); // DESKTOPVERTRES
            return new BoundingBox { X = 0, Y = 0, Width = width, Height = height };
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>
    /// 获取窗口物理边界
    /// </summary>
    public BoundingBox GetWindowPhysicalBounds(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var rect);
        return new BoundingBox
        {
            X = rect.Left,
            Y = rect.Top,
            Width = rect.Right - rect.Left,
            Height = rect.Bottom - rect.Top
        };
    }

    /// <summary>
    /// 验证坐标是否在屏幕范围内
    /// </summary>
    public bool IsOnScreen(BoundingBox box)
    {
        var screen = GetPrimaryScreenPhysicalBounds();
        return box.X >= 0 && box.Y >= 0
            && box.Right <= screen.Width
            && box.Bottom <= screen.Height;
    }

    // ---- 静态方法，供 ObservationService 使用 ----

    public static BoundingBox GetWindowPhysicalBoundsStatic(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var rect);
        return new BoundingBox
        {
            X = rect.Left,
            Y = rect.Top,
            Width = rect.Right - rect.Left,
            Height = rect.Bottom - rect.Top
        };
    }

    public static string GetWindowTitleStatic(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, 256);
        return sb.ToString();
    }
#endif
}
