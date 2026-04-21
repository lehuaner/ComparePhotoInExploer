namespace ComparePhotoInExploer;

/// <summary>
/// 缩放同步模式
/// </summary>
public enum SyncZoomMode
{
    SyncAlign,       // 同步对齐：所有图片缩放并对齐到鼠标位置
    IndependentZoom  // 独立缩放：所有图片同步缩放，但各图以自身比例位置为缩放中心
}

/// <summary>
/// 同步移动模式
/// </summary>
public enum SyncMoveMode
{
    DisableSyncZoom, // 关闭同步缩放
    DisableSyncMove, // 关闭同步移动
    DisableAll,      // 同时关闭
    EnableAll        // 同时开启
}

public partial class Form1 : Form
{
    private string[] _imagePaths;
    private int _imageCount;
    private int _cols;
    private int _rows;

    private bool _isDragging = false;
    private Point _lastMousePos;
    private float _zoomLevel = 1.0f;
    private int _shiftDragIndex = -1; // Shift拖动时选中的图片索引，-1表示未选中

    // 每张图独立的数据
    private Image?[] _images;
    private float[] _baseZooms;
    private PointF[] _offsets;
    private PointF[] _manualOffsets; // Shift拖动产生的额外偏移量

    // 历史记录
    private HistoryBarData _historyBarData = new();
    private List<HistoryGroup> _historyGroups = new();
    private int _hoverHistoryGroup = -1; // 悬停的历史组索引

    // 主题
    private AppTheme _currentTheme = AppTheme.Dark;
    private ThemeColorSet _colors = ThemeColorSet.Dark;

    private bool _isWindowMaximized = false;
    private bool _showHelp = false; // 是否显示按键说明（与历史记录互斥）
    private bool _showZoomHelp = false; // 是否显示缩放说明
    private SyncZoomMode _syncZoomMode = SyncZoomMode.SyncAlign; // 缩放同步模式
    private SyncMoveMode _syncMoveMode = SyncMoveMode.EnableAll; // 同步移动模式
    private float[] _zoomLevels; // 关闭同步缩放时，每张图独立的缩放级别
    private int _dragTargetIndex = -1; // 关闭同步移动时，拖动/滚轮移动的目标图片索引
    private bool _rightClickMenuEnabled = true; // 是否启用右键菜单
    private readonly ResetOverlayHelper _resetOverlay; // 重置偏移覆盖层

    // 拖放
    private bool _isDragOver = false; // 是否有文件正在拖入

    // 保存最大化前的窗口位置和大小，用于恢复
    private Rectangle _restoreBounds;

    // 边框调整大小区域宽度
    private const int ResizeBorderWidth = 6;

    private void ApplyRoundCorner()
    {
        NativeMethods.ApplyRoundCorner(this.Handle, this.Width, this.Height, _isWindowMaximized);
    }

    private readonly int _wmSettingChange = NativeMethods.RegisterWindowMessage("WM_SETTINGCHANGE");

    public Form1(string[] imagePaths)
    {
        InitializeComponent();

        // 设置全局引用，供 IPC 调用
        Program.Form1 = this;
        Program.Form1ReadyEvent.Set(); // 通知 IPC 线程主窗体已就绪
        this.FormClosed += (s, e) => Program.Form1 = null;

        // 设置窗口图标（任务栏和标题栏）- 从exe自身提取嵌入图标
        try
        {
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? null!;
        }
        catch { }

        // 初始化重置偏移覆盖层
        _resetOverlay = new ResetOverlayHelper(this);

        // 加载主题设置
        _currentTheme = AppSettings.LoadThemeSetting();
        ApplyTheme(_currentTheme);
        
        // 加载右键菜单设置
        _rightClickMenuEnabled = AppSettings.LoadRightClickMenuSetting();
        // 第一次启动时默认安装右键菜单
        if (_rightClickMenuEnabled)
        {
            RightClickMenuHelper.Install();
        }

        _imagePaths = imagePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        _imageCount = Math.Clamp(_imagePaths.Length, 0, 9);
        this.Text = _imageCount > 0 ? $"图片对比 ({_imageCount}张)" : "图片对比";

        // 计算网格尺寸
        (_cols, _rows) = GetGridLayout(Math.Max(1, _imageCount));

        // 初始化每张图的数据
        _images = new Image?[Math.Max(1, _imageCount)];
        _baseZooms = new float[Math.Max(1, _imageCount)];
        _offsets = new PointF[Math.Max(1, _imageCount)];
        _manualOffsets = new PointF[Math.Max(1, _imageCount)];
        _zoomLevels = new float[Math.Max(1, _imageCount)];
        for (int i = 0; i < _offsets.Length; i++)
        {
            _offsets[i] = new PointF(0, 0);
            _manualOffsets[i] = new PointF(0, 0);
            _zoomLevels[i] = 1.0f;
        }

        // 无边框 + 双缓冲
        this.FormBorderStyle = FormBorderStyle.None;
        this.DoubleBuffered = true;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = _imageCount <= 2 ? new Size(1000, 600) : new Size(1200, 800);

        // 加载上次的窗口位置和大小（覆盖默认值）
        var savedState = AppSettings.LoadWindowState();
        if (savedState != null)
        {
            var (maximized, bounds) = savedState.Value;
            if (maximized)
            {
                this.StartPosition = FormStartPosition.Manual;
                this.Location = new Point(bounds.X, bounds.Y);
                this.Size = new Size(bounds.Width, bounds.Height);
                this.WindowState = FormWindowState.Maximized;
                _isWindowMaximized = true;
            }
            else
            {
                this.StartPosition = FormStartPosition.Manual;
                this.Location = new Point(bounds.X, bounds.Y);
                this.Size = bounds.Size;
            }
        }

        // 设置最小窗口大小
        this.MinimumSize = new Size(500, 350);

        // 保存初始窗口位置
        _restoreBounds = this.Bounds;

        // 圆角边框
        this.Load += (s, e) => ApplyRoundCorner();

        // 关闭时保存窗口状态
        this.FormClosing += (s, e) => AppSettings.SaveWindowState(_isWindowMaximized, _isWindowMaximized ? _restoreBounds : this.Bounds);

        // 允许拖放
        this.AllowDrop = true;

        // 创建历史记录数据
        _historyBarData = new HistoryBarData();

        // 注册事件
        this.MouseDown += Form1_MouseDown;
        this.MouseMove += Form1_MouseMove;
        this.MouseUp += Form1_MouseUp;
        this.MouseWheel += Form1_MouseWheel;
        this.Paint += Form1_Paint;
        this.KeyDown += Form1_KeyDown;
        this.DragEnter += Form1_DragEnter;
        this.DragOver += Form1_DragOver;
        this.DragLeave += Form1_DragLeave;
        this.DragDrop += Form1_DragDrop;

        // 加载图片到内存缓存
        if (_imageCount > 0)
            LoadImages();

        // 加载历史记录
        _historyGroups = HistoryData.Load();
        _historyBarData.LoadGroups(_historyGroups);

        // 保存当前组到历史记录（异步处理缩略图生成，避免阻塞UI）
        if (_imageCount > 0)
            SaveCurrentToHistory();
    }

    /// <summary>
    /// 重写以接收系统主题变化消息，以及处理模态对话框期间的任务栏关闭请求
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == _wmSettingChange && _currentTheme == AppTheme.System)
        {
            // 系统主题变了，重新应用
            ApplyTheme(AppTheme.System);
            this.Invalidate();
        }
        // 当窗口被模态对话框禁用时，任务栏右键"关闭"发送的WM_SYSCOMMAND SC_CLOSE会被忽略
        // 直接关闭整个程序
        if (m.Msg == NativeMethods.WM_SYSCOMMAND && (int)m.WParam == NativeMethods.SC_CLOSE)
        {
            if (this.OwnedForms.Length > 0)
            {
                this.Close();
                return;
            }
        }
        // 无边框窗口的边框调整大小支持
        if (m.Msg == NativeMethods.WM_NCHITTEST && !_isWindowMaximized)
        {
            var pos = PointToClient(new Point((int)m.LParam));
            int x = pos.X, y = pos.Y;
            int w = this.ClientSize.Width, h = this.ClientSize.Height;
            int bw = ResizeBorderWidth;

            // 检测四角
            bool onLeft = x < bw, onRight = x >= w - bw;
            bool onTop = y < bw, onBottom = y >= h - bw;

            if (onTop && onLeft) { m.Result = (IntPtr)NativeMethods.HTTOPLEFT; return; }
            if (onTop && onRight) { m.Result = (IntPtr)NativeMethods.HTTOPRIGHT; return; }
            if (onBottom && onLeft) { m.Result = (IntPtr)NativeMethods.HTBOTTOMLEFT; return; }
            if (onBottom && onRight) { m.Result = (IntPtr)NativeMethods.HTBOTTOMRIGHT; return; }
            if (onLeft) { m.Result = (IntPtr)NativeMethods.HTLEFT; return; }
            if (onRight) { m.Result = (IntPtr)NativeMethods.HTRIGHT; return; }
            if (onTop) { m.Result = (IntPtr)NativeMethods.HTTOP; return; }
            if (onBottom) { m.Result = (IntPtr)NativeMethods.HTBOTTOM; return; }
        }
        base.WndProc(ref m);
    }

    #region 主题管理

    private void ApplyTheme(AppTheme theme)
    {
        _currentTheme = theme;
        _colors = ThemeColorSet.FromTheme(theme);
        _checkerBrush = null; // 重建棋盘格
        AppSettings.SaveThemeSetting(theme);
    }

    private void CycleTheme()
    {
        var next = _currentTheme switch
        {
            AppTheme.Dark => AppTheme.Light,
            AppTheme.Light => AppTheme.System,
            _ => AppTheme.Dark
        };
        ApplyTheme(next);
        this.Invalidate();
    }

    private static string GetThemeLabel(AppTheme theme) => theme switch
    {
        AppTheme.Dark => "暗色",
        AppTheme.Light => "亮色",
        _ => "跟随系统"
    };

    #endregion

    #region 拖放支持

    private void Form1_DragEnter(object? sender, DragEventArgs e)
    {
        if (FileDropHelper.ExtractImageFiles(e.Data) != null)
        {
            e.Effect = DragDropEffects.Copy;
            _isDragOver = true;
            this.Invalidate();
            return;
        }
        e.Effect = DragDropEffects.None;
    }

    private void Form1_DragOver(object? sender, DragEventArgs e)
    {
        if (FileDropHelper.ExtractImageFiles(e.Data) != null)
        {
            e.Effect = DragDropEffects.Copy;
            return;
        }
        e.Effect = DragDropEffects.None;
    }

    private void Form1_DragLeave(object? sender, EventArgs e)
    {
        _isDragOver = false;
        this.Invalidate();
    }

    private void Form1_DragDrop(object? sender, DragEventArgs e)
    {
        _isDragOver = false;

        var imageFiles = FileDropHelper.ExtractImageFiles(e.Data);
        if (imageFiles == null) return;

        // 清空当前对比并加载新图片组
        LoadNewGroup(imageFiles);
        SaveCurrentToHistory();
    }

    #endregion

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

        // 检查是否与当前图片相同（忽略顺序）
        var pathsSet = new HashSet<string>(paths);
        if (_imagePaths != null && _imagePaths.Length == paths.Length &&
            _imagePaths.All(p => pathsSet.Contains(p)))
            return;

        // 切换到该组图片
        LoadNewGroup(paths);

        // 将该组提到最前
        int idx = -1;
        for (int i = 0; i < _historyGroups.Count; i++)
        {
            if (_historyGroups[i].ImagePaths.Count == paths.Length &&
                _historyGroups[i].ImagePaths.All(p => pathsSet.Contains(p)))
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
        
        // 检查是否正在显示该组（忽略顺序）
        var removedSet = new HashSet<string>(removed.ImagePaths);
        bool isCurrentGroup = _imagePaths != null && 
            _imagePaths.Length == removed.ImagePaths.Count &&
            _imagePaths.All(p => removedSet.Contains(p));
        
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
        _shiftDragIndex = -1;
        for (int i = 0; i < _offsets.Length; i++)
        {
            _offsets[i] = new PointF(0, 0);
            _manualOffsets[i] = new PointF(0, 0);
            _zoomLevels[i] = 1.0f;
        }
        
        this.Text = "图片对比";
        this.Invalidate();
    }

    /// <summary>
    /// 从外部（IPC / 右键菜单后续实例）添加图片
    /// 将新图片与当前图片合并后重新加载
    /// </summary>
    public void AddImagesFromExternal(string[] newPaths)
    {
        if (newPaths == null || newPaths.Length == 0) return;

        // 过滤有效路径
        var validNew = newPaths.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).ToArray();
        if (validNew.Length == 0) return;

        // 与当前图片合并（去重）
        var existing = _imagePaths ?? Array.Empty<string>();
        var combined = existing.Concat(validNew).Distinct().Take(9).ToArray();

        // 如果有新图片被添加，重新加载
        if (combined.Length > existing.Length)
        {
            LoadNewGroup(combined);
            SaveCurrentToHistory();
        }
        else
        {
            // 图片没变化，将窗口提到前台
            ActivateWindow();
        }
    }

    /// <summary>
    /// 将窗口激活到前台
    /// </summary>
    private void ActivateWindow()
    {
        if (this.WindowState == FormWindowState.Minimized)
            this.WindowState = FormWindowState.Normal;
        this.Activate();
    }

    /// <summary>
    /// 加载新的图片组
    /// </summary>
    private void LoadNewGroup(string[] paths)
    {
        // 先清空旧图片
        for (int i = 0; i < _images.Length; i++)
            _images[i]?.Dispose();

        _imagePaths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        _imageCount = Math.Clamp(_imagePaths.Length, 1, 9);
        (_cols, _rows) = GetGridLayout(_imageCount);

        _images = new Image?[_imageCount];
        _baseZooms = new float[_imageCount];
        _offsets = new PointF[_imageCount];
        _manualOffsets = new PointF[_imageCount];
        _zoomLevels = new float[_imageCount];
        for (int i = 0; i < _imageCount; i++)
        {
            _offsets[i] = new PointF(0, 0);
            _manualOffsets[i] = new PointF(0, 0);
            _zoomLevels[i] = 1.0f;
        }

        _zoomLevel = 1.0f;
        _showHelp = false;
        _shiftDragIndex = -1;
        _resetOverlay.Hide();

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
        int topOffset = TitleBarHeight;
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

    private float GetEffectiveZoom(int index)
    {
        float level = IsSyncZoomDisabled() ? _zoomLevels[index] : _zoomLevel;
        return _baseZooms[index] * level;
    }

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.H)
        {
            _showHelp = !_showHelp;
            if (_showHelp)
                _historyBarData.Collapse();
            this.Invalidate();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            // Esc优先关闭当前打开的界面，只有都关闭时才关闭程序
            if (_resetOverlay.IsVisible)
            {
                _resetOverlay.Hide();
                this.Invalidate();
            }
            else if (_showHelp || _showZoomHelp)
            {
                _showHelp = false;
                _showZoomHelp = false;
                this.Invalidate();
            }
            else if (!_historyBarData.IsCollapsed)
            {
                _historyBarData.Collapse();
                _hoverHistoryGroup = -1;
                this.Invalidate();
            }
            else
            {
                this.Close();
            }
        }
    }

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



    // 棋盘格缓存
    private TextureBrush? _checkerBrush;

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



    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Alt || keyData == (Keys.Alt | Keys.Menu))
            return true;
        if (keyData == (Keys.Control | Keys.W))
        {
            // Ctrl+W优先关闭当前打开的界面，只有都关闭时才关闭程序
            if (_resetOverlay.IsVisible)
            {
                _resetOverlay.Hide();
                this.Invalidate();
            }
            else if (_showHelp || _showZoomHelp)
            {
                _showHelp = false;
                _showZoomHelp = false;
                this.Invalidate();
            }
            else if (!_historyBarData.IsCollapsed)
            {
                _historyBarData.Collapse();
                _hoverHistoryGroup = -1;
                this.Invalidate();
            }
            else
            {
                this.Close();
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool IsAltPressed()
    {
        return (ModifierKeys & Keys.Alt) == Keys.Alt;
    }

    /// <summary>
    /// 是否关闭了同步缩放（同步移动模式为"关闭同步缩放"或"同时关闭"时关闭）
    /// </summary>
    private bool IsSyncZoomDisabled() => _syncMoveMode == SyncMoveMode.DisableSyncZoom || _syncMoveMode == SyncMoveMode.DisableAll;

    /// <summary>
    /// 是否关闭了同步移动
    /// </summary>
    private bool IsSyncMoveDisabled() => _syncMoveMode == SyncMoveMode.DisableSyncMove || _syncMoveMode == SyncMoveMode.DisableAll;

    private void ToggleMaximize()
    {
        if (_isWindowMaximized)
        {
            this.WindowState = FormWindowState.Normal;
            _isWindowMaximized = false;
        }
        else
        {
            _restoreBounds = this.Bounds;
            this.WindowState = FormWindowState.Maximized;
            _isWindowMaximized = true;
        }
    }



    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _isWindowMaximized = this.WindowState == FormWindowState.Maximized;
        // 从最大化还原时，不更新 _restoreBounds（保持最大化前的位置）
        // 非最大化状态下的普通调整大小才更新
        if (!_isWindowMaximized && this.WindowState == FormWindowState.Normal)
        {
            _restoreBounds = this.Bounds;
        }
        ApplyRoundCorner();
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
        // 滚动操作主操作区时，自动收起历史记录、操作说明和重置偏移
        if (!_historyBarData.IsCollapsed || _showHelp || _resetOverlay.IsVisible || _showZoomHelp)
        {
            _historyBarData.Collapse();
            _showHelp = false;
            _showZoomHelp = false;
            _resetOverlay.Hide();
            _hoverHistoryGroup = -1;
        }

        if (ModifierKeys == Keys.Control)
        {
            float avgZoom = _baseZooms.Where(z => z > 0).DefaultIfEmpty(1f).Average() * _zoomLevel;
            float step = this.ClientSize.Width * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? step : -step;
            if (IsSyncMoveDisabled())
            {
                int idx = HitTest(e.Location);
                if (idx >= 0 && idx < _imageCount)
                    _offsets[idx] = new PointF(_offsets[idx].X + delta, _offsets[idx].Y);
            }
            else
            {
                for (int i = 0; i < _imageCount; i++)
                    _offsets[i] = new PointF(_offsets[i].X + delta, _offsets[i].Y);
            }
        }
        else if (IsAltPressed())
        {
            float zoomFactor = e.Delta > 0 ? 1.25f : 1f / 1.25f;
            PointF mousePos = e.Location;
            int activeIdx = HitTest(mousePos);

            if (IsSyncZoomDisabled())
            {
                // 关闭同步缩放模式：只缩放鼠标所在的那张图片
                if (activeIdx < 0 || activeIdx >= _imageCount || _images[activeIdx] == null)
                {
                    this.Invalidate();
                    return;
                }

                float oldZoomLevel = _zoomLevels[activeIdx];
                float newZoomLevel = oldZoomLevel * zoomFactor;
                if (newZoomLevel < 0.01f || newZoomLevel > 100f)
                    return;

                Rectangle activeRect = GetCellRect(activeIdx);
                Size activeImgSize = _images[activeIdx]!.Size;
                float oldEff = _baseZooms[activeIdx] * oldZoomLevel;
                float newEff = _baseZooms[activeIdx] * newZoomLevel;

                PointF norm = ZoomCalculator.ScreenToNormalized(mousePos, activeRect, oldEff, _offsets[activeIdx], activeImgSize);
                _offsets[activeIdx] = ZoomCalculator.ZoomAtNormalized(norm, activeRect, oldEff, newEff, _offsets[activeIdx], activeImgSize);
                _zoomLevels[activeIdx] = newZoomLevel;
            }
            else
            {
                // 同步对齐 / 独立缩放模式
                float oldZoomLevel = _zoomLevel;
                float newZoomLevel = _zoomLevel * zoomFactor;

                if (newZoomLevel < 0.01f || newZoomLevel > 100f)
                    return;

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

                // active图片：直接使用完整offset，确保以鼠标实际所指位置为基准缩放
                PointF norm = ZoomCalculator.ScreenToNormalized(mousePos, activeRect, oldEffActive, _offsets[activeIdx], activeImgSize);
                _offsets[activeIdx] = ZoomCalculator.ZoomAtNormalized(norm, activeRect, oldEffActive, newEffActive, _offsets[activeIdx], activeImgSize);

                float activeLocalX = mousePos.X - activeRect.Left;
                float activeLocalY = mousePos.Y - activeRect.Top;

                for (int i = 0; i < _imageCount; i++)
                {
                    if (i == activeIdx || _images[i] == null) continue;

                    Rectangle passiveRect = GetCellRect(i);
                    Size passiveImgSize = _images[i]!.Size;
                    float oldEffPassive = _baseZooms[i] * oldZoomLevel;
                    float newEffPassive = _baseZooms[i] * newZoomLevel;

                    if (_syncZoomMode == SyncZoomMode.SyncAlign)
                    {
                        // 同步对齐：将被动图片的同一归一化位置移到与主动图片鼠标位置对应的地方
                        PointF targetPos = new PointF(passiveRect.Left + activeLocalX, passiveRect.Top + activeLocalY);
                        var computed = ZoomCalculator.ZoomAndMoveToTarget(norm, passiveRect, newEffPassive, passiveImgSize, targetPos);
                        _offsets[i] = new PointF(computed.X + _manualOffsets[i].X, computed.Y + _manualOffsets[i].Y);
                    }
                    else
                    {
                        // 独立缩放：被动图片以与主动图片相同比例位置为缩放中心
                        // 即主图1/4处为缩放中心，从图也以其1/4处为缩放中心
                        _offsets[i] = ZoomCalculator.ZoomAtNormalized(norm, passiveRect, oldEffPassive, newEffPassive, _offsets[i], passiveImgSize);
                    }
                }

                _zoomLevel = newZoomLevel;
            }
        }
        else
        {
            float avgZoom = _baseZooms.Where(z => z > 0).DefaultIfEmpty(1f).Average() * _zoomLevel;
            float step = this.ClientSize.Height * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? step : -step;
            if (IsSyncMoveDisabled())
            {
                int idx = HitTest(e.Location);
                if (idx >= 0 && idx < _imageCount)
                    _offsets[idx] = new PointF(_offsets[idx].X, _offsets[idx].Y + delta);
            }
            else
            {
                for (int i = 0; i < _imageCount; i++)
                    _offsets[i] = new PointF(_offsets[i].X, _offsets[i].Y + delta);
            }
        }

        this.Invalidate();
    }
}
