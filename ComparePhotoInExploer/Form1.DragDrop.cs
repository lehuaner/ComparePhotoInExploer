namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的拖放支持逻辑
/// </summary>
public partial class Form1
{
    private void Form1_DragEnter(object? sender, DragEventArgs e)
    {
        if (FileDropHelper.ExtractImageFiles(e.Data) != null)
        {
            e.Effect = DragDropEffects.Copy;
            _isDragOver = true;
            this.Invalidate();
            return;
        }
        e.Effect = DragDropEffects.None;
    }

    private void Form1_DragOver(object? sender, DragEventArgs e)
    {
        if (FileDropHelper.ExtractImageFiles(e.Data) != null)
        {
            e.Effect = DragDropEffects.Copy;
            return;
        }
        e.Effect = DragDropEffects.None;
    }

    private void Form1_DragLeave(object? sender, EventArgs e)
    {
        _isDragOver = false;
        this.Invalidate();
    }

    private void Form1_DragDrop(object? sender, DragEventArgs e)
    {
        _isDragOver = false;

        var imageFiles = FileDropHelper.ExtractImageFiles(e.Data);
        if (imageFiles == null) return;

        // 清空当前对比并加载新图片组
        LoadNewGroup(imageFiles);
        SaveCurrentToHistory();
    }
}
