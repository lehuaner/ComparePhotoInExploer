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
            // 绘制按键说明
            if (_showInstructions)
            {
                DrawKeyInstructions(e.Graphics);
            }

            // 加载图片
            using (Image img1 = Image.FromFile(_imagePaths[0]))
            using (Image img2 = Image.FromFile(_imagePaths[1]))
            {
                // 计算绘制区域
                int halfWidth = this.ClientSize.Width / 2;
                int height = this.ClientSize.Height;

                // 绘制第一张图片
                Rectangle rect1 = new Rectangle(0, 0, halfWidth, height);
                DrawImage(e.Graphics, img1, rect1, _offset1, _zoomLevel, true);

                // 绘制第二张图片
                Rectangle rect2 = new Rectangle(halfWidth, 0, halfWidth, height);
                DrawImage(e.Graphics, img2, rect2, _offset2, _zoomLevel, false);

                // 绘制分隔线
                e.Graphics.DrawLine(Pens.Black, halfWidth, 0, halfWidth, height);
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

        for (int i = 0; i < instructions.Length; i++)
        {
            g.DrawString(instructions[i], this.Font, Brushes.Black, 10, 40 + i * 20);
        }
    }

    private void DrawImage(Graphics g, Image image, Rectangle drawArea, PointF offset, float zoom, bool isLeftImage)
    {
        float scaledWidth = image.Width * zoom;
        float scaledHeight = image.Height * zoom;

        float imgX = drawArea.Left + (drawArea.Width - scaledWidth) / 2f + offset.X;
        float imgY = drawArea.Top + (drawArea.Height - scaledHeight) / 2f + offset.Y;

        int halfWidth = this.ClientSize.Width / 2;
        int height = this.ClientSize.Height;

        // 计算可见区域（图片与drawArea的交集，再裁剪到对应半区）
        float visLeft = Math.Max(imgX, drawArea.Left);
        float visTop = Math.Max(imgY, drawArea.Top);
        float visRight = Math.Min(imgX + scaledWidth, drawArea.Right);
        float visBottom = Math.Min(imgY + scaledHeight, drawArea.Bottom);

        // 左侧图片不能超过中间线
        if (isLeftImage && visRight > halfWidth)
            visRight = halfWidth;
        // 右侧图片不能超过中间线
        if (!isLeftImage && visLeft < halfWidth)
            visLeft = halfWidth;

        float visWidth = visRight - visLeft;
        float visHeight = visBottom - visTop;

        if (visWidth <= 0 || visHeight <= 0)
            return;

        // 设置裁剪区域
        g.SetClip(isLeftImage
            ? new Rectangle(0, 0, halfWidth, height)
            : new Rectangle(halfWidth, 0, halfWidth, height));

        // 计算源矩形：可见区域对应到原图的坐标
        float srcX = (visLeft - imgX) / zoom;
        float srcY = (visTop - imgY) / zoom;
        float srcW = visWidth / zoom;
        float srcH = visHeight / zoom;

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
        // 拦截 Alt 键，防止其激活系统菜单导致滚轮事件异常
        if (keyData == Keys.Alt || keyData == (Keys.Alt | Keys.Menu))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// 从屏幕坐标反算指针指向图片的像素坐标（浮点数，精确到亚像素）
    /// </summary>
    private PointF ScreenToImagePixel(PointF screenPos, Rectangle drawArea, float zoom, PointF offset, Size imageSize)
    {
        float scaledWidth = imageSize.Width * zoom;
        float scaledHeight = imageSize.Height * zoom;
        float imgX = drawArea.Left + (drawArea.Width - scaledWidth) / 2f + offset.X;
        float imgY = drawArea.Top + (drawArea.Height - scaledHeight) / 2f + offset.Y;

        float pxX = (screenPos.X - imgX) / zoom;
        float pxY = (screenPos.Y - imgY) / zoom;
        return new PointF(pxX, pxY);
    }

    /// <summary>
    /// 以图片像素坐标为缩放中心，缩放后该像素的屏幕位置严格不变，按比例计算新偏移
    /// </summary>
    private PointF ZoomAtPixel(PointF pixel, Rectangle drawArea, float oldZoom, float newZoom, PointF oldOffset, Size imageSize)
    {
        float newScaledWidth = imageSize.Width * newZoom;
        float newScaledHeight = imageSize.Height * newZoom;

        // 缩放前该像素在屏幕上的位置
        float oldScaledWidth = imageSize.Width * oldZoom;
        float oldScaledHeight = imageSize.Height * oldZoom;
        float oldScreenX = drawArea.Left + (drawArea.Width - oldScaledWidth) / 2f + oldOffset.X + pixel.X * oldZoom;
        float oldScreenY = drawArea.Top + (drawArea.Height - oldScaledHeight) / 2f + oldOffset.Y + pixel.Y * oldZoom;

        // 缩放后该像素的屏幕位置公式：
        //   screenX = drawArea.Left + (drawArea.Width - newScaledWidth) / 2 + newOffset.X + pixel.X * newZoom
        // 要求 screenX == oldScreenX，解出 newOffset.X
        float newOffsetX = oldScreenX - drawArea.Left - (drawArea.Width - newScaledWidth) / 2f - pixel.X * newZoom;
        float newOffsetY = oldScreenY - drawArea.Top - (drawArea.Height - newScaledHeight) / 2f - pixel.Y * newZoom;

        return new PointF(newOffsetX, newOffsetY);
    }

    /// <summary>
    /// 判断当前是否按住 Alt 键
    /// </summary>
    private bool IsAltPressed()
    {
        return (ModifierKeys & Keys.Alt) == Keys.Alt;
    }

    private void Form1_MouseWheel(object sender, MouseEventArgs e)
    {
        if (ModifierKeys == Keys.Control)
        {
            // Ctrl+滚轮：左右移动
            int delta = e.Delta > 0 ? -10 : 10;
            _offset1.X += delta;
            _offset2.X += delta;
        }
        else if (IsAltPressed())
        {
            // Alt+滚轮：以鼠标指针所指图片像素为中心缩放
            float zoomFactor = e.Delta > 0 ? 1.1f : 0.9f;
            float oldZoom = _zoomLevel;
            float newZoom = _zoomLevel * zoomFactor;

            PointF mousePos = e.Location;
            int halfWidth = this.ClientSize.Width / 2;
            int height = this.ClientSize.Height;

            Rectangle rect1 = new Rectangle(0, 0, halfWidth, height);
            Rectangle rect2 = new Rectangle(halfWidth, 0, halfWidth, height);

            // 加载图片尺寸
            Size imgSize1, imgSize2;
            using (Image img1 = Image.FromFile(_imagePaths[0]))
                imgSize1 = img1.Size;
            using (Image img2 = Image.FromFile(_imagePaths[1]))
                imgSize2 = img2.Size;

            if (mousePos.X < halfWidth)
            {
                // 鼠标在左侧：先从左侧图算出指针指向的像素坐标
                PointF pixel = ScreenToImagePixel(mousePos, rect1, oldZoom, _offset1, imgSize1);
                // 左侧图以该像素为中心缩放
                _offset1 = ZoomAtPixel(pixel, rect1, oldZoom, newZoom, _offset1, imgSize1);
                // 右侧图也以相同的像素坐标为缩放中心（按比例计算）
                _offset2 = ZoomAtPixel(pixel, rect2, oldZoom, newZoom, _offset2, imgSize2);
            }
            else
            {
                // 鼠标在右侧：先从右侧图算出指针指向的像素坐标
                PointF pixel = ScreenToImagePixel(mousePos, rect2, oldZoom, _offset2, imgSize2);
                // 右侧图以该像素为中心缩放
                _offset2 = ZoomAtPixel(pixel, rect2, oldZoom, newZoom, _offset2, imgSize2);
                // 左侧图也以相同的像素坐标为缩放中心（按比例计算）
                _offset1 = ZoomAtPixel(pixel, rect1, oldZoom, newZoom, _offset1, imgSize1);
            }

            _zoomLevel = newZoom;
        }
        else
        {
            // 单独滚轮：上下移动
            int delta = e.Delta > 0 ? -10 : 10;
            _offset1.Y += delta;
            _offset2.Y += delta;
        }

        this.Invalidate();
    }
}
