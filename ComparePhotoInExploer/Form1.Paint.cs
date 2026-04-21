namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的绘制逻辑
/// </summary>
public partial class Form1
{
    // 棋盘格缓存
    private TextureBrush? _checkerBrush;

    private void Form1_Paint(object? sender, PaintEventArgs e)
    {
        try
        {
            // 历史记录和按键说明/缩放说明互斥
            if (!_historyBarData.IsCollapsed)
            {
                _showHelp = false;
                _showZoomHelp = false;
            }

            EnsureCheckerBrush();

            // 绘制自绘标题栏
            DrawTitleBar(e.Graphics);

            if (_imageCount > 0)
            {
                // 图片区域（不因历史记录展开而偏移）
                int totalCells = _cols * _rows;
                for (int i = 0; i < totalCells; i++)
                {
                    var rect = GetCellRect(i);

                    // 棋盘格背景
                    e.Graphics.FillRectangle(_checkerBrush!, rect);

                    // 绘制图片（仅在对应格子有图片时）
                    if (i < _imageCount && _images[i] != null)
                    {
                        DrawImage(e.Graphics, _images[i]!, rect, _offsets[i], GetEffectiveZoom(i));
                    }
                }

                // 绘制网格分割线
                using var pen = new Pen(_colors.GridLineColor, 2);
                int topOffset = TitleBarHeight;
                int availH = this.ClientSize.Height - topOffset;
                int cellW = this.ClientSize.Width / _cols;
                int cellH = availH / _rows;
                for (int c = 1; c < _cols; c++)
                    e.Graphics.DrawLine(pen, c * cellW, topOffset, c * cellW, this.ClientSize.Height);
                for (int r = 1; r < _rows; r++)
                    e.Graphics.DrawLine(pen, 0, topOffset + r * cellH, this.ClientSize.Width, topOffset + r * cellH);
            }
            else
            {
                // 无图片时显示提示
                DrawEmptyHint(e.Graphics);
            }

            // 拖放覆盖提示
            if (_isDragOver)
            {
                DrawDropOverlay(e.Graphics);
            }

            // 偏移重置覆盖层
            if (_resetOverlay.IsVisible && _imageCount > 0)
            {
                _resetOverlay.Draw(e.Graphics, _imageCount, _images, _manualOffsets, _imagePaths, _colors, _cols, _rows);
            }

            // 历史记录覆盖层（浮在图片区域上方）
            if (!_historyBarData.IsCollapsed && _historyBarData.GroupCount > 0)
            {
                _historyBarData.Draw(e.Graphics, 0, TitleBarHeight, this.ClientSize.Width, _hoverHistoryGroup, _colors);
            }

            // 按键说明（由标题栏按钮触发，与历史记录互斥）
            if (_showHelp)
            {
                DrawHelpPanel(e.Graphics);
            }

            // 缩放说明（由标题栏按钮触发，与历史记录互斥）
            if (_showZoomHelp)
            {
                DrawZoomHelpPanel(e.Graphics);
            }

            // 绘制窗口边框（圆角，适配主题）
            if (!_isWindowMaximized)
            {
                float r = NativeMethods.CornerRadius;
                float w = this.ClientSize.Width - 1;
                float h = this.ClientSize.Height - 1;
                using var borderPen = new Pen(_colors.WindowBorderColor, 1);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var borderPath = new System.Drawing.Drawing2D.GraphicsPath();
                borderPath.AddArc(0.5f, 0.5f, r, r, 180, 90);
                borderPath.AddArc(w - r, 0.5f, r, r, 270, 90);
                borderPath.AddArc(w - r, h - r, r, r, 0, 90);
                borderPath.AddArc(0.5f, h - r, r, r, 90, 90);
                borderPath.CloseFigure();
                e.Graphics.DrawPath(borderPen, borderPath);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            }
        }
        catch (Exception ex)
        {
            e.Graphics.DrawString($"错误: {ex.Message}", this.Font, Brushes.Red, 10, TitleBarHeight + 10);
        }
    }

    private void EnsureCheckerBrush()
    {
        if (_checkerBrush != null) return;

        int size = 8;
        using var bmp = new Bitmap(size * 2, size * 2);
        using (var g = Graphics.FromImage(bmp))
        {
            g.FillRectangle(new SolidBrush(_colors.CheckerLight), 0, 0, size * 2, size * 2);
            g.FillRectangle(new SolidBrush(_colors.CheckerDark), 0, 0, size, size);
            g.FillRectangle(new SolidBrush(_colors.CheckerDark), size, size, size, size);
        }
        _checkerBrush = new TextureBrush(bmp);
    }

    private void DrawImage(Graphics g, Image image, Rectangle drawArea, PointF offset, float zoom)
    {
        float scaledWidth = image.Width * zoom;
        float scaledHeight = image.Height * zoom;

        float imgX = drawArea.Left + (drawArea.Width - scaledWidth) / 2f + offset.X;
        float imgY = drawArea.Top + (drawArea.Height - scaledHeight) / 2f + offset.Y;

        float visLeft = Math.Max(imgX, drawArea.Left);
        float visTop = Math.Max(imgY, drawArea.Top);
        float visRight = Math.Min(imgX + scaledWidth, drawArea.Right);
        float visBottom = Math.Min(imgY + scaledHeight, drawArea.Bottom);

        float visWidth = visRight - visLeft;
        float visHeight = visBottom - visTop;

        if (visWidth <= 0 || visHeight <= 0)
            return;

        g.SetClip(drawArea);

        float srcX = (visLeft - imgX) / zoom;
        float srcY = (visTop - imgY) / zoom;
        float srcW = visWidth / zoom;
        float srcH = visHeight / zoom;

        g.InterpolationMode = zoom < 0.5f
            ? System.Drawing.Drawing2D.InterpolationMode.Bilinear
            : System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;

        g.DrawImage(image,
            new RectangleF(visLeft, visTop, visWidth, visHeight),
            new RectangleF(srcX, srcY, srcW, srcH),
            GraphicsUnit.Pixel);

        g.ResetClip();
    }
}
