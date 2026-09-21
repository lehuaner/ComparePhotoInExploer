using System.Drawing.Drawing2D;

namespace ComparePhotoInExploer;

// 统一标记模型：一切“区域”都是“一组相关的点”。2点=线,3点=三角形,4点=矩形/椭圆。不再单独存线段。
public class MarkerPoint { public int Id; public int Owner; public PointF P; }
public class MarkerRegion { public int Owner; public List<int> PointIds = new(); public bool Ellipse; }

/// <summary>撤销快照</summary>
internal class MarkerSnap
{
    public List<MarkerPoint> Points = new();
    public List<MarkerRegion> Regions = new();
    public int NextId;
}

/// <summary>
/// Form1 的标记（中键点/线/连点成形/框选/标尺参考线）交互与绘制。仅当前会话有效，不持久化。
/// </summary>
public partial class Form1
{
    private const int MarkerDragThreshold = 4;
    private const int MarkerHitRadius = 8;
    private const int MarkerLineHitTol = 6;      // 两点线的命中容差
    private const int MarkerEdgeInnerTol = 20;  // 区域边“向内”扩展的命中带
    private static readonly Color MarkerColor = Color.FromArgb(255, 92, 92);
    private static readonly Color MarkerSelectedColor = Color.FromArgb(80, 170, 255);

    private readonly List<MarkerPoint> _points = new();
    private readonly List<MarkerRegion> _regions = new();
    private int _nextMarkerId = 1;

    // 当前选中的区域（左键切换；Shift+中键向其追加顶点）
    private MarkerRegion? _selected;

    private readonly List<MarkerSnap> _undo = new();

    // 中键按下/拖拽状态
    private bool _midDown;
    private Point _midDownPos;
    private bool _midMoved;
    private bool _midShift;

    // 框选形状（绘制前预选）：false=矩形, true=椭圆
    private bool _areaEllipse;

    private bool HasAnyMarker() => _points.Count > 0 || _regions.Count > 0;

    #region 撤销

    private void PushUndo()
    {
        _undo.Add(new MarkerSnap
        {
            Points = _points.Select(p => new MarkerPoint { Id = p.Id, Owner = p.Owner, P = p.P }).ToList(),
            Regions = _regions.Select(r => new MarkerRegion { Owner = r.Owner, PointIds = new List<int>(r.PointIds), Ellipse = r.Ellipse }).ToList(),
            NextId = _nextMarkerId
        });
        if (_undo.Count > 200) _undo.RemoveAt(0);
    }

    private void UndoMarker()
    {
        if (_undo.Count == 0) return;
        var s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _points.Clear(); _points.AddRange(s.Points);
        _regions.Clear(); _regions.AddRange(s.Regions);
        _nextMarkerId = s.NextId;
        _selected = null;
        this.Invalidate();
    }

    #endregion

    #region 坐标变换便捷方法

    private Rectangle MarkerCell(int owner) => GetCellRect(owner);
    private float MarkerZoom(int owner) => GetEffectiveZoom(owner);
    private Size MarkerImgSize(int owner) => _images[owner]?.Size ?? Size.Empty;

    private PointF ScreenToImg(int owner, PointF screen) =>
        ZoomCalculator.ScreenToImagePixel(screen, MarkerCell(owner), MarkerZoom(owner), _offsets[owner], MarkerImgSize(owner));

    private PointF ImgToScreen(int owner, PointF px) =>
        ZoomCalculator.ImagePixelToScreen(px, MarkerCell(owner), MarkerZoom(owner), _offsets[owner], MarkerImgSize(owner));

    private bool OwnerValid(int owner) => owner >= 0 && owner < _imageCount && _images[owner] != null;

    private int AddPoint(int owner, PointF imgPx)
    {
        int id = _nextMarkerId++;
        _points.Add(new MarkerPoint { Id = id, Owner = owner, P = imgPx });
        return id;
    }

    /// <summary>删除点数<2且非当前选中的退化区域（其孤立点回到“独立点”，不算重复）</summary>
    private void PruneTinyRegions()
    {
        _regions.RemoveAll(r => r.PointIds.Count < 2 && r != _selected);
    }

    #endregion

    #region 创建

    private void CreatePointAt(PointF screen)
    {
        int owner = HitTest(screen);
        if (!OwnerValid(owner)) return;
        PushUndo();
        AddPoint(owner, ScreenToImg(owner, screen));
        this.Invalidate();
    }

    private void CreateLine(PointF screenA, PointF screenB)
    {
        int owner = HitTest(screenA);
        if (!OwnerValid(owner)) return;
        PushUndo();
        int a = AddPoint(owner, ScreenToImg(owner, screenA));
        int b = AddPoint(owner, ScreenToImg(owner, screenB));
        _regions.Add(new MarkerRegion { Owner = owner, PointIds = new List<int> { a, b } });
    }

    private void CreateArea(PointF screenA, PointF screenB)
    {
        int owner = HitTest(screenA);
        if (!OwnerValid(owner)) return;
        var ia = ScreenToImg(owner, screenA);
        var ib = ScreenToImg(owner, screenB);
        var rect = RectangleF.FromLTRB(Math.Min(ia.X, ib.X), Math.Min(ia.Y, ib.Y), Math.Max(ia.X, ib.X), Math.Max(ia.Y, ib.Y));
        PushUndo();
        var ids = new List<int>
        {
            AddPoint(owner, new PointF(rect.Left, rect.Top)),
            AddPoint(owner, new PointF(rect.Right, rect.Top)),
            AddPoint(owner, new PointF(rect.Right, rect.Bottom)),
            AddPoint(owner, new PointF(rect.Left, rect.Bottom)),
        };
        if (!_areaEllipse) ids = OrderConvex(ids);
        _regions.Add(new MarkerRegion { Owner = owner, PointIds = ids, Ellipse = _areaEllipse });
    }

    /// <summary>#5：点击标尺带 => 发射一条“两点线区域”，与手画线复用同一套逻辑</summary>
    private bool TryCreateRulerGuide(PointF screen)
    {
        if (!_rulerEnabled || _imageCount <= 0) return false;
        int owner = HitTest(screen);
        if (!OwnerValid(owner)) return false;
        var cell = MarkerCell(owner);
        int t = RulerHelper.Thickness;
        var sz = MarkerImgSize(owner);

        bool inTop = screen.Y >= cell.Top && screen.Y < cell.Top + t && screen.X >= cell.Left && screen.X <= cell.Right;
        bool inLeft = screen.X >= cell.Left && screen.X < cell.Left + t && screen.Y >= cell.Top && screen.Y <= cell.Bottom;
        if (!inTop && !inLeft) return false;

        var ip = ScreenToImg(owner, screen);
        PushUndo();
        int a, b;
        if (inTop) { a = AddPoint(owner, new PointF(ip.X, 0)); b = AddPoint(owner, new PointF(ip.X, sz.Height)); }
        else { a = AddPoint(owner, new PointF(0, ip.Y)); b = AddPoint(owner, new PointF(sz.Width, ip.Y)); }
        _regions.Add(new MarkerRegion { Owner = owner, PointIds = new List<int> { a, b } });
        this.Invalidate();
        return true;
    }

    #endregion

    #region 连点成形（Shift+中键：点已选区域则追加顶点，自动按点数成型；否则以该点起新区域）

    private void TryConnectPointAt(PointF screen)
    {
        int owner = HitTest(screen);
        if (!OwnerValid(owner)) return;

        PushUndo(); // 一次连接动作 = 一条撤销记录

        var pt = HitPoint(screen);
        if (pt == null)
        {
            int id = AddPoint(owner, ScreenToImg(owner, screen));
            pt = _points.First(p => p.Id == id);
        }

        // 该点已属于某区域？则以其为目标（合并当前选区），保证“一个点只在一处”
        var ptRegion = _regions.FirstOrDefault(r => r.PointIds.Contains(pt.Id));
        if (ptRegion != null)
        {
            if (_selected != null && _selected != ptRegion)
                MergeRegion(ptRegion, _selected); // 合并到 ptRegion
            _selected = ptRegion;
            ReorderRegion(_selected);
            this.Invalidate();
            return;
        }

        // pt 为独立/新建点
        if (_selected != null && _selected.Owner == owner)
        {
            if (!_selected.PointIds.Contains(pt.Id)) _selected.PointIds.Add(pt.Id); // 2→线,3→三角,4→矩形
            ReorderRegion(_selected);
        }
        else
        {
            _selected = new MarkerRegion { Owner = owner, PointIds = new List<int> { pt.Id } };
            _regions.Add(_selected); // 以该点起一个新区域（暂1点）
        }
        this.Invalidate();
    }

    private void MergeRegion(MarkerRegion into, MarkerRegion from)
    {
        foreach (var id in from.PointIds)
            if (!into.PointIds.Contains(id)) into.PointIds.Add(id);
        _regions.Remove(from);
    }

    private void ReorderRegion(MarkerRegion r)
    {
        if (r.Ellipse && r.PointIds.Count >= 4) return; // 椭圆保留四角顺序
        if (r.PointIds.Count >= 3) r.PointIds = OrderConvex(r.PointIds);
    }

    private List<int> OrderConvex(List<int> ids)
    {
        var pts = ids.Select(id => _points.FirstOrDefault(p => p.Id == id))
                     .Where(p => p != null).Cast<MarkerPoint>().ToList();
        if (pts.Count < 3) return ids;
        float cx = pts.Average(p => p.P.X), cy = pts.Average(p => p.P.Y);
        return pts.OrderBy(p =>
        {
            double a = Math.Atan2(p.P.Y - cy, p.P.X - cx);
            return a < 0 ? a + 2 * Math.PI : a;
        }).Select(p => p.Id).ToList();
    }

    #endregion

    #region 命中检测 / 选择 / 删除

    private MarkerPoint? HitPoint(PointF screen)
    {
        MarkerPoint? best = null;
        float bestDist = MarkerHitRadius;
        foreach (var p in _points)
        {
            if (!OwnerValid(p.Owner)) continue;
            float d = Dist(screen, ImgToScreen(p.Owner, p.P));
            if (d <= bestDist) { bestDist = d; best = p; }
        }
        return best;
    }

    private List<MarkerPoint> RegionPoints(MarkerRegion r) =>
        r.PointIds.Select(id => _points.FirstOrDefault(p => p.Id == id)).Where(p => p != null).Cast<MarkerPoint>().ToList();

    private List<PointF> RegionScreenPts(MarkerRegion r)
    {
        var res = new List<PointF>();
        foreach (var id in r.PointIds)
        {
            var p = _points.FirstOrDefault(pp => pp.Id == id);
            if (p != null && OwnerValid(p.Owner)) res.Add(ImgToScreen(p.Owner, p.P));
        }
        return res;
    }

    /// <summary>#4/#16：左键命中区域则切换选中；返回是否处理（命中了区域或点，不作为拖图）</summary>
    private bool TryToggleSelectAt(PointF screen)
    {
        var region = FindRegionAt(screen);
        if (region == null) return false;
        _selected = (_selected == region) ? null : region; // 再次点击=取消选中
        this.Invalidate();
        return true;
    }

    private MarkerRegion? FindRegionAt(PointF screen)
    {
        for (int i = _regions.Count - 1; i >= 0; i--)
        {
            var r = _regions[i];
            if (!OwnerValid(r.Owner) || r.PointIds.Count < 2) continue;
            var pts = RegionScreenPts(r);
            if (pts.Count < 2) continue;
            if (pts.Count == 2) { if (DistToSegment(screen, pts[0], pts[1]) <= MarkerLineHitTol) return r; }
            else
            {
                if (RegionEdgeDist(r, screen) <= MarkerEdgeInnerTol) return r;
                if (RegionInteriorHit(r, pts, screen)) return r;
            }
        }
        return null;
    }

    private bool DeleteMarkerAt(PointF screen)
    {
        PushUndo();

        // 1) 点：删除并让所属区域按剩余点降级
        var pt = HitPoint(screen);
        if (pt != null)
        {
            _points.Remove(pt);
            foreach (var r in _regions.Where(r => r.PointIds.Contains(pt.Id)).ToList())
                DropVertex(r, pt.Id, removePointAlreadyGone: true);
            PruneTinyRegions();
            return true;
        }

        // 2) 区域：边(含向内宽带)=>降级；深处内部=>删除整个
        for (int i = _regions.Count - 1; i >= 0; i--)
        {
            var r = _regions[i];
            if (!OwnerValid(r.Owner) || r.PointIds.Count < 2) continue;
            var pts = RegionScreenPts(r);
            if (pts.Count < 2) continue;

            if (pts.Count == 2)
            {
                if (DistToSegment(screen, pts[0], pts[1]) <= MarkerLineHitTol) { RemoveRegionFull(r); return true; }
                continue;
            }

            float eDist = RegionEdgeDist(r, screen);
            if (eDist <= MarkerEdgeInnerTol)
            {
                var (ea, eb) = RegionNearestEdgeIdx(r, screen);
                var ep = RegionPoints(r);
                var pa = ep[ea]; var pb = ep[eb];
                var near = Dist(screen, ImgToScreen(r.Owner, pa.P)) <= Dist(screen, ImgToScreen(r.Owner, pb.P)) ? pa : pb;
                DropVertex(r, near.Id, removePointAlreadyGone: false);
                PruneTinyRegions();
                return true;
            }
            if (RegionInteriorHit(r, pts, screen)) { RemoveRegionFull(r); return true; } // #删内部连顶点一起删
        }

        _undo.RemoveAt(_undo.Count - 1); // 未命中 => 回退脏记录
        return false;
    }

    /// <summary>删除整个区域及其所有顶点（点只属于此区域，故一并删除）</summary>
    private void RemoveRegionFull(MarkerRegion r)
    {
        var ids = new HashSet<int>(r.PointIds);
        _regions.Remove(r);
        _points.RemoveAll(p => ids.Contains(p.Id));
        if (_selected == r) _selected = null;
        PruneTinyRegions();
    }

    /// <summary>Del：删除当前选中的区域/线/点（复用 RemoveRegionFull，连顶点一起删）</summary>
    private bool DeleteSelectedMarker()
    {
        if (_selected == null) return false;
        PushUndo();
        RemoveRegionFull(_selected);
        this.Invalidate();
        return true;
    }

    /// <summary>从区域移除一个顶点 => 4→3→2，<2 删除区域</summary>
    private void DropVertex(MarkerRegion region, int pointId, bool removePointAlreadyGone)
    {
        region.PointIds.Remove(pointId);
        if (!removePointAlreadyGone)
        {
            var p = _points.FirstOrDefault(x => x.Id == pointId);
            if (p != null) _points.Remove(p);
        }
        if (region.PointIds.Count < 4) region.Ellipse = false;
        if (region.PointIds.Count >= 3) region.PointIds = OrderConvex(region.PointIds);
    }

    private float RegionEdgeDist(MarkerRegion r, PointF screen)
    {
        var pts = RegionScreenPts(r);
        if (pts.Count < 2) return float.MaxValue;
        if (pts.Count == 2) return DistToSegment(screen, pts[0], pts[1]);
        float best = float.MaxValue;
        for (int i = 0; i < pts.Count; i++)
            best = Math.Min(best, DistToSegment(screen, pts[i], pts[(i + 1) % pts.Count]));
        return best;
    }

    private (int a, int b) RegionNearestEdgeIdx(MarkerRegion r, PointF screen)
    {
        var pts = RegionScreenPts(r);
        int bestA = -1, bestB = -1; float best = float.MaxValue;
        int edges = pts.Count >= 3 ? pts.Count : 1;
        for (int i = 0; i < edges; i++)
        {
            int a = i, b = (i + 1) % pts.Count;
            float d = DistToSegment(screen, pts[a], pts[b]);
            if (d < best) { best = d; bestA = a; bestB = b; }
        }
        return (bestA, bestB);
    }

    private bool RegionInteriorHit(MarkerRegion r, List<PointF> pts, PointF screen)
    {
        if (r.Ellipse && pts.Count >= 4) return PointInEllipse(screen, BoundsOf(pts));
        return pts.Count >= 3 && PointInPolygon(screen, pts);
    }

    #endregion

    #region 绘制

    private void DrawMarkers(Graphics g)
    {
        using var pen = new Pen(MarkerColor, 2);
        using var selPen = new Pen(MarkerSelectedColor, 3);
        using var fillBrush = new SolidBrush(Color.FromArgb(55, MarkerColor));
        using var pointBrush = new SolidBrush(MarkerColor);
        using var idFont = new Font("Microsoft YaHei UI", 8F);

        // 区域（2点线 / 3三角 / 4矩形 / 椭圆）
        foreach (var r in _regions)
        {
            if (!OwnerValid(r.Owner) || r.PointIds.Count < 2) continue;
            var st = g.Save();
            g.SetClip(MarkerCell(r.Owner));
            var active = r == _selected;
            DrawRegionShape(g, RegionScreenPts(r), r.Ellipse, active ? selPen : pen, fillBrush, fillOutline: active);
            g.Restore(st);
        }

        // 点 + 连续编号；选中区域的顶点用蓝色高亮环
        int label = 0;
        foreach (var p in _points)
        {
            label++;
            if (!OwnerValid(p.Owner)) continue;
            var st = g.Save();
            g.SetClip(MarkerCell(p.Owner));
            var sp = ImgToScreen(p.Owner, p.P);
            DrawMarkerDot(g, sp, pointBrush, pen);
            if (_selected != null && _selected.PointIds.Contains(p.Id))
                g.DrawEllipse(selPen, sp.X - 6, sp.Y - 6, 12, 12);
            g.DrawString(label.ToString(), idFont, Brushes.White, sp.X + 6, sp.Y - 6);
            g.Restore(st);
        }

        if (_midDown && _midMoved)
            DrawPendingDrag(g, pen, pointBrush);
    }

    private static void DrawMarkerDot(Graphics g, PointF c, Brush fill, Pen pen)
    {
        g.FillEllipse(fill, c.X - 4, c.Y - 4, 8, 8);
        g.DrawEllipse(pen, c.X - 4, c.Y - 4, 8, 8);
    }

    private void DrawRegionShape(Graphics g, List<PointF> pts, bool ellipse, Pen pen, Brush fill, bool fillOutline)
    {
        if (pts.Count == 2) { g.DrawLine(pen, pts[0], pts[1]); return; }
        if (pts.Count < 3) return;
        var st = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (ellipse && pts.Count >= 4)
        {
            var r = BoundsOf(pts);
            if (fillOutline) g.FillEllipse(fill, r);
            g.DrawEllipse(pen, r);
        }
        else
        {
            if (fillOutline) g.FillPolygon(fill, pts.ToArray());
            g.DrawPolygon(pen, pts.ToArray());
        }
        g.Restore(st);
    }

    private void DrawPendingDrag(Graphics g, Pen pen, Brush pointBrush)
    {
        var cur = this.PointToClient(Cursor.Position);
        int owner = HitTest(cur);
        if (owner < 0) owner = HitTest(_midDownPos);
        if (!OwnerValid(owner)) return;

        var startImg = ScreenToImg(owner, _midDownPos);
        var curImg = ScreenToImg(owner, cur);
        var a = ImgToScreen(owner, startImg);
        var b = ImgToScreen(owner, curImg);

        var st = g.Save();
        g.SetClip(MarkerCell(owner));
        if (_midShift)
        {
            var r = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            if (_areaEllipse) g.DrawEllipse(pen, r); else g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
        else
        {
            g.DrawLine(pen, a, b);
            DrawMarkerDot(g, a, pointBrush, pen);
            DrawMarkerDot(g, b, pointBrush, pen);
        }
        g.Restore(st);
    }

    #endregion

    #region 几何工具

    private static float Dist(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static float DistToSegment(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        if (len2 == 0) return Dist(p, a);
        float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Dist(p, new PointF(a.X + t * dx, a.Y + t * dy));
    }

    private static RectangleF BoundsOf(List<PointF> pts)
    {
        float l = pts.Min(p => p.X), t = pts.Min(p => p.Y);
        float r = pts.Max(p => p.X), b = pts.Max(p => p.Y);
        return RectangleF.FromLTRB(l, t, r, b);
    }

    private static RectangleF GetPointsBounds(PointF[] pts)
    {
        float l = pts.Min(p => p.X), t = pts.Min(p => p.Y);
        float r = pts.Max(p => p.X), b = pts.Max(p => p.Y);
        return RectangleF.FromLTRB(l, t, r, b);
    }

    private static bool PointInEllipse(PointF p, RectangleF r)
    {
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        float rx = r.Width / 2, ry = r.Height / 2;
        if (rx <= 0 || ry <= 0) return false;
        float dx = (p.X - cx) / rx, dy = (p.Y - cy) / ry;
        return dx * dx + dy * dy <= 1.0f;
    }

    private static bool PointInPolygon(PointF p, List<PointF> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }

    #endregion

    #region 复制（CF_HTML 图文并茂 file:// + 文本；区域点不重复列出）

    private string OwnerName(int owner) =>
        owner >= 0 && owner < _imagePaths.Length ? Path.GetFileName(_imagePaths[owner]) : $"#{owner}";

    private void CopyMarkersToClipboard()
    {
        PruneTinyRegions();
        // 被区域(含线)引用的点不再单独列出（#6 去重）
        var inRegions = new HashSet<int>(_regions.SelectMany(r => r.PointIds));

        var groups = new List<object>();
        for (int i = 0; i < _imageCount; i++)
        {
            var standalonePts = _points.Where(p => p.Owner == i && !inRegions.Contains(p.Id)).ToList();
            var lines = _regions.Where(r => r.Owner == i && r.PointIds.Count == 2).ToList();
            var polys = _regions.Where(r => r.Owner == i && r.PointIds.Count >= 3).ToList();
            if (standalonePts.Count + lines.Count + polys.Count == 0) continue;

            groups.Add(new
            {
                image = OwnerName(i),
                points = standalonePts.Select(p => new { x = (int)p.P.X, y = (int)p.P.Y }).ToList(),
                lines = lines.Select(l => new { @from = Pt(l, 0), to = Pt(l, 1) }).ToList(),
                regions = polys.Select(r => new
                {
                    shape = (r.Ellipse && r.PointIds.Count >= 4) ? "ellipse" : r.PointIds.Count == 3 ? "triangle" : r.PointIds.Count == 4 ? "rectangle" : "polygon",
                    points = r.PointIds.Select(id => _points.FirstOrDefault(pp => pp.Id == id))
                              .Where(p => p != null).Select(p => new { x = (int)p!.P.X, y = (int)p.P.Y }).ToList()
                }).ToList()
            });
        }
        string text = System.Text.Json.JsonSerializer.Serialize(groups,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        var regionImages = CollectRegionImages();
        ClipboardExporter.WriteCopy(text, regionImages);
        foreach (var (_, bmp) in regionImages) bmp.Dispose();
    }

    private object Pt(MarkerRegion r, int idx)
    {
        var p = _points.FirstOrDefault(pp => pp.Id == r.PointIds[idx]);
        return new { x = (int)(p?.P.X ?? 0), y = (int)(p?.P.Y ?? 0) };
    }

    /// <summary>收集所有 >=3 点区域的透明位图</summary>
    private List<(string name, Bitmap bmp)> CollectRegionImages()
    {
        var list = new List<(string, Bitmap)>();
        foreach (var r in _regions)
        {
            if (!OwnerValid(r.Owner) || r.PointIds.Count < 3) continue;
            var imgPts = RegionPoints(r).Select(p => new PointF(p.P.X, p.P.Y)).ToArray();
            if (imgPts.Length < 3) continue;
            var bounds = GetPointsBounds(imgPts);
            using var path = new GraphicsPath();
            if (r.Ellipse && imgPts.Length >= 4) path.AddEllipse(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            else path.AddPolygon(imgPts);
            var bmp = CropRegion(_images[r.Owner]!, bounds, path);
            if (bmp != null) list.Add((OwnerName(r.Owner), bmp));
        }
        return list;
    }

    private static Bitmap? CropRegion(Image src, RectangleF raw, GraphicsPath? maskRaw)
    {
        int W = src.Width, H = src.Height;
        float L = Math.Max(0, raw.Left), T = Math.Max(0, raw.Top);
        float R = Math.Min(W, raw.Right), B = Math.Min(H, raw.Bottom);
        int w = (int)Math.Round(R - L), h = (int)Math.Round(B - T);
        if (w <= 0 || h <= 0) return null;

        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var gr = Graphics.FromImage(bmp);
        gr.Clear(Color.Transparent);
        gr.InterpolationMode = InterpolationMode.NearestNeighbor;
        gr.PixelOffsetMode = PixelOffsetMode.Half;

        GraphicsPath? shifted = null;
        if (maskRaw != null)
        {
            shifted = (GraphicsPath)maskRaw.Clone();
            using var mtx = new Matrix();
            mtx.Translate(-L, -T, MatrixOrder.Prepend);
            shifted.Transform(mtx);
            gr.SetClip(shifted);
        }
        gr.DrawImage(src, new Rectangle(0, 0, w, h), (int)L, (int)T, w, h, GraphicsUnit.Pixel);
        shifted?.Dispose();
        return bmp;
    }

    #endregion

    /// <summary>清空所有标记（切换图片组/重启时）</summary>
    private void ClearAllMarkers()
    {
        _points.Clear();
        _regions.Clear();
        _undo.Clear();
        _selected = null;
        _midDown = false;
        _midMoved = false;
    }
}
