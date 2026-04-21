namespace ComparePhotoInExploer;

/// <summary>
/// 主题适配的自定义消息框
/// </summary>
public class CustomMessageBox : Form
{
    private readonly ThemeColorSet _colors;
    private readonly string _message;
    private readonly string _title;
    private readonly MessageBoxButtons _buttons;
    private DialogResult _result = DialogResult.None;

    private const int TitleBarH = 32;
    private Rectangle _btnClose;
    private Rectangle _btnYes, _btnNo;
    private bool _hoverClose, _hoverYes, _hoverNo;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    public CustomMessageBox(string message, string title, MessageBoxButtons buttons, ThemeColorSet colors)
    {
        _message = message;
        _title = title;
        _buttons = buttons;
        _colors = colors;

        this.FormBorderStyle = FormBorderStyle.None;
        this.DoubleBuffered = true;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.KeyPreview = true;

        // 计算尺寸
        using var measureG = this.CreateGraphics();
        using var msgFont = new Font("Microsoft YaHei UI", 9F);
        var msgSize = measureG.MeasureString(_message, msgFont, 360);
        int contentW = (int)Math.Max(300, Math.Min(500, msgSize.Width + 60));
        int contentH = (int)msgSize.Height + TitleBarH + 70;
        this.ClientSize = new Size(contentW, contentH);

        this.BackColor = _colors.DialogBg;

        this.Paint += CustomMessageBox_Paint;
        this.MouseDown += CustomMessageBox_MouseDown;
        this.MouseMove += CustomMessageBox_MouseMove;
        this.MouseUp += CustomMessageBox_MouseUp;
        this.KeyDown += CustomMessageBox_KeyDown;
    }

    private void CustomMessageBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            _result = DialogResult.Cancel;
            this.Close();
        }
        else if (e.KeyCode == Keys.Enter)
        {
            _result = _buttons == MessageBoxButtons.YesNo ? DialogResult.Yes : DialogResult.OK;
            this.Close();
        }
    }

    public new DialogResult ShowDialog(IWin32Window? owner = null)
    {
        base.ShowDialog(owner);
        return _result;
    }

    private void CustomMessageBox_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = this.ClientSize.Width;
        int h = this.ClientSize.Height;

        // 边框
        using var borderPen = new Pen(_colors.DialogBorder, 1);
        g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);

        // 标题栏背景
        using var titleBgBrush = new SolidBrush(_colors.TitleBarBg);
        g.FillRectangle(titleBgBrush, 0, 0, w, TitleBarH);

        // 标题文字
        using var titleFont = new Font("Microsoft YaHei UI", 9F);
        using var titleFgBrush = new SolidBrush(_colors.TitleBarFg);
        var titleSize = g.MeasureString(_title, titleFont);
        g.DrawString(_title, titleFont, titleFgBrush,
            (w - titleSize.Width) / 2,
            (TitleBarH - titleSize.Height) / 2);

        // 关闭按钮
        _btnClose = new Rectangle(w - 46, 0, 46, TitleBarH);
        Color closeBg = _hoverClose ? _colors.TitleBarCloseHoverBg : Color.Transparent;
        Color closeFg = _hoverClose ? Color.White : _colors.TitleBarBtnFg;
        using (var closeBgBrush = new SolidBrush(closeBg))
            g.FillRectangle(closeBgBrush, _btnClose);
        using var closeFont = new Font("Segoe UI", 9F);
        using var closeFgBrush = new SolidBrush(closeFg);
        var closeSize = g.MeasureString("✕", closeFont);
        g.DrawString("✕", closeFont, closeFgBrush,
            _btnClose.Left + (_btnClose.Width - closeSize.Width) / 2,
            _btnClose.Top + (_btnClose.Height - closeSize.Height) / 2);

        // 消息内容
        using var msgFont = new Font("Microsoft YaHei UI", 9F);
        using var msgFgBrush = new SolidBrush(_colors.DialogFg);
        var msgRect = new RectangleF(20, TitleBarH + 16, w - 40, h - TitleBarH - 70);
        g.DrawString(_message, msgFont, msgFgBrush, msgRect);

        // 按钮
        int btnW = 80;
        int btnH = 30;
        int btnY = h - 16 - btnH;

        if (_buttons == MessageBoxButtons.YesNo)
        {
            _btnNo = new Rectangle(w - 20 - btnW, btnY, btnW, btnH);
            _btnYes = new Rectangle(_btnNo.Left - 10 - btnW, btnY, btnW, btnH);
        }
        else
        {
            _btnYes = new Rectangle(w - 20 - btnW, btnY, btnW, btnH);
            _btnNo = Rectangle.Empty;
        }

        if (_buttons == MessageBoxButtons.YesNo)
        {
            DrawDialogButton(g, _btnNo, "否", _hoverNo);
        }
        DrawDialogButton(g, _btnYes,
            _buttons == MessageBoxButtons.YesNo ? "是" : "确定",
            _hoverYes);
    }

    private void DrawDialogButton(Graphics g, Rectangle rect, string text, bool hover)
    {
        Color bg = hover ? _colors.DialogBtnHoverBg : _colors.DialogBtnBg;
        Color fg = _colors.DialogBtnFg;

        using var bgBrush = new SolidBrush(bg);
        g.FillRectangle(bgBrush, rect);
        using var borderPen = new Pen(_colors.DialogBorder, 1);
        g.DrawRectangle(borderPen, rect);

        using var font = new Font("Microsoft YaHei UI", 9F);
        using var fgBrush = new SolidBrush(fg);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, fgBrush,
            rect.Left + (rect.Width - size.Width) / 2,
            rect.Top + (rect.Height - size.Height) / 2);
    }

    private void CustomMessageBox_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Location.Y < TitleBarH)
        {
            if (_btnClose.Contains(e.Location))
            {
                _result = DialogResult.Cancel;
                this.Close();
                return;
            }
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            if (_btnYes.Contains(e.Location))
            {
                _result = _buttons == MessageBoxButtons.YesNo ? DialogResult.Yes : DialogResult.OK;
                this.Close();
                return;
            }
            if (_btnNo.Contains(e.Location))
            {
                _result = DialogResult.No;
                this.Close();
                return;
            }
        }
    }

    private void CustomMessageBox_MouseMove(object? sender, MouseEventArgs e)
    {
        bool newHoverClose = _btnClose.Contains(e.Location);
        bool newHoverYes = _btnYes.Contains(e.Location);
        bool newHoverNo = _btnNo.Contains(e.Location);

        if (newHoverClose != _hoverClose || newHoverYes != _hoverYes || newHoverNo != _hoverNo)
        {
            _hoverClose = newHoverClose;
            _hoverYes = newHoverYes;
            _hoverNo = newHoverNo;
            this.Invalidate();
        }
    }

    private void CustomMessageBox_MouseUp(object? sender, MouseEventArgs e)
    {
    }
}

/// <summary>
/// 便捷调用
/// </summary>
public static class ThemedMessageBox
{
    public static DialogResult Show(IWin32Window? owner, string message, string title, MessageBoxButtons buttons, ThemeColorSet colors)
    {
        using var box = new CustomMessageBox(message, title, buttons, colors);
        return box.ShowDialog(owner);
    }
}
