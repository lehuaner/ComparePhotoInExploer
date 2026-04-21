namespace ComparePhotoInExploer;

/// <summary>
/// 右键菜单注册表管理（安装/卸载"对比图片"右键菜单项）
/// </summary>
public static class RightClickMenuHelper
{
    // 右键菜单注册表路径（HKCU 不需要管理员权限）
    private const string RightClickMenuRegPath = @"Software\Classes\SystemFileAssociations\image\shell\ComparePhotos";

    /// <summary>
    /// 安装右键菜单项
    /// </summary>
    public static void Install()
    {
        try
        {
            string exePath = Application.ExecutablePath;

            // 删除旧项（如果存在）
            using (var baseKey = Microsoft.Win32.Registry.CurrentUser)
            {
                if (baseKey.OpenSubKey(RightClickMenuRegPath) != null)
                    baseKey.DeleteSubKeyTree(RightClickMenuRegPath);
            }

            // 创建注册表项
            using (var regKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RightClickMenuRegPath))
            {
                regKey.SetValue("MUIVerb", "对比图片");
                regKey.SetValue("MultiSelectModel", "Player");
                regKey.SetValue("Icon", $"{exePath},0");
            }

            // 创建 command 子项
            using (var cmdKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RightClickMenuRegPath + @"\command"))
            {
                cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
            }

            // 刷新 Shell 缓存，使右键菜单立即生效
            NativeMethods.SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    /// <summary>
    /// 卸载右键菜单项
    /// </summary>
    public static void Uninstall()
    {
        try
        {
            using (var baseKey = Microsoft.Win32.Registry.CurrentUser)
            {
                if (baseKey.OpenSubKey(RightClickMenuRegPath) != null)
                    baseKey.DeleteSubKeyTree(RightClickMenuRegPath);
            }

            // 刷新 Shell 缓存
            NativeMethods.SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }
}
