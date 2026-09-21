namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的绘制逻辑
/// </summary>
public partial class Form1
{
    // 棋盘格缓存
    private TextureBrush? _checkerBrush;

    // 马赛克阈值：当有效缩放(每个图片像素对应的屏幕像素)达到该倍数时，
    // 改用最近邻插值直接露出真实像素方块。硬编码、默认开启、无需配置。
    private const float MosaicThreshold = 3.0f; // 300%（1 图片像素 >= 3 屏幕像素）

    // P3：名称绘制缓存（避免每帧 new Font 与逐字符 MeasureString）
    private static readonly Font NameFont = new Font("Microsoft YaHei UI", 9F);
    private readonly Dictionary<(string, int), (string[] Lines, float BoxW)> _nameWrapCache = new();

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
                        var r = gap.Rect;
                        e.Graphics.FillRectangle(_checkerBrush!, r.X, r.Y, r.Width, r.Height);

                        // 逐边绘制，悬停时对应边高亮
                        // 左边：垂直分割线 col，行 row
                        bool leftHovered = _hoverSplitterIsVertical && _hoverSplitterIndex == gap.Col
                            && (shiftHeld ? _hoverSplitterRow == gap.Row : true);
                        var leftPen = leftHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                        e.Graphics.DrawLine(leftPen, (int)r.X, (int)r.Y, (int)r.X, (int)(r.Y + r.Height));
                        if (leftHovered) leftPen.Dispose();

                        // 右边：垂直分割线 col，行 row+1
                        bool rightHovered = _hoverSplitterIsVertical && _hoverSplitterIndex == gap.Col
                            && (shiftHeld ? _hoverSplitterRow == gap.Row + 1 : true);
                        var rightPen = rightHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                        e.Graphics.DrawLine(rightPen, (int)(r.X + r.Width), (int)r.Y, (int)(r.X + r.Width), (int)(r.Y + r.Height));
                        if (rightHovered) rightPen.Dispose();

                        // 上边：水平分割线 row，列 col+1
                        bool topHovered = !_hoverSplitterIsVertical && _hoverSplitterIndex == gap.Row
                            && (shiftHeld ? _hoverSplitterCol == gap.Col + 1 : true);
                        var topPen = topHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                        e.Graphics.DrawLine(topPen, (int)r.X, (int)r.Y, (int)(r.X + r.Width), (int)r.Y);
                        if (topHovered) topPen.Dispose();

                        // 下边：水平分割线 row，列 col
                        bool bottomHovered = !_hoverSplitterIsVertical && _hoverSplitterIndex == gap.Row
                            && (shiftHeld ? _hoverSplitterCol == gap.Col : true);
                        var bottomPen = bottomHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                        e.Graphics.DrawLine(bottomPen, (int)r.X, (int)(r.Y + r.Height), (int)(r.X + r.Width), (int)(r.Y + r.Height));
                        if (bottomHovered) bottomPen.Dispose();
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

                // 每框独立标尺（叠加在图片之上，位于标题栏按钮/历史栏下方）
                if (_rulerEnabled)
                {
                    for (int i = 0; i < _imageCount; i++)
                    {
                        if (_images[i] == null) continue;
                        RulerHelper.Draw(e.Graphics, GetCellRect(i), _images[i]!.Size, GetEffectiveZoom(i), _offsets[i], _colors);
                    }
                }

                // 左上角图片名称（完整文件名，超长换行）
                if (_nameEnabled)
                {
                    for (int i = 0; i < _imageCount; i++)
                    {
                        if (_images[i] == null) continue;
                        DrawImageName(e.Graphics, i, GetCellRect(i));
                    }
                }

                // 标记绘制（点/线/多边形/框，含构建预览）
                DrawMarkers(e.Graphics);
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

    /// <summary>
    /// 在每个格子左上角绘制完整图片文件名，超长时逐字换行（不截断）。
    /// 标尺开启时向内下偏移以避免与标尺带重叠。
    /// </summary>
    private void DrawImageName(Graphics g, int index, Rectangle cell)
    {
        if (index >= _imagePaths.Length) return;
        string name = Path.GetFileName(_imagePaths[index]);
        if (string.IsNullOrEmpty(name)) return;

        int inset = _rulerEnabled ? RulerHelper.Thickness + 4 : 6;
        float startX = cell.Left + inset;
        float startY = cell.Top + inset;
        float maxWidth = Math.Max(20, cell.Width - inset - 8);

        var (lines, boxW) = GetWrappedName(g, name, maxWidth);
        float lineH = NameFont.GetHeight(g) + 1f;
        float boxH = lines.Length * lineH;

        g.SetClip(cell);
        using (var bg = new SolidBrush(Color.FromArgb(150, _colors.CheckerDark)))
            g.FillRectangle(bg, startX - 3, startY - 2, boxW + 6, boxH + 3);
        using (var fg = new SolidBrush(_colors.TitleBarFg))
        {
            float y = startY;
            foreach (var l in lines)
            {
                g.DrawString(l, NameFont, fg, startX, y);
                y += lineH;
            }
        }
        g.ResetClip();
    }

    /// <summary>按最大宽度逐字换行（兼容中英文/无空格文件名），结果按 (名称,宽度) 缓存，避免每帧重复测量</summary>
    private (string[] Lines, float BoxW) GetWrappedName(Graphics g, string name, float maxWidth)
    {
        int key = (int)Math.Round(maxWidth);
        if (_nameWrapCache.TryGetValue((name, key), out var hit)) return hit;

        var lines = new List<string>();
        var sb = new System.Text.StringBuilder();
        float w = 0, boxW = 0;
        foreach (char c in name)
        {
            float cw = g.MeasureString(c.ToString(), NameFont).Width;
            if (w + cw > maxWidth && sb.Length > 0)
            {
                lines.Add(sb.ToString());
                boxW = Math.Max(boxW, w);
                sb.Clear();
                w = 0;
            }
            sb.Append(c);
            w += cw;
        }
        if (sb.Length > 0) { lines.Add(sb.ToString()); boxW = Math.Max(boxW, w); }

        var val = (lines.ToArray(), boxW);
        if (_nameWrapCache.Count > 300) _nameWrapCache.Clear();
        _nameWrapCache[(name, key)] = val;
        return val;
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

        // 达到马赛克阈值：最近邻 + 半像素对齐，露出整齐的像素方块
        if (zoom >= MosaicThreshold)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        }
        else
        {
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
            g.InterpolationMode = zoom < 0.5f
                ? System.Drawing.Drawing2D.InterpolationMode.Bilinear
                : System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        }

        g.DrawImage(image,
            new RectangleF(visLeft, visTop, visWidth, visHeight),
            new RectangleF(srcX, srcY, srcW, srcH),
            GraphicsUnit.Pixel);

        g.ResetClip();
    }
}
