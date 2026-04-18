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

    // 自绘标题栏
    public const int TitleBarHeight = 32;
    private Rectangle _btnMin, _btnMax, _btnClose, _btnHelp, _btnHistory, _btnTheme, _btnReset, _btnSyncZoom, _btnZoomHelp;
    private bool _hoverMin, _hoverMax, _hoverClose, _hoverHelp, _hoverHistory, _hoverTheme, _hoverReset, _hoverSyncZoom, _hoverZoomHelp;
    private bool _isWindowMaximized = false;
    private bool _showHelp = false; // 是否显示按键说明（与历史记录互斥）
    private bool _showZoomHelp = false; // 是否显示缩放说明
    private bool _syncZoomPosition = true; // 缩放时是否将被动图片对齐到主动图片鼠标所指位置
    private readonly ResetOverlayHelper _resetOverlay; // 重置偏移覆盖层

    // 拖放
    private bool _isDragOver = false; // 是否有文件正在拖入

    // Win32 拖拽移动
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    // Win32 圆角窗口
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);
    [DllImport("gdi32.dll")]
    private static extern int DeleteObject(IntPtr hObject);

    private const int CornerRadius = 12;

    private void ApplyRoundCorner()
    {
        if (_isWindowMaximized)
        {
            // 最大化时移除圆角
            SetWindowRgn(this.Handle, IntPtr.Zero, true);
        }
        else
        {
            var rgn = CreateRoundRectRgn(0, 0, this.Width + 1, this.Height + 1, CornerRadius, CornerRadius);
            SetWindowRgn(this.Handle, rgn, true);
            // SetWindowRgn 会接管 hRgn 的所有权，不需要 DeleteObject
        }
    }

    // Win32 监听系统主题变化
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int RegisterWindowMessage(string lpString);
    private readonly int _wmSettingChange = RegisterWindowMessage("WM_SETTINGCHANGE");

    public Form1(string[] imagePaths)
    {
        InitializeComponent();

        // 初始化重置偏移覆盖层
        _resetOverlay = new ResetOverlayHelper(this);

        // 加载主题设置
        LoadThemeSetting();
        ApplyTheme(_currentTheme);

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
        for (int i = 0; i < _offsets.Length; i++)
        {
            _offsets[i] = new PointF(0, 0);
            _manualOffsets[i] = new PointF(0, 0);
        }

        // 无边框 + 双缓冲
        this.FormBorderStyle = FormBorderStyle.None;
        this.DoubleBuffered = true;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = _imageCount <= 2 ? new Size(1000, 600) : new Size(1200, 800);

        // 圆角边框
        this.Load += (s, e) => ApplyRoundCorner();

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

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;

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
        if (m.Msg == WM_SYSCOMMAND && (int)m.WParam == SC_CLOSE)
        {
            if (this.OwnedForms.Length > 0)
            {
                this.Close();
                return;
            }
        }
        base.WndProc(ref m);
    }

    #region 主题管理

    private static readonly string ThemeSettingFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "Local", "ComparePhotoInExploer", "theme.txt");

    private void LoadThemeSetting()
    {
        try
        {
            if (File.Exists(ThemeSettingFile))
            {
                var text = File.ReadAllText(ThemeSettingFile).Trim();
                if (Enum.TryParse<AppTheme>(text, out var theme))
                {
                    _currentTheme = theme;
                    return;
                }
            }
        }
        catch { }
        _currentTheme = AppTheme.System;
    }

    private void SaveThemeSetting()
    {
        try
        {
            var dir = Path.GetDirectoryName(ThemeSettingFile)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ThemeSettingFile, _currentTheme.ToString());
        }
        catch { }
    }

    private void ApplyTheme(AppTheme theme)
    {
        _currentTheme = theme;
        _colors = ThemeColorSet.FromTheme(theme);
        _checkerBrush = null; // 重建棋盘格
        SaveThemeSetting();
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

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp", ".ico", ".svg" };

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ImageExtensions.Contains(ext);
    }

    private void Form1_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Any(IsImageFile))
            {
                e.Effect = DragDropEffects.Copy;
                _isDragOver = true;
                this.Invalidate();
                return;
            }
        }
        e.Effect = DragDropEffects.None;
    }

    private void Form1_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Any(IsImageFile))
            {
                e.Effect = DragDropEffects.Copy;
                return;
            }
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

        if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) return;

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null) return;

        var imageFiles = files.Where(IsImageFile).ToArray();
        if (imageFiles.Length == 0) return;

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
        }
        
        this.Text = "图片对比";
        this.Invalidate();
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
        for (int i = 0; i < _imageCount; i++)
        {
            _offsets[i] = new PointF(0, 0);
            _manualOffsets[i] = new PointF(0, 0);
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
                float r = CornerRadius;
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
    /// 无图片时的提示
    /// </summary>
    private void DrawEmptyHint(Graphics g)
    {
        int topOffset = TitleBarHeight;
        int availW = this.ClientSize.Width;
        int availH = this.ClientSize.Height - topOffset;

        // 填充背景
        using var bgBrush = new SolidBrush(_colors.CheckerLight);
        g.FillRectangle(bgBrush, 0, topOffset, availW, availH);

        string hint = "拖入图片进行对比";
        using var font = new Font("Microsoft YaHei UI", 16F);
        using var fgBrush = new SolidBrush(_colors.DropHintFg);
        var size = g.MeasureString(hint, font);
        float x = (availW - size.Width) / 2;
        float y = topOffset + (availH - size.Height) / 2;
        g.DrawString(hint, font, fgBrush, x, y);
    }

    /// <summary>
    /// 拖放时的覆盖层提示
    /// </summary>
    private void DrawDropOverlay(Graphics g)
    {
        int topOffset = TitleBarHeight;
        int availW = this.ClientSize.Width;
        int availH = this.ClientSize.Height - topOffset;

        using var bgBrush = new SolidBrush(Color.FromArgb(30, 100, 149, 237));
        g.FillRectangle(bgBrush, 0, topOffset, availW, availH);

        // 虚线边框
        using var borderPen = new Pen(_colors.DropHintBorder, 3) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawRectangle(borderPen, 6, topOffset + 6, availW - 12, availH - 12);

        string hint = "释放以加载图片";
        using var font = new Font("Microsoft YaHei UI", 16F);
        using var fgBrush = new SolidBrush(_colors.DropHintBorder);
        var size = g.MeasureString(hint, font);
        float x = (availW - size.Width) / 2;
        float y = topOffset + (availH - size.Height) / 2;
        g.DrawString(hint, font, fgBrush, x, y);
    }

    /// <summary>
    /// 自绘标题栏 — 左侧"历史记录"+"操作说明"+"主题"按钮，右侧最小化/最大化/关闭
    /// </summary>
    private void DrawTitleBar(Graphics g)
    {
        int w = this.ClientSize.Width;

        // 标题栏背景
        using var bgBrush = new SolidBrush(_colors.TitleBarBg);
        g.FillRectangle(bgBrush, 0, 0, w, TitleBarHeight);

        // 按钮起始位置
        int btnX = 8;

        // 历史记录按钮
        _btnHistory = new Rectangle(btnX, 0, 72, TitleBarHeight);
        bool historyActive = !_historyBarData.IsCollapsed;
        Color historyBg = historyActive ? _colors.TitleBarBtnActiveBg :
                          _hoverHistory ? _colors.TitleBarBtnHoverBg : Color.Transparent;
        using (var historyBgBrush = new SolidBrush(historyBg))
            g.FillRectangle(historyBgBrush, _btnHistory);
        using var historyFont = new Font("Microsoft YaHei UI", 9F);
        using var historyFgBrush = new SolidBrush(_colors.TitleBarFg);
        var historySize = g.MeasureString("历史记录", historyFont);
        g.DrawString("历史记录", historyFont, historyFgBrush,
            _btnHistory.Left + (_btnHistory.Width - historySize.Width) / 2,
            _btnHistory.Top + (_btnHistory.Height - historySize.Height) / 2);

        // 操作说明按钮
        btnX += 72 + 2;
        _btnHelp = new Rectangle(btnX, 0, 72, TitleBarHeight);
        Color helpBg = _showHelp ? _colors.TitleBarBtnActiveBg :
                       _hoverHelp ? _colors.TitleBarBtnHoverBg : Color.Transparent;
        using (var helpBgBrush = new SolidBrush(helpBg))
            g.FillRectangle(helpBgBrush, _btnHelp);
        using var helpFont = new Font("Microsoft YaHei UI", 9F);
        using var helpFgBrush = new SolidBrush(_colors.TitleBarFg);
        var helpSize = g.MeasureString("操作说明", helpFont);
        g.DrawString("操作说明", helpFont, helpFgBrush,
            _btnHelp.Left + (_btnHelp.Width - helpSize.Width) / 2,
            _btnHelp.Top + (_btnHelp.Height - helpSize.Height) / 2);

        // 主题切换按钮（固定宽度，避免文字变化导致按钮宽度跳动）
        btnX += 72 + 2;
        int themeBtnW = 72;
        _btnTheme = new Rectangle(btnX, 0, themeBtnW, TitleBarHeight);
        Color themeBg = _hoverTheme ? _colors.TitleBarBtnHoverBg : Color.Transparent;
        using (var themeBgBrush = new SolidBrush(themeBg))
            g.FillRectangle(themeBgBrush, _btnTheme);
        using var themeFont = new Font("Microsoft YaHei UI", 9F);
        using var themeFgBrush = new SolidBrush(_colors.TitleBarFg);
        string themeLabel = GetThemeLabel(_currentTheme);
        var themeLabelSize = g.MeasureString(themeLabel, themeFont);
        g.DrawString(themeLabel, themeFont, themeFgBrush,
            _btnTheme.Left + (_btnTheme.Width - themeLabelSize.Width) / 2,
            _btnTheme.Top + (_btnTheme.Height - themeLabelSize.Height) / 2);

        // 同步缩放位置按钮
        btnX += themeBtnW + 2;
        int syncZoomBtnW = 80;
        _btnSyncZoom = new Rectangle(btnX, 0, syncZoomBtnW, TitleBarHeight);
        Color syncZoomBg = _syncZoomPosition ? _colors.TitleBarBtnActiveBg :
                           _hoverSyncZoom ? _colors.TitleBarBtnHoverBg : Color.Transparent;
        using (var syncZoomBgBrush = new SolidBrush(syncZoomBg))
            g.FillRectangle(syncZoomBgBrush, _btnSyncZoom);
        using var syncZoomFont = new Font("Microsoft YaHei UI", 9F);
        using var syncZoomFgBrush = new SolidBrush(_colors.TitleBarFg);
        string syncZoomLabel = _syncZoomPosition ? "同步对齐" : "独立缩放";
        var syncZoomLabelSize = g.MeasureString(syncZoomLabel, syncZoomFont);
        g.DrawString(syncZoomLabel, syncZoomFont, syncZoomFgBrush,
            _btnSyncZoom.Left + (_btnSyncZoom.Width - syncZoomLabelSize.Width) / 2,
            _btnSyncZoom.Top + (_btnSyncZoom.Height - syncZoomLabelSize.Height) / 2);

        // 缩放说明按钮（小问号）
        btnX += syncZoomBtnW + 1;
        _btnZoomHelp = new Rectangle(btnX, 0, 28, TitleBarHeight);
        Color zoomHelpBg = _showZoomHelp ? _colors.TitleBarBtnActiveBg :
                           _hoverZoomHelp ? _colors.TitleBarBtnHoverBg : Color.Transparent;
        using (var zoomHelpBgBrush = new SolidBrush(zoomHelpBg))
            g.FillRectangle(zoomHelpBgBrush, _btnZoomHelp);
        using var zoomHelpFont = new Font("Microsoft YaHei UI", 9F);
        using var zoomHelpFgBrush = new SolidBrush(_colors.TitleBarFg);
        var zoomHelpLabelSize = g.MeasureString("?", zoomHelpFont);
        g.DrawString("?", zoomHelpFont, zoomHelpFgBrush,
            _btnZoomHelp.Left + (_btnZoomHelp.Width - zoomHelpLabelSize.Width) / 2,
            _btnZoomHelp.Top + (_btnZoomHelp.Height - zoomHelpLabelSize.Height) / 2);

        // 重置偏移按钮（仅在有偏移时显示）
        btnX += 28 + 2;
        bool hasAnyOffset = _imageCount > 0 && _manualOffsets.Any(o => o.X != 0 || o.Y != 0);
        if (hasAnyOffset)
        {
            _btnReset = new Rectangle(btnX, 0, 72, TitleBarHeight);
            Color resetBg = _resetOverlay.IsVisible ? _colors.TitleBarBtnActiveBg :
                            _hoverReset ? _colors.TitleBarBtnHoverBg : Color.Transparent;
            using (var resetBgBrush = new SolidBrush(resetBg))
                g.FillRectangle(resetBgBrush, _btnReset);
            using var resetFont = new Font("Microsoft YaHei UI", 9F);
            using var resetFgBrush = new SolidBrush(_colors.TitleBarFg);
            var resetSize = g.MeasureString("重置偏移", resetFont);
            g.DrawString("重置偏移", resetFont, resetFgBrush,
                _btnReset.Left + (_btnReset.Width - resetSize.Width) / 2,
                _btnReset.Top + (_btnReset.Height - resetSize.Height) / 2);
        }
        else
        {
            _btnReset = Rectangle.Empty;
        }

        // 窗口控制按钮区域
        int btnW = 46;
        int btnH = TitleBarHeight;
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
            bg = _colors.TitleBarCloseHoverBg;
            fg = Color.White;
        }
        else if (hover)
        {
            bg = _colors.TitleBarBtnHoverBg;
            fg = Color.White;
        }
        else
        {
            bg = Color.Transparent;
            fg = _colors.TitleBarBtnFg;
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
            "快捷键说明:",
            "- 鼠标左键拖动: 同步移动所有图片",
            "- Shift+左键拖动: 只移动鼠标所在的那张图片",
            "- 滚轮: 上下移动图片",
            "- Ctrl+滚轮: 左右移动图片",
            "- Alt+滚轮: 以鼠标指针为中心缩放图片",
            "- 拖入图片: 加载新的图片组进行对比",
            "- Esc / Ctrl+W: 关闭当前界面或程序",
        };

        float boxWidth = 380f;
        float boxHeight = instructions.Length * 22 + 20;
        int topOffset = TitleBarHeight;
        using (var bgBrush = new SolidBrush(_colors.HelpPanelBg))
        {
            g.FillRectangle(bgBrush, 5, topOffset + 5, boxWidth, boxHeight);
        }

        using var borderPen = new Pen(_colors.HelpPanelBorder, 1);
        g.DrawRectangle(borderPen, 5, topOffset + 5, boxWidth, boxHeight);

        for (int i = 0; i < instructions.Length; i++)
        {
            using var brush = i == 0 
                ? new SolidBrush(_colors.HelpTitleFg)
                : new SolidBrush(_colors.HelpTextFg);
            g.DrawString(instructions[i], i == 0 ? new Font(this.Font, FontStyle.Bold) : this.Font, brush, 12, topOffset + 10 + i * 22);
        }
    }

    /// <summary>
    /// 缩放说明面板
    /// </summary>
    private void DrawZoomHelpPanel(Graphics g)
    {
        string[] instructions = {
            "缩放模式说明:",
            "",
            "【同步对齐】",
            "Alt+滚轮缩放时，所有图片的同一比例位置",
            "会移动到与鼠标所指位置相同的地方。",
            "例如：鼠标指向主图1/4处，从图也会",
            "将其1/4处对齐到相同位置。",
            "",
            "【独立缩放】",
            "Alt+滚轮缩放时，从图以与主图相同的",
            "比例位置为缩放中心进行缩放。",
            "例如：主图以1/4处为缩放中心，",
            "从图也以其自身1/4处为缩放中心，",
            "两图缩放中心的比例位置相同，",
            "但不会移动到鼠标屏幕位置。"
        };

        float boxWidth = 310f;
        float boxHeight = instructions.Length * 20 + 20;
        int topOffset = TitleBarHeight;
        using (var bgBrush = new SolidBrush(_colors.HelpPanelBg))
        {
            g.FillRectangle(bgBrush, 5, topOffset + 5, boxWidth, boxHeight);
        }

        using var borderPen = new Pen(_colors.HelpPanelBorder, 1);
        g.DrawRectangle(borderPen, 5, topOffset + 5, boxWidth, boxHeight);

        using var titleFont = new Font(this.Font, FontStyle.Bold);
        using var sectionFont = new Font(this.Font, FontStyle.Bold);
        for (int i = 0; i < instructions.Length; i++)
        {
            bool isTitle = i == 0;
            bool isSection = instructions[i].StartsWith("【");
            using var brush = isTitle ? new SolidBrush(_colors.HelpTitleFg) : new SolidBrush(_colors.HelpTextFg);
            var font = isTitle ? titleFont : (isSection ? sectionFont : this.Font);
            g.DrawString(instructions[i], font, brush, 12, topOffset + 10 + i * 20);
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

    private void Form1_MouseDown(object? sender, MouseEventArgs e)
    {
        // 标题栏区域处理
        if (e.Location.Y < TitleBarHeight)
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
                {
                    _historyBarData.Collapse(); // 关闭历史记录
                    _resetOverlay.Hide(); // 关闭重置偏移
                    _showZoomHelp = false; // 关闭缩放说明
                }
                this.Invalidate();
                return;
            }
            // 历史记录按钮
            if (_btnHistory.Contains(e.Location))
            {
                _showHelp = false; // 关闭按键说明
                _showZoomHelp = false; // 关闭缩放说明
                _resetOverlay.Hide(); // 关闭重置偏移
                _historyBarData.ToggleCollapse();
                this.Invalidate();
                return;
            }
            // 主题切换按钮
            if (_btnTheme.Contains(e.Location))
            {
                CycleTheme();
                return;
            }
            // 同步缩放位置按钮
            if (_btnSyncZoom.Contains(e.Location))
            {
                _syncZoomPosition = !_syncZoomPosition;
                this.Invalidate();
                return;
            }
            // 缩放说明按钮
            if (_btnZoomHelp.Contains(e.Location))
            {
                _showZoomHelp = !_showZoomHelp;
                if (_showZoomHelp)
                {
                    _showHelp = false;
                    _historyBarData.Collapse();
                    _resetOverlay.Hide();
                }
                this.Invalidate();
                return;
            }
            // 重置偏移按钮
            if (!_btnReset.IsEmpty && _btnReset.Contains(e.Location))
            {
                _resetOverlay.IsVisible = !_resetOverlay.IsVisible;
                if (_resetOverlay.IsVisible)
                {
                    _showHelp = false;
                    _historyBarData.Collapse();
                }
                _resetOverlay.HoverCell = -1;
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
            int hitIdx = _historyBarData.HitTest(0, TitleBarHeight, this.ClientSize.Width, e.Location, 0);
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

                        var result = ThemedMessageBox.Show(this,
                            $"确定删除此历史记录组？\n\n{imgNames}",
                            "删除历史记录",
                            MessageBoxButtons.YesNo, _colors);

                        if (result == DialogResult.Yes)
                        {
                            OnHistoryGroupDeleteRequested(hitIdx);
                        }
                    }
                }
                return;
            }
        }

        // 点击主操作区时，自动收起历史记录、操作说明
        if (!_historyBarData.IsCollapsed || _showHelp || _showZoomHelp)
        {
            _historyBarData.Collapse();
            _showHelp = false;
            _showZoomHelp = false;
            _hoverHistoryGroup = -1;
            this.Invalidate();
        }

        // 重置偏移模式：如果点击在面板外，自动关闭并跳过本次操作
        if (_resetOverlay.IsVisible && !_resetOverlay.IsInOverlayArea(e.Location, _imageCount, _cols, _rows))
        {
            _resetOverlay.Hide();
            this.Invalidate();
            return;
        }

        // 重置偏移模式下的交互
        if (_resetOverlay.IsVisible)
        {
            if (e.Button == MouseButtons.Left)
            {
                // 点击重置按钮（根据选中状态决定功能）
                if (_resetOverlay.BatchResetButton.Contains(e.Location))
                {
                    if (_resetOverlay.SelectedCells.Count > 0)
                    {
                        // 有选中图片 → 重置选中
                        var names = string.Join(", ", _resetOverlay.SelectedCells.Select(i => Path.GetFileName(_imagePaths[i])));
                        var result = ThemedMessageBox.Show(this,
                            $"确定重置以下 {_resetOverlay.SelectedCells.Count} 张图片的偏移？\n\n{names}",
                            "重置选中偏移", MessageBoxButtons.YesNo, _colors);
                        if (result == DialogResult.Yes)
                        {
                            foreach (int idx in _resetOverlay.SelectedCells)
                            {
                                _offsets[idx] = new PointF(
                                    _offsets[idx].X - _manualOffsets[idx].X,
                                    _offsets[idx].Y - _manualOffsets[idx].Y);
                                _manualOffsets[idx] = new PointF(0, 0);
                            }
                            if (!_manualOffsets.Any(o => o.X != 0 || o.Y != 0))
                                _resetOverlay.Hide();
                            else
                                _resetOverlay.SelectedCells.Clear();
                            this.Invalidate();
                        }
                    }
                    else
                    {
                        // 无选中图片 → 全部重置
                        var offsetIndices = new List<int>();
                        for (int i = 0; i < _imageCount; i++)
                        {
                            if (_manualOffsets[i].X != 0 || _manualOffsets[i].Y != 0)
                                offsetIndices.Add(i);
                        }
                        if (offsetIndices.Count > 0)
                        {
                            var names = string.Join(", ", offsetIndices.Select(i => Path.GetFileName(_imagePaths[i])));
                            var result = ThemedMessageBox.Show(this,
                                $"确定重置以下 {offsetIndices.Count} 张图片的偏移？\n\n{names}",
                                "全部重置偏移", MessageBoxButtons.YesNo, _colors);
                            if (result == DialogResult.Yes)
                            {
                                foreach (int idx in offsetIndices)
                                {
                                    _offsets[idx] = new PointF(
                                        _offsets[idx].X - _manualOffsets[idx].X,
                                        _offsets[idx].Y - _manualOffsets[idx].Y);
                                    _manualOffsets[idx] = new PointF(0, 0);
                                }
                                _resetOverlay.Hide();
                                this.Invalidate();
                            }
                        }
                    }
                    return;
                }

                // 点击缩略图选中/取消选中
                int hitIdx = _resetOverlay.HitTestCell(e.Location, _imageCount, _manualOffsets, _cols, _rows);
                if (hitIdx >= 0)
                {
                    // 切换选中（普通点击也切换）
                    if (_resetOverlay.SelectedCells.Contains(hitIdx))
                        _resetOverlay.SelectedCells.Remove(hitIdx);
                    else if ((ModifierKeys & Keys.Control) == Keys.Control)
                        _resetOverlay.SelectedCells.Add(hitIdx);
                    else
                    {
                        _resetOverlay.SelectedCells.Clear();
                        _resetOverlay.SelectedCells.Add(hitIdx);
                    }
                    this.Invalidate();
                    return;
                }

                // 点击空白处：取消选择，并开始框选
                _resetOverlay.SelectedCells.Clear();
                _resetOverlay.StartSelection(e.Location);
                this.Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                // 右键点击图片重置
                int hitIdx = _resetOverlay.HitTestCell(e.Location, _imageCount, _manualOffsets, _cols, _rows);
                if (hitIdx >= 0)
                {
                    // 如果右键点中的图片已在选中列表中，则批量重置所有选中图片
                    var toReset = _resetOverlay.SelectedCells.Contains(hitIdx)
                        ? _resetOverlay.SelectedCells.ToList()
                        : new List<int> { hitIdx };

                    var names = string.Join(", ", toReset.Select(i => Path.GetFileName(_imagePaths[i])));
                    var result = ThemedMessageBox.Show(this,
                        $"确定重置以下 {toReset.Count} 张图片的偏移？\n\n{names}",
                        "重置偏移", MessageBoxButtons.YesNo, _colors);
                    if (result == DialogResult.Yes)
                    {
                        foreach (int idx in toReset)
                        {
                            _offsets[idx] = new PointF(
                                _offsets[idx].X - _manualOffsets[idx].X,
                                _offsets[idx].Y - _manualOffsets[idx].Y);
                            _manualOffsets[idx] = new PointF(0, 0);
                        }
                        if (!_manualOffsets.Any(o => o.X != 0 || o.Y != 0))
                            _resetOverlay.Hide();
                        else
                        {
                            foreach (int idx in toReset)
                                _resetOverlay.SelectedCells.Remove(idx);
                        }
                        this.Invalidate();
                    }
                }
                return;
            }
            return;
        }

        // 图片区域拖动
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _lastMousePos = e.Location;

            // Shift+左键：只拖动鼠标所在的那张图片
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                _shiftDragIndex = HitTest(e.Location);
            }
            else
            {
                _shiftDragIndex = -1;
            }
        }
    }

    private void Form1_MouseMove(object? sender, MouseEventArgs e)
    {
        // 标题栏悬停效果
        if (e.Location.Y < TitleBarHeight)
        {
            bool newHoverMin = _btnMin.Contains(e.Location);
            bool newHoverMax = _btnMax.Contains(e.Location);
            bool newHoverClose = _btnClose.Contains(e.Location);
            bool newHoverHelp = _btnHelp.Contains(e.Location);
            bool newHoverHistory = _btnHistory.Contains(e.Location);
            bool newHoverTheme = _btnTheme.Contains(e.Location);
            bool newHoverReset = !_btnReset.IsEmpty && _btnReset.Contains(e.Location);
            bool newHoverSyncZoom = _btnSyncZoom.Contains(e.Location);
            bool newHoverZoomHelp = _btnZoomHelp.Contains(e.Location);

            if (newHoverMin != _hoverMin || newHoverMax != _hoverMax || newHoverClose != _hoverClose || 
                newHoverHelp != _hoverHelp || newHoverHistory != _hoverHistory || newHoverTheme != _hoverTheme ||
                newHoverReset != _hoverReset || newHoverSyncZoom != _hoverSyncZoom || newHoverZoomHelp != _hoverZoomHelp)
            {
                _hoverMin = newHoverMin;
                _hoverMax = newHoverMax;
                _hoverClose = newHoverClose;
                _hoverHelp = newHoverHelp;
                _hoverHistory = newHoverHistory;
                _hoverTheme = newHoverTheme;
                _hoverReset = newHoverReset;
                _hoverSyncZoom = newHoverSyncZoom;
                _hoverZoomHelp = newHoverZoomHelp;
                this.Invalidate(new Rectangle(0, 0, this.ClientSize.Width, TitleBarHeight));
            }
            return;
        }

        // 历史记录覆盖层悬停检测
        if (!_historyBarData.IsCollapsed && _historyBarData.GroupCount > 0)
        {
            int newHover = _historyBarData.HitTest(0, TitleBarHeight, this.ClientSize.Width, e.Location, 0);
            if (newHover != _hoverHistoryGroup)
            {
                _hoverHistoryGroup = newHover;
                this.Cursor = newHover >= 0 ? Cursors.Hand : Cursors.Default;
                this.Invalidate();
            }
            if (newHover >= 0)
                return; // 在历史记录上，不拖动图片
        }

        // 重置偏移模式悬停和框选
        if (_resetOverlay.IsVisible)
        {
            // 框选拖动 — 实时更新选中状态并重绘
            if (_resetOverlay.IsSelecting)
            {
                _resetOverlay.UpdateSelection(e.Location, _imageCount, _manualOffsets, _cols, _rows);
                this.Invalidate();
                return;
            }

            int newHoverCell = _resetOverlay.HitTestCell(e.Location, _imageCount, _manualOffsets, _cols, _rows);
            if (newHoverCell != _resetOverlay.HoverCell)
            {
                _resetOverlay.HoverCell = newHoverCell;
                this.Cursor = newHoverCell >= 0 ? Cursors.Hand : Cursors.Default;
                this.Invalidate();
            }
            return; // 重置模式下不拖动图片
        }

        if (_isDragging)
        {
            int deltaX = e.X - _lastMousePos.X;
            int deltaY = e.Y - _lastMousePos.Y;

            if (_shiftDragIndex >= 0 && _shiftDragIndex < _imageCount)
            {
                // Shift拖动：只移动选中的图片
                int i = _shiftDragIndex;
                _offsets[i] = new PointF(_offsets[i].X + deltaX, _offsets[i].Y + deltaY);
                _manualOffsets[i] = new PointF(_manualOffsets[i].X + deltaX, _manualOffsets[i].Y + deltaY);
            }
            else
            {
                // 普通拖动：同步移动所有图片
                for (int i = 0; i < _imageCount; i++)
                {
                    _offsets[i] = new PointF(_offsets[i].X + deltaX, _offsets[i].Y + deltaY);
                }
            }

            _lastMousePos = e.Location;
            this.Invalidate();
        }
    }

    private void Form1_MouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
        _shiftDragIndex = -1;
        if (_resetOverlay.IsSelecting)
        {
            _resetOverlay.EndSelection();
            this.Invalidate();
        }
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

            // active图片：直接使用完整offset，确保以鼠标实际所指位置为基准缩放
            PointF norm = ScreenToNormalized(mousePos, activeRect, oldEffActive, _offsets[activeIdx], activeImgSize);
            _offsets[activeIdx] = ZoomAtNormalized(norm, activeRect, oldEffActive, newEffActive, _offsets[activeIdx], activeImgSize);

            float activeLocalX = mousePos.X - activeRect.Left;
            float activeLocalY = mousePos.Y - activeRect.Top;

            for (int i = 0; i < _imageCount; i++)
            {
                if (i == activeIdx || _images[i] == null) continue;

                Rectangle passiveRect = GetCellRect(i);
                Size passiveImgSize = _images[i]!.Size;
                float oldEffPassive = _baseZooms[i] * oldZoomLevel;
                float newEffPassive = _baseZooms[i] * newZoomLevel;

                if (_syncZoomPosition)
                {
                    // 同步对齐：将被动图片的同一归一化位置移到与主动图片鼠标位置对应的地方
                    PointF targetPos = new PointF(passiveRect.Left + activeLocalX, passiveRect.Top + activeLocalY);
                    var computed = ZoomAndMoveToTarget(norm, passiveRect, newEffPassive, passiveImgSize, targetPos);
                    _offsets[i] = new PointF(computed.X + _manualOffsets[i].X, computed.Y + _manualOffsets[i].Y);
                }
                else
                {
                    // 独立缩放：被动图片以与主动图片相同比例位置为缩放中心
                    // 即主图1/4处为缩放中心，从图也以其1/4处为缩放中心
                    _offsets[i] = ZoomAtNormalized(norm, passiveRect, oldEffPassive, newEffPassive, _offsets[i], passiveImgSize);
                }
            }

            _zoomLevel = newZoomLevel;
        }
        else
        {
            float avgZoom = _baseZooms.Where(z => z > 0).DefaultIfEmpty(1f).Average() * _zoomLevel;
            float step = this.ClientSize.Height * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? step : -step;
            for (int i = 0; i < _imageCount; i++)
                _offsets[i] = new PointF(_offsets[i].X, _offsets[i].Y + delta);
        }

        this.Invalidate();
    }
}
