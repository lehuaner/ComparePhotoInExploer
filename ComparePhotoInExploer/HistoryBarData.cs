namespace ComparePhotoInExploer;

/// <summary>
/// 历史记录数据管理（非控件，纯数据+绘制辅助）
/// </summary>
public class HistoryBarData
{
    private readonly List<HistoryGroup> _groups = new();
    private readonly Dictionary<string, List<Image>> _thumbnails = new(); // key = groupId
    private bool _collapsed = true;

    private const int ThumbnailSize = 32;
    private const int PaddingSize = 3;
    private const int SeparatorWidth = 12;
    private const int GroupPadding = 6;
    private const int RowHeight = ThumbnailSize + PaddingSize * 2;

    public bool IsCollapsed => _collapsed;
    public int GroupCount => _groups.Count;

    public void ToggleCollapse()
    {
        _collapsed = !_collapsed;
    }

    public void Collapse()
    {
        _collapsed = true;
    }

    public void Expand()
    {
        _collapsed = false;
    }

    /// <summary>
    /// 加载历史组数据及缩略图
    /// </summary>
    public void LoadGroups(List<HistoryGroup> groups, ThemeColorSet? colors = null)
    {
        if (groups == null) groups = new List<HistoryGroup>();

        foreach (var kv in _thumbnails)
        {
            foreach (var img in kv.Value)
                img?.Dispose();
        }
        _thumbnails.Clear();
        _groups.Clear();
        _groups.AddRange(groups);

        for (int gi = 0; gi < _groups.Count; gi++)
        {
            var list = new List<Image>();
            for (int ii = 0; ii < _groups[gi].ImagePaths.Count; ii++)
            {
                var pathHash = HistoryData.GetPathHash(_groups[gi].ImagePaths[ii]);
                var thumb = HistoryData.LoadThumbnail(pathHash);
                list.Add(thumb ?? CreatePlaceholder(colors));
            }
            _thumbnails[_groups[gi].Id] = list;
        }
    }

    private Image CreatePlaceholder(ThemeColorSet? colors = null)
    {
        var bmp = new Bitmap(ThumbnailSize, ThumbnailSize);
        using var g = Graphics.FromImage(bmp);
        g.Clear(colors?.HistoryPlaceholderBg ?? Color.FromArgb(230, 230, 230));
        return bmp;
    }

    /// <summary>
    /// 计算每个组的点击区域（相对于起始绘制位置）
    /// </summary>
    public List<(Rectangle bounds, int groupIndex)> CalcHitAreas(int availWidth)
    {
        var areas = new List<(Rectangle bounds, int groupIndex)>();
        if (_collapsed || _groups.Count == 0) return areas;

        int startX = GroupPadding;
        int x = startX;
        int y = GroupPadding;

        for (int gi = 0; gi < _groups.Count; gi++)
        {
            var group = _groups[gi];
            int imgCount = group.ImagePaths.Count;
            int groupWidth = imgCount * (ThumbnailSize + PaddingSize) + GroupPadding;

            if (x + groupWidth > availWidth - GroupPadding && x > startX)
            {
                x = startX;
                y += RowHeight + PaddingSize;
            }

            int groupStartX = x;
            x += imgCount * (ThumbnailSize + PaddingSize) + GroupPadding;

            var hitRect = new Rectangle(groupStartX, y, x - groupStartX, RowHeight);
            areas.Add((hitRect, gi));

            // 分隔符空间
            x += SeparatorWidth;
        }

        return areas;
    }

    /// <summary>
    /// 计算展开时所需的高度
    /// </summary>
    public int CalcExpandedHeight(int availWidth)
    {
        if (_collapsed || _groups.Count == 0) return 0;
        var areas = CalcHitAreas(availWidth);
        if (areas.Count == 0) return 0;
        int maxBottom = 0;
        foreach (var (bounds, _) in areas)
        {
            int bottom = bounds.Bottom + PaddingSize;
            if (bottom > maxBottom) maxBottom = bottom;
        }
        return maxBottom + GroupPadding;
    }

    /// <summary>
    /// 绘制历史记录覆盖层
    /// </summary>
    public void Draw(Graphics g, int drawX, int drawY, int drawWidth, int hoverGroupIndex, ThemeColorSet colors)
    {
        if (_collapsed || _groups.Count == 0) return;

        var hitAreas = CalcHitAreas(drawWidth);

        // 计算覆盖层总高度
        int totalHeight = 0;
        foreach (var (bounds, _) in hitAreas)
        {
            int bottom = bounds.Bottom + PaddingSize;
            if (bottom > totalHeight) totalHeight = bottom;
        }
        totalHeight += GroupPadding;

        // 半透明背景
        using var bgBrush = new SolidBrush(colors.HistoryOverlayBg);
        g.FillRectangle(bgBrush, drawX, drawY, drawWidth, totalHeight);

        // 底部分割线
        using var bottomPen = new Pen(colors.HistoryBorder, 1);
        g.DrawLine(bottomPen, drawX, drawY + totalHeight - 1, drawX + drawWidth, drawY + totalHeight - 1);

        for (int gi = 0; gi < _groups.Count; gi++)
        {
            var group = _groups[gi];
            if (gi >= hitAreas.Count) break;
            var (groupBounds, _) = hitAreas[gi];

            // 组区域偏移
            int gx = drawX + groupBounds.X;
            int gy = drawY + groupBounds.Y;

            // 悬停高亮 + 边框
            if (gi == hoverGroupIndex)
            {
                using var hlBrush = new SolidBrush(colors.HistoryHoverBg);
                g.FillRectangle(hlBrush, gx, gy, groupBounds.Width, groupBounds.Height);
                using var hlPen = new Pen(colors.HistoryHoverBorder, 1);
                g.DrawRectangle(hlPen, gx, gy, groupBounds.Width, groupBounds.Height);
            }
            else
            {
                // 每个组画边框
                using var borderPen = new Pen(colors.HistoryBorder, 1);
                g.DrawRectangle(borderPen, gx, gy, groupBounds.Width, groupBounds.Height);
            }

            // 绘制组内缩略图
            int tx = gx + GroupPadding;
            int ty = gy + PaddingSize;
            if (_thumbnails.TryGetValue(group.Id, out var thumbs))
            {
                for (int ii = 0; ii < thumbs.Count && ii < group.ImagePaths.Count; ii++)
                {
                    var rect = new Rectangle(tx, ty, ThumbnailSize, ThumbnailSize);
                    if (thumbs[ii] != null)
                    {
                        g.DrawImage(thumbs[ii], rect);
                    }
                    tx += ThumbnailSize + PaddingSize;
                }
            }
        }
    }

    /// <summary>
    /// 获取指定位置的组索引（-1 表示未命中）
    /// </summary>
    public int HitTest(int drawX, int drawY, int drawWidth, Point mousePos, int offsetY)
    {
        if (_collapsed || _groups.Count == 0) return -1;

        var hitAreas = CalcHitAreas(drawWidth);
        foreach (var (bounds, groupIndex) in hitAreas)
        {
            var absoluteBounds = new Rectangle(
                drawX + bounds.X,
                drawY + bounds.Y + offsetY,
                bounds.Width,
                bounds.Height);
            if (absoluteBounds.Contains(mousePos))
                return groupIndex;
        }
        return -1;
    }

    /// <summary>
    /// 获取指定索引的组路径
    /// </summary>
    public string[]? GetGroupPaths(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _groups.Count) return null;
        return _groups[groupIndex].ImagePaths.ToArray();
    }

    /// <summary>
    /// 获取指定索引的组
    /// </summary>
    public HistoryGroup? GetGroup(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _groups.Count) return null;
        return _groups[groupIndex];
    }

    /// <summary>
    /// 检查是否在覆盖层区域内
    /// </summary>
    public bool IsInOverlayArea(int drawY, int drawWidth, Point mousePos, int offsetY)
    {
        if (_collapsed || _groups.Count == 0) return false;
        int height = CalcExpandedHeight(drawWidth);
        return mousePos.Y >= drawY + offsetY && mousePos.Y < drawY + offsetY + height;
    }
}
