namespace ZenFakeCapture;

internal sealed class CliOptions
{
    public int WatchPid { get; set; }
    public bool NoTray { get; set; } = true;
    public string ProfilePath { get; set; } = "";
    public string BindHost { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8765;
    public int Fps { get; set; } = 15;
    public int CaptureScalePercent { get; set; } = 50;
    public int JpegQuality { get; set; } = 72;
    public bool UseWda { get; set; } = false;

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--watch-pid" when i + 1 < args.Length:
                    options.WatchPid = int.Parse(args[++i]);
                    break;
                case "--no-tray":
                    options.NoTray = true;
                    break;
                case "--profile" when i + 1 < args.Length:
                    options.ProfilePath = args[++i];
                    break;
                case "--host" when i + 1 < args.Length:
                    options.BindHost = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    options.Port = int.Parse(args[++i]);
                    break;
                case "--fps" when i + 1 < args.Length:
                    options.Fps = int.Parse(args[++i]);
                    break;
                case "--scale" when i + 1 < args.Length:
                    options.CaptureScalePercent = int.Parse(args[++i]);
                    break;
                case "--quality" when i + 1 < args.Length:
                    options.JpegQuality = int.Parse(args[++i]);
                    break;
                case "--wda":
                    options.UseWda = true;
                    break;
                case "--no-wda":
                case "--hole-buffer":
                    options.UseWda = false;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        if (options.WatchPid <= 0)
        {
            Console.Error.WriteLine("error: --watch-pid is required");
            PrintHelp();
            Environment.Exit(2);
        }

        return options;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            ZenFakeCapture — live desktop capture for zen_fake (Windows)

            Usage:
              ZenFakeCapture.exe --watch-pid <zen-pid> [--profile <path>] [--no-tray]
                [--host 127.0.0.1] [--port 8765] [--fps 15] [--scale 50] [--quality 72]
                [--wda] [--hole-buffer]

            Capture modes:
              --wda             SetWindowDisplayAffinity WDA_EXCLUDEFROMCAPTURE + full monitor blit
              --hole-buffer     Strip-based hole buffer (default)

            Endpoints:
              GET /stream   multipart/x-mixed-replace MJPEG
              POST /shutdown
            """
        );
    }
}
