using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PeekabooWin.Core.Ocr;

public class OcrPreprocessor
{
    public int ScaleFactor { get; set; } = 2;
    public bool EnableDenoising { get; set; } = true;
    public bool EnableBinarization { get; set; } = true;
    public int DenoiseRadius { get; set; } = 2;
    public int BinarizationThreshold { get; set; } = 0;

    public Bitmap Preprocess(Bitmap source)
    {
        var processed = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(processed))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (EnableDenoising)
            {
                using var denoised = ApplyMedianFilter(source);
                g.DrawImage(denoised, 0, 0, source.Width, source.Height);
            }
            else
            {
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }
        }

        if (EnableBinarization)
        {
            processed = ApplyAdaptiveBinarization(processed);
        }

        if (ScaleFactor > 1)
        {
            processed = ScaleBitmap(processed, ScaleFactor);
        }

        return processed;
    }

    private Bitmap ApplyMedianFilter(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        var sourceData = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var resultData = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            var stride = sourceData.Stride;
            var scan0 = sourceData.Scan0;
            var resultScan0 = resultData.Scan0;

            for (int y = DenoiseRadius; y < height - DenoiseRadius; y++)
            {
                for (int x = DenoiseRadius; x < width - DenoiseRadius; x++)
                {
                    var r = new List<byte>();
                    var g = new List<byte>();
                    var b = new List<byte>();

                    for (int ky = -DenoiseRadius; ky <= DenoiseRadius; ky++)
                    {
                        for (int kx = -DenoiseRadius; kx <= DenoiseRadius; kx++)
                        {
                            var idx = (y + ky) * stride + (x + kx) * 4;
                            b.Add(Marshal.ReadByte(scan0, idx));
                            g.Add(Marshal.ReadByte(scan0, idx + 1));
                            r.Add(Marshal.ReadByte(scan0, idx + 2));
                        }
                    }

                    r.Sort(); g.Sort(); b.Sort();
                    var medianIdx = r.Count / 2;

                    var pixelIdx = y * stride + x * 4;
                    Marshal.WriteByte(resultScan0, pixelIdx, b[medianIdx]);
                    Marshal.WriteByte(resultScan0, pixelIdx + 1, g[medianIdx]);
                    Marshal.WriteByte(resultScan0, pixelIdx + 2, r[medianIdx]);
                    Marshal.WriteByte(resultScan0, pixelIdx + 3, 255);
                }
            }

            for (int y = 0; y < DenoiseRadius; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var srcIdx = y * stride + x * 4;
                    var dstIdx = y * stride + x * 4;
                    for (int c = 0; c < 4; c++)
                        Marshal.WriteByte(resultScan0, dstIdx + c, Marshal.ReadByte(scan0, srcIdx + c));
                }
            }
            for (int y = height - DenoiseRadius; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var srcIdx = y * stride + x * 4;
                    var dstIdx = y * stride + x * 4;
                    for (int c = 0; c < 4; c++)
                        Marshal.WriteByte(resultScan0, dstIdx + c, Marshal.ReadByte(scan0, srcIdx + c));
                }
            }
            for (int y = DenoiseRadius; y < height - DenoiseRadius; y++)
            {
                for (int x = 0; x < DenoiseRadius; x++)
                {
                    var srcIdx = y * stride + x * 4;
                    var dstIdx = y * stride + x * 4;
                    for (int c = 0; c < 4; c++)
                        Marshal.WriteByte(resultScan0, dstIdx + c, Marshal.ReadByte(scan0, srcIdx + c));
                }
                for (int x = width - DenoiseRadius; x < width; x++)
                {
                    var srcIdx = y * stride + x * 4;
                    var dstIdx = y * stride + x * 4;
                    for (int c = 0; c < 4; c++)
                        Marshal.WriteByte(resultScan0, dstIdx + c, Marshal.ReadByte(scan0, srcIdx + c));
                }
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);
        }

        return result;
    }

    private Bitmap ApplyAdaptiveBinarization(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        var sourceData = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var resultData = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            var stride = sourceData.Stride;
            var scan0 = sourceData.Scan0;
            var resultScan0 = resultData.Scan0;
            var blockSize = 15;
            var threshold = BinarizationThreshold;

            if (threshold == 0)
            {
                var totalSum = 0L;
                var totalPixels = width * height;
                for (int i = 0; i < totalPixels * 4; i += 4)
                {
                    var gray = (byte)(0.299 * Marshal.ReadByte(scan0, i + 2) +
                                      0.587 * Marshal.ReadByte(scan0, i + 1) +
                                      0.114 * Marshal.ReadByte(scan0, i));
                    totalSum += gray;
                }
                threshold = (int)(totalSum / totalPixels);
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var sum = 0;
                    var count = 0;
                    var halfBlock = blockSize / 2;

                    for (int ky = -halfBlock; ky <= halfBlock && y + ky < height; ky++)
                    {
                        for (int kx = -halfBlock; kx <= halfBlock && x + kx < width; kx++)
                        {
                            if (y + ky >= 0 && x + kx >= 0)
                            {
                                var idx = (y + ky) * stride + (x + kx) * 4;
                                var gray = (byte)(0.299 * Marshal.ReadByte(scan0, idx + 2) +
                                                  0.587 * Marshal.ReadByte(scan0, idx + 1) +
                                                  0.114 * Marshal.ReadByte(scan0, idx));
                                sum += gray;
                                count++;
                            }
                        }
                    }

                    var localThreshold = count > 0 ? sum / count : threshold;
                    var pixelIdx = y * stride + x * 4;
                    var grayVal = (byte)(0.299 * Marshal.ReadByte(scan0, pixelIdx + 2) +
                                         0.587 * Marshal.ReadByte(scan0, pixelIdx + 1) +
                                         0.114 * Marshal.ReadByte(scan0, pixelIdx));
                    var value = grayVal > localThreshold ? 255 : 0;

                    Marshal.WriteByte(resultScan0, pixelIdx, (byte)value);
                    Marshal.WriteByte(resultScan0, pixelIdx + 1, (byte)value);
                    Marshal.WriteByte(resultScan0, pixelIdx + 2, (byte)value);
                    Marshal.WriteByte(resultScan0, pixelIdx + 3, 255);
                }
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);
        }

        return result;
    }

    private Bitmap ScaleBitmap(Bitmap source, int factor)
    {
        var newWidth = source.Width * factor;
        var newHeight = source.Height * factor;
        var result = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, newWidth, newHeight);
        }

        return result;
    }

    public Bitmap EnhanceContrast(Bitmap source, double factor = 1.5)
    {
        var width = source.Width;
        var height = source.Height;
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        var sourceData = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var resultData = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            var stride = sourceData.Stride;
            var scan0 = sourceData.Scan0;
            var resultScan0 = resultData.Scan0;

            for (int i = 0; i < width * height * 4; i += 4)
            {
                var r = Marshal.ReadByte(scan0, i + 2);
                var g = Marshal.ReadByte(scan0, i + 1);
                var b = Marshal.ReadByte(scan0, i);

                r = (byte)Math.Min(255, Math.Max(0, ((r - 128) * factor) + 128));
                g = (byte)Math.Min(255, Math.Max(0, ((g - 128) * factor) + 128));
                b = (byte)Math.Min(255, Math.Max(0, ((b - 128) * factor) + 128));

                Marshal.WriteByte(resultScan0, i, b);
                Marshal.WriteByte(resultScan0, i + 1, g);
                Marshal.WriteByte(resultScan0, i + 2, r);
                Marshal.WriteByte(resultScan0, i + 3, 255);
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);
        }

        return result;
    }

    public Bitmap Sharpen(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        var kernel = new double[,]
        {
            { 0, -1, 0 },
            { -1, 5, -1 },
            { 0, -1, 0 }
        };

        var sourceData = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var resultData = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            var stride = sourceData.Stride;
            var scan0 = sourceData.Scan0;
            var resultScan0 = resultData.Scan0;

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    double[] sum = { 0, 0, 0 };

                    for (int ky = -1; ky <= 1; ky++)
                    {
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            var idx = (y + ky) * stride + (x + kx) * 4;
                            var weight = kernel[ky + 1, kx + 1];
                            sum[0] += Marshal.ReadByte(scan0, idx) * weight;
                            sum[1] += Marshal.ReadByte(scan0, idx + 1) * weight;
                            sum[2] += Marshal.ReadByte(scan0, idx + 2) * weight;
                        }
                    }

                    var pixelIdx = y * stride + x * 4;
                    Marshal.WriteByte(resultScan0, pixelIdx, (byte)Math.Clamp(sum[0], 0, 255));
                    Marshal.WriteByte(resultScan0, pixelIdx + 1, (byte)Math.Clamp(sum[1], 0, 255));
                    Marshal.WriteByte(resultScan0, pixelIdx + 2, (byte)Math.Clamp(sum[2], 0, 255));
                    Marshal.WriteByte(resultScan0, pixelIdx + 3, 255);
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (y == 0 || y == height - 1 || x == 0 || x == width - 1)
                    {
                        var srcIdx = y * stride + x * 4;
                        var dstIdx = y * stride + x * 4;
                        for (int c = 0; c < 4; c++)
                            Marshal.WriteByte(resultScan0, dstIdx + c, Marshal.ReadByte(scan0, srcIdx + c));
                    }
                }
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
            result.UnlockBits(resultData);
        }

        return result;
    }
}
