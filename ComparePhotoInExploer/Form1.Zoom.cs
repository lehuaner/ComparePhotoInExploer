namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的缩放与滚轮处理逻辑
/// </summary>
public partial class Form1
{
    // ===== 平滑缩放（对数空间惯性滑行：速度+摩擦，滚完减速到停）=====
    private System.Windows.Forms.Timer? _zoomAnim;
    private bool _zoomRunning;                         // 尾段减速动画是否在跑
    private System.Windows.Forms.Timer? _zoomStop;     // 停止检测：滚动停下后触发一次尾段减速
    private float _zLogCur;      // 当前 log(level)
    private float _zLogVel;      // log(level)/秒（仅尾段减速用）
    private float _zPrevLevel;   // 上一帧已应用的 level
    private PointF _zAnchor;
    private int _zIdx;
    private bool _zSync;
    private long _zoomLastMs;
    private long _zPrevNotchMs;  // 上一格时刻（估滞滚速度）
    private float _zScrollVel;   // 滞滚瞬时速度 EMA(log/s)，供尾段续接
    private bool _zHasNotch;
    private const int ZoomSettleMs = 35;         // 停止滞滚多久后播放尾段减速（缩短以减少顿挫）
    private const float ZoomFrictionTau = 0.20f; // 尾段减速时间常数(秒)（越大滑得越久越软）
    private const float ZoomTailGain = 1.0f;     // 尾段初速 = 滞滚末速 * 该增益（满速续接）
    private const float ZoomVelEma = 0.4f;       // 滞滚速度平滑（越小越“记速”，快滚高速延续到尾段）
    private const int ZoomBurstGapMs = 220;      // 超过此间隔视为新手势→重置速度估计
    private const float ZoomVelCap = 8f;         // 速度上限(log/s)
    private const float ZoomMinSpeed = 0.03f;    // 低于此速度停止


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

        BeginInteraction(); // P1：滚轮交互中降插值，静止 120ms 后回高质

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
            // 平滑缩放：以鼠标为锚点缓动到 cur*factor（连续滚动可重定向）
            float zoomFactor = e.Delta > 0 ? 1.12f : 1f / 1.12f;
            StartZoom(e.Location, HitTest(e.Location), !IsSyncZoomDisabled(), zoomFactor);
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

    /// <summary>缩放：滞滚中 1:1 即时缩放（无动画/无阻尼）；只有停手后才由 ZoomStop_Tick 触发一次尾段减速。</summary>
    private void StartZoom(PointF anchor, int idx, bool sync, float factor)
    {
        if (_imageCount == 0) return;
        StopGlide();                 // 若尾段减速在跑，立即冻结，回到直接控制
        _zoomStop?.Stop();

        float cur = sync ? _zoomLevel : (idx >= 0 && idx < _imageCount ? _zoomLevels[idx] : _zoomLevel);
        float stepped = Math.Clamp(cur * factor, 0.01f, 100f);
        float dlog = MathF.Log(stepped / cur);
        if (Math.Abs(dlog) > 1e-6f)
            ApplyZoomStep(idx, anchor, cur, stepped, sync); // 即时 1:1

        _zIdx = idx; _zSync = sync; _zAnchor = anchor;
        _zLogCur = MathF.Log(stepped); _zPrevLevel = stepped;

        // 估计滞滚瞬时速度(EMA)，尾段据此续接，避免“突然减速”
        long nowMs = Environment.TickCount64;
        if (_zHasNotch && nowMs - _zPrevNotchMs < ZoomBurstGapMs)
        {
            float dtN = (nowMs - _zPrevNotchMs) / 1000f;
            if (dtN > 1e-3f)
                _zScrollVel = ZoomVelEma * Math.Clamp(dlog / dtN, -ZoomVelCap, ZoomVelCap)
                            + (1f - ZoomVelEma) * _zScrollVel;
        }
        else
        {
            _zScrollVel = Math.Clamp(dlog / 0.05f, -ZoomVelCap, ZoomVelCap); // 新手势首格
            _zHasNotch = true;
        }
        _zPrevNotchMs = nowMs;

        EnsureZoomTimers();
        _zoomStop!.Start();          // 重新武装“停止检测”
        this.Invalidate();
    }

    private void EnsureZoomTimers()
    {
        if (_zoomStop == null)
        {
            _zoomStop = new System.Windows.Forms.Timer { Interval = ZoomSettleMs };
            _zoomStop.Tick += ZoomStop_Tick;
        }
        if (_zoomAnim == null)
        {
            int hz = NativeMethods.GetMonitorRefreshHz(); if (hz <= 0) hz = 60;
            _zoomAnim = new System.Windows.Forms.Timer { Interval = Math.Clamp(1000 / hz, 1, 33) };
            _zoomAnim.Tick += ZoomAnim_Tick;
        }
    }

    // 滚动停下（ZoomSettleMs 内无新格）→ 播放一次尾段减速滑行
    private void ZoomStop_Tick(object? sender, EventArgs e)
    {
        _zoomStop!.Stop();           // 一次性
        if (MathF.Abs(_zScrollVel) < ZoomMinSpeed) return;
        // 尾段初速续接滞滚末速（方向与大小都衔接）→ 不再“突然减速”
        _zLogVel = Math.Clamp(_zScrollVel * ZoomTailGain, -ZoomVelCap, ZoomVelCap);
        _zScrollVel = 0f;            // 用后清零，避免下次未动也滑
        _zoomLastMs = Environment.TickCount64;
        _zoomRunning = true;
        _zoomAnim!.Start();
        BeginInteraction();
    }

    // 尾段减速：唯一的动画阶段，摩擦衰减到停
    private void ZoomAnim_Tick(object? sender, EventArgs e)
    {
        if (!_zoomRunning) { _zoomAnim?.Stop(); return; }
        long now = Environment.TickCount64;
        float dt = (now - _zoomLastMs) / 1000f; _zoomLastMs = now;
        if (dt > 0.05f) dt = 0.05f;
        if (dt <= 0f) dt = 0.001f;

        _zLogCur += _zLogVel * dt;
        float curLevel = MathF.Exp(_zLogCur);
        if (curLevel < 0.01f) { curLevel = 0.01f; _zLogCur = MathF.Log(curLevel); _zLogVel = 0f; }
        else if (curLevel > 100f) { curLevel = 100f; _zLogCur = MathF.Log(curLevel); _zLogVel = 0f; }

        if (Math.Abs(curLevel - _zPrevLevel) > 1e-6f)
            ApplyZoomStep(_zIdx, _zAnchor, _zPrevLevel, curLevel, _zSync);
        _zPrevLevel = curLevel;

        _zLogVel *= MathF.Exp(-dt / ZoomFrictionTau); // 摩擦衰减 → 减速到停
        this.Invalidate();

        if (Math.Abs(_zLogVel) < ZoomMinSpeed) StopGlide();
    }

    /// <summary>停止尾段减速（若正在跑）。</summary>
    private void StopGlide()
    {
        if (_zoomAnim != null && _zoomAnim.Enabled) { _zoomAnim.Stop(); SettleInteraction(); }
        _zoomRunning = false;
    }

    /// <summary>把相关图片从 oldLevel 步进到 newLevel，锚点 anchor 处内容保持不动。</summary>
    private void ApplyZoomStep(int idx, PointF anchor, float oldLevel, float newLevel, bool sync)
    {
        if (!sync)
        {
            if (idx < 0 || idx >= _imageCount || _images[idx] == null) return;
            float oldEff = _baseZooms[idx] * oldLevel, newEff = _baseZooms[idx] * newLevel;
            var rect = GetCellRect(idx); var sz = _images[idx]!.Size;
            var norm = ZoomCalculator.ScreenToNormalized(anchor, rect, oldEff, _offsets[idx], sz);
            _offsets[idx] = ZoomCalculator.ZoomAtNormalized(norm, rect, oldEff, newEff, _offsets[idx], sz);
            _zoomLevels[idx] = newLevel;
            return;
        }

        if (idx < 0 || idx >= _images.Length || _images[idx] == null)
        {
            _zoomLevel = newLevel;
            return;
        }

        var activeRect = GetCellRect(idx);
        var activeImgSize = _images[idx]!.Size;
        float oldEffA = _baseZooms[idx] * oldLevel, newEffA = _baseZooms[idx] * newLevel;

        var normA = ZoomCalculator.ScreenToNormalized(anchor, activeRect, oldEffA, _offsets[idx], activeImgSize);
        _offsets[idx] = ZoomCalculator.ZoomAtNormalized(normA, activeRect, oldEffA, newEffA, _offsets[idx], activeImgSize);

        float activeLocalX = anchor.X - activeRect.Left;
        float activeLocalY = anchor.Y - activeRect.Top;

        for (int i = 0; i < _imageCount; i++)
        {
            if (i == idx || _images[i] == null) continue;
            var passiveRect = GetCellRect(i);
            var passiveImgSize = _images[i]!.Size;
            float newEffP = _baseZooms[i] * newLevel;
            if (_syncZoomMode == SyncZoomMode.SyncAlign)
            {
                var targetPos = new PointF(passiveRect.Left + activeLocalX, passiveRect.Top + activeLocalY);
                var computed = ZoomCalculator.ZoomAndMoveToTarget(normA, passiveRect, newEffP, passiveImgSize, targetPos);
                _offsets[i] = new PointF(computed.X + _manualOffsets[i].X, computed.Y + _manualOffsets[i].Y);
            }
            else
            {
                float oldEffP = _baseZooms[i] * oldLevel;
                _offsets[i] = ZoomCalculator.ZoomAtNormalized(normA, passiveRect, oldEffP, newEffP, _offsets[i], passiveImgSize);
            }
        }
        _zoomLevel = newLevel;
    }

    /// <summary>立即停止缩放惯性滑行（冻结在当前 level），供拖拽等手动操作打断。</summary>
    private void StopZoomAnim()
    {
        _zoomStop?.Stop();
        StopGlide();
    }

    /// <summary>键盘 +/− 缩放：锚点用鼠标所在图（无则用图片区中心）。</summary>
    private void ZoomByKeys(float factor)
    {
        if (_imageCount == 0) return;
        PointF anchor = _lastMousePos;
        int idx = HitTest(anchor);
        if (idx < 0)
        {
            anchor = new PointF(this.ClientSize.Width / 2f, TitleBarHeight + (this.ClientSize.Height - TitleBarHeight) / 2f);
            idx = HitTest(anchor);
        }
        StartZoom(anchor, idx, !IsSyncZoomDisabled(), factor);
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
