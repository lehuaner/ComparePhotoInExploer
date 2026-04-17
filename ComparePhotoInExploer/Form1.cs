using System.Runtime.InteropServices;

namespace ComparePhotoInExploer;

public partial class Form1 : Form
{
    private string[] _imagePaths;
    private int _imageCount;
    private int _cols;
    private int _rows;

    private bool _isDragging = false;
    private Point _lastMousePos;
    private float _zoomLevel = 1.0f;

    // 每张图独立的数据
    private Image?[] _images;
    private float[] _baseZooms;
    private PointF[] _offsets;

    // 历史记录
    private HistoryBarData _historyBarData = new();
    private List<HistoryGroup> _historyGroups = new();
    private int _hoverHistoryGroup = -1; // 悬停的历史组索引

    // 自绘标题栏
    private const int TitleBarH = 32;
    private Rectangle _btnMin, _btnMax, _btnClose, _btnHelp, _btnHistory;
    private bool _hoverMin, _hoverMax, _hoverClose, _hoverHelp, _hoverHistory;
    private bool _isWindowMaximized = false;
    private bool _showHelp = false; // 是否显示按键说明（与历史记录互斥）

    // Win32 拖拽移动
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    public Form1(string[] imagePaths)
    {
        InitializeComponent();

        _imagePaths = imagePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        _imageCount = Math.Clamp(_imagePaths.Length, 1, 9);
        this.Text = $"图片对比 ({_imageCount}张)";

        // 计算网格尺寸
        (_cols, _rows) = GetGridLayout(_imageCount);

        // 初始化每张图的数据
        _images = new Image?[_imageCount];
        _baseZooms = new float[_imageCount];
        _offsets = new PointF[_imageCount];
        for (int i = 0; i < _imageCount; i++)
            _offsets[i] = new PointF(0, 0);

        // 无边框 + 双缓冲
        this.FormBorderStyle = FormBorderStyle.None;
        this.DoubleBuffered = true;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = _imageCount <= 2 ? new Size(1000, 600) : new Size(1200, 800);

        // 创建历史记录数据
        _historyBarData = new HistoryBarData();

        // 注册事件
        this.MouseDown += Form1_MouseDown;
        this.MouseMove += Form1_MouseMove;
        this.MouseUp += Form1_MouseUp;
        this.MouseWheel += Form1_MouseWheel;
        this.Paint += Form1_Paint;
        this.KeyDown += Form1_KeyDown;

        // 加载图片到内存缓存
        LoadImages();

        // 加载历史记录
        _historyGroups = HistoryData.Load();
        _historyBarData.LoadGroups(_historyGroups);

        // 保存当前组到历史记录（异步处理缩略图生成，避免阻塞UI）
        SaveCurrentToHistory();
    }

    /// <summary>
    /// 将当前打开的图片组保存到历史记录
    /// </summary>
    private void SaveCurrentToHistory()
    {
        if (_imagePaths == null || _imagePaths.Length == 0)
            return;

        // 更新历史数据（处理去重、排序、上限）
        _historyGroups = HistoryData.AddOrUpdateGroup(_historyGroups, _imagePaths);
        HistoryData.Save(_historyGroups);

        // 先用占位图加载 UI
        _historyBarData.LoadGroups(_historyGroups);

        // 异步生成缩略图，不阻塞UI — 只生成尚不存在的缩略图
        var pathsToGenerate = new List<(string path, string hash)>();
        foreach (var p in _imagePaths)
        {
            var hash = HistoryData.GetPathHash(p);
            if (!HistoryData.ThumbnailExists(hash))
                pathsToGenerate.Add((p, hash));
        }

        if (pathsToGenerate.Count == 0)
            return; // 所有缩略图已存在，无需生成

        var capturePaths = pathsToGenerate.ToArray();

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                foreach (var (path, hash) in capturePaths)
                {
                    using var thumb = ImageProcessor.CreateThumbnail(path);
                    HistoryData.SaveThumbnail(hash, thumb);
                }

                // 在UI线程刷新缩略图
                this.BeginInvoke(new Action(() =>
                {
                    _historyBarData.LoadGroups(_historyGroups);
                }));
            }
            catch
            {
                // 忽略生成失败
            }
        });
    }

    /// <summary>
    /// 历史记录组被点击 - 加载该组图片
    /// </summary>
    private void OnHistoryGroupClicked(string[] paths)
    {
        if (paths == null || paths.Length == 0) return;

        // 检查是否与当前图片相同
        if (_imagePaths.SequenceEqual(paths))
            return;

        // 切换到该组图片
        LoadNewGroup(paths);

        // 将该组提到最前
        int idx = -1;
        for (int i = 0; i < _historyGroups.Count; i++)
        {
            if (_historyGroups[i].ImagePaths.SequenceEqual(paths))
            {
                idx = i;
                break;
            }
        }

        if (idx > 0)
        {
            var group = _historyGroups[idx];
            _historyGroups.RemoveAt(idx);
            _historyGroups.Insert(0, group);
            HistoryData.Save(_historyGroups);
            _historyBarData.LoadGroups(_historyGroups);
        }
    }

    /// <summary>
    /// 历史记录组被请求删除
    /// </summary>
    private void OnHistoryGroupDeleteRequested(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _historyGroups.Count) return;

        var removed = _historyGroups[groupIndex];
        
        // 检查是否正在显示该组
        bool isCurrentGroup = _imagePaths != null && 
            _imagePaths.Length == removed.ImagePaths.Count &&
            _imagePaths.SequenceEqual(removed.ImagePaths);
        
        HistoryData.DeleteGroupThumbnails(_historyGroups, removed.Id);
        _historyGroups.RemoveAt(groupIndex);
        HistoryData.Save(_historyGroups);
        _historyBarData.LoadGroups(_historyGroups);
        
        // 如果删除的是当前对比组，清空显示
        if (isCurrentGroup)
        {
            ClearCurrentImages();
        }
    }

    /// <summary>
    /// 清空当前显示的图片
    /// </summary>
    private void ClearCurrentImages()
    {
        for (int i = 0; i < _images.Length; i++)
        {
            _images[i]?.Dispose();
            _images[i] = null;
        }
        _imagePaths = Array.Empty<string>();
        _imageCount = 0;
        _zoomLevel = 1.0f;
        for (int i = 0; i < _offsets.Length; i++)
            _offsets[i] = new PointF(0, 0);
        
        this.Text = "图片对比 (无图片)";
        this.Invalidate();
    }

    /// <summary>
    /// 加载新的图片组
    /// </summary>
    private void LoadNewGroup(string[] paths)
    {
        for (int i = 0; i < _images.Length; i++)
            _images[i]?.Dispose();

        _imagePaths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        _imageCount = Math.Clamp(_imagePaths.Length, 1, 9);
        (_cols, _rows) = GetGridLayout(_imageCount);

        _images = new Image?[_imageCount];
        _baseZooms = new float[_imageCount];
        _offsets = new PointF[_imageCount];
        for (int i = 0; i < _imageCount; i++)
            _offsets[i] = new PointF(0, 0);

        _zoomLevel = 1.0f;
        _showHelp = false;

        this.Text = $"图片对比 ({_imageCount}张)";

        LoadImages();
        UpdateBaseZoom();
        this.Invalidate();
    }

    /// <summary>
    /// 根据图片数量计算网格布局 (cols, rows)
    /// </summary>
    private static (int cols, int rows) GetGridLayout(int count)
    {
        return count switch
        {
            1 => (1, 1),
            2 => (2, 1),
            3 => (2, 2),
            4 => (2, 2),
            5 => (2, 3),
            6 => (2, 3),
            _ => (3, 3) // 7, 8, 9
        };
    }

    private void LoadImages()
    {
        if (_imagePaths == null || _imagePaths.Length == 0)
            return;

        try
        {
            for (int i = 0; i < _imageCount; i++)
            {
                _images[i]?.Dispose();
                _images[i] = null;

                if (i < _imagePaths.Length)
                {
                    using var fs = new FileStream(_imagePaths[i], FileMode.Open, FileAccess.Read);
                    _images[i] = Image.FromStream(fs);
                }
            }
        }
        catch
        {
            // 加载失败时保持为 null
        }
    }

    private static float CalculateFitZoom(Image image, Rectangle drawArea)
    {
        if (image == null) return 1.0f;
        float scaleX = (float)drawArea.Width / image.Width;
        float scaleY = (float)drawArea.Height / image.Height;
        return Math.Min(scaleX, scaleY);
    }

    /// <summary>
    /// 获取第 i 张图的绘制区域（仅扣除标题栏）
    /// </summary>
    private Rectangle GetCellRect(int index)
    {
        int topOffset = TitleBarH;
        int availW = Math.Max(1, this.ClientSize.Width);
        int availH = Math.Max(1, this.ClientSize.Height - topOffset);
        int cellW = Math.Max(1, availW / _cols);
        int cellH = Math.Max(1, availH / _rows);
        int col = index % _cols;
        int row = index / _cols;
        return new Rectangle(col * cellW, topOffset + row * cellH, cellW, cellH);
    }

    private int HitTest(PointF screenPos)
    {
        for (int i = 0; i < _imageCount; i++)
        {
            var rect = GetCellRect(i);
            if (rect.Contains((int)screenPos.X, (int)screenPos.Y))
                return i;
        }
        return -1;
    }

    private void UpdateBaseZoom()
    {
        for (int i = 0; i < _imageCount; i++)
        {
            if (_images[i] == null) continue;
            var rect = GetCellRect(i);
            _baseZooms[i] = CalculateFitZoom(_images[i]!, rect);
        }
    }

    private float GetEffectiveZoom(int index) => _baseZooms[index] * _zoomLevel;

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.H)
        {
            _showHelp = !_showHelp;
            if (_showHelp)
                _historyBarData.Collapse();
            this.Invalidate();
        }
    }

    private void Form1_Paint(object? sender, PaintEventArgs e)
    {
        if (_imagePaths == null || _imagePaths.Length == 0)
            return;

        try
        {
            // 历史记录和按键说明互斥
            if (!_historyBarData.IsCollapsed)
                _showHelp = false;

            EnsureCheckerBrush();

            // 绘制自绘标题栏
            DrawTitleBar(e.Graphics);

            // 图片区域（不因历史记录展开而偏移）
            for (int i = 0; i < _imageCount; i++)
            {
                var rect = GetCellRect(i);

                // 棋盘格背景
                e.Graphics.FillRectangle(_checkerBrush!, rect);

                // 绘制图片
                if (_images[i] != null)
                {
                    DrawImage(e.Graphics, _images[i]!, rect, _offsets[i], GetEffectiveZoom(i));
                }
            }

            // 绘制网格分割线
            using var pen = new Pen(Color.Black, 2);
            int topOffset = TitleBarH;
            int availH = this.ClientSize.Height - topOffset;
            int cellW = this.ClientSize.Width / _cols;
            int cellH = availH / _rows;
            for (int c = 1; c < _cols; c++)
                e.Graphics.DrawLine(pen, c * cellW, topOffset, c * cellW, this.ClientSize.Height);
            for (int r = 1; r < _rows; r++)
                e.Graphics.DrawLine(pen, 0, topOffset + r * cellH, this.ClientSize.Width, topOffset + r * cellH);

            // 历史记录覆盖层（浮在图片区域上方）
            if (!_historyBarData.IsCollapsed && _historyBarData.GroupCount > 0)
            {
                _historyBarData.Draw(e.Graphics, 0, TitleBarH, this.ClientSize.Width, _hoverHistoryGroup);
            }

            // 按键说明（由标题栏按钮触发，与历史记录互斥）
            if (_showHelp)
            {
                DrawHelpPanel(e.Graphics);
            }
        }
        catch (Exception ex)
        {
            e.Graphics.DrawString($"错误: {ex.Message}", this.Font, Brushes.Red, 10, TitleBarH + 10);
        }
    }

    /// <summary>
    /// 自绘标题栏 — 左侧"历史记录"+"操作说明"按钮，右侧最小化/最大化/关闭
    /// </summary>
    private void DrawTitleBar(Graphics g)
    {
        int w = this.ClientSize.Width;

        // 标题栏背景
        using var bgBrush = new SolidBrush(Color.FromArgb(32, 32, 32));
        g.FillRectangle(bgBrush, 0, 0, w, TitleBarH);

        // 按钮起始位置
        int btnX = 8;

        // 历史记录按钮
        _btnHistory = new Rectangle(btnX, 0, 72, TitleBarH);
        bool historyActive = !_historyBarData.IsCollapsed;
        Color historyBg = historyActive ? Color.FromArgb(62, 62, 62) :
                          _hoverHistory ? Color.FromArgb(50, 50, 50) : Color.Transparent;
        using (var historyBgBrush = new SolidBrush(historyBg))
            g.FillRectangle(historyBgBrush, _btnHistory);
        using var historyFont = new Font("Microsoft YaHei UI", 9F);
        using var historyFgBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
        var historySize = g.MeasureString("历史记录", historyFont);
        g.DrawString("历史记录", historyFont, historyFgBrush,
            _btnHistory.Left + (_btnHistory.Width - historySize.Width) / 2,
            _btnHistory.Top + (_btnHistory.Height - historySize.Height) / 2);

        // 操作说明按钮
        btnX += 72 + 2;
        _btnHelp = new Rectangle(btnX, 0, 72, TitleBarH);
        Color helpBg = _showHelp ? Color.FromArgb(62, 62, 62) :
                       _hoverHelp ? Color.FromArgb(50, 50, 50) : Color.Transparent;
        using (var helpBgBrush = new SolidBrush(helpBg))
            g.FillRectangle(helpBgBrush, _btnHelp);
        using var helpFont = new Font("Microsoft YaHei UI", 9F);
        using var helpFgBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
        var helpSize = g.MeasureString("操作说明", helpFont);
        g.DrawString("操作说明", helpFont, helpFgBrush,
            _btnHelp.Left + (_btnHelp.Width - helpSize.Width) / 2,
            _btnHelp.Top + (_btnHelp.Height - helpSize.Height) / 2);

        // 窗口控制按钮区域
        int btnW = 46;
        int btnH = TitleBarH;
        int x = w - btnW * 3;

        _btnMin = new Rectangle(x, 0, btnW, btnH);
        _btnMax = new Rectangle(x + btnW, 0, btnW, btnH);
        _btnClose = new Rectangle(x + btnW * 2, 0, btnW, btnH);

        // 最小化按钮
        DrawControlButton(g, _btnMin, "─", _hoverMin, false);
        // 最大化按钮
        DrawControlButton(g, _btnMax, _isWindowMaximized ? "❐" : "□", _hoverMax, false);
        // 关闭按钮
        DrawControlButton(g, _btnClose, "✕", _hoverClose, true);
    }

    private void DrawControlButton(Graphics g, Rectangle rect, string text, bool hover, bool isClose)
    {
        Color bg, fg;
        if (isClose && hover)
        {
            bg = Color.FromArgb(232, 17, 35);
            fg = Color.White;
        }
        else if (hover)
        {
            bg = Color.FromArgb(62, 62, 62);
            fg = Color.White;
        }
        else
        {
            bg = Color.Transparent;
            fg = Color.FromArgb(200, 200, 200);
        }

        using var bgBrush = new SolidBrush(bg);
        g.FillRectangle(bgBrush, rect);

        using var fgBrush = new SolidBrush(fg);
        // 最大化/还原图标用更大字号
        float fontSize = (text == "□" || text == "❐") ? 12F : 9F;
        using var font = new Font("Segoe UI", fontSize);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, fgBrush,
            rect.Left + (rect.Width - size.Width) / 2,
            rect.Top + (rect.Height - size.Height) / 2);
    }

    private void DrawHelpPanel(Graphics g)
    {
        string[] instructions = {
            "操作说明:",
            "- 鼠标左键拖动: 同步移动所有图片",
            "- 滚轮: 上下移动图片",
            "- Ctrl+滚轮: 左右移动图片",
            "- Alt+滚轮: 以鼠标指针为中心缩放图片",
            "- 标题栏'历史记录': 点击展开/收起历史对比记录",
            "- 标题栏'按键说明': 点击查看操作说明"
        };

        float boxWidth = 380f;
        float boxHeight = instructions.Length * 22 + 20;
        int topOffset = TitleBarH;
        using (var bgBrush = new SolidBrush(Color.FromArgb(230, 32, 32, 32)))
        {
            g.FillRectangle(bgBrush, 5, topOffset + 5, boxWidth, boxHeight);
        }

        using var borderPen = new Pen(Color.FromArgb(80, 80, 80), 1);
        g.DrawRectangle(borderPen, 5, topOffset + 5, boxWidth, boxHeight);

        for (int i = 0; i < instructions.Length; i++)
        {
            using var brush = i == 0 
                ? new SolidBrush(Color.FromArgb(255, 220, 220, 220))
                : new SolidBrush(Color.FromArgb(200, 200, 200));
            g.DrawString(instructions[i], i == 0 ? new Font(this.Font, FontStyle.Bold) : this.Font, brush, 12, topOffset + 10 + i * 22);
        }
    }

    // 棋盘格缓存
    private TextureBrush? _checkerBrush;

    private void EnsureCheckerBrush()
    {
        if (_checkerBrush != null) return;

        int size = 8;
        using var bmp = new Bitmap(size * 2, size * 2);
        using (var g = Graphics.FromImage(bmp))
        {
            g.FillRectangle(Brushes.White, 0, 0, size * 2, size * 2);
            g.FillRectangle(Brushes.LightGray, 0, 0, size, size);
            g.FillRectangle(Brushes.LightGray, size, size, size, size);
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

    private void Form1_MouseDown(object? sender, MouseEventArgs e)
    {
        // 标题栏区域处理
        if (e.Location.Y < TitleBarH)
        {
            if (_btnClose.Contains(e.Location))
            {
                this.Close();
                return;
            }
            if (_btnMax.Contains(e.Location))
            {
                ToggleMaximize();
                return;
            }
            if (_btnMin.Contains(e.Location))
            {
                this.WindowState = FormWindowState.Minimized;
                return;
            }
            // 按键说明按钮
            if (_btnHelp.Contains(e.Location))
            {
                _showHelp = !_showHelp;
                if (_showHelp)
                    _historyBarData.Collapse(); // 关闭历史记录
                this.Invalidate();
                return;
            }
            // 历史记录按钮
            if (_btnHistory.Contains(e.Location))
            {
                _showHelp = false; // 关闭按键说明
                _historyBarData.ToggleCollapse();
                this.Invalidate();
                return;
            }
            // 拖动窗口
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            return;
        }

        // 历史记录覆盖层区域 — 处理点击
        if (!_historyBarData.IsCollapsed && _historyBarData.GroupCount > 0)
        {
            int hitIdx = _historyBarData.HitTest(0, TitleBarH, this.ClientSize.Width, e.Location, 0);
            if (hitIdx >= 0)
            {
                if (e.Button == MouseButtons.Left)
                {
                    var paths = _historyBarData.GetGroupPaths(hitIdx);
                    if (paths != null)
                        OnHistoryGroupClicked(paths);
                }
                else if (e.Button == MouseButtons.Right)
                {
                    var group = _historyBarData.GetGroup(hitIdx);
                    if (group != null)
                    {
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
                            OnHistoryGroupDeleteRequested(hitIdx);
                        }
                    }
                }
                return;
            }
        }

        // 点击主操作区时，自动收起历史记录和操作说明
        if (!_historyBarData.IsCollapsed || _showHelp)
        {
            _historyBarData.Collapse();
            _showHelp = false;
            _hoverHistoryGroup = -1;
            this.Invalidate();
        }

        // 图片区域拖动
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _lastMousePos = e.Location;
        }
    }

    private void Form1_MouseMove(object? sender, MouseEventArgs e)
    {
        // 标题栏悬停效果
        if (e.Location.Y < TitleBarH)
        {
            bool newHoverMin = _btnMin.Contains(e.Location);
            bool newHoverMax = _btnMax.Contains(e.Location);
            bool newHoverClose = _btnClose.Contains(e.Location);
            bool newHoverHelp = _btnHelp.Contains(e.Location);
            bool newHoverHistory = _btnHistory.Contains(e.Location);

            if (newHoverMin != _hoverMin || newHoverMax != _hoverMax || newHoverClose != _hoverClose || newHoverHelp != _hoverHelp || newHoverHistory != _hoverHistory)
            {
                _hoverMin = newHoverMin;
                _hoverMax = newHoverMax;
                _hoverClose = newHoverClose;
                _hoverHelp = newHoverHelp;
                _hoverHistory = newHoverHistory;
                this.Invalidate(new Rectangle(0, 0, this.ClientSize.Width, TitleBarH));
            }
            return;
        }

        // 历史记录覆盖层悬停检测
        if (!_historyBarData.IsCollapsed && _historyBarData.GroupCount > 0)
        {
            int newHover = _historyBarData.HitTest(0, TitleBarH, this.ClientSize.Width, e.Location, 0);
            if (newHover != _hoverHistoryGroup)
            {
                _hoverHistoryGroup = newHover;
                this.Cursor = newHover >= 0 ? Cursors.Hand : Cursors.Default;
                this.Invalidate();
            }
            if (newHover >= 0)
                return; // 在历史记录上，不拖动图片
        }

        if (_isDragging)
        {
            int deltaX = e.X - _lastMousePos.X;
            int deltaY = e.Y - _lastMousePos.Y;

            for (int i = 0; i < _imageCount; i++)
            {
                _offsets[i] = new PointF(_offsets[i].X + deltaX, _offsets[i].Y + deltaY);
            }

            _lastMousePos = e.Location;
            this.Invalidate();
        }
    }

    private void Form1_MouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Alt || keyData == (Keys.Alt | Keys.Menu))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static PointF ScreenToNormalized(PointF screenPos, Rectangle drawArea, float zoom, PointF offset, Size imageSize)
    {
        float scaledWidth = imageSize.Width * zoom;
        float scaledHeight = imageSize.Height * zoom;
        float imgX = drawArea.Left + (drawArea.Width - scaledWidth) / 2f + offset.X;
        float imgY = drawArea.Top + (drawArea.Height - scaledHeight) / 2f + offset.Y;

        float normX = (screenPos.X - imgX) / scaledWidth;
        float normY = (screenPos.Y - imgY) / scaledHeight;
        return new PointF(normX, normY);
    }

    private static PointF ZoomAtNormalized(PointF norm, Rectangle drawArea, float oldZoom, float newZoom, PointF oldOffset, Size imageSize)
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

    private static PointF ZoomAndMoveToTarget(PointF norm, Rectangle drawArea, float newZoom, Size imageSize, PointF targetScreenPos)
    {
        float newScaledWidth = imageSize.Width * newZoom;
        float newScaledHeight = imageSize.Height * newZoom;
        float offsetX = targetScreenPos.X - drawArea.Left - (drawArea.Width - newScaledWidth) / 2f - norm.X * newScaledWidth;
        float offsetY = targetScreenPos.Y - drawArea.Top - (drawArea.Height - newScaledHeight) / 2f - norm.Y * newScaledHeight;
        return new PointF(offsetX, offsetY);
    }

    private bool IsAltPressed()
    {
        return (ModifierKeys & Keys.Alt) == Keys.Alt;
    }

    private void ToggleMaximize()
    {
        if (_isWindowMaximized)
        {
            this.WindowState = FormWindowState.Normal;
            _isWindowMaximized = false;
        }
        else
        {
            this.WindowState = FormWindowState.Maximized;
            _isWindowMaximized = true;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _isWindowMaximized = this.WindowState == FormWindowState.Maximized;
        UpdateBaseZoom();
        this.Invalidate();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        UpdateBaseZoom();
        _zoomLevel = 1.0f;
        for (int i = 0; i < _imageCount; i++)
            _offsets[i] = new PointF(0, 0);
        this.Invalidate();
    }

    private void Form1_MouseWheel(object? sender, MouseEventArgs e)
    {
        // 滚动操作主操作区时，自动收起历史记录和操作说明
        if (!_historyBarData.IsCollapsed || _showHelp)
        {
            _historyBarData.Collapse();
            _showHelp = false;
            _hoverHistoryGroup = -1;
        }

        if (ModifierKeys == Keys.Control)
        {
            float avgZoom = _baseZooms.Where(z => z > 0).DefaultIfEmpty(1f).Average() * _zoomLevel;
            float step = this.ClientSize.Width * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? -step : step;
            for (int i = 0; i < _imageCount; i++)
                _offsets[i] = new PointF(_offsets[i].X + delta, _offsets[i].Y);
        }
        else if (IsAltPressed())
        {
            float zoomFactor = e.Delta > 0 ? 1.25f : 1f / 1.25f;
            float oldZoomLevel = _zoomLevel;
            float newZoomLevel = _zoomLevel * zoomFactor;

            if (newZoomLevel < 0.01f || newZoomLevel > 100f)
                return;

            PointF mousePos = e.Location;
            int activeIdx = HitTest(mousePos);
            if (activeIdx < 0 || activeIdx >= _images.Length || _images[activeIdx] == null)
            {
                _zoomLevel = newZoomLevel;
                this.Invalidate();
                return;
            }

            Rectangle activeRect = GetCellRect(activeIdx);
            Size activeImgSize = _images[activeIdx]!.Size;
            float oldEffActive = _baseZooms[activeIdx] * oldZoomLevel;
            float newEffActive = _baseZooms[activeIdx] * newZoomLevel;

            PointF norm = ScreenToNormalized(mousePos, activeRect, oldEffActive, _offsets[activeIdx], activeImgSize);
            _offsets[activeIdx] = ZoomAtNormalized(norm, activeRect, oldEffActive, newEffActive, _offsets[activeIdx], activeImgSize);

            float activeLocalX = mousePos.X - activeRect.Left;
            float activeLocalY = mousePos.Y - activeRect.Top;

            for (int i = 0; i < _imageCount; i++)
            {
                if (i == activeIdx || _images[i] == null) continue;

                Rectangle passiveRect = GetCellRect(i);
                Size passiveImgSize = _images[i]!.Size;
                float newEffPassive = _baseZooms[i] * newZoomLevel;

                PointF targetPos = new PointF(passiveRect.Left + activeLocalX, passiveRect.Top + activeLocalY);
                _offsets[i] = ZoomAndMoveToTarget(norm, passiveRect, newEffPassive, passiveImgSize, targetPos);
            }

            _zoomLevel = newZoomLevel;
        }
        else
        {
            float avgZoom = _baseZooms.Where(z => z > 0).DefaultIfEmpty(1f).Average() * _zoomLevel;
            float step = this.ClientSize.Height * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? -step : step;
            for (int i = 0; i < _imageCount; i++)
                _offsets[i] = new PointF(_offsets[i].X, _offsets[i].Y + delta);
        }

        this.Invalidate();
    }
}
