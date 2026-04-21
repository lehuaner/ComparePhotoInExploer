namespace ComparePhotoInExploer;

public partial class Form1
{
    // 自绘标题栏
    public const int TitleBarHeight = 32;
    private Rectangle _btnMin, _btnMax, _btnClose, _btnHelp, _btnHistory, _btnTheme, _btnReset, _btnSyncZoom, _btnZoomHelp, _btnRightClickMenu, _btnSyncMove;
    private bool _hoverMin, _hoverMax, _hoverClose, _hoverHelp, _hoverHistory, _hoverTheme, _hoverReset, _hoverSyncZoom, _hoverZoomHelp, _hoverRightClickMenu, _hoverSyncMove;

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

        // 同步缩放模式按钮
        btnX += themeBtnW + 2;
        int syncZoomBtnW = 80;
        _btnSyncZoom = new Rectangle(btnX, 0, syncZoomBtnW, TitleBarHeight);
        bool syncZoomActive = _syncZoomMode == SyncZoomMode.SyncAlign;
        Color syncZoomBg = syncZoomActive ? _colors.TitleBarBtnActiveBg :
                           _hoverSyncZoom ? _colors.TitleBarBtnHoverBg : Color.Transparent;
        using (var syncZoomBgBrush = new SolidBrush(syncZoomBg))
            g.FillRectangle(syncZoomBgBrush, _btnSyncZoom);
        using var syncZoomFont = new Font("Microsoft YaHei UI", 9F);
        using var syncZoomFgBrush = new SolidBrush(_colors.TitleBarFg);
        string syncZoomLabel = _syncZoomMode == SyncZoomMode.SyncAlign ? "同步对齐" : "独立缩放";
        var syncZoomLabelSize = g.MeasureString(syncZoomLabel, syncZoomFont);
        g.DrawString(syncZoomLabel, syncZoomFont, syncZoomFgBrush,
            _btnSyncZoom.Left + (_btnSyncZoom.Width - syncZoomLabelSize.Width) / 2,
            _btnSyncZoom.Top + (_btnSyncZoom.Height - syncZoomLabelSize.Height) / 2);

        // 同步移动模式按钮
        btnX += syncZoomBtnW + 2;
        int syncMoveBtnW = 96;
        _btnSyncMove = new Rectangle(btnX, 0, syncMoveBtnW, TitleBarHeight);
        bool syncMoveActive = _syncMoveMode == SyncMoveMode.EnableAll;
        Color syncMoveBg = syncMoveActive ? _colors.TitleBarBtnActiveBg :
                           _hoverSyncMove ? _colors.TitleBarBtnHoverBg : Color.Transparent;
        using (var syncMoveBgBrush = new SolidBrush(syncMoveBg))
            g.FillRectangle(syncMoveBgBrush, _btnSyncMove);
        using var syncMoveFont = new Font("Microsoft YaHei UI", 9F);
        using var syncMoveFgBrush = new SolidBrush(_colors.TitleBarFg);
        string syncMoveLabel = _syncMoveMode switch
        {
            SyncMoveMode.DisableSyncZoom => "关闭同步缩放",
            SyncMoveMode.DisableSyncMove => "关闭同步移动",
            SyncMoveMode.DisableAll => "同时关闭",
            _ => "同时开启"
        };
        var syncMoveLabelSize = g.MeasureString(syncMoveLabel, syncMoveFont);
        g.DrawString(syncMoveLabel, syncMoveFont, syncMoveFgBrush,
            _btnSyncMove.Left + (_btnSyncMove.Width - syncMoveLabelSize.Width) / 2,
            _btnSyncMove.Top + (_btnSyncMove.Height - syncMoveLabelSize.Height) / 2);

        // 缩放说明按钮（小问号）
        btnX += syncMoveBtnW + 1;
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

        // 右键菜单单选框
        btnX += 28 + 10;
        int rightClickMenuBtnW = 120;
        _btnRightClickMenu = new Rectangle(btnX, 0, rightClickMenuBtnW, TitleBarHeight);
        Color rightClickMenuBg = _hoverRightClickMenu ? _colors.TitleBarBtnHoverBg : Color.Transparent;
        using (var rightClickMenuBgBrush = new SolidBrush(rightClickMenuBg))
            g.FillRectangle(rightClickMenuBgBrush, _btnRightClickMenu);
        
        // 绘制单选框
        int checkBoxSize = 16;
        int checkBoxX = _btnRightClickMenu.Left + 8;
        int checkBoxY = (_btnRightClickMenu.Height - checkBoxSize) / 2;
        Rectangle checkBoxRect = new Rectangle(checkBoxX, checkBoxY, checkBoxSize, checkBoxSize);
        
        // 绘制单选框边框
        using var checkBoxPen = new Pen(_colors.TitleBarFg, 1);
        g.DrawEllipse(checkBoxPen, checkBoxRect);
        
        // 绘制单选框内部（选中时）
        if (_rightClickMenuEnabled)
        {
            int innerCircleSize = 8;
            int innerCircleX = checkBoxX + (checkBoxSize - innerCircleSize) / 2;
            int innerCircleY = checkBoxY + (checkBoxSize - innerCircleSize) / 2;
            using var checkBoxInnerBrush = new SolidBrush(_colors.TitleBarFg);
            g.FillEllipse(checkBoxInnerBrush, innerCircleX, innerCircleY, innerCircleSize, innerCircleSize);
        }
        
        // 绘制文本
        using var rightClickMenuFont = new Font("Microsoft YaHei UI", 9F);
        using var rightClickMenuFgBrush = new SolidBrush(_colors.TitleBarFg);
        string rightClickMenuLabel = "添加到右键菜单";
        var rightClickMenuLabelSize = g.MeasureString(rightClickMenuLabel, rightClickMenuFont);
        g.DrawString(rightClickMenuLabel, rightClickMenuFont, rightClickMenuFgBrush,
            checkBoxX + checkBoxSize + 8,
            _btnRightClickMenu.Top + (_btnRightClickMenu.Height - rightClickMenuLabelSize.Height) / 2);

        // 重置偏移按钮（仅在有偏移时显示）
        btnX += rightClickMenuBtnW + 2;
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
}
