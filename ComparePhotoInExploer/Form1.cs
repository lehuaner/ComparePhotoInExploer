namespace ComparePhotoInExploer;

public partial class Form1 : Form
{
    private string[] _imagePaths;
    private bool _isDragging = false;
    private Point _lastMousePos;
    private float _zoomLevel = 1.0f;
    private PointF _offset1 = new PointF(0, 0);
    private PointF _offset2 = new PointF(0, 0);
    private bool _showInstructions = true;
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
        _toggleButton.Text = "▼";
        _toggleButton.Size = new Size(30, 30);
        _toggleButton.Location = new Point(10, 10);
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
            g.DrawString(instructions[i], this.Font, Brushes.Black, 10, 10 + i * 20);
        }
    }

    private void DrawImage(Graphics g, Image image, Rectangle drawArea, PointF offset, float zoom, bool isLeftImage)
    {
        float scaledWidth = image.Width * zoom;
        float scaledHeight = image.Height * zoom;

        float x = drawArea.Left + (drawArea.Width - scaledWidth) / 2 + offset.X;
        float y = drawArea.Top + (drawArea.Height - scaledHeight) / 2 + offset.Y;

        // 检查图片是否超过中间线，如果超过则不绘制
        int halfWidth = this.ClientSize.Width / 2;
        if (isLeftImage && x + scaledWidth > halfWidth)
        {
            // 左侧图片超过中间线，只绘制不超过的部分
            float visibleWidth = halfWidth - x;
            if (visibleWidth > 0)
            {
                g.DrawImage(image, x, y, visibleWidth, scaledHeight);
            }
        }
        else if (!isLeftImage && x < halfWidth)
        {
            // 右侧图片超过中间线，只绘制不超过的部分
            float visibleX = halfWidth;
            float visibleWidth = x + scaledWidth - halfWidth;
            if (visibleWidth > 0)
            {
                g.DrawImage(image, visibleX, y, visibleWidth, scaledHeight);
            }
        }
        else
        {
            // 图片未超过中间线，正常绘制
            g.DrawImage(image, x, y, scaledWidth, scaledHeight);
        }
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

    private void Form1_MouseWheel(object sender, MouseEventArgs e)
    {
        if (ModifierKeys == Keys.Control)
        {
            // Ctrl+滚轮：左右移动
            int delta = e.Delta > 0 ? -10 : 10;
            _offset1.X += delta;
            _offset2.X += delta;
        }
        else if (ModifierKeys == Keys.Alt)
        {
            // Alt+滚轮：以鼠标指针为中心缩放
            float zoomFactor = e.Delta > 0 ? 1.1f : 0.9f;
            
            // 计算鼠标在窗口中的相对位置
            Point mousePos = e.Location;
            int halfWidth = this.ClientSize.Width / 2;
            
            // 计算缩放前鼠标在图片上的位置
            PointF mouseInImage1 = new PointF(
                (mousePos.X - halfWidth / 2 - _offset1.X) / _zoomLevel,
                (mousePos.Y - this.ClientSize.Height / 2 - _offset1.Y) / _zoomLevel
            );
            
            PointF mouseInImage2 = new PointF(
                (mousePos.X - halfWidth - halfWidth / 2 - _offset2.X) / _zoomLevel,
                (mousePos.Y - this.ClientSize.Height / 2 - _offset2.Y) / _zoomLevel
            );
            
            // 执行缩放
            _zoomLevel *= zoomFactor;
            
            // 调整偏移量，使鼠标指针位置保持不变
            _offset1.X = mousePos.X - halfWidth / 2 - mouseInImage1.X * _zoomLevel;
            _offset1.Y = mousePos.Y - this.ClientSize.Height / 2 - mouseInImage1.Y * _zoomLevel;
            
            _offset2.X = mousePos.X - halfWidth - halfWidth / 2 - mouseInImage2.X * _zoomLevel;
            _offset2.Y = mousePos.Y - this.ClientSize.Height / 2 - mouseInImage2.Y * _zoomLevel;
        }
        else
        {
            // 单独无快捷键滚轮：上下移动
            int delta = e.Delta > 0 ? -10 : 10;
            _offset1.Y += delta;
            _offset2.Y += delta;
        }

        this.Invalidate();
    }
}
