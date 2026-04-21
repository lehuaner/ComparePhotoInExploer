namespace ComparePhotoInExploer;

/// <summary>
/// 文件拖放辅助：识别图片文件扩展名，判断是否为图片
/// </summary>
public static class FileDropHelper
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp", ".ico", ".svg" };

    /// <summary>
    /// 判断文件是否为支持的图片格式
    /// </summary>
    public static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ImageExtensions.Contains(ext);
    }

    /// <summary>
    /// 从拖放数据中提取图片文件路径，返回 null 表示无有效图片
    /// </summary>
    public static string[]? ExtractImageFiles(System.Windows.Forms.IDataObject? data)
    {
        if (data?.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop) != true)
            return null;

        var files = (string[]?)data.GetData(System.Windows.Forms.DataFormats.FileDrop);
        if (files == null) return null;

        var imageFiles = files.Where(IsImageFile).ToArray();
        return imageFiles.Length > 0 ? imageFiles : null;
    }
}
