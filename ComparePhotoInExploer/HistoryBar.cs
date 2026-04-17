namespace ComparePhotoInExploer;

/// <summary>
/// 历史记录长条控件 - 置于窗口顶部，标题栏行含折叠按钮，
/// 展开后显示缩略图组
/// </summary>
public class HistoryBar : Panel
{
    private readonly List<HistoryGroup> _groups = new();
    private readonly Dictionary<string, List<Image>> _thumbnails = new(); // key = groupId
    private bool _collapsed = true;

    private const int ThumbnailSize = 24;
    private const int PaddingSize = 2;
    private const int SeparatorWidth = 8;
    private const int RowHeight = ThumbnailSize + PaddingSize * 2;

    /// <summary>
    /// 当用户点击某组历史记录时触发
    /// </summary>
    public event Action<string[]>? HistoryGroupClicked;

    /// <summary>
    /// 当用户请求删除某组历史记录时触发
    /// </summary>
    public event Action<int>? HistoryGroupDeleteRequested;

    private readonly List<(Rectangle bounds, int groupIndex)> _hitAreas = new();
    private int _hoverGroupIndex = -1;

    public HistoryBar()
    {
        this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        this.BackColor = Color.Transparent;
        this.DoubleBuffered = true;
        this.Padding = new Padding(0);
        this.Margin = new Padding(0);
        UpdateHeight();
    }

    public bool IsCollapsed => _collapsed;

    /// <summary>
    /// 加载历史组数据及缩略图
    /// </summary>
    public void LoadGroups(List<HistoryGroup> groups)
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
                list.Add(thumb ?? CreatePlaceholder());
            }
            _thumbnails[_groups[gi].Id] = list;
        }

        UpdateHeight();
        this.Invalidate();
    }

    private Image CreatePlaceholder()
    {
        var bmp = new Bitmap(ThumbnailSize, ThumbnailSize);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(230, 230, 230));
        return bmp;
    }

    public void ToggleCollapse()
    {
        _collapsed = !_collapsed;
        UpdateHeight();
        this.Invalidate();
    }

    private bool _updatingHeight = false;

    private void UpdateHeight()
    {
        if (_updatingHeight) return;
        _updatingHeight = true;
        try
        {
            int newH;
            if (_collapsed || _groups.Count == 0)
            {
                newH = 0; // 折叠时不占空间
            }
            else
            {
                int rows = CalcRowCount();
                newH = rows * RowHeight + PaddingSize * 2;
            }
            if (this.Height != newH)
                this.Height = newH;
        }
        finally
        {
            _updatingHeight = false;
        }
    }

    private int CalcRowCount()
    {
        if (_groups.Count == 0) return 0;

        int availWidth = this.ClientSize.Width - PaddingSize * 2;
        if (availWidth <= 0) availWidth = 800;

        int x = 0;
        int rows = 1;

        for (int gi = 0; gi < _groups.Count; gi++)
        {
            int groupWidth = _groups[gi].ImagePaths.Count * (ThumbnailSize + PaddingSize) + SeparatorWidth;
            if (x + groupWidth > availWidth && x > 0)
            {
                rows++;
                x = 0;
            }
            x += groupWidth;
        }

        return rows;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        UpdateHeight();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // 不绘制任何背景，保持透明

        // 折叠状态或无历史记录时直接返回
        if (_collapsed || _groups.Count == 0)
            return;

        _hitAreas.Clear();

        int availWidth = this.ClientSize.Width - PaddingSize * 2;
        int startX = PaddingSize;
        int startY = PaddingSize; // 不再有内部标题行，从顶部开始
        int x = startX;
        int y = startY;

        for (int gi = 0; gi < _groups.Count; gi++)
        {
            var group = _groups[gi];
            int imgCount = group.ImagePaths.Count;
            int groupWidth = imgCount * (ThumbnailSize + PaddingSize) + SeparatorWidth;

            if (x + groupWidth > availWidth + PaddingSize && x > startX)
            {
                x = startX;
                y += RowHeight;
            }

            int groupStartX = x;

            // 悬停高亮背景
            if (gi == _hoverGroupIndex)
            {
                var highlightRect = new Rectangle(groupStartX - 1, y + 1, imgCount * (ThumbnailSize + PaddingSize) + 2, RowHeight - 2);
                using var hlBrush = new SolidBrush(Color.FromArgb(60, 100, 149, 237));
                e.Graphics.FillRectangle(hlBrush, highlightRect);
            }

            // 绘制组内缩略图
            if (_thumbnails.TryGetValue(group.Id, out var thumbs))
            {
                for (int ii = 0; ii < thumbs.Count && ii < imgCount; ii++)
                {
                    var rect = new Rectangle(x, y + PaddingSize, ThumbnailSize, ThumbnailSize);

                    using var borderPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1);
                    e.Graphics.DrawRectangle(borderPen, rect);

                    if (thumbs[ii] != null)
                    {
                        e.Graphics.DrawImage(thumbs[ii], rect);
                    }

                    x += ThumbnailSize + PaddingSize;
                }
            }

            // 组分隔符
            using var sepPen = new Pen(Color.FromArgb(120, 255, 255, 255), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            e.Graphics.DrawLine(sepPen, x + SeparatorWidth / 2, y + 4, x + SeparatorWidth / 2, y + RowHeight - 4);

            var hitRect = new Rectangle(groupStartX, y, x - groupStartX + SeparatorWidth, RowHeight);
            _hitAreas.Add((hitRect, gi));

            x += SeparatorWidth;
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (_collapsed) return;

        if (e.Button == MouseButtons.Left)
        {
            foreach (var (bounds, groupIndex) in _hitAreas)
            {
                if (bounds.Contains(e.Location))
                {
                    if (groupIndex >= 0 && groupIndex < _groups.Count)
                    {
                        HistoryGroupClicked?.Invoke(_groups[groupIndex].ImagePaths.ToArray());
                    }
                    return;
                }
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            foreach (var (bounds, groupIndex) in _hitAreas)
            {
                if (bounds.Contains(e.Location))
                {
                    if (groupIndex >= 0 && groupIndex < _groups.Count)
                    {
                        var group = _groups[groupIndex];
                        string imgNames = string.Join("\n", group.ImagePaths.Take(3).Select(p => Path.GetFileName(p)));
                        if (group.ImagePaths.Count > 3)
                            imgNames += $"\n...等{group.ImagePaths.Count}张";

                        var result = MessageBox.Show(
                            $"确定删除此历史记录组？\n\n{imgNames}",
                            "删除历史记录",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2);

                        if (result == DialogResult.Yes)
                        {
                            HistoryGroupDeleteRequested?.Invoke(groupIndex);
                        }
                    }
                    return;
                }
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_collapsed) return;

        int newHover = -1;
        foreach (var (bounds, groupIndex) in _hitAreas)
        {
            if (bounds.Contains(e.Location))
            {
                newHover = groupIndex;
                break;
            }
        }

        if (newHover != _hoverGroupIndex)
        {
            _hoverGroupIndex = newHover;
            this.Cursor = newHover >= 0 ? Cursors.Hand : Cursors.Default;
            this.Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverGroupIndex >= 0)
        {
            _hoverGroupIndex = -1;
            this.Cursor = Cursors.Default;
            this.Invalidate();
        }
    }

    public void Expand()
    {
        if (_collapsed)
        {
            _collapsed = false;
            UpdateHeight();
            this.Invalidate();
        }
    }

    /// <summary>
    /// 折叠历史记录（供外部调用）
    /// </summary>
    public void Collapse()
    {
        if (!_collapsed)
        {
            _collapsed = true;
            UpdateHeight();
            this.Invalidate();
        }
    }
}
