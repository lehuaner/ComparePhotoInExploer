using System.Runtime.InteropServices;
using System.Text;

namespace ComparePhotoInExploer;

/// <summary>
/// 用 Win32 原生 API 写剪贴板，直接放置各格式的原始字节，
/// 绕开 WinForms DataObject 在 .NET10 下把自定义格式的 byte[] 误转成 "System.Byte[]" 的问题。
/// </summary>
internal static class NativeClipboard
{
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint CF_UNICODETEXT = 13;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string lpszFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private static readonly Latin1Encoding Latin1 = new();

    /// <summary>放置 CF_HTML(原始字节) + Rich Text Format + CF_UNICODETEXT</summary>
    public static bool SetHtmlRtfText(byte[] html, string rtf, string text)
    {
        uint cfHtml = RegisterClipboardFormatW("HTML Format");
        uint cfRtf = RegisterClipboardFormatW("Rich Text Format");

        var rtfBytes = Latin1.GetBytes((rtf ?? "") + "\0");
        var textBytes = Encoding.Unicode.GetBytes((text ?? "") + "\0");

        var items = new List<(uint fmt, byte[] data)>();
        if (cfHtml != 0) items.Add((cfHtml, html));
        if (cfRtf != 0) items.Add((cfRtf, rtfBytes));
        items.Add((CF_UNICODETEXT, textBytes));

        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero)) { Thread.Sleep(50); continue; }
            bool ok = true;
            try
            {
                EmptyClipboard();
                foreach (var (fmt, data) in items)
                {
                    IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
                    if (h == IntPtr.Zero) { ok = false; break; }
                    IntPtr p = GlobalLock(h);
                    Marshal.Copy(data, 0, p, data.Length);
                    GlobalUnlock(h);
                    if (SetClipboardData(fmt, h) == IntPtr.Zero)
                    {
                        GlobalFree(h); // 失败才释放；成功时内存归系统所有
                        ok = false;
                    }
                }
            }
            finally { CloseClipboard(); }

            if (ok) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    /// <summary>逐字符 1:1 映射的 Latin1(ISO-8859-1) 编码，保证 RTF 字节不被改写</summary>
    private sealed class Latin1Encoding : Encoding
    {
        public override int GetByteCount(char[] chars, int index, int count) => count;
        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            for (int i = 0; i < charCount; i++)
                bytes[byteIndex + i] = (byte)(chars[charIndex + i] & 0xFF);
            return charCount;
        }
        public override int GetCharCount(byte[] bytes, int index, int count) => count;
        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            for (int i = 0; i < byteCount; i++)
                chars[charIndex + i] = (char)bytes[byteIndex + i];
            return byteCount;
        }
        public override int GetMaxByteCount(int charCount) => charCount;
        public override int GetMaxCharCount(int byteCount) => byteCount;
    }
}
