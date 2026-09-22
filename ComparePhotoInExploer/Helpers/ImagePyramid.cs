using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ComparePhotoInExploer;

/// <summary>
/// 为一张图构建逐级减半的降采样金字塔。levels[k] 尺寸为原图的 1/2^(k+1)。
/// 用途：交互拖拽大图时从"接近目标分辨率"的那级绘制，避免 GDI+ 每帧对海量源像素做全量降采样。
/// 约定：某级的像素坐标 = 原图像素坐标 * (1/2^(k+1))。
/// </summary>
internal static class ImagePyramid
{
    public const int MinSide = 128;  // 短边小于此值不再继续细分
    public const int MaxLevels = 8;  // 级别上限，控制内存

    /// <summary>从原图逐级 1/2 构建金字塔（各级用高质量双三次盒式降采样，抗锯齿良好）。</summary>
    public static List<Bitmap> Build(Image full)
    {
        var levels = new List<Bitmap>();
        Image src = full;
        for (int k = 0; k < MaxLevels; k++)
        {
            int nw = src.Width / 2;
            int nh = src.Height / 2;
            if (nw < 1 || nh < 1) break;
            if (Math.Min(nw, nh) < MinSide) break;
            var nb = Downscale(src, nw, nh);
            levels.Add(nb);
            src = nb; // 上一级已在 levels 中（随金字塔统一释放），继续从其减半
        }
        return levels;
    }

    private static Bitmap Downscale(Image src, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;
        g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
        return bmp;
    }

    public static void Dispose(List<Bitmap>? levels)
    {
        if (levels == null) return;
        foreach (var b in levels) b.Dispose();
    }
}
