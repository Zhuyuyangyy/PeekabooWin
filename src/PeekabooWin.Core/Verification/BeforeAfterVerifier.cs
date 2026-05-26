using System.Drawing;
using System.Drawing.Imaging;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Perception;

namespace PeekabooWin.Core.Verification;

/// <summary>
/// 前后截图验证器 — 视觉差分 + 状态匹配
/// 
/// VerificationScore = 0.4 × VisualChange
///                    + 0.3 × ExpectedStateMatch
///                    + 0.2 × ElementStateChange
///                    + 0.1 × ErrorAbsence
/// 
/// verification_score < 0.6 → 重试一次
/// 重试失败 → 重新截图+重规划
/// 否则 → 询问用户
/// </summary>
public class BeforeAfterVerifier
{
    public VerificationResult Verify(
        byte[] beforeImage,
        byte[] afterImage,
        VerificationContext context)
    {
        var result = new VerificationResult();

        // 1. VisualChange — 是否发生了视觉变化
        result.VisualChange = ComputeVisualChange(beforeImage, afterImage);

        // 2. ExpectedStateMatch — 是否达到预期状态
        result.ExpectedStateMatch = ComputeExpectedStateMatch(beforeImage, afterImage, context);

        // 3. ElementStateChange — 目标元素状态是否改变
        result.ElementStateChange = ComputeElementStateChange(beforeImage, afterImage, context);

        // 4. ErrorAbsence — 是否没有出现错误
        result.ErrorAbsence = ComputeErrorAbsence(beforeImage, afterImage, context);

        // 综合分数
        result.VerificationScore = 0.4 * result.VisualChange
                                 + 0.3 * result.ExpectedStateMatch
                                 + 0.2 * result.ElementStateChange
                                 + 0.1 * result.ErrorAbsence;

        // 决策
        if (result.VerificationScore >= 0.6)
        {
            result.Outcome = VerificationOutcome.Success;
            result.Message = $"验证通过 (score={result.VerificationScore:F2})";
        }
        else
        {
            result.Outcome = VerificationOutcome.Failed;
            result.Message = $"验证失败 (score={result.VerificationScore:F2})，建议重试";
            result.RecoverySuggestion = GenerateRecoverySuggestion(result);
        }

        return result;
    }

    /// <summary>
    /// 计算两张图片的视觉差异度 (0=完全相同, 1=完全不同)
    /// </summary>
    private double ComputeVisualChange(byte[] before, byte[] after)
    {
        if (before == null || after == null || before.Length == 0 || after.Length == 0)
            return 0.5; // 中性值

        try
        {
            using var msBefore = new MemoryStream(before);
            using var msAfter = new MemoryStream(after);
            using var imgBefore = Image.FromStream(msBefore);
            using var imgAfter = Image.FromStream(msAfter);

            // 简单降采样后比较像素差异
            const int sampleSize = 64;
            var bmpBefore = new Bitmap(imgBefore);
            var bmpAfter = new Bitmap(imgAfter);

            var w = Math.Min(bmpBefore.Width, bmpAfter.Width);
            var h = Math.Min(bmpBefore.Height, bmpAfter.Height);

            // 降采样步长
            var stepX = Math.Max(1, w / sampleSize);
            var stepY = Math.Max(1, h / sampleSize);

            long diffPixels = 0;
            long totalSamples = 0;

            for (int y = 0; y < h; y += stepY)
            {
                for (int x = 0; x < w; x += stepX)
                {
                    var c1 = bmpBefore.GetPixel(x, y);
                    var c2 = bmpAfter.GetPixel(x, y);

                    var dr = Math.Abs(c1.R - c2.R);
                    var dg = Math.Abs(c1.G - c2.G);
                    var db = Math.Abs(c1.B - c2.B);

                    if (dr + dg + db > 30) diffPixels++;
                    totalSamples++;
                }
            }

            if (totalSamples == 0) return 0.5;
            return Math.Min(1.0, (double)diffPixels / totalSamples);
        }
        catch (Exception ex)
        {
            PekaLogger.Warn("BeforeAfterVerifier", "Visual change computation failed", ex);
            return 0.5;
        }
    }

    /// <summary>
    /// 检查预期文本是否出现在截图中
    /// </summary>
    private double ComputeExpectedStateMatch(byte[] before, byte[] after, VerificationContext context)
    {
        if (string.IsNullOrEmpty(context.ExpectedText)) return 0.5;

        // 简单方案：检查 after 中是否包含预期文本（假设有 OCR 结果）
        // TODO: 接入 OCR 服务
        var afterText = context.AfterOcrText ?? "";
        if (afterText.Contains(context.ExpectedText))
            return 1.0;

        return 0.2;
    }

    /// <summary>
    /// 检查目标元素状态是否改变
    /// </summary>
    private double ComputeElementStateChange(byte[] before, byte[] after, VerificationContext context)
    {
        var element = context.TargetElement;
        if (element == null) return 0.5;

        // 检查目标区域是否有变化
        var bbox = element.BBox;
        // TODO: 实现感兴趣区域（ROI）的像素比较
        return 0.5; // 占位
    }

    /// <summary>
    /// 检查是否没有出现错误提示
    /// </summary>
    private double ComputeErrorAbsence(byte[] before, byte[] after, VerificationContext context)
    {
        // TODO: 接入 OCR，检测 error/error_dialog/alert 等关键词
        var afterText = context.AfterOcrText ?? "";
        var errorKeywords = new[] { "error", "错误", "alert", "warning", "警告", "failed", "失败" };
        if (errorKeywords.Any(k => afterText.Contains(k)))
            return 0.0;

        return 1.0;
    }

    private string GenerateRecoverySuggestion(VerificationResult result)
    {
        if (result.VisualChange < 0.1)
            return "截图无变化，可能点击未生效，建议重新点击目标元素";
        if (result.ErrorAbsence < 0.5)
            return "检测到错误提示，建议检查页面状态后重试";
        if (result.ExpectedStateMatch < 0.3)
            return "预期内容未出现，建议重新输入或检查输入内容";
        return "验证失败，建议重新截图并重新规划动作";
    }
}

/// <summary>
/// 验证上下文
/// </summary>
public class VerificationContext
{
    public string ActionType { get; set; } = "";
    public UiElement? TargetElement { get; set; }
    public string? ExpectedText { get; set; }
    public string? InputText { get; set; }
    public string? BeforeOcrText { get; set; }
    public string? AfterOcrText { get; set; }
}

/// <summary>
/// 验证结果
/// </summary>
public class VerificationResult
{
    public VerificationOutcome Outcome { get; set; } = VerificationOutcome.Unknown;
    public double VerificationScore { get; set; }

    public double VisualChange { get; set; }
    public double ExpectedStateMatch { get; set; }
    public double ElementStateChange { get; set; }
    public double ErrorAbsence { get; set; }

    public string Message { get; set; } = "";
    public string? RecoverySuggestion { get; set; }
}

public enum VerificationOutcome
{
    Unknown,
    Success,
    Failed
}