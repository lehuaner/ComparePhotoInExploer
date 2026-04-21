namespace ComparePhotoInExploer;

public partial class Form1
{
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

    private void DrawHelpPanel(Graphics g)
    {
        string[] instructions = {
            "快捷键说明:",
            "- 鼠标左键拖动: 同步移动所有图片",
            "- Shift+左键拖动: 只移动鼠标所在的那张图片",
            "- Tab+左键拖动: 将图片拖到另一张图片位置互换",
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
            "但不会移动到鼠标屏幕位置。",
            "",
            "同步移动模式说明:",
            "",
            "【关闭同步缩放】",
            "Alt+滚轮缩放时，只缩放鼠标所在的图片。",
            "拖动和滚轮移动行为不变。",
            "",
            "【关闭同步移动】",
            "拖动和滚轮移动时，只移动鼠标所在的图片。",
            "Alt+滚轮缩放行为不变。",
            "",
            "【同时关闭】",
            "同时关闭同步缩放和同步移动。",
            "Alt+滚轮只缩放鼠标所在的图片，",
            "拖动和滚轮只移动鼠标所在的图片。",
            "",
            "【同时开启】",
            "恢复默认：所有操作同步进行。"
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
}
