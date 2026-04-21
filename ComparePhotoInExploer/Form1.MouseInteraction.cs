namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的鼠标交互逻辑（MouseDown / MouseMove / MouseUp）
/// </summary>
public partial class Form1
{
    private void Form1_MouseDown(object? sender, MouseEventArgs e)
    {
        // 标题栏区域处理
        if (e.Location.Y < TitleBarHeight)
        {
            if (_btnClose.Contains(e.Location))
            {
                this.Close();
                return;
            }
            if (_btnMax.Contains(e.Location))
            {
                ToggleMaximize();
                return;
            }
            if (_btnMin.Contains(e.Location))
            {
                this.WindowState = FormWindowState.Minimized;
                return;
            }
            // 按键说明按钮
            if (_btnHelp.Contains(e.Location))
            {
                _showHelp = !_showHelp;
                if (_showHelp)
                {
                    _historyBarData.Collapse(); // 关闭历史记录
                    _resetOverlay.Hide(); // 关闭重置偏移
                    _showZoomHelp = false; // 关闭缩放说明
                }
                this.Invalidate();
                return;
            }
            // 历史记录按钮
            if (_btnHistory.Contains(e.Location))
            {
                _showHelp = false; // 关闭按键说明
                _showZoomHelp = false; // 关闭缩放说明
                _resetOverlay.Hide(); // 关闭重置偏移
                _historyBarData.ToggleCollapse();
                this.Invalidate();
                return;
            }
            // 主题切换按钮
            if (_btnTheme.Contains(e.Location))
            {
                CycleTheme();
                return;
            }
            // 同步缩放模式按钮
            if (_btnSyncZoom.Contains(e.Location))
            {
                _syncZoomMode = _syncZoomMode == SyncZoomMode.SyncAlign
                    ? SyncZoomMode.IndependentZoom
                    : SyncZoomMode.SyncAlign;
                this.Invalidate();
                return;
            }
            // 同步移动模式按钮
            if (_btnSyncMove.Contains(e.Location))
            {
                _syncMoveMode = _syncMoveMode switch
                {
                    SyncMoveMode.DisableSyncZoom => SyncMoveMode.DisableSyncMove,
                    SyncMoveMode.DisableSyncMove => SyncMoveMode.DisableAll,
                    SyncMoveMode.DisableAll => SyncMoveMode.EnableAll,
                    _ => SyncMoveMode.DisableSyncZoom
                };
                // 切换到关闭同步缩放模式时，将当前全局缩放级别同步到各图
                if (IsSyncZoomDisabled())
                {
                    for (int i = 0; i < _zoomLevels.Length; i++)
                        _zoomLevels[i] = _zoomLevel;
                }
                // 从关闭同步缩放切换回同步时，将第一张图的缩放级别作为全局缩放级别
                else if (_zoomLevels.Length > 0)
                {
                    _zoomLevel = _zoomLevels[0];
                }
                this.Invalidate();
                return;
            }
            // 缩放说明按钮
            if (_btnZoomHelp.Contains(e.Location))
            {
                _showZoomHelp = !_showZoomHelp;
                if (_showZoomHelp)
                {
                    _showHelp = false;
                    _historyBarData.Collapse();
                    _resetOverlay.Hide();
                }
                this.Invalidate();
                return;
            }
            // 右键菜单单选框
            if (_btnRightClickMenu.Contains(e.Location))
            {
                _rightClickMenuEnabled = !_rightClickMenuEnabled;
                AppSettings.SaveRightClickMenuSetting(_rightClickMenuEnabled);
                
                if (_rightClickMenuEnabled)
                {
                    RightClickMenuHelper.Install();
                }
                else
                {
                    RightClickMenuHelper.Uninstall();
                }
                
                this.Invalidate();
                return;
            }
            // 重置偏移按钮
            if (!_btnReset.IsEmpty && _btnReset.Contains(e.Location))
            {
                _resetOverlay.IsVisible = !_resetOverlay.IsVisible;
                if (_resetOverlay.IsVisible)
                {
                    _showHelp = false;
                    _historyBarData.Collapse();
                }
                _resetOverlay.HoverCell = -1;
                this.Invalidate();
                return;
            }
            // 拖动窗口
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(this.Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HTCAPTION, 0);
            return;
        }

        // 历史记录覆盖层区域 — 处理点击
        if (!_historyBarData.IsCollapsed && _historyBarData.GroupCount > 0)
        {
            int hitIdx = _historyBarData.HitTest(0, TitleBarHeight, this.ClientSize.Width, e.Location, 0);
            if (hitIdx >= 0)
            {
                if (e.Button == MouseButtons.Left)
                {
                    var paths = _historyBarData.GetGroupPaths(hitIdx);
                    if (paths != null)
                        OnHistoryGroupClicked(paths);
                }
                else if (e.Button == MouseButtons.Right)
                {
                    var group = _historyBarData.GetGroup(hitIdx);
                    if (group != null)
                    {
                        string imgNames = string.Join("\n", group.ImagePaths.Take(3).Select(p => Path.GetFileName(p)));
                        if (group.ImagePaths.Count > 3)
                            imgNames += $"\n...等{group.ImagePaths.Count}张";

                        var result = ThemedMessageBox.Show(this,
                            $"确定删除此历史记录组？\n\n{imgNames}",
                            "删除历史记录",
                            MessageBoxButtons.YesNo, _colors);

                        if (result == DialogResult.Yes)
                        {
                            OnHistoryGroupDeleteRequested(hitIdx);
                        }
                    }
                }
                return;
            }
        }

        // 点击主操作区时，自动收起历史记录、操作说明
        if (!_historyBarData.IsCollapsed || _showHelp || _showZoomHelp)
        {
            _historyBarData.Collapse();
            _showHelp = false;
            _showZoomHelp = false;
            _hoverHistoryGroup = -1;
            this.Invalidate();
        }

        // 重置偏移模式：如果点击在面板外，自动关闭并跳过本次操作
        if (_resetOverlay.IsVisible && !_resetOverlay.IsInOverlayArea(e.Location, _imageCount, _cols, _rows))
        {
            _resetOverlay.Hide();
            this.Invalidate();
            return;
        }

        // 重置偏移模式下的交互
        if (_resetOverlay.IsVisible)
        {
            if (e.Button == MouseButtons.Left)
            {
                // 点击重置按钮（根据选中状态决定功能）
                if (_resetOverlay.BatchResetButton.Contains(e.Location))
                {
                    if (_resetOverlay.SelectedCells.Count > 0)
                    {
                        // 有选中图片 → 重置选中
                        var names = string.Join(", ", _resetOverlay.SelectedCells.Select(i => Path.GetFileName(_imagePaths[i])));
                        var result = ThemedMessageBox.Show(this,
                            $"确定重置以下 {_resetOverlay.SelectedCells.Count} 张图片的偏移？\n\n{names}",
                            "重置选中偏移", MessageBoxButtons.YesNo, _colors);
                        if (result == DialogResult.Yes)
                        {
                            foreach (int idx in _resetOverlay.SelectedCells)
                            {
                                _offsets[idx] = new PointF(
                                    _offsets[idx].X - _manualOffsets[idx].X,
                                    _offsets[idx].Y - _manualOffsets[idx].Y);
                                _manualOffsets[idx] = new PointF(0, 0);
                            }
                            if (!_manualOffsets.Any(o => o.X != 0 || o.Y != 0))
                                _resetOverlay.Hide();
                            else
                                _resetOverlay.SelectedCells.Clear();
                            this.Invalidate();
                        }
                    }
                    else
                    {
                        // 无选中图片 → 全部重置
                        var offsetIndices = new List<int>();
                        for (int i = 0; i < _imageCount; i++)
                        {
                            if (_manualOffsets[i].X != 0 || _manualOffsets[i].Y != 0)
                                offsetIndices.Add(i);
                        }
                        if (offsetIndices.Count > 0)
                        {
                            var names = string.Join(", ", offsetIndices.Select(i => Path.GetFileName(_imagePaths[i])));
                            var result = ThemedMessageBox.Show(this,
                                $"确定重置以下 {offsetIndices.Count} 张图片的偏移？\n\n{names}",
                                "全部重置偏移", MessageBoxButtons.YesNo, _colors);
                            if (result == DialogResult.Yes)
                            {
                                foreach (int idx in offsetIndices)
                                {
                                    _offsets[idx] = new PointF(
                                        _offsets[idx].X - _manualOffsets[idx].X,
                                        _offsets[idx].Y - _manualOffsets[idx].Y);
                                    _manualOffsets[idx] = new PointF(0, 0);
                                }
                                _resetOverlay.Hide();
                                this.Invalidate();
                            }
                        }
                    }
                    return;
                }

                // 点击缩略图选中/取消选中
                int hitIdx = _resetOverlay.HitTestCell(e.Location, _imageCount, _manualOffsets, _cols, _rows);
                if (hitIdx >= 0)
                {
                    // 切换选中（普通点击也切换）
                    if (_resetOverlay.SelectedCells.Contains(hitIdx))
                        _resetOverlay.SelectedCells.Remove(hitIdx);
                    else if ((ModifierKeys & Keys.Control) == Keys.Control)
                        _resetOverlay.SelectedCells.Add(hitIdx);
                    else
                    {
                        _resetOverlay.SelectedCells.Clear();
                        _resetOverlay.SelectedCells.Add(hitIdx);
                    }
                    this.Invalidate();
                    return;
                }

                // 点击空白处：取消选择，并开始框选
                _resetOverlay.SelectedCells.Clear();
                _resetOverlay.StartSelection(e.Location);
                this.Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                // 右键点击图片重置
                int hitIdx = _resetOverlay.HitTestCell(e.Location, _imageCount, _manualOffsets, _cols, _rows);
                if (hitIdx >= 0)
                {
                    // 如果右键点中的图片已在选中列表中，则批量重置所有选中图片
                    var toReset = _resetOverlay.SelectedCells.Contains(hitIdx)
                        ? _resetOverlay.SelectedCells.ToList()
                        : new List<int> { hitIdx };

                    var names = string.Join(", ", toReset.Select(i => Path.GetFileName(_imagePaths[i])));
                    var result = ThemedMessageBox.Show(this,
                        $"确定重置以下 {toReset.Count} 张图片的偏移？\n\n{names}",
                        "重置偏移", MessageBoxButtons.YesNo, _colors);
                    if (result == DialogResult.Yes)
                    {
                        foreach (int idx in toReset)
                        {
                            _offsets[idx] = new PointF(
                                _offsets[idx].X - _manualOffsets[idx].X,
                                _offsets[idx].Y - _manualOffsets[idx].Y);
                            _manualOffsets[idx] = new PointF(0, 0);
                        }
                        if (!_manualOffsets.Any(o => o.X != 0 || o.Y != 0))
                            _resetOverlay.Hide();
                        else
                        {
                            foreach (int idx in toReset)
                                _resetOverlay.SelectedCells.Remove(idx);
                        }
                        this.Invalidate();
                    }
                }
                return;
            }
            return;
        }

        // Tab+左键：进入图片互换拖动模式
        if (e.Button == MouseButtons.Left && IsTabPressed())
        {
            int hitIdx = HitTest(e.Location);
            if (hitIdx >= 0 && hitIdx < _imageCount)
            {
                _isTabSwapping = true;
                _tabSwapSourceIndex = hitIdx;
                _tabSwapTargetIndex = -1;
                this.Cursor = Cursors.Hand;
            }
            return;
        }

        // 图片区域拖动
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _lastMousePos = e.Location;

            // Shift+左键：只拖动鼠标所在的那张图片
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                _shiftDragIndex = HitTest(e.Location);
            }
            else if (IsSyncMoveDisabled())
            {
                // 关闭同步移动时：只拖动鼠标所在的那张图片
                _shiftDragIndex = HitTest(e.Location);
                _dragTargetIndex = _shiftDragIndex;
            }
            else
            {
                _shiftDragIndex = -1;
                _dragTargetIndex = -1;
            }
        }
    }

    private void Form1_MouseMove(object? sender, MouseEventArgs e)
    {
        // 边框调整大小区域光标
        if (!_isWindowMaximized && e.Location.Y >= TitleBarHeight)
        {
            int x = e.Location.X, y = e.Location.Y;
            int w = this.ClientSize.Width, h = this.ClientSize.Height;
            int bw = ResizeBorderWidth;

            bool onLeft = x < bw, onRight = x >= w - bw;
            bool onTop = y < bw, onBottom = y >= h - bw;

            if ((onTop && onLeft) || (onBottom && onRight))
                this.Cursor = Cursors.SizeNWSE;
            else if ((onTop && onRight) || (onBottom && onLeft))
                this.Cursor = Cursors.SizeNESW;
            else if (onLeft || onRight)
                this.Cursor = Cursors.SizeWE;
            else if (onTop || onBottom)
                this.Cursor = Cursors.SizeNS;
            else if (this.Cursor != Cursors.Default && this.Cursor != Cursors.Hand)
                this.Cursor = Cursors.Default;
        }

        // 标题栏悬停效果
        if (e.Location.Y < TitleBarHeight)
        {
            bool newHoverMin = _btnMin.Contains(e.Location);
            bool newHoverMax = _btnMax.Contains(e.Location);
            bool newHoverClose = _btnClose.Contains(e.Location);
            bool newHoverHelp = _btnHelp.Contains(e.Location);
            bool newHoverHistory = _btnHistory.Contains(e.Location);
            bool newHoverTheme = _btnTheme.Contains(e.Location);
            bool newHoverReset = !_btnReset.IsEmpty && _btnReset.Contains(e.Location);
            bool newHoverSyncZoom = _btnSyncZoom.Contains(e.Location);
            bool newHoverZoomHelp = _btnZoomHelp.Contains(e.Location);
            bool newHoverRightClickMenu = _btnRightClickMenu.Contains(e.Location);
            bool newHoverSyncMove = _btnSyncMove.Contains(e.Location);

            if (newHoverMin != _hoverMin || newHoverMax != _hoverMax || newHoverClose != _hoverClose || 
                newHoverHelp != _hoverHelp || newHoverHistory != _hoverHistory || newHoverTheme != _hoverTheme ||
                newHoverReset != _hoverReset || newHoverSyncZoom != _hoverSyncZoom || newHoverZoomHelp != _hoverZoomHelp ||
                newHoverRightClickMenu != _hoverRightClickMenu || newHoverSyncMove != _hoverSyncMove)
            {
                _hoverMin = newHoverMin;
                _hoverMax = newHoverMax;
                _hoverClose = newHoverClose;
                _hoverHelp = newHoverHelp;
                _hoverHistory = newHoverHistory;
                _hoverTheme = newHoverTheme;
                _hoverReset = newHoverReset;
                _hoverSyncZoom = newHoverSyncZoom;
                _hoverZoomHelp = newHoverZoomHelp;
                _hoverRightClickMenu = newHoverRightClickMenu;
                _hoverSyncMove = newHoverSyncMove;
                this.Invalidate(new Rectangle(0, 0, this.ClientSize.Width, TitleBarHeight));
            }
            return;
        }

        // 历史记录覆盖层悬停检测
        if (!_historyBarData.IsCollapsed && _historyBarData.GroupCount > 0)
        {
            int newHover = _historyBarData.HitTest(0, TitleBarHeight, this.ClientSize.Width, e.Location, 0);
            if (newHover != _hoverHistoryGroup)
            {
                _hoverHistoryGroup = newHover;
                this.Cursor = newHover >= 0 ? Cursors.Hand : Cursors.Default;
                this.Invalidate();
            }
            if (newHover >= 0)
                return; // 在历史记录上，不拖动图片
        }

        // 重置偏移模式悬停和框选
        if (_resetOverlay.IsVisible)
        {
            // 框选拖动 — 实时更新选中状态并重绘
            if (_resetOverlay.IsSelecting)
            {
                _resetOverlay.UpdateSelection(e.Location, _imageCount, _manualOffsets, _cols, _rows);
                this.Invalidate();
                return;
            }

            int newHoverCell = _resetOverlay.HitTestCell(e.Location, _imageCount, _manualOffsets, _cols, _rows);
            if (newHoverCell != _resetOverlay.HoverCell)
            {
                _resetOverlay.HoverCell = newHoverCell;
                this.Cursor = newHoverCell >= 0 ? Cursors.Hand : Cursors.Default;
                this.Invalidate();
            }
            return; // 重置模式下不拖动图片
        }

        if (_isDragging)
        {
            int deltaX = e.X - _lastMousePos.X;
            int deltaY = e.Y - _lastMousePos.Y;

            if (_shiftDragIndex >= 0 && _shiftDragIndex < _imageCount)
            {
                // Shift拖动：只移动选中的图片（记录为手动偏移）
                int i = _shiftDragIndex;
                _offsets[i] = new PointF(_offsets[i].X + deltaX, _offsets[i].Y + deltaY);
                _manualOffsets[i] = new PointF(_manualOffsets[i].X + deltaX, _manualOffsets[i].Y + deltaY);
            }
            else if (_dragTargetIndex >= 0 && _dragTargetIndex < _imageCount)
            {
                // 关闭同步移动：只移动鼠标所在的图片（不记录为手动偏移）
                int i = _dragTargetIndex;
                _offsets[i] = new PointF(_offsets[i].X + deltaX, _offsets[i].Y + deltaY);
            }
            else
            {
                // 普通拖动：同步移动所有图片
                for (int i = 0; i < _imageCount; i++)
                {
                    _offsets[i] = new PointF(_offsets[i].X + deltaX, _offsets[i].Y + deltaY);
                }
            }

            _lastMousePos = e.Location;
            this.Invalidate();
        }

        // Tab互换拖动模式：检测悬停目标，Tab释放则取消
        if (_isTabSwapping)
        {
            // Tab键释放时取消互换
            if (!IsTabPressed())
            {
                _isTabSwapping = false;
                _tabSwapSourceIndex = -1;
                _tabSwapTargetIndex = -1;
                this.Cursor = Cursors.Default;
                this.Invalidate();
                return;
            }

            int hitIdx = HitTest(e.Location);
            int newTarget = (hitIdx >= 0 && hitIdx < _imageCount && hitIdx != _tabSwapSourceIndex) ? hitIdx : -1;
            if (newTarget != _tabSwapTargetIndex)
            {
                _tabSwapTargetIndex = newTarget;
                this.Invalidate();
            }
        }
    }

    private void Form1_MouseUp(object? sender, MouseEventArgs e)
    {
        // Tab互换拖动释放：交换图片
        if (_isTabSwapping)
        {
            if (_tabSwapTargetIndex >= 0 && _tabSwapSourceIndex >= 0)
            {
                SwapImages(_tabSwapSourceIndex, _tabSwapTargetIndex);
            }
            _isTabSwapping = false;
            _tabSwapSourceIndex = -1;
            _tabSwapTargetIndex = -1;
            this.Cursor = Cursors.Default;
            this.Invalidate();
            return;
        }

        _isDragging = false;
        _shiftDragIndex = -1;
        _dragTargetIndex = -1;
        if (_resetOverlay.IsSelecting)
        {
            _resetOverlay.EndSelection();
            this.Invalidate();
        }
    }
}
