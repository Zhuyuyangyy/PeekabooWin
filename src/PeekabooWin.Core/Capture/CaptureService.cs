using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Core.Capture;

public class CaptureService
{
    private readonly WindowService _windowService;

    public CaptureService(WindowService windowService)
    {
        _windowService = windowService;
    }

    #region Win32 API

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    const int SM_CXSCREEN = 0;
    const int SM_CYSCREEN = 1;
    const uint SRCCOPY = 0x00CC0020;

    #endregion

    /// <summary>
    /// 截取全屏
    /// </summary>
    public CaptureResult CaptureScreen(string outputPath)
    {
        try
        {
            int screenW = GetSystemMetrics(SM_CXSCREEN);
            int screenH = GetSystemMetrics(SM_CYSCREEN);

            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, screenW, screenH);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            BitBlt(hdcMem, 0, 0, screenW, screenH, hdcScreen, 0, 0, SRCCOPY);

            SelectObject(hdcMem, hOld);

            using var bmp = Image.FromHbitmap(hBitmap);
            bmp.Save(outputPath, ImageFormat.Png);

            Cleanup(hdcMem, hBitmap, hdcScreen);

            return new CaptureResult
            {
                Success = true,
                Path = outputPath,
                Width = screenW,
                Height = screenH
            };
        }
        catch (Exception ex)
        {
            return new CaptureResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 截取指定窗口
    /// </summary>
    public CaptureResult CaptureWindow(string keyword, string outputPath)
    {
        try
        {
            var win = _windowService.FindWindow(keyword);
            if (win == null)
                return new CaptureResult { Success = false, Error = $"Window not found: {keyword}" };

            return CaptureWindowHandle(win.HandleIntPtr, win.Title, outputPath);
        }
        catch (Exception ex)
        {
            return new CaptureResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 用窗口句柄截取窗口
    /// </summary>
    public CaptureResult CaptureWindowHandle(IntPtr hWnd, string title, string outputPath)
    {
        IntPtr hdcScreen = IntPtr.Zero;
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            var rect = _windowService.GetWindowRect(hWnd);
            if (rect == null)
                return new CaptureResult { Success = false, Error = "Cannot get window rect" };

            int w = rect.Width;
            int h = rect.Height;

            hdcScreen = GetDC(IntPtr.Zero);
            hdcMem = CreateCompatibleDC(hdcScreen);
            hBitmap = CreateCompatibleBitmap(hdcScreen, w, h);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            BitBlt(hdcMem, 0, 0, w, h, hdcScreen, rect.X, rect.Y, SRCCOPY);

            SelectObject(hdcMem, hOld);

            using var bmp = Image.FromHbitmap(hBitmap);
            bmp.Save(outputPath, ImageFormat.Png);

            return new CaptureResult
            {
                Success = true,
                Path = outputPath,
                Width = w,
                Height = h,
                WindowTitle = title
            };
        }
        catch (Exception ex)
        {
            return new CaptureResult { Success = false, Error = ex.Message };
        }
        finally
        {
            Cleanup(hdcMem, hBitmap, hdcScreen);
        }
    }

    private void Cleanup(IntPtr hdcMem, IntPtr hBitmap, IntPtr hdcScreen)
    {
        if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
        if (hdcMem != IntPtr.Zero) DeleteDC(hdcMem);
        if (hdcScreen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcScreen);
    }
}
