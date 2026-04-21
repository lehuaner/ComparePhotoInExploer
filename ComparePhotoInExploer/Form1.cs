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
}
