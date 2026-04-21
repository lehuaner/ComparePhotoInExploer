namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的缩放与滚轮处理逻辑
/// </summary>
public partial class Form1
{
    private void Form1_MouseWheel(object? sender, MouseEventArgs e)
    {
        // 滚动操作主操作区时，自动收起历史记录、操作说明和重置偏移
        if (!_historyBarData.IsCollapsed || _showHelp || _resetOverlay.IsVisible || _showZoomHelp)
        {
            _historyBarData.Collapse();
            _showHelp = false;
            _showZoomHelp = false;
            _resetOverlay.Hide();
            _hoverHistoryGroup = -1;
        }

        if (ModifierKeys == Keys.Control)
        {
            float avgZoom = _baseZooms.Where(z => z > 0).DefaultIfEmpty(1f).Average() * _zoomLevel;
            float step = this.ClientSize.Width * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? step : -step;
            if (IsSyncMoveDisabled())
            {
                int idx = HitTest(e.Location);
                if (idx >= 0 && idx < _imageCount)
                    _offsets[idx] = new PointF(_offsets[idx].X + delta, _offsets[idx].Y);
            }
            else
            {
                for (int i = 0; i < _imageCount; i++)
                    _offsets[i] = new PointF(_offsets[i].X + delta, _offsets[i].Y);
            }
        }
        else if (IsAltPressed())
        {
            float zoomFactor = e.Delta > 0 ? 1.25f : 1f / 1.25f;
            PointF mousePos = e.Location;
            int activeIdx = HitTest(mousePos);

            if (IsSyncZoomDisabled())
            {
                // 关闭同步缩放模式：只缩放鼠标所在的那张图片
                if (activeIdx < 0 || activeIdx >= _imageCount || _images[activeIdx] == null)
                {
                    this.Invalidate();
                    return;
                }

                float oldZoomLevel = _zoomLevels[activeIdx];
                float newZoomLevel = oldZoomLevel * zoomFactor;
                if (newZoomLevel < 0.01f || newZoomLevel > 100f)
                    return;

                Rectangle activeRect = GetCellRect(activeIdx);
                Size activeImgSize = _images[activeIdx]!.Size;
                float oldEff = _baseZooms[activeIdx] * oldZoomLevel;
                float newEff = _baseZooms[activeIdx] * newZoomLevel;

                PointF norm = ZoomCalculator.ScreenToNormalized(mousePos, activeRect, oldEff, _offsets[activeIdx], activeImgSize);
                _offsets[activeIdx] = ZoomCalculator.ZoomAtNormalized(norm, activeRect, oldEff, newEff, _offsets[activeIdx], activeImgSize);
                _zoomLevels[activeIdx] = newZoomLevel;
            }
            else
            {
                // 同步对齐 / 独立缩放模式
                float oldZoomLevel = _zoomLevel;
                float newZoomLevel = _zoomLevel * zoomFactor;

                if (newZoomLevel < 0.01f || newZoomLevel > 100f)
                    return;

                if (activeIdx < 0 || activeIdx >= _images.Length || _images[activeIdx] == null)
                {
                    _zoomLevel = newZoomLevel;
                    this.Invalidate();
                    return;
                }

                Rectangle activeRect = GetCellRect(activeIdx);
                Size activeImgSize = _images[activeIdx]!.Size;
                float oldEffActive = _baseZooms[activeIdx] * oldZoomLevel;
                float newEffActive = _baseZooms[activeIdx] * newZoomLevel;

                // active图片：直接使用完整offset，确保以鼠标实际所指位置为基准缩放
                PointF norm = ZoomCalculator.ScreenToNormalized(mousePos, activeRect, oldEffActive, _offsets[activeIdx], activeImgSize);
                _offsets[activeIdx] = ZoomCalculator.ZoomAtNormalized(norm, activeRect, oldEffActive, newEffActive, _offsets[activeIdx], activeImgSize);

                float activeLocalX = mousePos.X - activeRect.Left;
                float activeLocalY = mousePos.Y - activeRect.Top;

                for (int i = 0; i < _imageCount; i++)
                {
                    if (i == activeIdx || _images[i] == null) continue;

                    Rectangle passiveRect = GetCellRect(i);
                    Size passiveImgSize = _images[i]!.Size;
                    float oldEffPassive = _baseZooms[i] * oldZoomLevel;
                    float newEffPassive = _baseZooms[i] * newZoomLevel;

                    if (_syncZoomMode == SyncZoomMode.SyncAlign)
                    {
                        // 同步对齐：将被动图片的同一归一化位置移到与主动图片鼠标位置对应的地方
                        PointF targetPos = new PointF(passiveRect.Left + activeLocalX, passiveRect.Top + activeLocalY);
                        var computed = ZoomCalculator.ZoomAndMoveToTarget(norm, passiveRect, newEffPassive, passiveImgSize, targetPos);
                        _offsets[i] = new PointF(computed.X + _manualOffsets[i].X, computed.Y + _manualOffsets[i].Y);
                    }
                    else
                    {
                        // 独立缩放：被动图片以与主动图片相同比例位置为缩放中心
                        // 即主图1/4处为缩放中心，从图也以其1/4处为缩放中心
                        _offsets[i] = ZoomCalculator.ZoomAtNormalized(norm, passiveRect, oldEffPassive, newEffPassive, _offsets[i], passiveImgSize);
                    }
                }

                _zoomLevel = newZoomLevel;
            }
        }
        else
        {
            float avgZoom = _baseZooms.Where(z => z > 0).DefaultIfEmpty(1f).Average() * _zoomLevel;
            float step = this.ClientSize.Height * 0.05f * avgZoom;
            float delta = e.Delta > 0 ? step : -step;
            if (IsSyncMoveDisabled())
            {
                int idx = HitTest(e.Location);
                if (idx >= 0 && idx < _imageCount)
                    _offsets[idx] = new PointF(_offsets[idx].X, _offsets[idx].Y + delta);
            }
            else
            {
                for (int i = 0; i < _imageCount; i++)
                    _offsets[i] = new PointF(_offsets[i].X, _offsets[i].Y + delta);
            }
        }

        this.Invalidate();
    }

    /// <summary>
    /// 是否关闭了同步缩放（同步移动模式为"关闭同步缩放"或"同时关闭"时关闭）
    /// </summary>
    private bool IsSyncZoomDisabled() => _syncMoveMode == SyncMoveMode.DisableSyncZoom || _syncMoveMode == SyncMoveMode.DisableAll;

    /// <summary>
    /// 是否关闭了同步移动
    /// </summary>
    private bool IsSyncMoveDisabled() => _syncMoveMode == SyncMoveMode.DisableSyncMove || _syncMoveMode == SyncMoveMode.DisableAll;

    private float GetEffectiveZoom(int index)
    {
        float level = IsSyncZoomDisabled() ? _zoomLevels[index] : _zoomLevel;
        return _baseZooms[index] * level;
    }
}
