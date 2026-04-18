namespace ComparePhotoInExploer;

/// <summary>
/// 重置偏移覆盖层的布局计算、绘制与交互逻辑
/// </summary>
public class ResetOverlayHelper
{
    private readonly Form1 _form;

    // 状态
    public bool IsVisible { get; set; } = false;
    public int HoverCell { get; set; } = -1;
    public HashSet<int> SelectedCells { get; } = new();
    public bool IsSelecting { get; set; } = false;
    public Point SelectStart { get; set; }
    public Point SelectEnd { get; set; }
    public Rectangle LastSelectRect { get; set; } = Rectangle.Empty;
    public Rectangle BatchResetButton { get; set; } = Rectangle.Empty;

    // 布局参数
    private const int ThumbSize = 56;
    private const int CellGap = 6;
    private const int CellPad = 4;
    private const int GroupPadding = 10;
    private const int BatchBtnW = 100;
    private const int BatchBtnH = 28;
    private const int HintH = 18;

    public ResetOverlayHelper(Form1 form)
    {
        _form = form;
    }

    /// <summary>
    /// 获取当前布局参数（供外部使用）
    /// </summary>
    public ResetOverlayLayout GetLayout(int imageCount, int availW, int topOffset)
    {
        int cellW = ThumbSize + CellPad * 2;
        int cellH = ThumbSize + CellPad * 2;

        int maxCols = Math.Max(1, (availW - GroupPadding * 2) / (cellW + CellGap));
        int cols = Math.Min(maxCols, imageCount);
        int rows = (imageCount + cols - 1) / cols;

        int gridW = cols * cellW + (cols - 1) * CellGap;
        int gridH = rows * cellH + (rows - 1) * CellGap;

        int gridStartX = (availW - gridW) / 2;
        int gridStartY = topOffset + GroupPadding;

        int totalHeight = GroupPadding + gridH + CellGap + BatchBtnH + CellGap + HintH + GroupPadding;

        return new ResetOverlayLayout
        {
            Cols = cols,
            Rows = rows,
            CellW = cellW,
            CellH = cellH,
            GridW = gridW,
            GridH = gridH,
            GridStartX = gridStartX,
            GridStartY = gridStartY,
            TotalHeight = totalHeight,
            TopOffset = topOffset,
            AvailW = availW
        };
    }

    /// <summary>
    /// 绘制重置偏移覆盖层
    /// </summary>
    public void Draw(Graphics g, int imageCount, Image?[] images, PointF[] manualOffsets, string[] imagePaths, ThemeColorSet colors)
    {
        if (imageCount == 0) return;

        int topOffset = Form1.TitleBarHeight;
        int availW = _form.ClientSize.Width;

        var layout = GetLayout(imageCount, availW, topOffset);
        var imagesRef = images;
        var offsetsRef = manualOffsets;

        // 半透明背景
        using var bgBrush = new SolidBrush(colors.HistoryOverlayBg);
        g.FillRectangle(bgBrush, 0, topOffset, availW, layout.TotalHeight);

        // 底部分割线
        using var bottomPen = new Pen(colors.HistoryBorder, 1);
        g.DrawLine(bottomPen, 0, topOffset + layout.TotalHeight - 1, availW, topOffset + layout.TotalHeight - 1);

        // 绘制每张缩略图
        for (int i = 0; i < imageCount; i++)
        {
            int col = i % layout.Cols;
            int row = i / layout.Cols;
            int cx = layout.GridStartX + col * (layout.CellW + CellGap);
            int cy = layout.GridStartY + row * (layout.CellH + CellGap);
            var cellRect = new Rectangle(cx, cy, layout.CellW, layout.CellH);

            bool hasOffset = offsetsRef[i].X != 0 || offsetsRef[i].Y != 0;
            bool isSelected = SelectedCells.Contains(i);
            bool isHover = i == HoverCell;

            // 格子背景和边框
            if (hasOffset)
            {
                if (isSelected)
                {
                    using var selBrush = new SolidBrush(colors.ResetCellSelectedBg);
                    g.FillRectangle(selBrush, cellRect);
                    using var selPen = new Pen(colors.ResetCellSelectedBorder, 2);
                    g.DrawRectangle(selPen, cellRect);
                }
                else if (isHover)
                {
                    using var hlBrush = new SolidBrush(colors.HistoryHoverBg);
                    g.FillRectangle(hlBrush, cellRect);
                    using var hlPen = new Pen(colors.HistoryHoverBorder, 1);
                    g.DrawRectangle(hlPen, cellRect);
                }
                else
                {
                    using var borderPen = new Pen(colors.HistoryBorder, 1);
                    g.DrawRectangle(borderPen, cellRect);
                }
            }
            else
            {
                // 无偏移的图：淡化显示，不可交互
                using var borderPen = new Pen(Color.FromArgb(60, colors.HistoryBorder), 1);
                g.DrawRectangle(borderPen, cellRect);
            }

            // 绘制缩略图
            int tx = cx + CellPad;
            int ty = cy + CellPad;
            if (imagesRef[i] != null)
            {
                g.DrawImage(imagesRef[i]!, tx, ty, ThumbSize, ThumbSize);
                if (!hasOffset)
                {
                    // 无偏移图淡化 — 用半透明覆盖
                    using var fadeBrush = new SolidBrush(Color.FromArgb(180, colors.HistoryOverlayBg));
                    g.FillRectangle(fadeBrush, tx, ty, ThumbSize, ThumbSize);
                }
            }

            // 有偏移标记（橙色小圆点）
            if (hasOffset)
            {
                using var markBrush = new SolidBrush(colors.ResetCellHasOffsetMark);
                g.FillEllipse(markBrush, cx + layout.CellW - 12, cy + 2, 9, 9);
            }
        }

        // 全部重置按钮 — 居中在网格下方
        int btnY = layout.GridStartY + layout.GridH + CellGap;
        var batchBtnRect = new Rectangle((availW - BatchBtnW) / 2, btnY, BatchBtnW, BatchBtnH);

        bool hasAnyOffset = offsetsRef.Any(o => o.X != 0 || o.Y != 0);
        Color batchBg = hasAnyOffset ? colors.DialogBtnActiveBg : colors.DialogBtnBg;
        Color batchFg = hasAnyOffset ? Color.White : colors.DialogBtnFg;
        using (var batchBgBrush = new SolidBrush(batchBg))
            g.FillRectangle(batchBgBrush, batchBtnRect);
        using var batchBorderPen = new Pen(colors.DialogBorder, 1);
        g.DrawRectangle(batchBorderPen, batchBtnRect);
        using var batchFont = new Font("Microsoft YaHei UI", 9F);
        using var batchFgBrush = new SolidBrush(batchFg);
        var batchLabel = "全部重置";
        var batchLabelSize = g.MeasureString(batchLabel, batchFont);
        g.DrawString(batchLabel, batchFont, batchFgBrush,
            batchBtnRect.Left + (batchBtnRect.Width - batchLabelSize.Width) / 2,
            batchBtnRect.Top + (batchBtnRect.Height - batchLabelSize.Height) / 2);
        BatchResetButton = batchBtnRect;

        // 操作说明
        using var hintFont = new Font("Microsoft YaHei UI", 8F);
        using var hintFg = new SolidBrush(colors.ResetPanelSubFg);
        string hintText = "左键选中/取消 | 右键重置选中 | 框选多选 | Esc关闭";
        var hintTextSize = g.MeasureString(hintText, hintFont);
        g.DrawString(hintText, hintFont, hintFg,
            (availW - hintTextSize.Width) / 2,
            btnY + BatchBtnH + CellGap);
    }

    /// <summary>
    /// 检测鼠标是否在重置偏移面板区域内
    /// </summary>
    public bool IsInOverlayArea(Point mousePos, int imageCount)
    {
        if (!IsVisible || imageCount == 0) return false;

        int topOffset = Form1.TitleBarHeight;
        int availW = _form.ClientSize.Width;
        var layout = GetLayout(imageCount, availW, topOffset);

        return mousePos.Y >= topOffset && mousePos.Y < topOffset + layout.TotalHeight;
    }

    /// <summary>
    /// 检测鼠标是否在重置面板的某个缩略图上（仅有偏移的图片可选中）
    /// </summary>
    public int HitTestCell(Point mousePos, int imageCount, PointF[] manualOffsets)
    {
        int topOffset = Form1.TitleBarHeight;
        int availW = _form.ClientSize.Width;
        var layout = GetLayout(imageCount, availW, topOffset);

        for (int i = 0; i < imageCount; i++)
        {
            int col = i % layout.Cols;
            int row = i / layout.Cols;
            int cx = layout.GridStartX + col * (layout.CellW + CellGap);
            int cy = layout.GridStartY + row * (layout.CellH + CellGap);
            var cellRect = new Rectangle(cx, cy, layout.CellW, layout.CellH);
            if (cellRect.Contains(mousePos))
            {
                // 仅有偏移的图片可以被选中
                if (manualOffsets[i].X != 0 || manualOffsets[i].Y != 0)
                    return i;
                return -1;
            }
        }
        return -1;
    }

    /// <summary>
    /// 获取框选范围内命中的缩略图索引集合（仅有偏移的图片可选中）
    /// </summary>
    public HashSet<int> GetCellsInSelection(Point start, Point end, int imageCount, PointF[] manualOffsets)
    {
        var result = new HashSet<int>();
        int selX = Math.Min(start.X, end.X);
        int selY = Math.Min(start.Y, end.Y);
        int selW = Math.Abs(end.X - start.X);
        int selH = Math.Abs(end.Y - start.Y);
        var selRect = new Rectangle(selX, selY, selW, selH);

        int topOffset = Form1.TitleBarHeight;
        int availW = _form.ClientSize.Width;
        var layout = GetLayout(imageCount, availW, topOffset);

        for (int i = 0; i < imageCount; i++)
        {
            int col = i % layout.Cols;
            int row = i / layout.Cols;
            int cx = layout.GridStartX + col * (layout.CellW + CellGap);
            int cy = layout.GridStartY + row * (layout.CellH + CellGap);
            var cellRect = new Rectangle(cx, cy, layout.CellW, layout.CellH);
            if (selRect.IntersectsWith(cellRect))
            {
                if (manualOffsets[i].X != 0 || manualOffsets[i].Y != 0)
                    result.Add(i);
            }
        }
        return result;
    }

    /// <summary>
    /// 隐藏重置偏移界面并清除状态
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
        SelectedCells.Clear();
        HoverCell = -1;
        IsSelecting = false;
        LastSelectRect = Rectangle.Empty;
    }
}

/// <summary>
/// 重置偏移覆盖层的布局计算结果
/// </summary>
public struct ResetOverlayLayout
{
    public int Cols;
    public int Rows;
    public int CellW;
    public int CellH;
    public int GridW;
    public int GridH;
    public int GridStartX;
    public int GridStartY;
    public int TotalHeight;
    public int TopOffset;
    public int AvailW;
}
