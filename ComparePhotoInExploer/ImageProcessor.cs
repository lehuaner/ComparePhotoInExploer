using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ComparePhotoInExploer;

/// <summary>
/// 图片处理：以最短边为方形，截取颜色最丰富区域，然后压缩
/// </summary>
public static class ImageProcessor
{
    /// <summary>
    /// 缩略图边长（像素）
    /// </summary>
    private const int ThumbnailSize = 24;

    /// <summary>
    /// 将图片裁剪为方形（以最短边为边长），选取颜色最丰富区域，再压缩为缩略图
    /// </summary>
    public static Bitmap CreateThumbnail(string imagePath)
    {
        using var src = LoadImage(imagePath);
        if (src == null) return CreateEmptyThumbnail();

        int side = Math.Min(src.Width, src.Height);
        var (bestX, bestY) = FindMostColorfulRegion(src, side);
        var cropped = CropSquare(src, bestX, bestY, side);
        var thumb = CompressWithDetail(cropped, ThumbnailSize);
        cropped.Dispose();
        return thumb;
    }

    private static Bitmap? LoadImage(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            return new Bitmap(fs);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap CreateEmptyThumbnail()
    {
        var bmp = new Bitmap(ThumbnailSize, ThumbnailSize);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.LightGray);
        return bmp;
    }

    /// <summary>
    /// 使用 BitmapData 直接内存访问，大幅提升颜色丰富度计算性能
    /// </summary>
    private static (int x, int y) FindMostColorfulRegion(Bitmap src, int side)
    {
        int maxShiftX = src.Width - side;
        int maxShiftY = src.Height - side;

        if (maxShiftX <= 0 && maxShiftY <= 0)
            return (0, 0);

        // 将整张图像素数据一次性提取到内存
        var pixelData = ExtractPixelData(src);

        // 粗搜索步长
        int step = Math.Max(2, Math.Min(maxShiftX, maxShiftY) / 8);
        if (step < 2) step = 2;

        double bestScore = -1;
        int bestX = 0, bestY = 0;

        // 粗搜索
        for (int y = 0; y <= maxShiftY; y += step)
        {
            for (int x = 0; x <= maxShiftX; x += step)
            {
                double score = CalcColorRichnessFast(pixelData, src.Width, src.Height, x, y, side);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        // 精搜索（在最佳点附近细化）
        int fineRange = step;
        int fineStep = Math.Max(1, fineRange / 3);
        for (int y = Math.Max(0, bestY - fineRange); y <= Math.Min(maxShiftY, bestY + fineRange); y += fineStep)
        {
            for (int x = Math.Max(0, bestX - fineRange); x <= Math.Min(maxShiftX, bestX + fineRange); x += fineStep)
            {
                double score = CalcColorRichnessFast(pixelData, src.Width, src.Height, x, y, side);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        return (bestX, bestY);
    }

    /// <summary>
    /// 将 Bitmap 像素数据一次性提取为 byte[] 数组 (BGR格式)
    /// </summary>
    private static byte[] ExtractPixelData(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
            var data = new byte[bytes];
            Marshal.Copy(bmpData.Scan0, data, 0, bytes);
            return data;
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }
    }

    /// <summary>
    /// 使用预提取的像素数据快速计算颜色丰富度（单遍计算方差，Welford算法）
    /// </summary>
    private static double CalcColorRichnessFast(byte[] pixelData, int imgW, int imgH, int startX, int startY, int side)
    {
        int endX = Math.Min(startX + side, imgW);
        int endY = Math.Min(startY + side, imgH);

        // 计算每行字节数（Format24bppRgb 的 stride 可能有对齐填充）
        int stride = imgW * 3;
        // 按 4 字节对齐的 stride
        int actualStride = (imgW * 3 + 3) & ~3;

        // 采样步长（在已缩小后的搜索范围内均匀采样）
        int sampleStep = Math.Max(2, side / 24);

        // Welford 在线方差算法（单遍）
        long n = 0;
        double meanR = 0, meanG = 0, meanB = 0;
        double m2R = 0, m2G = 0, m2B = 0;

        for (int y = startY; y < endY; y += sampleStep)
        {
            int rowOffset = y * actualStride;
            for (int x = startX; x < endX; x += sampleStep)
            {
                int idx = rowOffset + x * 3;
                if (idx + 2 >= pixelData.Length) continue;

                // Format24bppRgb: B, G, R
                double b = pixelData[idx];
                double g = pixelData[idx + 1];
                double r = pixelData[idx + 2];

                n++;
                double dB = b - meanB;
                meanB += dB / n;
                m2B += dB * (b - meanB);

                double dG = g - meanG;
                meanG += dG / n;
                m2G += dG * (g - meanG);

                double dR = r - meanR;
                meanR += dR / n;
                m2R += dR * (r - meanR);
            }
        }

        if (n < 2) return 0;

        // 方差 = m2 / n
        return (m2R + m2G + m2B) / n;
    }

    private static Bitmap CropSquare(Bitmap src, int x, int y, int side)
    {
        int w = Math.Min(side, src.Width - x);
        int h = Math.Min(side, src.Height - y);
        var rect = new Rectangle(x, y, w, h);
        return src.Clone(rect, src.PixelFormat);
    }

    private static Bitmap CompressWithDetail(Bitmap src, int targetSize)
    {
        var thumb = new Bitmap(targetSize, targetSize);
        using var g = Graphics.FromImage(thumb);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, targetSize, targetSize);
        return thumb;
    }
}
