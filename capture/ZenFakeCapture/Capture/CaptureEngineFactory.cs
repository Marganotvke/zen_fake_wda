namespace ZenFakeCapture.Capture;

internal static class CaptureEngineFactory
{
    internal static ICaptureEngine Create(int watchPid, int scale, int jpegQuality, bool preferWda)
    {
        if (preferWda)
        {
            return new WdaCaptureEngine(watchPid, scale, jpegQuality);
        }

        return new HoleBufferEngine(watchPid, scale, jpegQuality);
    }
}
