using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ZenFakeCapture.Win32;

namespace ZenFakeCapture.Capture;

/// <summary>
/// Hole-buffer desktop capture at a unified scale.
/// Copies only monitor strips outside the Zen window each frame (not full-screen).
/// </summary>
internal sealed class HoleBufferEngine : ICaptureEngine
{
    private static readonly ImageCodecInfo JpegCodec =
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    private readonly int _watchPid;
    private readonly int _scalePercent;
    private readonly int _jpegQuality;
    private readonly EncoderParameters _jpegParams;
    private readonly MemoryStream _jpegStream = new(256 * 1024);

    private Bitmap? _buffer;
    private Graphics? _bufferGraphics;
    private Bitmap? _scratch;
    private Rectangle _monitorBounds;
    private Rectangle _scaledBounds;
    private float _scale = 1f;
    private bool _bufferSeeded;

    public HoleBufferEngine(int watchPid, int scalePercent, int jpegQuality)
    {
        _watchPid = watchPid;
        _scalePercent = Math.Clamp(scalePercent, 10, 100);
        _jpegQuality = Math.Clamp(jpegQuality, 30, 95);
        _jpegParams = new EncoderParameters(1);
        _jpegParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_jpegQuality);
    }

    /// <summary>Skip capture when Zen is minimized; keeps last encoded frame.</summary>
    public bool IsPaused { get; private set; }

    public string ModeName => "hole-buffer";

    public byte[]? CaptureFrame()
    {
        _monitorBounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        _scale = _scalePercent / 100f;
        _scaledBounds = new Rectangle(
            0,
            0,
            Math.Max(1, (int)(_monitorBounds.Width * _scale)),
            Math.Max(1, (int)(_monitorBounds.Height * _scale))
        );

        EnsureBuffer();

        var hwnd = WindowHelper.FindMainWindowForProcess(_watchPid);
        if (hwnd != IntPtr.Zero && WindowHelper.IsWindowMinimized(hwnd))
        {
            IsPaused = true;
            return null;
        }

        IsPaused = false;
        var holeNative = GetHoleRectNative(hwnd);

        if (!_bufferSeeded || holeNative.IsEmpty)
        {
            SeedBufferFull();
        }
        else
        {
            CopyStripsOutsideHole(holeNative);
        }

        return EncodeJpeg(_buffer!);
    }

    private void EnsureBuffer()
    {
        if (_buffer != null &&
            _buffer.Width == _scaledBounds.Width &&
            _buffer.Height == _scaledBounds.Height)
        {
            return;
        }

        DisposeGraphics();
        _buffer?.Dispose();
        _scratch?.Dispose();
        _scratch = null;
        _bufferSeeded = false;

        _buffer = new Bitmap(_scaledBounds.Width, _scaledBounds.Height, PixelFormat.Format32bppArgb);
        _bufferGraphics = Graphics.FromImage(_buffer);
        _bufferGraphics.InterpolationMode = InterpolationMode.Bilinear;
        _bufferGraphics.CompositingMode = CompositingMode.SourceCopy;
    }

    private void SeedBufferFull()
    {
        BlitStripFromScreen(
            new Rectangle(0, 0, _monitorBounds.Width, _monitorBounds.Height),
            new Rectangle(0, 0, _scaledBounds.Width, _scaledBounds.Height)
        );
        _bufferSeeded = true;
    }

    private void CopyStripsOutsideHole(Rectangle holeNative)
    {
        var w = _monitorBounds.Width;
        var h = _monitorBounds.Height;

        if (holeNative.Top > 0)
        {
            BlitStripFromScreen(
                new Rectangle(0, 0, w, holeNative.Top),
                new Rectangle(0, 0, _scaledBounds.Width, ScaleLen(holeNative.Top))
            );
        }

        if (holeNative.Bottom < h)
        {
            var srcY = holeNative.Bottom;
            var srcH = h - srcY;
            var dstY = ScaleLen(holeNative.Bottom);
            BlitStripFromScreen(
                new Rectangle(0, srcY, w, srcH),
                new Rectangle(0, dstY, _scaledBounds.Width, _scaledBounds.Height - dstY)
            );
        }

        var midSrcTop = holeNative.Top;
        var midSrcH = holeNative.Height;

        if (holeNative.Left > 0)
        {
            BlitStripFromScreen(
                new Rectangle(0, midSrcTop, holeNative.Left, midSrcH),
                new Rectangle(0, ScaleLen(midSrcTop), ScaleLen(holeNative.Left), ScaleLen(midSrcH))
            );
        }

        if (holeNative.Right < w)
        {
            var srcX = holeNative.Right;
            var srcW = w - srcX;
            BlitStripFromScreen(
                new Rectangle(srcX, midSrcTop, srcW, midSrcH),
                new Rectangle(ScaleLen(srcX), ScaleLen(midSrcTop), ScaleLen(srcW), ScaleLen(midSrcH))
            );
        }
    }

    private void BlitStripFromScreen(Rectangle srcNative, Rectangle dstScaled)
    {
        if (srcNative.Width <= 0 || srcNative.Height <= 0 ||
            dstScaled.Width <= 0 || dstScaled.Height <= 0)
        {
            return;
        }

        EnsureScratch(srcNative.Width, srcNative.Height);
        using (var g = Graphics.FromImage(_scratch!))
        {
            g.CopyFromScreen(
                _monitorBounds.Left + srcNative.Left,
                _monitorBounds.Top + srcNative.Top,
                0,
                0,
                srcNative.Size,
                CopyPixelOperation.SourceCopy
            );
        }

        // Draw only the captured strip — scratch may be larger from a prior frame.
        var srcRect = new Rectangle(0, 0, srcNative.Width, srcNative.Height);
        _bufferGraphics!.DrawImage(_scratch, dstScaled, srcRect, GraphicsUnit.Pixel);
    }

    private void EnsureScratch(int width, int height)
    {
        if (_scratch != null &&
            _scratch.Width == width &&
            _scratch.Height == height)
        {
            return;
        }

        _scratch?.Dispose();
        _scratch = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    }

    private int ScaleLen(int nativeLen) => Math.Max(1, (int)(nativeLen * _scale));

    private Rectangle GetHoleRectNative(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !WindowHelper.TryGetWindowRect(hwnd, out var win))
        {
            return Rectangle.Empty;
        }

        var rel = Rectangle.Intersect(
            new Rectangle(
                win.Left - _monitorBounds.Left,
                win.Top - _monitorBounds.Top,
                win.Width,
                win.Height
            ),
            new Rectangle(0, 0, _monitorBounds.Width, _monitorBounds.Height)
        );

        return rel.Width <= 0 || rel.Height <= 0 ? Rectangle.Empty : rel;
    }

    private byte[] EncodeJpeg(Bitmap bitmap)
    {
        _jpegStream.SetLength(0);
        bitmap.Save(_jpegStream, JpegCodec, _jpegParams);
        return _jpegStream.ToArray();
    }

    private void DisposeGraphics()
    {
        _bufferGraphics?.Dispose();
        _bufferGraphics = null;
    }

    public void Dispose()
    {
        DisposeGraphics();
        _buffer?.Dispose();
        _buffer = null;
        _scratch?.Dispose();
        _scratch = null;
        _jpegParams.Dispose();
        _jpegStream.Dispose();
    }
}
