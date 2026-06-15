namespace ZenFakeCapture.Capture;

internal interface ICaptureEngine : IDisposable
{
    string ModeName { get; }

    bool IsPaused { get; }

    byte[]? CaptureFrame();
}
