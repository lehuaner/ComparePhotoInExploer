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

    // P1：交互(拖拽/缩放)中为 true → 用廉价插值降每帧 GDI+ 开销；静止后回高质
    private bool _fastInterp;

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

            // 统一走全分辨率绘制（静止/拖拽一致，像素无损）
            RenderScene(e.Graphics);
        }
        catch (Exception ex)
        {
            e.Graphics.DrawString($"错误: {ex.Message}", this.Font, Brushes.Red, 10, TitleBarHeight + 10);
        }
    }

    private void RenderScene(Graphics g)
    {
        EnsureCheckerBrush();

        // 绘制自绘标题栏
        DrawTitleBar(g);

        if (_imageCount > 0)
        {
            // 先填充整个图片区域背景（防止分割线移动后格子间出现间隙）
            using var bgBrush = new SolidBrush(_colors.CheckerDark);
            g.FillRectangle(bgBrush, 0, TitleBarHeight, this.ClientSize.Width, this.ClientSize.Height - TitleBarHeight);

            // 绘制每个格子的棋盘格背景
            int totalCells = _cols * _rows;
            for (int i = 0; i < totalCells; i++)
                g.FillRectangle(_checkerBrush!, GetCellRect(i));

            // L2-A：图片优先走 Direct2D(GPU 缩放，大图快且不糊)，失败自动回退 GDI+ DrawImage
            DrawImagesPhase(g);

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

                    bool isHovered = _hoverSplitterIsVertical && _hoverSplitterIndex == c
                        && (shiftHeld ? _hoverSplitterRow == r : true);
                    var linePen = isHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                    g.DrawLine(linePen, (int)lineX, (int)lineTop, (int)lineX, (int)lineBottom);
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

                    bool isHovered = !_hoverSplitterIsVertical && _hoverSplitterIndex == r
                        && (shiftHeld ? _hoverSplitterCol == c : true);
                    var linePen = isHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                    g.DrawLine(linePen, (int)lineLeft, (int)lineY, (int)lineRight, (int)lineY);
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
                    g.FillRectangle(_checkerBrush!, r.X, r.Y, r.Width, r.Height);

                    bool leftHovered = _hoverSplitterIsVertical && _hoverSplitterIndex == gap.Col
                        && (shiftHeld ? _hoverSplitterRow == gap.Row : true);
                    var leftPen = leftHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                    g.DrawLine(leftPen, (int)r.X, (int)r.Y, (int)r.X, (int)(r.Y + r.Height));
                    if (leftHovered) leftPen.Dispose();

                    bool rightHovered = _hoverSplitterIsVertical && _hoverSplitterIndex == gap.Col
                        && (shiftHeld ? _hoverSplitterRow == gap.Row + 1 : true);
                    var rightPen = rightHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                    g.DrawLine(rightPen, (int)(r.X + r.Width), (int)r.Y, (int)(r.X + r.Width), (int)(r.Y + r.Height));
                    if (rightHovered) rightPen.Dispose();

                    bool topHovered = !_hoverSplitterIsVertical && _hoverSplitterIndex == gap.Row
                        && (shiftHeld ? _hoverSplitterCol == gap.Col + 1 : true);
                    var topPen = topHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                    g.DrawLine(topPen, (int)r.X, (int)r.Y, (int)(r.X + r.Width), (int)r.Y);
                    if (topHovered) topPen.Dispose();

                    bool bottomHovered = !_hoverSplitterIsVertical && _hoverSplitterIndex == gap.Row
                        && (shiftHeld ? _hoverSplitterCol == gap.Col : true);
                    var bottomPen = bottomHovered ? new Pen(_colors.SplitterHoverColor, 2) : pen;
                    g.DrawLine(bottomPen, (int)r.X, (int)(r.Y + r.Height), (int)(r.X + r.Width), (int)(r.Y + r.Height));
                    if (bottomHovered) bottomPen.Dispose();
                }
            }

            // Tab互换拖动模式：绘制目标位置高亮边框和源位置半透明覆盖
            if (_isTabSwapping)
            {
                if (_tabSwapSourceIndex >= 0 && _tabSwapSourceIndex < _imageCount)
                {
                    var srcRect = GetCellRect(_tabSwapSourceIndex);
                    using var srcOverlay = new SolidBrush(Color.FromArgb(60, 100, 149, 237));
                    g.FillRectangle(srcOverlay, srcRect);
                }

                if (_tabSwapTargetIndex >= 0 && _tabSwapTargetIndex < _imageCount)
                {
                    var tgtRect = GetCellRect(_tabSwapTargetIndex);
                    using var highlightPen = new Pen(Color.FromArgb(100, 149, 237), 3);
                    g.DrawRectangle(highlightPen, tgtRect.X + 1, tgtRect.Y + 1, tgtRect.Width - 2, tgtRect.Height - 2);
                }
            }

            // 每框独立标尺（叠加在图片之上）
            if (_rulerEnabled)
            {
                for (int i = 0; i < _imageCount; i++)
                {
                    if (_images[i] == null) continue;
                    RulerHelper.Draw(g, GetCellRect(i), _images[i]!.Size, GetEffectiveZoom(i), _offsets[i], _colors);
                }
            }

            // 左上角图片名称（完整文件名，超长换行）
            if (_nameEnabled)
            {
                for (int i = 0; i < _imageCount; i++)
                {
                    if (_images[i] == null) continue;
                    DrawImageName(g, i, GetCellRect(i));
                }
            }

            // 标记绘制（点/线/多边形/框，含构建预览）
            DrawMarkers(g);
        }
        else
        {
            // 无图片时显示提示
            DrawEmptyHint(g);
        }

        if (_isDragOver)
        {
            DrawDropOverlay(g);
        }
        if (_resetOverlay.IsVisible && _imageCount > 0)
        {
            _resetOverlay.Draw(g, _imageCount, _images, _manualOffsets, _imagePaths, _colors, _cols, _rows);
        }
        if (!_historyBarData.IsCollapsed && _historyBarData.GroupCount > 0)
        {
            _historyBarData.Draw(g, 0, TitleBarHeight, this.ClientSize.Width, _hoverHistoryGroup, _colors);
        }
        if (_showHelp)
        {
            DrawHelpPanel(g);
        }
        if (_showZoomHelp)
        {
            DrawZoomHelpPanel(g);
        }

        // 绘制窗口边框（圆角，适配主题）
        if (!_isWindowMaximized)
        {
            float r = NativeMethods.CornerRadius;
            float w = this.ClientSize.Width - 1;
            float h = this.ClientSize.Height - 1;
            using var borderPen = new Pen(_colors.WindowBorderColor, 1);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var borderPath = new System.Drawing.Drawing2D.GraphicsPath();
            borderPath.AddArc(0.5f, 0.5f, r, r, 180, 90);
            borderPath.AddArc(w - r, 0.5f, r, r, 270, 90);
            borderPath.AddArc(w - r, h - r, r, r, 0, 90);
            borderPath.AddArc(0.5f, h - r, r, r, 90, 90);
            borderPath.CloseFigure();
            g.DrawPath(borderPen, borderPath);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
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

    // ===== L2-A：Direct2D GPU 图片绘制 =====
    private D2DImageRenderer? _d2d;
    private bool _d2dFailed;

    private void EnsureD2D()
    {
        if (_d2d != null || _d2dFailed) return;
        try { _d2d = new D2DImageRenderer(); }
        catch { _d2dFailed = true; _d2d = null; }
    }

    /// <summary>用 D2D 画所有可见图片（绑定到 GDI+ 后缓冲 HDC）；不可用时回退 GDI+。</summary>
    private void DrawImagesPhase(Graphics g)
    {
        EnsureD2D();

        bool drawn = false;
        if (_d2d != null)
        {
            var jobs = new List<D2DJob>();
            for (int i = 0; i < _imageCount; i++)
            {
                if (_images[i] == null) continue;
                if (TryBuildD2DJob(i, out var job)) jobs.Add(job);
            }
            if (jobs.Count > 0)
            {
                try
                {
                    g.Flush();
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        _d2d.Render(hdc, new Rectangle(0, 0, this.ClientSize.Width, this.ClientSize.Height), jobs);
                    }
                    finally { g.ReleaseHdc(); }
                    drawn = true;
                }
                catch
                {
                    drawn = false;
                    _d2dFailed = true;
                    _d2d?.Dispose();
                    _d2d = null;
                }
            }
        }

        if (!drawn)
        {
            for (int i = 0; i < _imageCount; i++)
            {
                if (_images[i] == null) continue;
                DrawImage(g, _images[i]!, i, GetCellRect(i), _offsets[i], GetEffectiveZoom(i));
            }
        }
    }

    private bool TryBuildD2DJob(int i, out D2DJob job)
    {
        job = default;
        var image = _images[i]!;
        var drawArea = GetCellRect(i);
        float zoom = GetEffectiveZoom(i);
        var offset = _offsets[i];

        float scaledWidth = image.Width * zoom;
        float scaledHeight = image.Height * zoom;
        float imgX = drawArea.Left + (drawArea.Width - scaledWidth) / 2f + offset.X;
        float imgY = drawArea.Top + (drawArea.Height - scaledHeight) / 2f + offset.Y;

        float visLeft = Math.Max(imgX, drawArea.Left);
        float visTop = Math.Max(imgY, drawArea.Top);
        float visRight = Math.Min(imgX + scaledWidth, drawArea.Right);
        float visBottom = Math.Min(imgY + scaledHeight, drawArea.Bottom);
        float visW = visRight - visLeft;
        float visH = visBottom - visTop;
        if (visW <= 0 || visH <= 0) return false;

        float srcX = (visLeft - imgX) / zoom;
        float srcY = (visTop - imgY) / zoom;
        float srcW = visW / zoom;
        float srcH = visH / zoom;

        // 缩放闪烁修复：明显降采样时从金字塔预降采样层取源（盒式预滤波 → GPU 线性不再摩尔纹闪烁）
        Bitmap srcBmp = (Bitmap)_images[i]!;
        float us = 1f;
        if (zoom < MosaicThreshold && _pyramid != null && i < _pyramid.Length)
        {
            var levels = _pyramid[i];
            float f = srcW / visW; // 全图→目标降采样倍率
            if (levels != null && levels.Count > 0 && f > 1.5f)
            {
                int k = Math.Min((int)Math.Floor(Math.Log2(f)), levels.Count);
                if (k >= 1) { us = 1f / (1 << k); srcBmp = levels[k - 1]; }
            }
        }

        job = new D2DJob(srcBmp,
            visLeft, visTop, visW, visH,
            srcX * us, srcY * us, srcW * us, srcH * us,
            drawArea.Left, drawArea.Top, drawArea.Right, drawArea.Bottom);
        return true;
    }

    private void DrawImage(Graphics g, Image image, int imageIndex, Rectangle drawArea, PointF offset, float zoom)
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

        float srcX = (visLeft - imgX) / zoom;
        float srcY = (visTop - imgY) / zoom;
        float srcW = visWidth / zoom;
        float srcH = visHeight / zoom;

        // O1：交互拖拽且为明显降采样时，改用金字塔中“最贴近目标分辨率”的一级，源像素量大减 → 每帧快很多；
        // 静止（_fastInterp=false）仍走原图全分辨率，像素与之前完全一致。
        Image drawSrc = image;
        RectangleF srcRectF = new RectangleF(srcX, srcY, srcW, srcH);
        if (_fastInterp && zoom < MosaicThreshold && _pyramid != null && imageIndex >= 0 && imageIndex < _pyramid.Length)
        {
            var levels = _pyramid[imageIndex];
            float f = srcW / visWidth; // 全图→目标的降采样倍率
            if (levels != null && levels.Count > 0 && f > 1.5f)
            {
                int k = (int)Math.Floor(Math.Log2(f)); // 期望的整级减半数
                k = Math.Min(k, levels.Count);         // 不超过已有层级
                if (k >= 1)
                {
                    float s = 1f / (1 << k);           // 该级像素 / 原图像素
                    drawSrc = levels[k - 1];
                    srcRectF = new RectangleF(srcX * s, srcY * s, srcW * s, srcH * s);
                }
            }
        }

        g.SetClip(drawArea);

        // 达到马赛克阈值：最近邻 + 半像素对齐，露出整齐的像素方块
        if (zoom >= MosaicThreshold)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        }
        else
        {
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
            // P1：交互中统一用 Bilinear（快），静止时 zoom<0.5 用 Bilinear、否则 HighQualityBilinear
            g.InterpolationMode = (_fastInterp || zoom < 0.5f)
                ? System.Drawing.Drawing2D.InterpolationMode.Bilinear
                : System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        }

        g.DrawImage(drawSrc,
            new RectangleF(visLeft, visTop, visWidth, visHeight),
            srcRectF,
            GraphicsUnit.Pixel);

        g.ResetClip();
    }
}
