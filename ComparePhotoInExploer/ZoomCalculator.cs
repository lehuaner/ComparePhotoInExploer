namespace ComparePhotoInExploer;

/// <summary>
/// 缩放几何计算：屏幕坐标与归一化坐标之间的转换，以及缩放偏移计算
/// </summary>
public static class ZoomCalculator
{
    /// <summary>
    /// 将屏幕坐标转换为图片归一化坐标 (0~1)
    /// </summary>
    public static PointF ScreenToNormalized(PointF screenPos, Rectangle drawArea, float zoom, PointF offset, Size imageSize)
    {
        float scaledWidth = imageSize.Width * zoom;
        float scaledHeight = imageSize.Height * zoom;
        float imgX = drawArea.Left + (drawArea.Width - scaledWidth) / 2f + offset.X;
        float imgY = drawArea.Top + (drawArea.Height - scaledHeight) / 2f + offset.Y;

        float normX = (screenPos.X - imgX) / scaledWidth;
        float normY = (screenPos.Y - imgY) / scaledHeight;
        return new PointF(normX, normY);
    }

    /// <summary>
    /// 在给定归一化点处缩放，计算新的偏移量，保持该点在屏幕上的位置不变
    /// </summary>
    public static PointF ZoomAtNormalized(PointF norm, Rectangle drawArea, float oldZoom, float newZoom, PointF oldOffset, Size imageSize)
    {
        float oldScaledWidth = imageSize.Width * oldZoom;
        float oldScaledHeight = imageSize.Height * oldZoom;
        float oldScreenX = drawArea.Left + (drawArea.Width - oldScaledWidth) / 2f + oldOffset.X + norm.X * oldScaledWidth;
        float oldScreenY = drawArea.Top + (drawArea.Height - oldScaledHeight) / 2f + oldOffset.Y + norm.Y * oldScaledHeight;

        float newScaledWidth = imageSize.Width * newZoom;
        float newScaledHeight = imageSize.Height * newZoom;
        float newOffsetX = oldScreenX - drawArea.Left - (drawArea.Width - newScaledWidth) / 2f - norm.X * newScaledWidth;
        float newOffsetY = oldScreenY - drawArea.Top - (drawArea.Height - newScaledHeight) / 2f - norm.Y * newScaledHeight;

        return new PointF(newOffsetX, newOffsetY);
    }

    /// <summary>
    /// 缩放并将归一化点移动到目标屏幕位置
    /// </summary>
    public static PointF ZoomAndMoveToTarget(PointF norm, Rectangle drawArea, float newZoom, Size imageSize, PointF targetScreenPos)
    {
        float newScaledWidth = imageSize.Width * newZoom;
        float newScaledHeight = imageSize.Height * newZoom;
        float offsetX = targetScreenPos.X - drawArea.Left - (drawArea.Width - newScaledWidth) / 2f - norm.X * newScaledWidth;
        float offsetY = targetScreenPos.Y - drawArea.Top - (drawArea.Height - newScaledHeight) / 2f - norm.Y * newScaledHeight;
        return new PointF(offsetX, offsetY);
    }

    /// <summary>
    /// 图片左上角在屏幕上的坐标（与 DrawImage 完全一致）
    /// </summary>
    public static PointF GetImageTopLeft(Rectangle drawArea, float zoom, PointF offset, Size imageSize)
    {
        float scaledWidth = imageSize.Width * zoom;
        float scaledHeight = imageSize.Height * zoom;
        float imgX = drawArea.Left + (drawArea.Width - scaledWidth) / 2f + offset.X;
        float imgY = drawArea.Top + (drawArea.Height - scaledHeight) / 2f + offset.Y;
        return new PointF(imgX, imgY);
    }

    /// <summary>
    /// 屏幕坐标 -> 图片原始像素坐标（可越界，为负或超出图片尺寸）
    /// </summary>
    public static PointF ScreenToImagePixel(PointF screenPos, Rectangle drawArea, float zoom, PointF offset, Size imageSize)
    {
        var tl = GetImageTopLeft(drawArea, zoom, offset, imageSize);
        if (zoom == 0f) return new PointF(0, 0);
        return new PointF((screenPos.X - tl.X) / zoom, (screenPos.Y - tl.Y) / zoom);
    }

    /// <summary>
    /// 图片原始像素坐标 -> 屏幕坐标
    /// </summary>
    public static PointF ImagePixelToScreen(PointF imgPixel, Rectangle drawArea, float zoom, PointF offset, Size imageSize)
    {
        var tl = GetImageTopLeft(drawArea, zoom, offset, imageSize);
        return new PointF(tl.X + imgPixel.X * zoom, tl.Y + imgPixel.Y * zoom);
    }
}
