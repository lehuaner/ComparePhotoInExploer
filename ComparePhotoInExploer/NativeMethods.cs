using System.Runtime.InteropServices;

namespace ComparePhotoInExploer;

/// <summary>
/// Win32 P/Invoke 声明与原生窗口辅助方法
/// </summary>
public static class NativeMethods
{
    // 拖拽移动窗口
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HTCAPTION = 2;

    // 圆角窗口
    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("gdi32.dll")]
    public static extern int DeleteObject(IntPtr hObject);

    public const int CornerRadius = 12;

    // 监听系统主题变化
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int RegisterWindowMessage(string lpString);

    // Shell 通知刷新
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    // 窗口消息常量
    public const int WM_SYSCOMMAND = 0x0112;
    public const int SC_CLOSE = 0xF060;
    public const int WM_NCHITTEST = 0x0084;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;

    /// <summary>
    /// 为窗口应用圆角区域
    /// </summary>
    public static void ApplyRoundCorner(IntPtr handle, int width, int height, bool isMaximized)
    {
        if (isMaximized)
        {
            SetWindowRgn(handle, IntPtr.Zero, true);
        }
        else
        {
            var rgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, CornerRadius, CornerRadius);
            SetWindowRgn(handle, rgn, true);
        }
    }
}
