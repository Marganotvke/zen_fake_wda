namespace ZenFakeCapture.Capture;

/// <summary>
/// Thread-safe holder for the latest JPEG frame (reference swap, no disk I/O).
/// </summary>
internal sealed class FrameBuffer
{
    private byte[]? _latest;

    public void Set(byte[] frame) => Volatile.Write(ref _latest, frame);

    public byte[]? GetLatest() => Volatile.Read(ref _latest);
}
