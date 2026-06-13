using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Core.Capture;

public class CaptureService
{
    private readonly WindowService _windowService;
    private readonly DpiContext _dpiContext;

    /// <summary>
    /// Creates a new CaptureService instance.
    /// </summary>
    /// <param name="windowService">Service for window enumeration and lookup.</param>
    /// <param name="dpiContext">DPI context for scale-aware operations. Defaults to DpiContext.Default.</param>
    public CaptureService(WindowService windowService, DpiContext? dpiContext = null)
    {
        _windowService = windowService;
        _dpiContext = dpiContext ?? DpiContext.Default;
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
    /// Captures the entire screen and saves it to the specified path.
    /// Sets ScaleFactor on the result from DpiContext.GetPrimaryScale().
    /// </summary>
    /// <param name="outputPath">The file path where the screenshot will be saved.</param>
    /// <returns>A CaptureResult with the capture dimensions and scale factor.</returns>
    public CaptureResult CaptureScreen(string outputPath)
    {
        try
        {
            double scaleFactor = _dpiContext.GetPrimaryScale();
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
                Height = screenH,
                ScaleFactor = scaleFactor
            };
        }
        catch (Exception ex)
        {
            PekaLogger.Error("CaptureService", $"CaptureScreen failed: {ex.Message}", ex);
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
    /// 用窗口句柄截取窗口。
    /// Uses physical pixel coordinates for BitBlt to ensure correct capture under DPI scaling.
    /// </summary>
    public CaptureResult CaptureWindowHandle(IntPtr hWnd, string title, string outputPath)
    {
        IntPtr hdcScreen = IntPtr.Zero;
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            double scaleFactor = _dpiContext.GetScaleFactor(hWnd);

            var rect = _windowService.GetWindowRect(hWnd);
            if (rect == null)
                return new CaptureResult { Success = false, Error = "Cannot get window rect" };

            // WindowService returns logical coordinates; BitBlt on a DPI-aware process
            // operates in physical pixel coordinates. Scale up to capture the full window.
            int logicalX = rect.X;
            int logicalY = rect.Y;
            int logicalW = rect.Width;
            int logicalH = rect.Height;

            var (physX, physY) = _dpiContext.LogicalToPhysical(logicalX, logicalY, scaleFactor);
            var (physW, physH) = _dpiContext.LogicalToPhysical(logicalW, logicalH, scaleFactor);

            PekaLogger.Debug("CaptureService",
                $"CaptureWindowHandle: logical=({logicalX},{logicalY},{logicalW}x{logicalH}), " +
                $"physical=({physX},{physY},{physW}x{physH}), scale={scaleFactor:F2}");

            hdcScreen = GetDC(IntPtr.Zero);
            hdcMem = CreateCompatibleDC(hdcScreen);
            hBitmap = CreateCompatibleBitmap(hdcScreen, physW, physH);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            BitBlt(hdcMem, 0, 0, physW, physH, hdcScreen, physX, physY, SRCCOPY);

            SelectObject(hdcMem, hOld);

            using var bmp = Image.FromHbitmap(hBitmap);
            bmp.Save(outputPath, ImageFormat.Png);

            return new CaptureResult
            {
                Success = true,
                Path = outputPath,
                Width = physW,
                Height = physH,
                WindowTitle = title,
                ScaleFactor = scaleFactor
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

    /// <summary>
    /// Captures a rectangular region of the screen using Graphics.CopyFromScreen.
    /// </summary>
    /// <param name="x">The X coordinate of the upper-left corner (in physical pixels).</param>
    /// <param name="y">The Y coordinate of the upper-left corner (in physical pixels).</param>
    /// <param name="width">The width of the region to capture (in physical pixels).</param>
    /// <param name="height">The height of the region to capture (in physical pixels).</param>
    /// <param name="outputPath">The file path where the capture will be saved.</param>
    /// <returns>A CaptureResult indicating success or failure with dimensions.</returns>
    public CaptureResult CaptureRegion(int x, int y, int width, int height, string outputPath)
    {
        if (width <= 0 || height <= 0)
        {
            PekaLogger.Warn("CaptureService", $"CaptureRegion called with invalid dimensions: {width}x{height}");
            return new CaptureResult { Success = false, Error = $"Invalid capture dimensions: {width}x{height}" };
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            return new CaptureResult { Success = false, Error = "Output path cannot be null or empty" };
        }

        try
        {
            double scaleFactor = _dpiContext.GetPrimaryScale();

            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }

            // Ensure output directory exists
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            bmp.Save(outputPath, ImageFormat.Png);

            PekaLogger.Debug("CaptureService",
                $"CaptureRegion: captured ({x},{y}) {width}x{height} -> {outputPath}, scale={scaleFactor:F2}");

            return new CaptureResult
            {
                Success = true,
                Path = outputPath,
                Width = width,
                Height = height,
                ScaleFactor = scaleFactor
            };
        }
        catch (Exception ex)
        {
            PekaLogger.Error("CaptureService", $"CaptureRegion failed: {ex.Message}", ex);
            return new CaptureResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Captures the full screen, downsamples it for LLM consumption, and returns the image as a base64-encoded PNG string.
    /// </summary>
    /// <param name="maxWidth">Maximum width for the downsampled image. Defaults to 1920.</param>
    /// <returns>A base64-encoded PNG string, or null if capture fails.</returns>
    public string? CaptureScreenAsBase64(int maxWidth = 1920)
    {
        string? tempPath = null;
        try
        {
            tempPath = Path.Combine(
                Path.GetTempPath(),
                $"peekaboo_capture_{Guid.NewGuid():N}.png");

            var result = CaptureScreen(tempPath);
            if (!result.Success || result.Path == null)
            {
                PekaLogger.Warn("CaptureService", $"CaptureScreenAsBase64: screen capture failed: {result.Error}");
                return null;
            }

            byte[] jpegBytes = DownsampleForLlm(result.Path, maxWidth);
            string base64 = Convert.ToBase64String(jpegBytes);

            PekaLogger.Debug("CaptureService",
                $"CaptureScreenAsBase64: produced {base64.Length} chars of base64 from {result.Width}x{result.Height}");

            return base64;
        }
        catch (Exception ex)
        {
            PekaLogger.Error("CaptureService", $"CaptureScreenAsBase64 failed: {ex.Message}", ex);
            return null;
        }
        finally
        {
            if (tempPath != null && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    /// <summary>
    /// Loads an image from disk, resizes it to fit within the specified max width while preserving aspect ratio,
    /// and returns the result as JPEG bytes suitable for LLM consumption.
    /// </summary>
    /// <param name="imagePath">Path to the source image file.</param>
    /// <param name="maxWidth">Maximum width of the output image. Defaults to 1920.</param>
    /// <returns>JPEG-encoded bytes of the resized image.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the source image does not exist.</exception>
    /// <exception cref="ArgumentException">Thrown when parameters are invalid.</exception>
    public byte[] DownsampleForLlm(string imagePath, int maxWidth = 1920)
    {
        if (string.IsNullOrEmpty(imagePath))
        {
            throw new ArgumentException("Image path cannot be null or empty.", nameof(imagePath));
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Source image not found: {imagePath}", imagePath);
        }

        if (maxWidth <= 0)
        {
            throw new ArgumentException($"Max width must be positive, got: {maxWidth}", nameof(maxWidth));
        }

        try
        {
            using var sourceImage = Image.FromFile(imagePath);

            int sourceWidth = sourceImage.Width;
            int sourceHeight = sourceImage.Height;

            // If already smaller than maxWidth, just convert to JPEG without upscaling
            int targetWidth, targetHeight;
            if (sourceWidth <= maxWidth)
            {
                targetWidth = sourceWidth;
                targetHeight = sourceHeight;
            }
            else
            {
                double ratio = (double)maxWidth / sourceWidth;
                targetWidth = maxWidth;
                targetHeight = (int)Math.Round(sourceHeight * ratio);
            }

            using var resized = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(sourceImage, 0, 0, targetWidth, targetHeight);
            }

            using var ms = new MemoryStream();

            // Find JPEG encoder
            var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(codec => codec.MimeType == "image/jpeg");

            if (jpegEncoder != null)
            {
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
                resized.Save(ms, jpegEncoder, encoderParams);
            }
            else
            {
                // Fallback: save as JPEG using default encoder
                resized.Save(ms, ImageFormat.Jpeg);
            }

            PekaLogger.Debug("CaptureService",
                $"DownsampleForLlm: {imagePath} ({sourceWidth}x{sourceHeight}) -> ({targetWidth}x{targetHeight}), " +
                $"JPEG bytes={ms.Length}");

            return ms.ToArray();
        }
        catch (FileNotFoundException)
        {
            throw; // Re-throw known exceptions
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PekaLogger.Error("CaptureService", $"DownsampleForLlm failed for '{imagePath}': {ex.Message}", ex);
            throw;
        }
    }

    private void Cleanup(IntPtr hdcMem, IntPtr hBitmap, IntPtr hdcScreen)
    {
        if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
        if (hdcMem != IntPtr.Zero) DeleteDC(hdcMem);
        if (hdcScreen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcScreen);
    }
}
