namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的历史记录管理逻辑（控制器层）
/// 协调 HistoryData（数据层）和 HistoryBarData（视图层）与 Form1 自身状态
/// </summary>
public partial class Form1
{
    /// <summary>
    /// 将当前打开的图片组保存到历史记录
    /// </summary>
    private void SaveCurrentToHistory()
    {
        if (_imagePaths == null || _imagePaths.Length == 0)
            return;

        // 更新历史数据（处理去重、排序、上限）
        _historyGroups = HistoryData.AddOrUpdateGroup(_historyGroups, _imagePaths);
        HistoryData.Save(_historyGroups);

        // 先用占位图加载 UI
        _historyBarData.LoadGroups(_historyGroups);

        // 异步生成缩略图，不阻塞UI — 只生成尚不存在的缩略图
        var pathsToGenerate = new List<(string path, string hash)>();
        foreach (var p in _imagePaths)
        {
            var hash = HistoryData.GetPathHash(p);
            if (!HistoryData.ThumbnailExists(hash))
                pathsToGenerate.Add((p, hash));
        }

        if (pathsToGenerate.Count == 0)
            return; // 所有缩略图已存在，无需生成

        var capturePaths = pathsToGenerate.ToArray();

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                foreach (var (path, hash) in capturePaths)
                {
                    using var thumb = ImageProcessor.CreateThumbnail(path);
                    HistoryData.SaveThumbnail(hash, thumb);
                }

                // 在UI线程刷新缩略图
                this.BeginInvoke(new Action(() =>
                {
                    _historyBarData.LoadGroups(_historyGroups);
                }));
            }
            catch
            {
                // 忽略生成失败
            }
        });
    }

    /// <summary>
    /// 历史记录组被点击 - 加载该组图片
    /// </summary>
    private void OnHistoryGroupClicked(string[] paths)
    {
        if (paths == null || paths.Length == 0) return;

        // 检查是否与当前图片相同（忽略顺序）
        var pathsSet = new HashSet<string>(paths);
        if (_imagePaths != null && _imagePaths.Length == paths.Length &&
            _imagePaths.All(p => pathsSet.Contains(p)))
            return;

        // 切换到该组图片
        LoadNewGroup(paths);

        // 将该组提到最前
        int idx = -1;
        for (int i = 0; i < _historyGroups.Count; i++)
        {
            if (_historyGroups[i].ImagePaths.Count == paths.Length &&
                _historyGroups[i].ImagePaths.All(p => pathsSet.Contains(p)))
            {
                idx = i;
                break;
            }
        }

        if (idx > 0)
        {
            var group = _historyGroups[idx];
            _historyGroups.RemoveAt(idx);
            _historyGroups.Insert(0, group);
            HistoryData.Save(_historyGroups);
            _historyBarData.LoadGroups(_historyGroups);
        }
    }

    /// <summary>
    /// 历史记录组被请求删除
    /// </summary>
    private void OnHistoryGroupDeleteRequested(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _historyGroups.Count) return;

        var removed = _historyGroups[groupIndex];
        
        // 检查是否正在显示该组（忽略顺序）
        var removedSet = new HashSet<string>(removed.ImagePaths);
        bool isCurrentGroup = _imagePaths != null && 
            _imagePaths.Length == removed.ImagePaths.Count &&
            _imagePaths.All(p => removedSet.Contains(p));
        
        HistoryData.DeleteGroupThumbnails(_historyGroups, removed.Id);
        _historyGroups.RemoveAt(groupIndex);
        HistoryData.Save(_historyGroups);
        _historyBarData.LoadGroups(_historyGroups);
        
        // 如果删除的是当前对比组，清空显示
        if (isCurrentGroup)
        {
            ClearCurrentImages();
        }
    }

    /// <summary>
    /// 清空当前显示的图片
    /// </summary>
    private void ClearCurrentImages()
    {
        for (int i = 0; i < _images.Length; i++)
        {
            _images[i]?.Dispose();
            _images[i] = null;
        }
        _imagePaths = Array.Empty<string>();
        _imageCount = 0;
        _zoomLevel = 1.0f;
        _shiftDragIndex = -1;
        for (int i = 0; i < _offsets.Length; i++)
        {
            _offsets[i] = new PointF(0, 0);
            _manualOffsets[i] = new PointF(0, 0);
            _zoomLevels[i] = 1.0f;
        }
        
        this.Text = "图片对比";
        this.Invalidate();
    }
}
