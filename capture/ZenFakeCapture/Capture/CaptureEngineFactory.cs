namespace ZenFakeCapture.Capture;

internal static class CaptureEngineFactory
{
    internal static ICaptureEngine Create(int watchPid, int scale, int jpegQuality, bool preferWda)
    {
        if (preferWda)
        {
            var wda = new WdaCaptureEngine(watchPid, scale, jpegQuality);
            if (wda.WdaActive)
            {
                return wda;
            }

            wda.Dispose();
            Console.WriteLine("WDA_EXCLUDEFROMCAPTURE unavailable — falling back to hole-buffer capture");
        }

        return new HoleBufferEngine(watchPid, scale, jpegQuality);
    }
}
