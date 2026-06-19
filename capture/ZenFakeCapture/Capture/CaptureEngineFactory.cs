namespace ZenFakeCapture.Capture;

internal static class CaptureEngineFactory
{
    internal static ICaptureEngine Create(
        int watchPid,
        int scale,
        int jpegQuality,
        bool preferWda,
        int fullRefreshMs = 0,
        bool fullHide = false
    )
    {
        if (preferWda)
        {
            return new WdaCaptureEngine(watchPid, scale, jpegQuality);
        }

        if (fullHide)
        {
            return new FullHideCaptureEngine(watchPid, scale, jpegQuality);
        }

        return new HoleBufferEngine(watchPid, scale, jpegQuality, fullRefreshMs);
    }
}
