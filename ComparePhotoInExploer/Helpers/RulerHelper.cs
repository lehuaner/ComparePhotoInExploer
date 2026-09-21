using System.Drawing.Drawing2D;

namespace ComparePhotoInExploer;

/// <summary>
/// 每框独立标尺绘制：以图片原始像素为单位，叠加在每个格子的顶部(横向)与左侧(纵向)，
/// 跟随图片移动/缩放，分度值按 1/2/5×10ⁿ 自适应。
/// </summary>
public static class RulerHelper
{
    /// <summary>标尺带厚度（屏幕像素）</summary>
    public const int Thickness = 18;

    // 主刻度的目标屏幕间距（约多少像素一个主刻度）
    private const double TargetMajorPixels = 64.0;

    // P3：静态字体 + 刻度标签宽度缓存（避免每帧 new Font 与重复 MeasureString）
    private static readonly Font RulerFont = new Font("Microsoft YaHei UI", 7.5F);
    private static readonly Dictionary<string, float> LabelWidthCache = new();

    private static float LabelWidth(Graphics g, string s)
    {
        if (LabelWidthCache.TryGetValue(s, out var w)) return w;
        w = g.MeasureString(s, RulerFont).Width;
        if (LabelWidthCache.Count > 500) LabelWidthCache.Clear();
        LabelWidthCache[s] = w;
        return w;
    }

    public static void Draw(Graphics g, Rectangle cell, Size imageSize, float effZoom, PointF offset, ThemeColorSet colors)
    {
        if (effZoom <= 0f || imageSize.Width <= 0 || imageSize.Height <= 0)
            return;

        var tl = ZoomCalculator.GetImageTopLeft(cell, effZoom, offset, imageSize);
        float imgX = tl.X, imgY = tl.Y;

        double major = NiceStep(TargetMajorPixels / effZoom);
        double minor = major / 5.0;
        if (minor <= 0) return;

        var state = g.Save();
        g.SetClip(cell);

        using var bandBrush = new SolidBrush(Color.FromArgb(150, colors.CheckerDark));
        using var borderPen = new Pen(colors.GridLineColor, 1);
        using var tickPen = new Pen(colors.TitleBarFg, 1);
        using var labelBrush = new SolidBrush(colors.TitleBarFg);

        // ===== 顶部横向标尺 =====
        g.FillRectangle(bandBrush, cell.Left, cell.Top, cell.Width, Thickness);
        g.DrawLine(borderPen, cell.Left, cell.Top + Thickness, cell.Right, cell.Top + Thickness);

        double pxStart = (cell.Left - imgX) / effZoom;
        double pxEnd = (cell.Right - imgX) / effZoom;
        for (double p = Math.Floor(pxStart / minor) * minor; p <= pxEnd; p += minor)
        {
            if (p < 0 || p > imageSize.Width) continue;
            float sx = imgX + (float)p * effZoom;
            bool isMajor = IsMultiple(p, major);
            float tickH = isMajor ? Thickness : Thickness * 0.5f;
            g.DrawLine(tickPen, sx, cell.Top, sx, cell.Top + tickH);
            if (isMajor)
            {
                string label = ((int)Math.Round(p)).ToString();
                float lw = LabelWidth(g, label);
                float lx = Math.Min(sx + 2, cell.Right - lw - 1);
                g.DrawString(label, RulerFont, labelBrush, lx, cell.Top + 1);
            }
        }

        // ===== 左侧纵向标尺 =====
        g.FillRectangle(bandBrush, cell.Left, cell.Top, Thickness, cell.Height);
        g.DrawLine(borderPen, cell.Left + Thickness, cell.Top, cell.Left + Thickness, cell.Bottom);

        double pyStart = (cell.Top - imgY) / effZoom;
        double pyEnd = (cell.Bottom - imgY) / effZoom;
        for (double p = Math.Floor(pyStart / minor) * minor; p <= pyEnd; p += minor)
        {
            if (p < 0 || p > imageSize.Height) continue;
            float sy = imgY + (float)p * effZoom;
            bool isMajor = IsMultiple(p, major);
            float tickW = isMajor ? Thickness : Thickness * 0.5f;
            g.DrawLine(tickPen, cell.Left, sy, cell.Left + tickW, sy);
            if (isMajor)
            {
                string label = ((int)Math.Round(p)).ToString();
                var ts = g.Save();
                g.TranslateTransform(cell.Left + 2, sy + 2);
                g.DrawString(label, RulerFont, labelBrush, 0, 0);
                g.Restore(ts);
            }
        }

        g.Restore(state);
    }

    /// <summary>把原始步长归一到 1/2/5×10ⁿ 中不小于它的最小值</summary>
    private static double NiceStep(double raw)
    {
        if (raw <= 0) return 1;
        double exp = Math.Floor(Math.Log10(raw));
        double frac = Math.Pow(10, exp);
        double n = raw / frac; // 1..10
        double nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10;
        return nice * frac;
    }

    private static bool IsMultiple(double p, double step)
    {
        if (step <= 0) return false;
        double r = p / step;
        return Math.Abs(r - Math.Round(r)) < 1e-6;
    }
}
