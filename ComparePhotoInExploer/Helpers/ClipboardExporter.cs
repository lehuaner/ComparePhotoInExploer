using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Text;

namespace ComparePhotoInExploer;

/// <summary>
/// 剪贴板导出（一份图文并帽，微信与 WPS/Word 都能粘贴出图+字）：
/// CF_HTML(HTML Format，图片用 data-uri 内嵌) + RTF(内嵌 PNG，Word/WPS 备用) + 纯文本。
/// 关键：所有格式用 autoConvert=false 写入，否则 HTML 的 byte[] 会被强转成 "System.Byte[]"。
/// 不写 Bitmap/CF_DIB——微信会优先取位图而导致"只剩图片、丢文字"。
/// </summary>
public static class ClipboardExporter
{
    public record struct RtfImage(string Name, byte[] Png, int W, int H);

    /// <summary>写入剪贴板（原生 Win32）：CF_HTML(图文) + RTF(图文) + 纯文本</summary>
    public static void WriteCopy(string plainText, List<(string name, Bitmap bmp)> images)
    {
        images.RemoveAll(x => x.bmp == null);
        var enc = new UTF8Encoding(false);

        var rtfs = new List<RtfImage>();
        foreach (var (name, bmp) in images)
            rtfs.Add(new RtfImage(name, ToPngBytes(bmp), bmp.Width, bmp.Height));

        byte[] html = BuildCfHtml(BuildHtmlFragment(plainText, rtfs), enc);
        string rtf = BuildRtf(plainText, rtfs);

        // 不用 WinForms Clipboard/DataObject（会把 HTML 的 byte[] 误转成 "System.Byte[]"），直接写原生字节
        NativeClipboard.SetHtmlRtfText(html, rtf, plainText);
    }

    /// <summary>把位图编码为 PNG 字节</summary>
    public static byte[] ToPngBytes(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    #region HTML

    private static string BuildHtmlFragment(string text, List<RtfImage> images)
    {
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:'Microsoft YaHei UI',Segoe UI,sans-serif;font-size:13px;color:#222;\">");
        sb.Append("<pre style=\"background:#f5f5f5;border:1px solid #ddd;padding:8px;white-space:pre-wrap;\">")
          .Append(WebUtility.HtmlEncode(text ?? "")).Append("</pre>");
        foreach (var img in images)
        {
            string uri = "data:image/png;base64," + Convert.ToBase64String(img.Png);
            sb.Append("<div style=\"margin-top:10px;\">")
              .Append(WebUtility.HtmlEncode(img.Name)).Append("<br/>")
              .Append("<img src=\"").Append(uri)
              .Append("\" width=\"").Append(img.W).Append("\" height=\"").Append(img.H)
              .Append("\" style=\"max-width:600px;height:auto;\"/>")
              .Append("</div>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static byte[] BuildCfHtml(string inner, UTF8Encoding enc)
    {
        const string pre = "<html>\r\n<body>\r\n<!--StartFragment-->";
        const string post = "<!--EndFragment-->\r\n</body>\r\n</html>";
        const string headerFmt =
            "Version:0.9\r\n" +
            "StartHTML:{0}\r\n" +
            "EndHTML:{1}\r\n" +
            "StartFragment:{2}\r\n" +
            "EndFragment:{3}\r\n";

        int headerLen = enc.GetByteCount(string.Format(headerFmt, "0000000000", "0000000000", "0000000000", "0000000000"));
        int startHtml = headerLen;
        int startFragment = startHtml + enc.GetByteCount(pre);
        int endFragment = startFragment + enc.GetByteCount(inner);
        int endHtml = endFragment + enc.GetByteCount(post);

        string header = string.Format(headerFmt,
            startHtml.ToString("D10"), endHtml.ToString("D10"),
            startFragment.ToString("D10"), endFragment.ToString("D10"));

        return enc.GetBytes(header + pre + inner + post);
    }

    #endregion

    #region RTF

    private static string BuildRtf(string text, List<RtfImage> images)
    {
        var sb = new StringBuilder();
        sb.Append("{\\rtf1\\ansi\\ansicpg936\\deff0\\deflang1033");
        sb.Append("{\\fonttbl{\\f0 Microsoft YaHei UI;}{\\f1 Consolas;}}");
        sb.Append("\\uc1 \r\n");
        sb.Append(@"\pard\f1\fs18 ");
        sb.Append(RtfEscape(text));
        sb.Append("\\par\\par ");

        foreach (var img in images)
        {
            sb.Append(@"\pard\f0 ");
            sb.Append(RtfEscape(img.Name));
            sb.Append("\\par ");
            int wTwips = img.W * 15, hTwips = img.H * 15;
            sb.Append("{\\pict\\pngblip\\picw").Append(wTwips)
              .Append("\\pich").Append(hTwips)
              .Append("\\picwgoal").Append(wTwips)
              .Append("\\pichgoal").Append(hTwips).Append(' ');
            sb.Append(Convert.ToHexString(img.Png).ToLowerInvariant());
            sb.Append("}\\par ");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string RtfEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '{': sb.Append("\\{"); break;
                case '}': sb.Append("\\}"); break;
                case '\r': break;
                case '\n': sb.Append("\\par\r\n"); break;
                default:
                    if (c > 127)
                    {
                        int code = c;
                        if (code > 32767) code -= 65536;
                        sb.Append("\\u").Append(code).Append('?');
                    }
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    #endregion
}
