using System.Runtime.InteropServices;

namespace SnowDesk;

/// <summary>
/// Click-through layered window over the virtual screen.
/// It is topmost only while snow is showing, never activates, and is hidden once the fade ends.
/// </summary>
sealed class SnowOverlay : NativeWindow, IDisposable
{
    const int ExStyle =
        NativeMethods.WsExLayered |
        NativeMethods.WsExTransparent |
        NativeMethods.WsExToolWindow |
        NativeMethods.WsExNoActivate |
        NativeMethods.WsExTopmost;

    int _x, _y, _w, _h;
    IntPtr _screenDc;
    IntPtr _memDc;
    IntPtr _dib;
    IntPtr _bits;
    IntPtr _oldBitmap;
    bool _shown;

    public bool IsShown => _shown;

    public void EnsureSize(Scene scene)
    {
        if (scene.Width <= 0 || scene.Height <= 0)
            return;

        bool same = Handle != IntPtr.Zero
                    && scene.Width == _w
                    && scene.Height == _h
                    && scene.OriginX == _x
                    && scene.OriginY == _y
                    && _bits != IntPtr.Zero;
        if (same)
            return;

        long pixels = (long)scene.Width * scene.Height;
        if (pixels <= 0 || pixels > 16_000_000)
            return;

        _x = scene.OriginX;
        _y = scene.OriginY;
        _w = scene.Width;
        _h = scene.Height;

        if (Handle == IntPtr.Zero)
        {
            CreateHandle(new CreateParams
            {
                Caption = "SnowDesk Overlay",
                X = _x,
                Y = _y,
                Width = _w,
                Height = _h,
                Style = NativeMethods.WsPopup,
                ExStyle = ExStyle,
            });
            NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GwlExStyle, (IntPtr)ExStyle);
        }
        else
        {
            NativeMethods.SetWindowPos(Handle, NativeMethods.HwndTopmost, _x, _y, _w, _h, NativeMethods.SwpNoActivate);
        }

        RecreateDib();
    }

    public void ShowOverlay()
    {
        if (Handle == IntPtr.Zero)
            return;
        NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HwndTopmost,
            _x,
            _y,
            _w,
            _h,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        _shown = true;
    }

    public void HideOverlay()
    {
        if (Handle == IntPtr.Zero || !_shown)
            return;
        NativeMethods.ShowWindow(Handle, NativeMethods.SwHide);
        _shown = false;
    }

    public void Render(SnowSimulation simulation, float opacity)
    {
        if (Handle == IntPtr.Zero || _bits == IntPtr.Zero || _memDc == IntPtr.Zero)
            return;

        long bytes = (long)_w * _h * 4;
        if (bytes <= 0 || bytes > int.MaxValue)
            return;

        unsafe
        {
            var pixels = new Span<byte>((void*)_bits, (int)bytes);
            pixels.Clear();
            SnowRasterizer.Render(pixels, _w, _h, simulation, opacity);
        }

        var destination = new NativeMethods.Point { X = _x, Y = _y };
        var size = new NativeMethods.Size { Cx = _w, Cy = _h };
        var source = new NativeMethods.Point();
        var blend = new NativeMethods.BlendFunction
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1,
        };
        NativeMethods.UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size, _memDc, ref source, 0, ref blend, NativeMethods.UlwAlpha);
    }

    public void ReleaseBuffer() => DestroyDib();

    public void Dispose()
    {
        ReleaseBuffer();
        HideOverlay();
        if (Handle != IntPtr.Zero)
            DestroyHandle();
    }

    void RecreateDib()
    {
        DestroyDib();
        _screenDc = NativeMethods.GetDC(IntPtr.Zero);
        _memDc = NativeMethods.CreateCompatibleDC(_screenDc);
        var info = new NativeMethods.BitmapInfo
        {
            bmiHeader = new NativeMethods.BitmapInfoHeader
            {
                biSize = Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                biWidth = _w,
                biHeight = -_h,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            },
        };
        _dib = NativeMethods.CreateDIBSection(_screenDc, ref info, 0, out _bits, IntPtr.Zero, 0);
        if (_dib != IntPtr.Zero)
            _oldBitmap = NativeMethods.SelectObject(_memDc, _dib);
    }

    void DestroyDib()
    {
        if (_memDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero)
            NativeMethods.SelectObject(_memDc, _oldBitmap);
        _oldBitmap = IntPtr.Zero;
        if (_dib != IntPtr.Zero)
            NativeMethods.DeleteObject(_dib);
        _dib = IntPtr.Zero;
        _bits = IntPtr.Zero;
        if (_memDc != IntPtr.Zero)
            NativeMethods.DeleteDC(_memDc);
        _memDc = IntPtr.Zero;
        if (_screenDc != IntPtr.Zero)
            NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);
        _screenDc = IntPtr.Zero;
    }
}
