namespace ComparePhotoInExploer;

/// <summary>
/// 应用设置持久化（主题、右键菜单开关、窗口位置与大小）
/// </summary>
public static class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "Local", "ComparePhotoInExploer");

    private static readonly string ThemeSettingFile = Path.Combine(SettingsDir, "theme.txt");
    private static readonly string WindowStateFile = Path.Combine(SettingsDir, "windowstate.txt");
    private static readonly string RightClickMenuSettingFile = Path.Combine(SettingsDir, "rightclickmenu.txt");

    #region 主题设置

    /// <summary>
    /// 加载主题设置，默认跟随系统
    /// </summary>
    public static AppTheme LoadThemeSetting()
    {
        try
        {
            if (File.Exists(ThemeSettingFile))
            {
                var text = File.ReadAllText(ThemeSettingFile).Trim();
                if (Enum.TryParse<AppTheme>(text, out var theme))
                    return theme;
            }
        }
        catch { }
        return AppTheme.System;
    }

    /// <summary>
    /// 保存主题设置
    /// </summary>
    public static void SaveThemeSetting(AppTheme theme)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(ThemeSettingFile, theme.ToString());
        }
        catch { }
    }

    #endregion

    #region 右键菜单设置

    /// <summary>
    /// 加载右键菜单开关设置，默认开启
    /// </summary>
    public static bool LoadRightClickMenuSetting()
    {
        try
        {
            if (File.Exists(RightClickMenuSettingFile))
            {
                var text = File.ReadAllText(RightClickMenuSettingFile).Trim();
                if (bool.TryParse(text, out var enabled))
                    return enabled;
            }
        }
        catch { }
        return true;
    }

    /// <summary>
    /// 保存右键菜单开关设置
    /// </summary>
    public static void SaveRightClickMenuSetting(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(RightClickMenuSettingFile, enabled.ToString());
        }
        catch { }
    }

    #endregion

    #region 窗口状态

    /// <summary>
    /// 保存窗口位置和大小
    /// </summary>
    public static void SaveWindowState(bool isMaximized, Rectangle bounds)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllLines(WindowStateFile, new[]
            {
                isMaximized ? "1" : "0",
                bounds.X.ToString(),
                bounds.Y.ToString(),
                bounds.Width.ToString(),
                bounds.Height.ToString()
            });
        }
        catch { }
    }

    /// <summary>
    /// 加载窗口位置和大小，返回 null 表示使用默认值
    /// </summary>
    public static (bool maximized, Rectangle bounds)? LoadWindowState()
    {
        try
        {
            if (!File.Exists(WindowStateFile)) return null;
            var lines = File.ReadAllLines(WindowStateFile);
            if (lines.Length < 5) return null;

            bool maximized = lines[0].Trim() == "1";
            int x = int.Parse(lines[1].Trim());
            int y = int.Parse(lines[2].Trim());
            int w = int.Parse(lines[3].Trim());
            int h = int.Parse(lines[4].Trim());

            // 确保窗口在可见屏幕范围内
            var screen = Screen.FromPoint(new Point(x, y));
            if (screen != null)
            {
                var bounds = screen.Bounds;
                if (x < bounds.Left - w || x > bounds.Right || y < bounds.Top - h || y > bounds.Bottom)
                    return null;
            }

            return (maximized, new Rectangle(x, y, Math.Max(w, 400), Math.Max(h, 300)));
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
