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
    public Rectangle BatchResetButton { get; set; } = Rectangle.Empty;

    // 布局参数
    private const int ThumbSize = 56;
    private const int CellGap = 6;
    private const int CellPad = 4;
    private const int GroupPadding = 10;
    private const int BatchBtnW = 100;
    private const int BatchBtnH = 28;
    private const int HintH = 18;

    // 框选前的选中状态（用于框选过程中的实时预览）
    private HashSet<int> _preSelectCells = new();

    public ResetOverlayHelper(Form1 form)
    {
        _form = form;
    }

    /// <summary>
    /// 获取当前布局参数（使用主操作界面的列数/行数保持一致）
    /// </summary>
    public ResetOverlayLayout GetLayout(int imageCount, int availW, int topOffset, int mainCols, int mainRows)
    {
        int cellW = ThumbSize + CellPad * 2;
        int cellH = ThumbSize + CellPad * 2;

        // 使用主界面的列数和行数
        int cols = mainCols;
        int rows = mainRows;

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
    public void Draw(Graphics g, int imageCount, Image?[] images, PointF[] manualOffsets, string[] imagePaths, ThemeColorSet colors, int mainCols, int mainRows)
    {
        if (imageCount == 0) return;

        int topOffset = Form1.TitleBarHeight;
        int availW = _form.ClientSize.Width;

        var layout = GetLayout(imageCount, availW, topOffset, mainCols, mainRows);

        // 半透明背景
        using var bgBrush = new SolidBrush(colors.HistoryOverlayBg);
        g.FillRectangle(bgBrush, 0, topOffset, availW, layout.TotalHeight);

        // 底部分割线
        using var bottomPen = new Pen(colors.HistoryBorder, 1);
        g.DrawLine(bottomPen, 0, topOffset + layout.TotalHeight - 1, availW, topOffset + layout.TotalHeight - 1);

        // 绘制每张缩略图
        for (int i = 0; i < layout.Cols * layout.Rows; i++)
        {
            int col = i % layout.Cols;
            int row = i / layout.Cols;
            int cx = layout.GridStartX + col * (layout.CellW + CellGap);
            int cy = layout.GridStartY + row * (layout.CellH + CellGap);
            var cellRect = new Rectangle(cx, cy, layout.CellW, layout.CellH);

            bool hasImage = i < imageCount;
            bool hasOffset = hasImage && (manualOffsets[i].X != 0 || manualOffsets[i].Y != 0);
            bool isSelected = hasImage && SelectedCells.Contains(i);
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
            else if (hasImage)
            {
                // 无偏移的图：淡化显示，不可交互
                using var borderPen = new Pen(Color.FromArgb(60, colors.HistoryBorder), 1);
                g.DrawRectangle(borderPen, cellRect);
            }
            else
            {
                // 空格子（图片数量不足以填满网格时）
                using var borderPen = new Pen(Color.FromArgb(40, colors.HistoryBorder), 1);
                g.DrawRectangle(borderPen, cellRect);
            }

            // 绘制缩略图
            if (hasImage && images[i] != null)
            {
                int tx = cx + CellPad;
                int ty = cy + CellPad;
                g.DrawImage(images[i]!, tx, ty, ThumbSize, ThumbSize);
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

        // 框选范围矩形
        if (IsSelecting)
        {
            int selX = Math.Min(SelectStart.X, SelectEnd.X);
            int selY = Math.Min(SelectStart.Y, SelectEnd.Y);
            int selW = Math.Abs(SelectEnd.X - SelectStart.X);
            int selH = Math.Abs(SelectEnd.Y - SelectStart.Y);
            if (selW > 2 && selH > 2)
            {
                var selRect = new Rectangle(selX, selY, selW, selH);
                using var selFillBrush = new SolidBrush(Color.FromArgb(40, 100, 149, 237));
                g.FillRectangle(selFillBrush, selRect);
                using var selBorderPen = new Pen(Color.FromArgb(100, 149, 237), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                g.DrawRectangle(selBorderPen, selRect);
            }
        }

        // 重置按钮 — 居中在网格下方（根据选中状态切换文字和功能）
        int btnY = layout.GridStartY + layout.GridH + CellGap;
        var batchBtnRect = new Rectangle((availW - BatchBtnW) / 2, btnY, BatchBtnW, BatchBtnH);

        bool hasAnyOffset = manualOffsets.Any(o => o.X != 0 || o.Y != 0);
        Color batchBg = hasAnyOffset ? colors.DialogBtnActiveBg : colors.DialogBtnBg;
        Color batchFg = hasAnyOffset ? Color.White : colors.DialogBtnFg;
        using (var batchBgBrush = new SolidBrush(batchBg))
            g.FillRectangle(batchBgBrush, batchBtnRect);
        using var batchBorderPen = new Pen(colors.DialogBorder, 1);
        g.DrawRectangle(batchBorderPen, batchBtnRect);
        using var batchFont = new Font("Microsoft YaHei UI", 9F);
        using var batchFgBrush = new SolidBrush(batchFg);
        // 有选中图片时显示"重置选中"，否则显示"全部重置"
        string batchLabel = SelectedCells.Count > 0 ? "重置选中" : "全部重置";
        var batchLabelSize = g.MeasureString(batchLabel, batchFont);
        g.DrawString(batchLabel, batchFont, batchFgBrush,
            batchBtnRect.Left + (batchBtnRect.Width - batchLabelSize.Width) / 2,
            batchBtnRect.Top + (batchBtnRect.Height - batchLabelSize.Height) / 2);
        BatchResetButton = batchBtnRect;

        // 操作说明
        using var hintFont = new Font("Microsoft YaHei UI", 8F);
        using var hintFg = new SolidBrush(colors.ResetPanelSubFg);
        string hintText = "左键选中/取消 | Ctrl+点击或框选多选 | 右键重置 | Esc关闭";
        var hintTextSize = g.MeasureString(hintText, hintFont);
        g.DrawString(hintText, hintFont, hintFg,
            (availW - hintTextSize.Width) / 2,
            btnY + BatchBtnH + CellGap);
    }

    /// <summary>
    /// 检测鼠标是否在重置偏移面板区域内
    /// </summary>
    public bool IsInOverlayArea(Point mousePos, int imageCount, int mainCols, int mainRows)
    {
        if (!IsVisible || imageCount == 0) return false;

        int topOffset = Form1.TitleBarHeight;
        int availW = _form.ClientSize.Width;
        var layout = GetLayout(imageCount, availW, topOffset, mainCols, mainRows);

        return mousePos.Y >= topOffset && mousePos.Y < topOffset + layout.TotalHeight;
    }

    /// <summary>
    /// 检测鼠标是否在重置面板的某个缩略图上（仅有偏移的图片可选中）
    /// </summary>
    public int HitTestCell(Point mousePos, int imageCount, PointF[] manualOffsets, int mainCols, int mainRows)
    {
        int topOffset = Form1.TitleBarHeight;
        int availW = _form.ClientSize.Width;
        var layout = GetLayout(imageCount, availW, topOffset, mainCols, mainRows);

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
    public HashSet<int> GetCellsInSelection(Point start, Point end, int imageCount, PointF[] manualOffsets, int mainCols, int mainRows)
    {
        var result = new HashSet<int>();
        int selX = Math.Min(start.X, end.X);
        int selY = Math.Min(start.Y, end.Y);
        int selW = Math.Abs(end.X - start.X);
        int selH = Math.Abs(end.Y - start.Y);
        var selRect = new Rectangle(selX, selY, selW, selH);

        int topOffset = Form1.TitleBarHeight;
        int availW = _form.ClientSize.Width;
        var layout = GetLayout(imageCount, availW, topOffset, mainCols, mainRows);

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
    /// 开始框选，保存当前选中状态
    /// </summary>
    public void StartSelection(Point start)
    {
        IsSelecting = true;
        SelectStart = start;
        SelectEnd = start;
        _preSelectCells = new HashSet<int>(SelectedCells);
    }

    /// <summary>
    /// 更新框选终点并实时计算选中状态
    /// </summary>
    public void UpdateSelection(Point end, int imageCount, PointF[] manualOffsets, int mainCols, int mainRows)
    {
        SelectEnd = end;

        // 实时计算框选命中项
        int selW = Math.Abs(SelectEnd.X - SelectStart.X);
        int selH = Math.Abs(SelectEnd.Y - SelectStart.Y);

        SelectedCells.Clear();
        foreach (var idx in _preSelectCells)
            SelectedCells.Add(idx);

        if (selW > 2 && selH > 2)
        {
            var hitCells = GetCellsInSelection(SelectStart, SelectEnd, imageCount, manualOffsets, mainCols, mainRows);
            foreach (var idx in hitCells)
                SelectedCells.Add(idx);
        }
    }

    /// <summary>
    /// 结束框选
    /// </summary>
    public void EndSelection()
    {
        IsSelecting = false;
        _preSelectCells.Clear();
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
        _preSelectCells.Clear();
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
