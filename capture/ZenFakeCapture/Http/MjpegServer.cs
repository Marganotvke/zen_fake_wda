using System.Net;
using System.Text;

namespace ZenFakeCapture.Http;

internal sealed class MjpegServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Func<byte[]?> _frameProvider;
    private readonly Func<int> _frameIntervalMs;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public MjpegServer(string host, int port, Func<byte[]?> frameProvider, Func<int> frameIntervalMs)
    {
        _frameProvider = frameProvider;
        _frameIntervalMs = frameIntervalMs;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{host}:{port}/");
    }

    public string BaseUrl => _listener.Prefixes.First().TrimEnd('/');

    public void Start()
    {
        _listener.Start();
        _loopTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (ctx != null)
            {
                _ = Task.Run(() => HandleRequestAsync(ctx));
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        if (ctx == null)
        {
            return;
        }

        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (path.Equals("/shutdown", StringComparison.OrdinalIgnoreCase) &&
                ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
                _cts.Cancel();
                return;
            }

            if (!path.Equals("/stream", StringComparison.OrdinalIgnoreCase))
            {
                var body = Encoding.UTF8.GetBytes("ZenFakeCapture — use GET /stream or POST /shutdown");
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.Response.OutputStream.WriteAsync(body);
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "multipart/x-mixed-replace; boundary=zenfake";
            ctx.Response.SendChunked = true;
            var stream = ctx.Response.OutputStream;
            var preamble = Encoding.ASCII.GetBytes("--zenfake\r\n");
            await stream.WriteAsync(preamble);

            while (!_cts.IsCancellationRequested && ctx.Response.OutputStream.CanWrite)
            {
                byte[]? frame = null;
                try
                {
                    frame = _frameProvider();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"ZenFakeCapture frame capture failed: {ex.Message}");
                }

                if (frame == null || frame.Length == 0)
                {
                    await Task.Delay(50, _cts.Token);
                    continue;
                }

                var header = Encoding.ASCII.GetBytes(
                    $"\r\n--zenfake\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n"
                );
                await stream.WriteAsync(header);
                await stream.WriteAsync(frame);
                await stream.FlushAsync();

                // Pace to capture FPS; avoid busy-looping when paused/minimized.
                await Task.Delay(_frameIntervalMs(), _cts.Token);
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Client disconnected or server shutting down.
        }
        finally
        {
            try
            {
                ctx.Response.OutputStream.Close();
            }
            catch
            {
                // ignored
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // ignored
        }

        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignored
        }

        _cts.Dispose();
    }
}
