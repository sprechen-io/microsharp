using System.Net;

namespace MicroSharp.Browser.Tests;

internal sealed class PublishedSiteServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public PublishedSiteServer(string root, Action<string, int>? observeResponse = null)
    {
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        Root = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(Root.AbsoluteUri);
        _listener.Start();
        _loop = Task.Run(() => Listen(Path.GetFullPath(root), observeResponse));
    }

    public Uri Root { get; }

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

    private async Task Listen(string root, Action<string, int>? observeResponse)
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

            var relative = (context.Request.Url?.AbsolutePath ?? "/").TrimStart('/');
            if (relative.Length == 0)
            {
                relative = "index.html";
            }

            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            byte[] body;
            var status = 200;
            if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full))
            {
                body = "missing"u8.ToArray();
                status = 404;
            }
            else
            {
                body = await File.ReadAllBytesAsync(full, _stop.Token).ConfigureAwait(false);
            }

            context.Response.StatusCode = status;
            context.Response.ContentType = ContentType(full);
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body, _stop.Token).ConfigureAwait(false);
            context.Response.Close();
            observeResponse?.Invoke("/" + relative, status);
        }
    }

    private static string ContentType(string path)
        => Path.GetExtension(path) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json",
            ".css" => "text/css",
            ".wasm" => "application/wasm",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
}
