using WinAgent.Core.Models;

#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
#endif

namespace WinAgent.Core.Verification;

/// <summary>
/// 验证服务 — 操作前后截图对比
///
/// 核心原则:
/// 1. click 后强制 verify
/// 2. 像素级 diff 检测变化
/// 3. 元素级变化检测 (可选，需要重新 observe)
/// </summary>
public class VerificationService
{
#if WINDOWS
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    private const int SRCCOPY = 0x00CC0020;

    /// <summary>
    /// 捕获当前屏幕截图
    /// </summary>
    public string CaptureScreen(string? outputPath = null)
    {
        outputPath ??= Path.Combine(Path.GetTempPath(), $"verify_{Guid.NewGuid():N}.png");

        var screenW = SystemInformation.VirtualScreen.Width;
        var screenH = SystemInformation.VirtualScreen.Height;

        using var bitmap = new Bitmap(screenW, screenH, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(screenW, screenH));
        bitmap.Save(outputPath, ImageFormat.Png);

        return outputPath;
    }

    /// <summary>
    /// 捕获窗口截图
    /// </summary>
    public string CaptureWindow(IntPtr hwnd, string? outputPath = null)
    {
        outputPath ??= Path.Combine(Path.GetTempPath(), $"verify_{Guid.NewGuid():N}.png");

        var rect = new System.Drawing.Rectangle();
        GetWindowRect(hwnd, ref rect);

        using var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(rect.X, rect.Y, 0, 0, new System.Drawing.Size(rect.Width, rect.Height));
        bitmap.Save(outputPath, ImageFormat.Png);

        return outputPath;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, ref System.Drawing.Rectangle lpRect);

    /// <summary>
    /// 比较两张截图的像素差异
    /// </summary>
    public VerificationResult Compare(string beforePath, string afterPath)
    {
        using var before = new Bitmap(beforePath);
        using var after = new Bitmap(afterPath);

        if (before.Width != after.Width || before.Height != after.Height)
        {
            return new VerificationResult
            {
                Changed = true,
                PixelDiffRatio = 1.0,
                ChangeDescription = "Screenshot dimensions differ"
            };
        }

        var totalPixels = before.Width * before.Height;
        var diffPixels = 0;

        for (int y = 0; y < before.Height; y++)
        {
            for (int x = 0; x < before.Width; x++)
            {
                var p1 = before.GetPixel(x, y);
                var p2 = after.GetPixel(x, y);

                if (Math.Abs(p1.R - p2.R) > 30 ||
                    Math.Abs(p1.G - p2.G) > 30 ||
                    Math.Abs(p1.B - p2.B) > 30)
                {
                    diffPixels++;
                }
            }
        }

        var ratio = (double)diffPixels / totalPixels;

        return new VerificationResult
        {
            Changed = ratio > 0.001,
            PixelDiffRatio = ratio,
            ChangeDescription = ratio > 0.001
                ? $"Screen changed: {diffPixels} pixels ({ratio:P2})"
                : "No significant change detected",
            BeforeScreenshot = beforePath,
            AfterScreenshot = afterPath
        };
    }
#endif

    /// <summary>
    /// 比较两个 observe 结果的元素变化
    /// </summary>
    public List<ElementChange> CompareElements(ObservationResult before, ObservationResult after)
    {
        var changes = new List<ElementChange>();

        var beforeIds = before.Elements.Select(e => e.Id).ToHashSet();
        var afterIds = after.Elements.Select(e => e.Id).ToHashSet();

        // 新出现的元素
        foreach (var id in afterIds.Except(beforeIds))
        {
            changes.Add(new ElementChange { ElementId = id, Type = ChangeType.Appeared });
        }

        // 消失的元素
        foreach (var id in beforeIds.Except(afterIds))
        {
            changes.Add(new ElementChange { ElementId = id, Type = ChangeType.Disappeared });
        }

        // 变化的元素
        foreach (var beforeEl in before.Elements)
        {
            var afterEl = after.Elements.FirstOrDefault(e => e.Id == beforeEl.Id);
            if (afterEl == null) continue;

            if (beforeEl.Name != afterEl.Name)
            {
                changes.Add(new ElementChange
                {
                    ElementId = beforeEl.Id,
                    Type = ChangeType.TextChanged,
                    Detail = $"'{beforeEl.Name}' → '{afterEl.Name}'"
                });
            }

            if (beforeEl.Enabled != afterEl.Enabled || beforeEl.Visible != afterEl.Visible)
            {
                changes.Add(new ElementChange
                {
                    ElementId = beforeEl.Id,
                    Type = ChangeType.StateChanged,
                    Detail = $"enabled:{beforeEl.Enabled}→{afterEl.Enabled}, visible:{beforeEl.Visible}→{afterEl.Visible}"
                });
            }

            if (beforeEl.BBox.X != afterEl.BBox.X || beforeEl.BBox.Y != afterEl.BBox.Y)
            {
                changes.Add(new ElementChange
                {
                    ElementId = beforeEl.Id,
                    Type = ChangeType.Moved,
                    Detail = $"({beforeEl.BBox.X},{beforeEl.BBox.Y}) → ({afterEl.BBox.X},{afterEl.BBox.Y})"
                });
            }
        }

        return changes;
    }
}
