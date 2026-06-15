using ZenFakeCapture.Capture;
using ZenFakeCapture.Http;
using ZenFakeCapture.Prefs;
using ZenFakeCapture.Win32;

namespace ZenFakeCapture;

internal static class Program
{
    private const string PrefFps = "zen.fake_transparency.background_fps";
    private const string PrefScale = "zen.fake_transparency.capture_scale";
    private const int PrefPollMs = 5000;
    private const int ProcessCheckMs = 1000;

    [STAThread]
    private static int Main(string[] args)
    {
        var options = CliOptions.Parse(args);
        using var mutex = new Mutex(true, $"ZenFakeCapture-{options.WatchPid}", out var created);
        if (!created)
        {
            Console.Error.WriteLine("Another capture instance is already running for this session.");
            return 1;
        }

        var profilePath = options.ProfilePath;
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            Console.Error.WriteLine("error: --profile is required (Zen profile directory)");
            return 2;
        }

        var prefs = new PrefWatcher(profilePath);
        prefs.TryReloadIfChanged(0);

        var fps = prefs.GetInt(PrefFps, options.Fps);
        var scale = prefs.GetInt(PrefScale, options.CaptureScalePercent);
        var frameIntervalMs = Math.Max(1000 / Math.Max(1, fps), 16);

        ICaptureEngine? engine = CaptureEngineFactory.Create(
            options.WatchPid,
            scale,
            options.JpegQuality,
            options.UseWda
        );
        var frames = new FrameBuffer();
        using var cts = new CancellationTokenSource();

        Console.WriteLine(
            $"ZenFakeCapture pid={Environment.ProcessId} watch={options.WatchPid} mode={engine.ModeName} scale={scale}% fps={fps}"
        );

        var server = new MjpegServer(
            options.BindHost,
            options.Port,
            frames.GetLatest,
            () => frameIntervalMs
        );
        server.Start();
        Console.WriteLine($"MJPEG stream: {server.BaseUrl}/stream");

        var lastProcessCheck = 0L;
        var captureTask = Task.Run(async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cts.Token.IsCancellationRequested)
            {
                var now = Environment.TickCount64;
                if (now - lastProcessCheck >= ProcessCheckMs)
                {
                    lastProcessCheck = now;
                    if (!WindowHelper.IsProcessAlive(options.WatchPid))
                    {
                        cts.Cancel();
                        break;
                    }
                }

                if (prefs.TryReloadIfChanged(PrefPollMs))
                {
                    fps = prefs.GetInt(PrefFps, options.Fps);
                    var newScale = prefs.GetInt(PrefScale, options.CaptureScalePercent);
                    frameIntervalMs = Math.Max(1000 / Math.Max(1, fps), 16);
                    if (newScale != scale)
                    {
                        scale = newScale;
                        engine.Dispose();
                        engine = CaptureEngineFactory.Create(
                            options.WatchPid,
                            scale,
                            options.JpegQuality,
                            options.UseWda
                        );
                    }
                }

                var jpeg = engine.CaptureFrame();
                if (jpeg != null && jpeg.Length > 0)
                {
                    frames.Set(jpeg);
                }

                var elapsed = (int)sw.ElapsedMilliseconds;
                var delay = engine.IsPaused
                    ? Math.Max(frameIntervalMs, 500)
                    : Math.Max(0, frameIntervalMs - elapsed);
                sw.Restart();
                try
                {
                    await Task.Delay(delay, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, cts.Token);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            captureTask.Wait();
        }
        catch (AggregateException)
        {
            // expected on cancel
        }

        engine.Dispose();
        server.Dispose();
        return 0;
    }
}
