using System.Runtime.InteropServices;

namespace PeekabooWin.Core.Infrastructure;

/// <summary>
/// Provides DPI awareness utilities for screen coordinate conversions.
/// Priority chain: GetDpiForWindow → GetDpiForSystem → GetScaleFactorForMonitor → GetDeviceCaps (last resort).
/// </summary>
public class DpiContext
{
    private const string Source = "DpiContext";

    /// <summary>
    /// Default singleton instance for static/convenience access.
    /// </summary>
    public static DpiContext Default { get; } = new DpiContext();

    // Cache the primary scale so we don't re-query Win32 on every call.
    private double _cachedPrimaryScale = -1;

    #region Win32 API

    /// <summary>
    /// Retrieves the DPI for the window associated with the specified handle.
    /// Available on Windows 10 version 1607 and later.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// Retrieves the DPI for the system (primary monitor) in a DPI-aware manner.
    /// Available on Windows 10 version 1607 and later.
    /// This always returns the correct DPI regardless of the calling process's DPI awareness.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForSystem();

    /// <summary>
    /// Retrieves the scale factor for the specified monitor.
    /// Available on Windows 8 and later via shcore.dll.
    /// </summary>
    [DllImport("shcore.dll", SetLastError = true)]
    private static extern int GetScaleFactorForMonitor(IntPtr hMonitor, out uint pScale);

    /// <summary>
    /// Retrieves a handle to the primary display monitor.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    /// <summary>
    /// Retrieves device-specific information for the specified device context.
    /// Used ONLY as a last-resort fallback for DPI calculation.
    /// NOTE: For DPI-aware processes, this returns the virtualized base DPI (96),
    /// NOT the actual system DPI. This is why it is the lowest-priority method.
    /// </summary>
    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    /// <summary>
    /// Retrieves a device context (DC) for the client area of a specified window.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    /// <summary>
    /// Releases a device context (DC), freeing it for use by other applications.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>
    /// Retrieves system metrics for the primary display monitor.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>Index for logical pixels per inch on the X axis.</summary>
    private const int LOGPIXELSX = 88;

    /// <summary>Standard DPI at 100% scaling.</summary>
    private const double StandardDpi = 96.0;

    /// <summary>System metrics index for primary screen width.</summary>
    private const int SM_CXSCREEN = 0;

    /// <summary>System metrics index for primary screen height.</summary>
    private const int SM_CYSCREEN = 1;

    /// <summary>Flag for MonitorFromWindow: return the primary monitor.</summary>
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    #endregion

    /// <summary>
    /// Gets the DPI scale factor for the specified window handle.
    /// Uses GetDpiForWindow when available (Win10 1607+), falls back to GetPrimaryScale.
    /// </summary>
    /// <param name="hwnd">Handle to the window. Use IntPtr.Zero for the primary desktop.</param>
    /// <returns>The DPI scale factor (e.g., 1.0, 1.25, 1.5, 2.0).</returns>
    public virtual double GetScaleFactor(IntPtr hwnd)
    {
        try
        {
            // Try GetDpiForWindow first (Win10 1607+) — per-window DPI
            if (hwnd != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi > 0)
                {
                    double scale = dpi / StandardDpi;
                    PekaLogger.Debug(Source, $"GetDpiForWindow returned DPI={dpi}, scale={scale:F2} for hwnd={hwnd}");
                    return scale;
                }
            }

            // Fallback to system-wide primary scale
            return GetPrimaryScale();
        }
        catch (EntryPointNotFoundException)
        {
            // GetDpiForWindow not available on this Windows version
            PekaLogger.Warn(Source, "GetDpiForWindow not available, falling back to system DPI");
            return GetPrimaryScale();
        }
        catch (Exception ex)
        {
            PekaLogger.Warn(Source, $"Error getting scale factor for hwnd={hwnd}: {ex.Message}");
            return 1.0;
        }
    }

    /// <summary>
    /// Gets the DPI scale factor for the primary monitor.
    /// Priority: GetDpiForSystem → GetScaleFactorForMonitor → GetDeviceCaps (last resort).
    /// 
    /// IMPORTANT: GetDeviceCaps(hdc, LOGPIXELSX) returns the virtualized base DPI (96)
    /// for DPI-aware processes, NOT the actual system DPI. This is why it is only used
    /// as a last resort when all other Win32 methods fail.
    /// </summary>
    /// <returns>The primary monitor DPI scale factor (e.g., 1.0, 1.25, 1.5, 2.0).</returns>
    public virtual double GetPrimaryScale()
    {
        // Return cached value if available
        if (_cachedPrimaryScale > 0)
            return _cachedPrimaryScale;

        // Priority 1: GetDpiForSystem (Win10 1607+)
        // This is the most reliable method — it returns the correct DPI regardless of
        // the calling process's DPI awareness context.
        try
        {
            uint dpi = GetDpiForSystem();
            if (dpi > 0)
            {
                double scale = dpi / StandardDpi;
                PekaLogger.Debug(Source, $"GetDpiForSystem returned DPI={dpi}, primary scale={scale:F2}");
                _cachedPrimaryScale = scale;
                return scale;
            }
        }
        catch (EntryPointNotFoundException)
        {
            PekaLogger.Debug(Source, "GetDpiForSystem not available (pre-Win10 1607), trying next method");
        }
        catch (Exception ex)
        {
            PekaLogger.Warn(Source, $"GetDpiForSystem failed: {ex.Message}, trying next method");
        }

        // Priority 2: GetScaleFactorForMonitor (Win8+)
        try
        {
            IntPtr hMonitor = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
            if (hMonitor != IntPtr.Zero)
            {
                int hr = GetScaleFactorForMonitor(hMonitor, out uint percent);
                if (hr == 0 && percent > 0)
                {
                    double scale = percent / 100.0;
                    PekaLogger.Debug(Source, $"GetScaleFactorForMonitor returned {percent}%, primary scale={scale:F2}");
                    _cachedPrimaryScale = scale;
                    return scale;
                }
            }
        }
        catch (EntryPointNotFoundException)
        {
            PekaLogger.Debug(Source, "GetScaleFactorForMonitor not available, trying next method");
        }
        catch (Exception ex)
        {
            PekaLogger.Warn(Source, $"GetScaleFactorForMonitor failed: {ex.Message}, trying next method");
        }

        // Priority 3 (LAST RESORT): GetDeviceCaps
        // WARNING: For DPI-aware processes, this returns virtualized DPI (96).
        // This means scale will be 1.0 even on a 150% display. Only used as absolute fallback.
        IntPtr hdc = IntPtr.Zero;
        try
        {
            hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
            {
                PekaLogger.Warn(Source, "All DPI detection methods failed, defaulting scale to 1.0");
                _cachedPrimaryScale = 1.0;
                return 1.0;
            }

            int dpi = GetDeviceCaps(hdc, LOGPIXELSX);
            double scale = dpi / StandardDpi;
            PekaLogger.Warn(Source,
                $"GetDeviceCaps returned DPI={dpi}, primary scale={scale:F2}. " +
                "WARNING: This is the fallback method — for DPI-aware processes the value may be inaccurate. " +
                "GetDpiForSystem and GetScaleFactorForMonitor were not available.");
            _cachedPrimaryScale = scale;
            return scale;
        }
        catch (Exception ex)
        {
            PekaLogger.Warn(Source, $"Error getting primary scale via GetDeviceCaps: {ex.Message}");
            _cachedPrimaryScale = 1.0;
            return 1.0;
        }
        finally
        {
            if (hdc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }
    }

    /// <summary>
    /// Converts logical (DPI-independent) coordinates to physical (pixel) coordinates.
    /// </summary>
    /// <param name="lx">Logical X coordinate.</param>
    /// <param name="ly">Logical Y coordinate.</param>
    /// <param name="scale">DPI scale factor.</param>
    /// <returns>Physical pixel coordinates (px, py).</returns>
    public virtual (int px, int py) LogicalToPhysical(int lx, int ly, double scale)
    {
        if (scale <= 0)
        {
            PekaLogger.Warn(Source, $"Invalid scale factor {scale}, defaulting to 1.0");
            scale = 1.0;
        }

        int px = (int)Math.Round(lx * scale);
        int py = (int)Math.Round(ly * scale);

        PekaLogger.Debug(Source, $"LogicalToPhysical: ({lx},{ly}) @ {scale:F2} -> ({px},{py})");
        return (px, py);
    }

    /// <summary>
    /// Converts physical (pixel) coordinates to logical (DPI-independent) coordinates.
    /// </summary>
    /// <param name="px">Physical X coordinate in pixels.</param>
    /// <param name="py">Physical Y coordinate in pixels.</param>
    /// <param name="scale">DPI scale factor.</param>
    /// <returns>Logical coordinates (lx, ly).</returns>
    public virtual (int lx, int ly) PhysicalToLogical(int px, int py, double scale)
    {
        if (scale <= 0)
        {
            PekaLogger.Warn(Source, $"Invalid scale factor {scale}, defaulting to 1.0");
            scale = 1.0;
        }

        int lx = (int)Math.Round(px / scale);
        int ly = (int)Math.Round(py / scale);

        PekaLogger.Debug(Source, $"PhysicalToLogical: ({px},{py}) @ {scale:F2} -> ({lx},{ly})");
        return (lx, ly);
    }

    /// <summary>
    /// Gets the primary screen bounds in logical (DPI-independent) coordinates.
    /// </summary>
    /// <returns>Screen width and height in logical coordinates.</returns>
    public virtual (int width, int height) GetScreenBounds()
    {
        try
        {
            int physicalWidth = GetSystemMetrics(SM_CXSCREEN);
            int physicalHeight = GetSystemMetrics(SM_CYSCREEN);

            // GetSystemMetrics returns physical pixels, convert to logical
            double scale = GetPrimaryScale();
            int logicalWidth = (int)Math.Round(physicalWidth / scale);
            int logicalHeight = (int)Math.Round(physicalHeight / scale);

            PekaLogger.Debug(Source,
                $"GetScreenBounds: physical=({physicalWidth}x{physicalHeight}), " +
                $"scale={scale:F2}, logical=({logicalWidth}x{logicalHeight})");

            return (logicalWidth, logicalHeight);
        }
        catch (Exception ex)
        {
            PekaLogger.Warn(Source, $"Error getting screen bounds: {ex.Message}");
            // Fallback: return raw system metrics
            return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
        }
    }

    /// <summary>
    /// Gets the primary screen bounds in physical pixels.
    /// Use this for Win32 cursor movement and SendInput coordinate validation.
    /// </summary>
    /// <returns>Screen width and height in physical pixels.</returns>
    public virtual (int width, int height) GetPhysicalScreenBounds()
    {
        return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    /// <summary>
    /// Validates that the given physical pixel coordinates are within screen bounds.
    /// </summary>
    /// <param name="x">Physical X coordinate.</param>
    /// <param name="y">Physical Y coordinate.</param>
    /// <returns>True if the coordinates are within screen bounds; false otherwise.</returns>
    public virtual bool IsWithinScreenBounds(int x, int y)
    {
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);
        return x >= 0 && x < screenWidth && y >= 0 && y < screenHeight;
    }
}
