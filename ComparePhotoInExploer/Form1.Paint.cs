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
                // 先填充整个图片区域背景（防止分割线移动后格子间出现间隙）
                using var bgBrush = new SolidBrush(_colors.CheckerDark);
                e.Graphics.FillRectangle(bgBrush, 0, TitleBarHeight, this.ClientSize.Width, this.ClientSize.Height - TitleBarHeight);

                // 绘制每个格子的棋盘格背景和图片
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

                // 绘制网格分割线（使用单元格级别的位置）
                using var pen = new Pen(_colors.GridLineColor, 2);
                bool shiftHeld = IsShiftPressed();

                // 垂直分割线：逐行绘制
                for (int c = 0; c < _cols - 1; c++)
                {
                    for (int r = 0; r < _rows; r++)
                    {
                        int cellIdx = r * _cols + c;
                        float lineX = GetCellLeft(cellIdx) + GetCellWidth(cellIdx);
                        float lineTop = GetCellTop(cellIdx);
                        float lineBottom = lineTop + GetCellHeight(cellIdx);

                        // 普通模式：整条高亮；Shift模式：只高亮一节
                        bool isHovered = _hoverSplitterIsVertical && _hoverSplitterIndex == c
                            && (shiftHeld ? _hoverSplitterRow == r : true);
                        var linePen = isHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                        e.Graphics.DrawLine(linePen, (int)lineX, (int)lineTop, (int)lineX, (int)lineBottom);
                        if (isHovered) linePen.Dispose();
                    }
                }

                // 水平分割线：逐列绘制
                for (int r = 0; r < _rows - 1; r++)
                {
                    for (int c = 0; c < _cols; c++)
                    {
                        int cellIdx = r * _cols + c;
                        float lineY = GetCellTop(cellIdx) + GetCellHeight(cellIdx);
                        float lineLeft = GetCellLeft(cellIdx);
                        float lineRight = lineLeft + GetCellWidth(cellIdx);

                        // 普通模式：整条高亮；Shift模式：只高亮一节
                        bool isHovered = !_hoverSplitterIsVertical && _hoverSplitterIndex == r
                            && (shiftHeld ? _hoverSplitterCol == c : true);
                        var linePen = isHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                        e.Graphics.DrawLine(linePen, (int)lineLeft, (int)lineY, (int)lineRight, (int)lineY);
                        if (isHovered) linePen.Dispose();
                    }
                }

                // 填充华容道空区域（Shift拖动后单元格边界不对齐产生的未覆盖区域）
                var gapRegions = GetGapRegions();
                if (gapRegions.Count > 0)
                {
                    foreach (var gap in gapRegions)
                    {
                        e.Graphics.FillRectangle(_checkerBrush!, gap.X, gap.Y, gap.Width, gap.Height);
                    }
                }

                // Tab互换拖动模式：绘制目标位置高亮边框和源位置半透明覆盖
                if (_isTabSwapping)
                {
                    // 源位置半透明遮罩
                    if (_tabSwapSourceIndex >= 0 && _tabSwapSourceIndex < _imageCount)
                    {
                        var srcRect = GetCellRect(_tabSwapSourceIndex);
                        using var srcOverlay = new SolidBrush(Color.FromArgb(60, 100, 149, 237));
                        e.Graphics.FillRectangle(srcOverlay, srcRect);
                    }

                    // 目标位置高亮边框
                    if (_tabSwapTargetIndex >= 0 && _tabSwapTargetIndex < _imageCount)
                    {
                        var tgtRect = GetCellRect(_tabSwapTargetIndex);
                        using var highlightPen = new Pen(Color.FromArgb(100, 149, 237), 3);
                        e.Graphics.DrawRectangle(highlightPen, tgtRect.X + 1, tgtRect.Y + 1, tgtRect.Width - 2, tgtRect.Height - 2);
                    }
                }
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
