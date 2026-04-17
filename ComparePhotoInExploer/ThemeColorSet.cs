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
