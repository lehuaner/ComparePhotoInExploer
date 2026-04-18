namespace ComparePhotoInExploer;

/// <summary>
/// 界面主题模式
/// </summary>
public enum AppTheme
{
    Dark,
    Light,
    System
}

/// <summary>
/// 主题颜色集合
/// </summary>
public class ThemeColorSet
{
    // 标题栏
    public Color TitleBarBg { get; init; }
    public Color TitleBarFg { get; init; }
    public Color TitleBarBtnHoverBg { get; init; }
    public Color TitleBarBtnActiveBg { get; init; }
    public Color TitleBarBtnFg { get; init; }
    public Color TitleBarCloseHoverBg { get; init; }

    // 棋盘格
    public Color CheckerLight { get; init; }
    public Color CheckerDark { get; init; }

    // 网格分割线
    public Color GridLineColor { get; init; }

    // 历史记录覆盖层
    public Color HistoryOverlayBg { get; init; }
    public Color HistoryBorder { get; init; }
    public Color HistoryHoverBg { get; init; }
    public Color HistoryHoverBorder { get; init; }
    public Color HistoryPlaceholderBg { get; init; }

    // 帮助面板
    public Color HelpPanelBg { get; init; }
    public Color HelpPanelBorder { get; init; }
    public Color HelpTitleFg { get; init; }
    public Color HelpTextFg { get; init; }

    // 拖放提示
    public Color DropHintBorder { get; init; }
    public Color DropHintFg { get; init; }

    // 自定义对话框
    public Color DialogBg { get; init; }
    public Color DialogFg { get; init; }
    public Color DialogBorder { get; init; }
    public Color DialogBtnBg { get; init; }
    public Color DialogBtnFg { get; init; }
    public Color DialogBtnHoverBg { get; init; }
    public Color DialogBtnActiveBg { get; init; }

    // 窗口边框
    public Color WindowBorderColor { get; init; }

    // 重置偏移面板
    public Color ResetPanelBg { get; init; }
    public Color ResetPanelBorder { get; init; }
    public Color ResetPanelFg { get; init; }
    public Color ResetPanelSubFg { get; init; }
    public Color ResetCellBg { get; init; }
    public Color ResetCellBorder { get; init; }
    public Color ResetCellHoverBg { get; init; }
    public Color ResetCellSelectedBg { get; init; }
    public Color ResetCellSelectedBorder { get; init; }
    public Color ResetCellHasOffsetMark { get; init; }

    public static ThemeColorSet Dark => new()
    {
        TitleBarBg = Color.FromArgb(32, 32, 32),
        TitleBarFg = Color.FromArgb(200, 200, 200),
        TitleBarBtnHoverBg = Color.FromArgb(62, 62, 62),
        TitleBarBtnActiveBg = Color.FromArgb(62, 62, 62),
        TitleBarBtnFg = Color.FromArgb(200, 200, 200),
        TitleBarCloseHoverBg = Color.FromArgb(232, 17, 35),

        CheckerLight = Color.FromArgb(58, 58, 58),
        CheckerDark = Color.FromArgb(42, 42, 42),

        GridLineColor = Color.FromArgb(60, 60, 60),

        HistoryOverlayBg = Color.FromArgb(200, 24, 24, 24),
        HistoryBorder = Color.FromArgb(80, 80, 80),
        HistoryHoverBg = Color.FromArgb(40, 100, 149, 237),
        HistoryHoverBorder = Color.FromArgb(100, 149, 237),
        HistoryPlaceholderBg = Color.FromArgb(60, 60, 60),

        HelpPanelBg = Color.FromArgb(230, 32, 32, 32),
        HelpPanelBorder = Color.FromArgb(80, 80, 80),
        HelpTitleFg = Color.FromArgb(255, 220, 220, 220),
        HelpTextFg = Color.FromArgb(200, 200, 200),

        DropHintBorder = Color.FromArgb(100, 149, 237),
        DropHintFg = Color.FromArgb(180, 180, 180),

        DialogBg = Color.FromArgb(45, 45, 45),
        DialogFg = Color.FromArgb(220, 220, 220),
        DialogBorder = Color.FromArgb(80, 80, 80),
        DialogBtnBg = Color.FromArgb(62, 62, 62),
        DialogBtnFg = Color.FromArgb(200, 200, 200),
        DialogBtnHoverBg = Color.FromArgb(80, 80, 80),
        DialogBtnActiveBg = Color.FromArgb(100, 149, 237),

        ResetPanelBg = Color.FromArgb(38, 38, 38),
        ResetPanelBorder = Color.FromArgb(70, 70, 70),
        ResetPanelFg = Color.FromArgb(220, 220, 220),
        ResetPanelSubFg = Color.FromArgb(140, 140, 140),
        ResetCellBg = Color.FromArgb(50, 50, 50),
        ResetCellBorder = Color.FromArgb(70, 70, 70),
        ResetCellHoverBg = Color.FromArgb(65, 65, 65),
        ResetCellSelectedBg = Color.FromArgb(40, 90, 160),
        ResetCellSelectedBorder = Color.FromArgb(100, 149, 237),
        ResetCellHasOffsetMark = Color.FromArgb(255, 165, 0),

        WindowBorderColor = Color.FromArgb(80, 80, 80),
    };

    public static ThemeColorSet Light => new()
    {
        TitleBarBg = Color.FromArgb(240, 240, 240),
        TitleBarFg = Color.FromArgb(40, 40, 40),
        TitleBarBtnHoverBg = Color.FromArgb(220, 220, 220),
        TitleBarBtnActiveBg = Color.FromArgb(210, 210, 210),
        TitleBarBtnFg = Color.FromArgb(60, 60, 60),
        TitleBarCloseHoverBg = Color.FromArgb(232, 17, 35),

        CheckerLight = Color.White,
        CheckerDark = Color.LightGray,

        GridLineColor = Color.FromArgb(180, 180, 180),

        HistoryOverlayBg = Color.FromArgb(220, 245, 245, 245),
        HistoryBorder = Color.FromArgb(180, 180, 180),
        HistoryHoverBg = Color.FromArgb(40, 100, 149, 237),
        HistoryHoverBorder = Color.FromArgb(100, 149, 237),
        HistoryPlaceholderBg = Color.FromArgb(230, 230, 230),

        HelpPanelBg = Color.FromArgb(230, 248, 248, 248),
        HelpPanelBorder = Color.FromArgb(180, 180, 180),
        HelpTitleFg = Color.FromArgb(30, 30, 30),
        HelpTextFg = Color.FromArgb(60, 60, 60),

        DropHintBorder = Color.FromArgb(100, 149, 237),
        DropHintFg = Color.FromArgb(100, 100, 100),

        DialogBg = Color.FromArgb(245, 245, 245),
        DialogFg = Color.FromArgb(30, 30, 30),
        DialogBorder = Color.FromArgb(180, 180, 180),
        DialogBtnBg = Color.FromArgb(230, 230, 230),
        DialogBtnFg = Color.FromArgb(40, 40, 40),
        DialogBtnHoverBg = Color.FromArgb(210, 210, 210),
        DialogBtnActiveBg = Color.FromArgb(100, 149, 237),

        ResetPanelBg = Color.FromArgb(242, 242, 242),
        ResetPanelBorder = Color.FromArgb(190, 190, 190),
        ResetPanelFg = Color.FromArgb(30, 30, 30),
        ResetPanelSubFg = Color.FromArgb(120, 120, 120),
        ResetCellBg = Color.FromArgb(255, 255, 255),
        ResetCellBorder = Color.FromArgb(190, 190, 190),
        ResetCellHoverBg = Color.FromArgb(230, 240, 255),
        ResetCellSelectedBg = Color.FromArgb(180, 210, 250),
        ResetCellSelectedBorder = Color.FromArgb(100, 149, 237),
        ResetCellHasOffsetMark = Color.FromArgb(230, 130, 0),

        WindowBorderColor = Color.FromArgb(180, 180, 180),
    };

    public static ThemeColorSet FromTheme(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Dark => Dark,
            AppTheme.Light => Light,
            _ => IsSystemDark() ? Dark : Light
        };
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0;
        }
        catch { }
        return false;
    }
}
