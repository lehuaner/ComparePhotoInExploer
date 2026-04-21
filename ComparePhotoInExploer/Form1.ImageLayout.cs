namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的图片加载与布局计算逻辑
/// </summary>
public partial class Form1
{
    /// <summary>
    /// 根据图片数量计算网格布局 (cols, rows)
    /// </summary>
    private static (int cols, int rows) GetGridLayout(int count)
    {
        return count switch
        {
            1 => (1, 1),
            2 => (2, 1),
            3 => (2, 2),
            4 => (2, 2),
            5 => (2, 3),
            6 => (2, 3),
            _ => (3, 3) // 7, 8, 9
        };
    }

    /// <summary>
    /// 加载新的图片组
    /// </summary>
    private void LoadNewGroup(string[] paths)
    {
        // 先清空旧图片
        for (int i = 0; i < _images.Length; i++)
            _images[i]?.Dispose();

        _imagePaths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        _imageCount = Math.Clamp(_imagePaths.Length, 1, 9);
        (_cols, _rows) = GetGridLayout(_imageCount);

        _images = new Image?[_imageCount];
        _baseZooms = new float[_imageCount];
        _offsets = new PointF[_imageCount];
        _manualOffsets = new PointF[_imageCount];
        _zoomLevels = new float[_imageCount];
        for (int i = 0; i < _imageCount; i++)
        {
            _offsets[i] = new PointF(0, 0);
            _manualOffsets[i] = new PointF(0, 0);
            _zoomLevels[i] = 1.0f;
        }

        _zoomLevel = 1.0f;
        _showHelp = false;
        _shiftDragIndex = -1;
        _resetOverlay.Hide();
        InitSplitters();
        ResetSplitters();

        this.Text = $"图片对比 ({_imageCount}张)";

        LoadImages();
        UpdateBaseZoom();
        this.Invalidate();
    }

    private void LoadImages()
    {
        if (_imagePaths == null || _imagePaths.Length == 0)
            return;

        try
        {
            for (int i = 0; i < _imageCount; i++)
            {
                _images[i]?.Dispose();
                _images[i] = null;

                if (i < _imagePaths.Length)
                {
                    using var fs = new FileStream(_imagePaths[i], FileMode.Open, FileAccess.Read);
                    _images[i] = Image.FromStream(fs);
                }
            }
        }
        catch
        {
            // 加载失败时保持为 null
        }
    }

    private static float CalculateFitZoom(Image image, Rectangle drawArea)
    {
        if (image == null) return 1.0f;
        float scaleX = (float)drawArea.Width / image.Width;
        float scaleY = (float)drawArea.Height / image.Height;
        return Math.Min(scaleX, scaleY);
    }

    /// <summary>
    /// 获取第 i 张图的绘制区域（仅扣除标题栏，含分割线偏移）
    /// </summary>
    private Rectangle GetCellRect(int index)
    {
        float x = GetCellLeft(index);
        float y = GetCellTop(index);
        float w = GetCellWidth(index);
        float h = GetCellHeight(index);
        return new Rectangle((int)x, (int)y, Math.Max(1, (int)w), Math.Max(1, (int)h));
    }

    private int HitTest(PointF screenPos)
    {
        for (int i = 0; i < _imageCount; i++)
        {
            var rect = GetCellRect(i);
            if (rect.Contains((int)screenPos.X, (int)screenPos.Y))
                return i;
        }
        return -1;
    }

    private void UpdateBaseZoom()
    {
        for (int i = 0; i < _imageCount; i++)
        {
            if (_images[i] == null) continue;
            var rect = GetCellRect(i);
            _baseZooms[i] = CalculateFitZoom(_images[i]!, rect);
        }
    }

    /// <summary>
    /// 交换两张图片的位置（图片对象、路径、偏移、缩放等所有数据互换）
    /// </summary>
    private void SwapImages(int indexA, int indexB)
    {
        if (indexA < 0 || indexA >= _imageCount || indexB < 0 || indexB >= _imageCount || indexA == indexB)
            return;

        // 交换图片对象
        (_images[indexA], _images[indexB]) = (_images[indexB], _images[indexA]);

        // 交换路径
        (_imagePaths[indexA], _imagePaths[indexB]) = (_imagePaths[indexB], _imagePaths[indexA]);

        // 交换偏移
        (_offsets[indexA], _offsets[indexB]) = (_offsets[indexB], _offsets[indexA]);

        // 交换手动偏移
        (_manualOffsets[indexA], _manualOffsets[indexB]) = (_manualOffsets[indexB], _manualOffsets[indexA]);

        // 交换基础缩放
        (_baseZooms[indexA], _baseZooms[indexB]) = (_baseZooms[indexB], _baseZooms[indexA]);

        // 交换独立缩放级别
        (_zoomLevels[indexA], _zoomLevels[indexB]) = (_zoomLevels[indexB], _zoomLevels[indexA]);

        this.Invalidate();
    }
}
