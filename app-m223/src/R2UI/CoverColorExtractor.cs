using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HuayuanPreview.R2;

/// <summary>
/// R5: 封面主色提取。缩 64×64 采样，转 HSV，S&gt;0.15 优先、灰像素减半，
/// 输出主色供背景染色。取色在后台线程执行，UI 只接收结果。
/// </summary>
internal static class CoverColorExtractor
{
    internal static Task<Color?> ExtractAsync(Bitmap? bitmap, CancellationToken ct = default)
    {
        if (bitmap == null) return Task.FromResult<Color?>(null);
        return Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                return Extract(bitmap);
            }
            catch (OperationCanceledException) { return null; }
            catch { return null; } // 取色失败静默回退，不报错
        }, ct);
    }

    private static Color? Extract(Bitmap bitmap)
    {
        int size = (int)DesignTokens.CoverSampleSize; // 64
        using var scaled = bitmap.CreateScaledBitmap(new PixelSize(size, size), BitmapInterpolationMode.MediumQuality);
        if (scaled == null) return null;

        // 统一读入 BGRA8888 缓冲，避免后端格式差异。
        var target = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = target.Lock())
        {
            scaled.CopyPixels(fb);
        }

        var pixels = new byte[size * size * 4];
        using (var fb = target.Lock())
        {
            var stride = fb.RowBytes;
            var address = fb.Address;
            if (address == IntPtr.Zero) return null;
            var row = new byte[stride];
            for (int y = 0; y < size; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(address, y * stride), row, 0, Math.Min(stride, row.Length));
                for (int x = 0; x < size; x++)
                {
                    int src = x * 4;
                    int dst = (y * size + x) * 4;
                    if (src + 3 >= row.Length) continue;
                    pixels[dst] = row[src];         // B
                    pixels[dst + 1] = row[src + 1]; // G
                    pixels[dst + 2] = row[src + 2]; // R
                    pixels[dst + 3] = row[src + 3]; // A
                }
            }
        }

        // 累加：S>0.15 优先、灰像素减半。
        double rSum = 0, gSum = 0, bSum = 0, weightSum = 0;
        for (int i = 0; i < size * size; i++)
        {
            int p = i * 4;
            double r = pixels[p + 2] / 255.0, g = pixels[p + 1] / 255.0, b = pixels[p] / 255.0;
            var (_, s, _) = RgbToHsv(r, g, b);
            double weight = s > DesignTokens.CoverMinSaturation ? 1.0 : DesignTokens.CoverGrayWeight;
            if (s <= DesignTokens.CoverMinSaturation) weight *= DesignTokens.CoverGrayWeight;
            rSum += r * weight; gSum += g * weight; bSum += b * weight; weightSum += weight;
        }
        if (weightSum <= 0) return null;
        return Color.FromRgb(
            (byte)Math.Clamp((int)(rSum / weightSum * 255), 0, 255),
            (byte)Math.Clamp((int)(gSum / weightSum * 255), 0, 255),
            (byte)Math.Clamp((int)(bSum / weightSum * 255), 0, 255));
    }

    private static (double H, double S, double V) RgbToHsv(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;
        double s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }

    /// <summary>把封面主色映射为染背景色：深色 S≤0.35/V≤0.28，浅色 S≤0.20/V≥0.92。</summary>
    internal static Color TintForBackground(Color main, bool dark)
    {
        var (h, s, v) = RgbToHsv(main.R / 255.0, main.G / 255.0, main.B / 255.0);
        double targetS = dark ? DesignTokens.DarkBackSaturation : DesignTokens.LightBackSaturation;
        double targetV = dark ? DesignTokens.DarkBackValue : DesignTokens.LightBackValue;
        return HsvToRgb(h, Math.Min(s, targetS), dark ? Math.Min(v, targetV) : Math.Max(v, targetV));
    }

    internal static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return Color.FromRgb(
            (byte)Math.Clamp((int)((r + m) * 255), 0, 255),
            (byte)Math.Clamp((int)((g + m) * 255), 0, 255),
            (byte)Math.Clamp((int)((b + m) * 255), 0, 255));
    }
}
