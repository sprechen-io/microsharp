using System.Net;
using System.Text;

namespace MicroSharp.Browser.Tests;

internal sealed class LoopbackStaticServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public LoopbackStaticServer(string wwwroot, string? indexHtml = null)
    {
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        Root = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(Root.AbsoluteUri);
        _listener.Start();
        _loop = Task.Run(() => Listen(wwwroot, indexHtml ?? IndexHtml));
    }

    public Uri Root { get; }

    public static string IndexHtml { get; } =
        """
        <!DOCTYPE html>
        <html>
        <body>
        <script type="module">
        import * as mic from "./microsharp-capture.js";
        const pending = [];
        let fault = null;
        window.microsharpDotNet = {
          async invokeMethodAsync(method, arg) {
            if (method === "OnPcmChunk" && arg) {
              const bytes = arg instanceof Uint8Array ? arg : new Uint8Array(arg);
              pending.push(Array.from(bytes));
            }
            if (method === "OnCaptureFault") {
              fault = String(arg ?? "");
            }
          }
        };
        window.microsharpE2E = {
          describe: () => mic.describe(),
          permission: () => mic.permission(),
          requestAccess: (preferences) => mic.requestAccess(preferences),
          acquire: (preferences) => mic.acquire(preferences),
          start: (sessionId, format) => mic.start(sessionId, window.microsharpDotNet, format),
          stop: async (sessionId) => mic.stop(sessionId),
          release: async (sessionId) => mic.release(sessionId),
          takeStatus: () => {
            const chunks = pending.splice(0, pending.length);
            const currentFault = fault;
            fault = null;
            return { chunks, fault: currentFault };
          }
        };
        window.__microsharpReady = true;
        </script>
        </body>
        </html>
        """;

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException)
        {
        }

        _stop.Dispose();
    }

    private async Task Listen(string wwwroot, string indexHtml)
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                break;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            byte[] body;
            string type;
            var status = 200;
            if (path is "/" or "/index.html")
            {
                body = Encoding.UTF8.GetBytes(indexHtml);
                type = "text/html; charset=utf-8";
            }
            else if (path is "/microsharp-capture.js" or "/pcm-recorder-processor.js" or "/pcm-playback-processor.js")
            {
                var file = Path.Combine(wwwroot, path.TrimStart('/'));
                body = await File.ReadAllBytesAsync(file, _stop.Token).ConfigureAwait(false);
                type = "text/javascript; charset=utf-8";
            }
            else
            {
                body = "missing"u8.ToArray();
                type = "text/plain";
                status = 404;
            }

            context.Response.StatusCode = status;
            context.Response.ContentType = type;
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body, _stop.Token).ConfigureAwait(false);
            context.Response.Close();
        }
    }
}
