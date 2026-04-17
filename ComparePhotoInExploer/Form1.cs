namespace ComparePhotoInExploer;

public partial class Form1 : Form
{
    private readonly string[] _imagePaths;
    private readonly int _imageCount;
    private readonly int _cols;
    private readonly int _rows;

    private bool _isDragging = false;
    private Point _lastMousePos;
    private float _zoomLevel = 1.0f;
    private bool _showInstructions = false;
    private Button _toggleButton;

    // 每张图独立的数据
    private readonly Image?[] _images;
    private readonly float[] _baseZooms;
    private readonly PointF[] _offsets;

    public Form1(string[] imagePaths)
    {
        InitializeComponent();
        _imagePaths = imagePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        _imageCount = Math.Clamp(_imagePaths.Length, 1, 9);
        this.Text = $"图片对比 ({_imageCount}张)";
        this.Size = _imageCount <= 2 ? new Size(1000, 600) : new Size(1200, 800);
        this.DoubleBuffered = true;

        // 计算网格尺寸
        (_cols, _rows) = GetGridLayout(_imageCount);

        // 初始化每张图的数据
        _images = new Image?[_imageCount];
        _baseZooms = new float[_imageCount];
        _offsets = new PointF[_imageCount];
        for (int i = 0; i < _imageCount; i++)
            _offsets[i] = new PointF(0, 0);

        // 创建切换按钮
        _toggleButton = new Button();
        _toggleButton.Text = "▶";
        _toggleButton.Size = new Size(30, 30);
        _toggleButton.Location = new Point(10, 10);
        _toggleButton.FlatStyle = FlatStyle.Flat;
        _toggleButton.FlatAppearance.BorderSize = 0;
        _toggleButton.FlatAppearance.MouseOverBackColor = Color.Transparent;
        _toggleButton.FlatAppearance.MouseDownBackColor = Color.Transparent;
        _toggleButton.BackColor = Color.Transparent;
        _toggleButton.Click += ToggleButton_Click;
        this.Controls.Add(_toggleButton);

        // 注册事件
        this.MouseDown += Form1_MouseDown;
        this.MouseMove += Form1_MouseMove;
        this.MouseUp += Form1_MouseUp;
        this.MouseWheel += Form1_MouseWheel;
        this.Paint += Form1_Paint;
        this.Resize += Form1_Resize;

        // 加载图片到内存缓存
        LoadImages();
    }

    /// <summary>
    /// 根据图片数量计算网格布局 (cols, rows)
    /// 2→1×2, 3→2×2, 4→2×2, 5→2×3, 6→2×3, 7→3×3, 8→3×3, 9→3×3
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

    /// <summary>
    /// 计算使图片适配绘制区域（保持宽高比）的缩放比例
    /// </summary>
    private static float CalculateFitZoom(Image image, Rectangle drawArea)
    {
        if (image == null) return 1.0f;
        float scaleX = (float)drawArea.Width / image.Width;
        float scaleY = (float)drawArea.Height / image.Height;
        return Math.Min(scaleX, scaleY);
    }

    /// <summary>
    /// 获取第 i 张图的绘制区域
    /// </summary>
    private Rectangle GetCellRect(int index)
    {
        int cellW = this.ClientSize.Width / _cols;
        int cellH = this.ClientSize.Height / _rows;
        int col = index % _cols;
        int row = index / _cols;
        return new Rectangle(col * cellW, row * cellH, cellW, cellH);
    }

    /// <summary>
    /// 根据屏幕坐标确定鼠标所在的图片索引，-1 表示不在任何图片上
    /// </summary>
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

    /// <summary>
    /// 更新每张图的基础缩放比例，使其各自 fit-to-window
    /// </summary>
    private void UpdateBaseZoom()
    {
        for (int i = 0; i < _imageCount; i++)
        {
            if (_images[i] == null) continue;
            var rect = GetCellRect(i);
            _baseZooms[i] = CalculateFitZoom(_images[i]!, rect);
        }
    }

    /// <summary>
    /// 获取图片的实际缩放 = 基础缩放 × 公共倍率
    /// </summary>
    private float GetEffectiveZoom(int index) => _baseZooms[index] * _zoomLevel;

    private void Form1_Resize(object? sender, EventArgs e)
    {
        UpdateBaseZoom();
        this.Invalidate();
    }

    private void ToggleButton_Click(object sender, EventArgs e)
    {
        _showInstructions = !_showInstructions;
        _toggleButton.Text = _showInstructions ? "▼" : "▶";
        this.Invalidate();
    }

    private void Form1_Paint(object sender, PaintEventArgs e)
    {
        if (_imagePaths == null || _imagePaths.Length == 0)
            return;

        try
        {
            EnsureCheckerBrush();

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
            int cellW = this.ClientSize.Width / _cols;
            int cellH = this.ClientSize.Height / _rows;
            for (int c = 1; c < _cols; c++)
                e.Graphics.DrawLine(pen, c * cellW, 0, c * cellW, this.ClientSize.Height);
            for (int r = 1; r < _rows; r++)
                e.Graphics.DrawLine(pen, 0, r * cellH, this.ClientSize.Width, r * cellH);

            // 按键提示在最上层绘制
            if (_showInstructions)
            {
                DrawKeyInstructions(e.Graphics);
            }
        }
        catch (Exception ex)
        {
            e.Graphics.DrawString($"错误: {ex.Message}", this.Font, Brushes.Red, 10, 10);
        }
    }

    private void DrawKeyInstructions(Graphics g)
    {
        string[] instructions = {
            "按键说明:",
            "- 鼠标左键拖动: 同步移动所有图片",
            "- 滚轮: 上下移动图片",
            "- Ctrl+滚轮: 左右移动图片",
            "- Alt+滚轮: 以鼠标指针为中心缩放图片"
        };

        float boxWidth = 320f;
        float boxHeight = instructions.Length * 20 + 10;
        using (var bgBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
        {
            g.FillRectangle(bgBrush, 5, 35, boxWidth, boxHeight);
        }

        for (int i = 0; i < instructions.Length; i++)
        {
            g.DrawString(instructions[i], this.Font, Brushes.Black, 10, 40 + i * 20);
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

        // 缩小较多时使用低质量插值以提升性能，避免小图拖动卡顿
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
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _lastMousePos = e.Location;
        }
    }

    private void Form1_MouseMove(object sender, MouseEventArgs e)
    {
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

    private void Form1_MouseUp(object sender, MouseEventArgs e)
    {
        _isDragging = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Alt || keyData == (Keys.Alt | Keys.Menu))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// 从屏幕坐标反算指针指向图片的归一化坐标（0~1 比例位置）
    /// </summary>
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

    /// <summary>
    /// 以归一化坐标（0~1 比例位置）为缩放中心，缩放后该比例位置的屏幕位置严格不变
    /// </summary>
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

    /// <summary>
    /// 以归一化坐标为缩放中心缩放，并将该点移动到目标屏幕位置（用于被动图同步缩放中心位置）
    /// </summary>
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

    /// <summary>
    /// 首次显示时自动计算初始缩放，使图片都能完整显示
    /// </summary>
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
            // Ctrl+滚轮：左右移动
            float avgZoom = _baseZooms.Where(z => z > 0).DefaultIfEmpty(1f).Average() * _zoomLevel;
            float step = this.ClientSize.Width * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? -step : step;
            for (int i = 0; i < _imageCount; i++)
                _offsets[i] = new PointF(_offsets[i].X + delta, _offsets[i].Y);
        }
        else if (IsAltPressed())
        {
            // Alt+滚轮：以鼠标指针为中心缩放
            float zoomFactor = e.Delta > 0 ? 1.25f : 1f / 1.25f;
            float oldZoomLevel = _zoomLevel;
            float newZoomLevel = _zoomLevel * zoomFactor;

            if (newZoomLevel < 0.01f || newZoomLevel > 100f)
                return;

            PointF mousePos = e.Location;
            int activeIdx = HitTest(mousePos);
            if (activeIdx < 0 || _images[activeIdx] == null)
            {
                _zoomLevel = newZoomLevel;
                this.Invalidate();
                return;
            }

            Rectangle activeRect = GetCellRect(activeIdx);
            Size activeImgSize = _images[activeIdx]!.Size;
            float oldEffActive = _baseZooms[activeIdx] * oldZoomLevel;
            float newEffActive = _baseZooms[activeIdx] * newZoomLevel;

            // 主动图：以鼠标位置为中心缩放，不移动
            PointF norm = ScreenToNormalized(mousePos, activeRect, oldEffActive, _offsets[activeIdx], activeImgSize);
            _offsets[activeIdx] = ZoomAtNormalized(norm, activeRect, oldEffActive, newEffActive, _offsets[activeIdx], activeImgSize);

            // 鼠标在主动图面板中的局部坐标
            float activeLocalX = mousePos.X - activeRect.Left;
            float activeLocalY = mousePos.Y - activeRect.Top;

            // 被动图：以相同归一化坐标缩放，并将缩放中心移到对应位置
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
            // 单独滚轮：上下移动
            float avgZoom = _baseZooms.Where(z => z > 0).DefaultIfEmpty(1f).Average() * _zoomLevel;
            float step = this.ClientSize.Height * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? -step : step;
            for (int i = 0; i < _imageCount; i++)
                _offsets[i] = new PointF(_offsets[i].X, _offsets[i].Y + delta);
        }

        this.Invalidate();
    }
}
