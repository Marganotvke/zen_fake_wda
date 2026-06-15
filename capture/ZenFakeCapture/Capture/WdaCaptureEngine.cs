using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ZenFakeCapture.Win32;

namespace ZenFakeCapture.Capture;

/// <summary>
/// Full-monitor capture after excluding the Zen window via WDA_EXCLUDEFROMCAPTURE.
/// Simpler and usually faster than hole-buffer strip compositing.
/// </summary>
internal sealed class WdaCaptureEngine : ICaptureEngine
{
    private const int MaxAffinityAttempts = 90;

    private static readonly ImageCodecInfo JpegCodec =
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    private readonly int _watchPid;
    private readonly int _scalePercent;
    private readonly int _jpegQuality;
    private readonly EncoderParameters _jpegParams;
    private readonly MemoryStream _jpegStream = new(256 * 1024);

    private Bitmap? _buffer;
    private Graphics? _bufferGraphics;
    private Bitmap? _nativeFrame;
    private Graphics? _nativeGraphics;
    private Rectangle _monitorBounds;
    private Rectangle _scaledBounds;
    private IntPtr _affinityHwnd = IntPtr.Zero;
    private int _affinityAttempts;

    public WdaCaptureEngine(int watchPid, int scalePercent, int jpegQuality)
    {
        _watchPid = watchPid;
        _scalePercent = Math.Clamp(scalePercent, 10, 100);
        _jpegQuality = Math.Clamp(jpegQuality, 30, 95);
        _jpegParams = new EncoderParameters(1);
        _jpegParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_jpegQuality);
    }

    public string ModeName => "wda";

    public bool IsPaused { get; private set; }

    /// <summary>True when WDA cannot be applied; Program should recreate with hole-buffer.</summary>
    public bool FallbackRequested { get; private set; }

    public byte[]? CaptureFrame()
    {
        if (FallbackRequested)
        {
            return null;
        }

        _monitorBounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var scale = _scalePercent / 100f;
        _scaledBounds = new Rectangle(
            0,
            0,
            Math.Max(1, (int)(_monitorBounds.Width * scale)),
            Math.Max(1, (int)(_monitorBounds.Height * scale))
        );

        var hwnd = WindowHelper.FindMainWindowForProcess(_watchPid);
        if (hwnd == IntPtr.Zero || WindowHelper.IsWindowMinimized(hwnd))
        {
            IsPaused = true;
            return null;
        }

        if (!EnsureAffinity(hwnd))
        {
            IsPaused = true;
            _affinityAttempts++;
            if (_affinityAttempts >= MaxAffinityAttempts)
            {
                FallbackRequested = true;
                Console.WriteLine("WDA_EXCLUDEFROMCAPTURE failed after retries — requesting hole-buffer fallback");
            }

            return null;
        }

        _affinityAttempts = 0;
        IsPaused = false;
        EnsureBuffers();

        _nativeGraphics!.CopyFromScreen(
            _monitorBounds.Left,
            _monitorBounds.Top,
            0,
            0,
            _monitorBounds.Size,
            CopyPixelOperation.SourceCopy
        );

        _bufferGraphics!.DrawImage(_nativeFrame, _scaledBounds);
        return EncodeJpeg(_buffer!);
    }

    private bool EnsureAffinity(IntPtr hwnd)
    {
        if (_affinityHwnd != IntPtr.Zero && hwnd == _affinityHwnd)
        {
            return true;
        }

        if (!WindowHelper.TryExcludeFromCapture(hwnd))
        {
            return false;
        }

        if (_affinityHwnd != IntPtr.Zero && _affinityHwnd != hwnd)
        {
            WindowHelper.ClearExcludeFromCapture(_affinityHwnd);
        }

        _affinityHwnd = hwnd;
        Console.WriteLine($"WDA_EXCLUDEFROMCAPTURE enabled on Zen HWND 0x{hwnd:X}");
        return true;
    }

    private void EnsureBuffers()
    {
        if (_buffer != null &&
            _buffer.Width == _scaledBounds.Width &&
            _buffer.Height == _scaledBounds.Height &&
            _nativeFrame != null &&
            _nativeFrame.Width == _monitorBounds.Width &&
            _nativeFrame.Height == _monitorBounds.Height)
        {
            return;
        }

        _bufferGraphics?.Dispose();
        _nativeGraphics?.Dispose();
        _buffer?.Dispose();
        _nativeFrame?.Dispose();

        _nativeFrame = new Bitmap(_monitorBounds.Width, _monitorBounds.Height, PixelFormat.Format32bppArgb);
        _nativeGraphics = Graphics.FromImage(_nativeFrame);
        _buffer = new Bitmap(_scaledBounds.Width, _scaledBounds.Height, PixelFormat.Format32bppArgb);
        _bufferGraphics = Graphics.FromImage(_buffer);
        _bufferGraphics.InterpolationMode = InterpolationMode.Bilinear;
        _bufferGraphics.CompositingMode = CompositingMode.SourceCopy;
    }

    private byte[] EncodeJpeg(Bitmap bitmap)
    {
        _jpegStream.SetLength(0);
        bitmap.Save(_jpegStream, JpegCodec, _jpegParams);
        return _jpegStream.ToArray();
    }

    public void Dispose()
    {
        WindowHelper.ClearExcludeFromCapture(_affinityHwnd);
        _affinityHwnd = IntPtr.Zero;
        _bufferGraphics?.Dispose();
        _nativeGraphics?.Dispose();
        _buffer?.Dispose();
        _nativeFrame?.Dispose();
        _jpegParams.Dispose();
        _jpegStream.Dispose();
    }
}
