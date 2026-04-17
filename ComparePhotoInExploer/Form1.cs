namespace ComparePhotoInExploer;

public partial class Form1 : Form
{
    private string[] _imagePaths;
    private bool _isDragging = false;
    private Point _lastMousePos;
    private float _zoomLevel = 1.0f;
    private PointF _offset1 = new PointF(0, 0);
    private PointF _offset2 = new PointF(0, 0);
    private bool _showInstructions = false;
    private Button _toggleButton;

    // 缓存图片，避免每次 Paint 从磁盘重新加载
    private Image? _image1;
    private Image? _image2;

    // 每张图独立的基础缩放比例（fit-to-window），_zoomLevel 作为公共相对倍率
    private float _baseZoom1 = 1.0f;
    private float _baseZoom2 = 1.0f;

    public Form1(string[] imagePaths)
    {
        InitializeComponent();
        _imagePaths = imagePaths;
        this.Text = "图片对比";
        this.Size = new Size(1000, 600);
        this.DoubleBuffered = true;

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

    private void LoadImages()
    {
        if (_imagePaths == null || _imagePaths.Length < 2)
            return;

        try
        {
            _image1?.Dispose();
            _image2?.Dispose();

            using var fs1 = new FileStream(_imagePaths[0], FileMode.Open, FileAccess.Read);
            _image1 = Image.FromStream(fs1);

            using var fs2 = new FileStream(_imagePaths[1], FileMode.Open, FileAccess.Read);
            _image2 = Image.FromStream(fs2);
        }
        catch
        {
            // 加载失败时保持为 null
        }
    }

    /// <summary>
    /// 计算使图片适配绘制区域（保持宽高比）的缩放比例
    /// </summary>
    private float CalculateFitZoom(Image image, Rectangle drawArea)
    {
        if (image == null) return 1.0f;
        float scaleX = (float)drawArea.Width / image.Width;
        float scaleY = (float)drawArea.Height / image.Height;
        return Math.Min(scaleX, scaleY);
    }

    /// <summary>
    /// 更新两张图的基础缩放比例，使其各自 fit-to-window，同比例图片显示相同大小
    /// </summary>
    private void UpdateBaseZoom()
    {
        if (_image1 == null || _image2 == null)
            return;

        int halfWidth = this.ClientSize.Width / 2;
        int height = this.ClientSize.Height;

        var rect1 = new Rectangle(0, 0, halfWidth, height);
        var rect2 = new Rectangle(halfWidth, 0, halfWidth, height);

        _baseZoom1 = CalculateFitZoom(_image1, rect1);
        _baseZoom2 = CalculateFitZoom(_image2, rect2);
    }

    /// <summary>
    /// 获取图片的实际缩放 = 基础缩放 × 公共倍率
    /// </summary>
    private float GetEffectiveZoom(bool isLeftImage) => isLeftImage ? _baseZoom1 * _zoomLevel : _baseZoom2 * _zoomLevel;

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
        if (_imagePaths == null || _imagePaths.Length < 2)
            return;

        try
        {
            if (_image1 == null || _image2 == null)
            {
                e.Graphics.DrawString("图片加载失败", this.Font, Brushes.Red, 10, 10);
                return;
            }

            int halfWidth = this.ClientSize.Width / 2;
            int height = this.ClientSize.Height;

            // 全局绘制棋盘格背景
            EnsureCheckerBrush();
            e.Graphics.FillRectangle(_checkerBrush!, 0, 0, halfWidth, height);
            e.Graphics.FillRectangle(_checkerBrush!, halfWidth, 0, halfWidth, height);

            // 每张图使用独立的实际缩放
            Rectangle rect1 = new Rectangle(0, 0, halfWidth, height);
            DrawImage(e.Graphics, _image1, rect1, _offset1, GetEffectiveZoom(true), true);

            Rectangle rect2 = new Rectangle(halfWidth, 0, halfWidth, height);
            DrawImage(e.Graphics, _image2, rect2, _offset2, GetEffectiveZoom(false), false);

            using var pen = new Pen(Color.Black, 2);
            e.Graphics.DrawLine(pen, halfWidth, 0, halfWidth, height);

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
            "- 鼠标左键拖动: 同步移动两张图片",
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

    private void DrawImage(Graphics g, Image image, Rectangle drawArea, PointF offset, float zoom, bool isLeftImage)
    {
        float scaledWidth = image.Width * zoom;
        float scaledHeight = image.Height * zoom;

        float imgX = drawArea.Left + (drawArea.Width - scaledWidth) / 2f + offset.X;
        float imgY = drawArea.Top + (drawArea.Height - scaledHeight) / 2f + offset.Y;

        int halfWidth = this.ClientSize.Width / 2;
        int height = this.ClientSize.Height;

        float visLeft = Math.Max(imgX, drawArea.Left);
        float visTop = Math.Max(imgY, drawArea.Top);
        float visRight = Math.Min(imgX + scaledWidth, drawArea.Right);
        float visBottom = Math.Min(imgY + scaledHeight, drawArea.Bottom);

        if (isLeftImage && visRight > halfWidth)
            visRight = halfWidth;
        if (!isLeftImage && visLeft < halfWidth)
            visLeft = halfWidth;

        float visWidth = visRight - visLeft;
        float visHeight = visBottom - visTop;

        if (visWidth <= 0 || visHeight <= 0)
            return;

        g.SetClip(isLeftImage
            ? new Rectangle(0, 0, halfWidth, height)
            : new Rectangle(halfWidth, 0, halfWidth, height));

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

            _offset1.X += deltaX;
            _offset1.Y += deltaY;
            _offset2.X += deltaX;
            _offset2.Y += deltaY;

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
    private PointF ScreenToNormalized(PointF screenPos, Rectangle drawArea, float zoom, PointF offset, Size imageSize)
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
    private PointF ZoomAtNormalized(PointF norm, Rectangle drawArea, float oldZoom, float newZoom, PointF oldOffset, Size imageSize)
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

    private bool IsAltPressed()
    {
        return (ModifierKeys & Keys.Alt) == Keys.Alt;
    }

    /// <summary>
    /// 首次显示时自动计算初始缩放，使两张图片都能完整显示
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_image1 != null && _image2 != null)
        {
            UpdateBaseZoom();
            _zoomLevel = 1.0f;
            _offset1 = new PointF(0, 0);
            _offset2 = new PointF(0, 0);
            this.Invalidate();
        }
    }

    private void Form1_MouseWheel(object sender, MouseEventArgs e)
    {
        if (ModifierKeys == Keys.Control)
        {
            // Ctrl+滚轮：左右移动，步长按当前显示大小的比例计算
            float avgZoom = (_baseZoom1 + _baseZoom2) / 2f * _zoomLevel;
            float step = this.ClientSize.Width * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? -step : step;
            _offset1.X += delta;
            _offset2.X += delta;
        }
        else if (IsAltPressed())
        {
            // Alt+滚轮：以鼠标指针为中心缩放（使用归一化坐标同步，确保同比例图片缩放一致）
            float zoomFactor = e.Delta > 0 ? 1.25f : 1f / 1.25f;
            float oldZoomLevel = _zoomLevel;
            float newZoomLevel = _zoomLevel * zoomFactor;

            if (newZoomLevel < 0.01f || newZoomLevel > 100f)
                return;

            PointF mousePos = e.Location;
            int halfWidth = this.ClientSize.Width / 2;
            int height = this.ClientSize.Height;

            Rectangle rect1 = new Rectangle(0, 0, halfWidth, height);
            Rectangle rect2 = new Rectangle(halfWidth, 0, halfWidth, height);

            Size imgSize1 = _image1?.Size ?? Size.Empty;
            Size imgSize2 = _image2?.Size ?? Size.Empty;

            float oldEff1 = _baseZoom1 * oldZoomLevel, newEff1 = _baseZoom1 * newZoomLevel;
            float oldEff2 = _baseZoom2 * oldZoomLevel, newEff2 = _baseZoom2 * newZoomLevel;

            if (mousePos.X < halfWidth)
            {
                // 鼠标在左侧：从左侧图算出归一化坐标，两张图用同一归一化坐标缩放
                PointF norm = ScreenToNormalized(mousePos, rect1, oldEff1, _offset1, imgSize1);
                _offset1 = ZoomAtNormalized(norm, rect1, oldEff1, newEff1, _offset1, imgSize1);
                _offset2 = ZoomAtNormalized(norm, rect2, oldEff2, newEff2, _offset2, imgSize2);
            }
            else
            {
                // 鼠标在右侧：从右侧图算出归一化坐标，两张图用同一归一化坐标缩放
                PointF norm = ScreenToNormalized(mousePos, rect2, oldEff2, _offset2, imgSize2);
                _offset2 = ZoomAtNormalized(norm, rect2, oldEff2, newEff2, _offset2, imgSize2);
                _offset1 = ZoomAtNormalized(norm, rect1, oldEff1, newEff1, _offset1, imgSize1);
            }

            _zoomLevel = newZoomLevel;
        }
        else
        {
            // 单独滚轮：上下移动，步长按当前显示大小的比例计算
            float avgZoom = (_baseZoom1 + _baseZoom2) / 2f * _zoomLevel;
            float step = this.ClientSize.Height * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? -step : step;
            _offset1.Y += delta;
            _offset2.Y += delta;
        }

        this.Invalidate();
    }
}
