using System.Drawing;
using System.Drawing.Imaging;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using DCommon = Vortice.DCommon;

namespace ComparePhotoInExploer;

/// <summary>单张图片的 GPU 绘制任务：源 GDI+ 位图、目标矩形(设备像素)、源子矩形(位图像素)、裁剪矩形。</summary>
internal readonly struct D2DJob
{
    public readonly Bitmap Src;
    public readonly float Dx, Dy, Dw, Dh;
    public readonly float Sx, Sy, Sw, Sh;
    public readonly float ClipL, ClipT, ClipR, ClipB;
    public readonly int InterpCode; // 0=NearestNeighbor,1=Linear,2=Fant(≈双三次)

    public D2DJob(Bitmap src, float dx, float dy, float dw, float dh,
                  float sx, float sy, float sw, float sh,
                  float clipL, float clipT, float clipR, float clipB, int interpCode = 1)
    {
        Src = src; Dx = dx; Dy = dy; Dw = dw; Dh = dh;
        Sx = sx; Sy = sy; Sw = sw; Sh = sh;
        ClipL = clipL; ClipT = clipT; ClipR = clipR; ClipB = clipB; InterpCode = interpCode;
    }
}

/// <summary>
/// Direct2D DC 渲染目标：把大图缩放绘制交给 GPU（GDI 兼容，绑定到 GDI+ 后缓冲的 HDC），
/// 与现有 GDI+ 共存。GDI+ 负责背景/文字/UI，D2D 只画图片。任一环节异常由调用方回退到 GDI+ DrawImage。
/// </summary>
internal sealed class D2DImageRenderer : IDisposable
{
    private readonly ID2D1Factory _factory;
    private readonly ID2D1DCRenderTarget _target;
    private readonly Dictionary<Bitmap, ID2D1Bitmap> _cache = new();

    private static readonly DCommon.PixelFormat PixFmt = new()
    {
        Format = Format.B8G8R8A8_UNorm,
        AlphaMode = DCommon.AlphaMode.Ignore
    };

    public D2DImageRenderer()
    {
        _factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.SingleThreaded);
        float dpiX = 96f, dpiY = 96f;
        _factory.GetDesktopDpi(out dpiX, out dpiY);

        var rtp = new RenderTargetProperties(
            RenderTargetType.Default, PixFmt, dpiX, dpiY,
            RenderTargetUsage.GdiCompatible, FeatureLevel.Default);
        _target = _factory.CreateDCRenderTarget(rtp);
    }

    /// <summary>源位图变化(重新加载)时调用，丢弃已失效的 GPU 位图缓存。</summary>
    public void ClearBitmapCache()
    {
        foreach (var b in _cache.Values) b.Dispose();
        _cache.Clear();
    }

    public void Render(IntPtr hdc, Rectangle bounds, IReadOnlyList<D2DJob> jobs)
    {
        _target.BindDC(hdc, new RawRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
        _target.BeginDraw();
        foreach (var j in jobs)
        {
            var bmp = GetOrCreate(j.Src);
            if (bmp == null) continue;

            _target.PushAxisAlignedClip(new RawRectF(j.ClipL, j.ClipT, j.ClipR, j.ClipB), AntialiasMode.Aliased);
            var interp = j.InterpCode == 0
                ? BitmapInterpolationMode.NearestNeighbor
                : BitmapInterpolationMode.Linear;
            _target.DrawBitmap(bmp,
                new RawRectF(j.Dx, j.Dy, j.Dx + j.Dw, j.Dy + j.Dh),
                1f,
                interp,
                new RawRectF(j.Sx, j.Sy, j.Sx + j.Sw, j.Sy + j.Sh));
            _target.PopAxisAlignedClip();
        }
        _target.EndDraw();
    }

    private ID2D1Bitmap? GetOrCreate(Bitmap src)
    {
        if (_cache.TryGetValue(src, out var cached)) return cached;
        try
        {
            var rect = new Rectangle(0, 0, src.Width, src.Height);
            var data = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var props = new BitmapProperties(PixFmt);
                var created = _target.CreateBitmap(
                    new SizeI(src.Width, src.Height), data.Scan0, (uint)data.Stride, props);
                _cache[src] = created;
                return created;
            }
            finally { src.UnlockBits(data); }
        }
        catch { return null; }
    }

    public void Dispose()
    {
        ClearBitmapCache();
        _target.Dispose();
        _factory.Dispose();
    }
}
