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
    private bool _showInstructions = false;

    // 每张图独立的数据
    private Image?[] _images;
    private float[] _baseZooms;
    private PointF[] _offsets;

    // 历史记录
    private HistoryBar _historyBar = null!;
    private List<HistoryGroup> _historyGroups = new();

    // 自绘标题栏
    private const int TitleBarH = 32;
    private Rectangle _btnMin, _btnMax, _btnClose;
    private bool _hoverMin, _hoverMax, _hoverClose;
    private bool _isWindowMaximized = false;

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

        // 创建历史记录条
        _historyBar = new HistoryBar();
        _historyBar.HistoryGroupClicked += OnHistoryGroupClicked;
        _historyBar.HistoryGroupDeleteRequested += OnHistoryGroupDeleteRequested;

        // 注册事件
        this.MouseDown += Form1_MouseDown;
        this.MouseMove += Form1_MouseMove;
        this.MouseUp += Form1_MouseUp;
        this.MouseWheel += Form1_MouseWheel;
        this.Paint += Form1_Paint;
        this.KeyDown += Form1_KeyDown;

        // 先加载图片到内存缓存（在添加控件前）
        LoadImages();

        // 添加历史记录条到控件（后添加避免布局问题）
        _historyBar.Dock = DockStyle.Top;
        this.Controls.Add(_historyBar);

        // 加载历史记录
        _historyGroups = HistoryData.Load();
        _historyBar.LoadGroups(_historyGroups);

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
        _historyBar.LoadGroups(_historyGroups);

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
                    _historyBar.LoadGroups(_historyGroups);
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
            _historyBar.LoadGroups(_historyGroups);
        }
    }

    /// <summary>
    /// 历史记录组被请求删除
    /// </summary>
    private void OnHistoryGroupDeleteRequested(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _historyGroups.Count) return;

        var removed = _historyGroups[groupIndex];
        HistoryData.DeleteGroupThumbnails(_historyGroups, removed.Id);
        _historyGroups.RemoveAt(groupIndex);
        HistoryData.Save(_historyGroups);
        _historyBar.LoadGroups(_historyGroups);
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
        _showInstructions = false;

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
    /// 获取第 i 张图的绘制区域（扣除标题栏 + 历史栏高度）
    /// </summary>
    private Rectangle GetCellRect(int index)
    {
        int topOffset = TitleBarH + _historyBar.Height;
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
            _showInstructions = !_showInstructions;
            this.Invalidate();
        }
    }

    private void Form1_Paint(object sender, PaintEventArgs e)
    {
        if (_imagePaths == null || _imagePaths.Length == 0)
            return;

        try
        {
            EnsureCheckerBrush();

            // 绘制自绘标题栏
            DrawTitleBar(e.Graphics);

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
            int topOffset = TitleBarH + _historyBar.Height;
            int availH = this.ClientSize.Height - topOffset;
            int cellW = this.ClientSize.Width / _cols;
            int cellH = availH / _rows;
            for (int c = 1; c < _cols; c++)
                e.Graphics.DrawLine(pen, c * cellW, topOffset, c * cellW, this.ClientSize.Height);
            for (int r = 1; r < _rows; r++)
                e.Graphics.DrawLine(pen, 0, topOffset + r * cellH, this.ClientSize.Width, topOffset + r * cellH);

            // 按键提示
            if (_showInstructions)
            {
                DrawKeyInstructions(e.Graphics);
            }
        }
        catch (Exception ex)
        {
            e.Graphics.DrawString($"错误: {ex.Message}", this.Font, Brushes.Red, 10, TitleBarH + _historyBar.Height + 10);
        }
    }

    /// <summary>
    /// 自绘标题栏 — 左侧显示"历史记录"按钮，右侧显示最小化/最大化/关闭
    /// </summary>
    private void DrawTitleBar(Graphics g)
    {
        int w = this.ClientSize.Width;

        // 标题栏背景
        using var bgBrush = new SolidBrush(Color.FromArgb(32, 32, 32));
        g.FillRectangle(bgBrush, 0, 0, w, TitleBarH);

        // 标题文字
        using var titleFont = new Font("Segoe UI", 9F);
        using var titleTextBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
        string titleText = this.Text;
        g.DrawString(titleText, titleFont, titleTextBrush, 12, 8);

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
        using var font = new Font("Segoe UI", 9F);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, fgBrush,
            rect.Left + (rect.Width - size.Width) / 2,
            rect.Top + (rect.Height - size.Height) / 2);
    }

    private void DrawKeyInstructions(Graphics g)
    {
        string[] instructions = {
            "按键说明:",
            "- 鼠标左键拖动: 同步移动所有图片",
            "- 滚轮: 上下移动图片",
            "- Ctrl+滚轮: 左右移动图片",
            "- Alt+滚轮: 以鼠标指针为中心缩放图片",
            "- H键: 显示/隐藏按键提示"
        };

        float boxWidth = 340f;
        float boxHeight = instructions.Length * 20 + 10;
        int topOffset = TitleBarH + _historyBar.Height;
        using (var bgBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
        {
            g.FillRectangle(bgBrush, 5, topOffset + 5, boxWidth, boxHeight);
        }

        for (int i = 0; i < instructions.Length; i++)
        {
            g.DrawString(instructions[i], this.Font, Brushes.Black, 10, topOffset + 10 + i * 20);
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

    private void Form1_MouseDown(object sender, MouseEventArgs e)
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
            // 历史记录折叠按钮区域 — 点击标题栏左侧
            if (e.Location.X < 100)
            {
                _historyBar.ToggleCollapse();
                return;
            }
            // 拖动窗口
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            return;
        }

        // 历史栏区域
        if (e.Location.Y < TitleBarH + _historyBar.Height)
        {
            // 由 HistoryBar 自行处理
            return;
        }

        // 图片区域拖动
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _lastMousePos = e.Location;
        }
    }

    private void Form1_MouseMove(object sender, MouseEventArgs e)
    {
        // 标题栏悬停效果
        if (e.Location.Y < TitleBarH)
        {
            bool newHoverMin = _btnMin.Contains(e.Location);
            bool newHoverMax = _btnMax.Contains(e.Location);
            bool newHoverClose = _btnClose.Contains(e.Location);

            if (newHoverMin != _hoverMin || newHoverMax != _hoverMax || newHoverClose != _hoverClose)
            {
                _hoverMin = newHoverMin;
                _hoverMax = newHoverMax;
                _hoverClose = newHoverClose;
                this.Invalidate(new Rectangle(0, 0, this.ClientSize.Width, TitleBarH));
            }
            return;
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

    private void Form1_MouseWheel(object sender, MouseEventArgs e)
    {
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
