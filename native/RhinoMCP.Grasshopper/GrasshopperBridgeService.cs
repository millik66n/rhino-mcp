using System.Net;
using System.Text;
using System.Text.Json;
using Rhino;

namespace RhinoMCP.Grasshopper;

internal sealed class GrasshopperBridgeService : IDisposable
{
    private readonly object _sync = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cancellation;

    public static GrasshopperBridgeService Instance { get; } = new();

    public void Start(int port)
    {
        lock (_sync)
        {
            if (_listener is not null)
                return;
            try
            {
                _cancellation = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                _ = ListenAsync(_listener, _cancellation.Token);
                RhinoApp.WriteLine($"[Rhino MCP] Grasshopper bridge ready on port {port}.");
            }
            catch (Exception ex)
            {
                _listener?.Close();
                _listener = null;
                _cancellation?.Dispose();
                _cancellation = null;
                RhinoApp.WriteLine($"[Rhino MCP] Could not start Grasshopper bridge: {ex.Message}");
            }
        }
    }

    private async Task ListenAsync(HttpListener listener, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext context = await listener.GetContextAsync().ConfigureAwait(false);
                _ = HandleAsync(context, cancellation);
            }
            catch (HttpListenerException) when (cancellation.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[Rhino MCP] Grasshopper listener error: {ex.Message}");
            }
        }
    }

    private static async Task HandleAsync(HttpListenerContext context, CancellationToken cancellation)
    {
        Dictionary<string, object?> response;
        try
        {
            if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/health")
            {
                response = new()
                {
                    ["status"] = "ok",
                    ["message"] = global::Grasshopper.Instances.ActiveCanvas?.Document is null
                        ? "Grasshopper is open; no definition is active." : "Grasshopper is ready.",
                };
            }
            else if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/command")
            {
                using JsonDocument request = await JsonDocument.ParseAsync(
                    context.Request.InputStream, cancellationToken: cancellation).ConfigureAwait(false);
                response = await DispatchOnUiThreadAsync(request.RootElement.Clone()).ConfigureAwait(false);
            }
            else
            {
                context.Response.StatusCode = 404;
                response = new() { ["status"] = "error", ["message"] = "Not found." };
            }
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            response = new() { ["status"] = "error", ["message"] = ex.Message };
        }

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(response);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = body.Length;
        context.Response.KeepAlive = true;
        await context.Response.OutputStream.WriteAsync(
            body, 0, body.Length, cancellation).ConfigureAwait(false);
        context.Response.Close();
    }

    private static Task<Dictionary<string, object?>> DispatchOnUiThreadAsync(JsonElement request)
    {
        TaskCompletionSource<Dictionary<string, object?>> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            try
            {
                string command = request.GetProperty("type").GetString()
                    ?? throw new ArgumentException("Command type is required.");
                JsonElement parameters = request.TryGetProperty("params", out JsonElement value)
                    ? value : default;
                completion.SetResult(new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["data"] = GrasshopperDispatcher.Dispatch(command, parameters),
                });
            }
            catch (Exception ex)
            {
                completion.SetResult(new Dictionary<string, object?>
                {
                    ["status"] = "error", ["message"] = ex.Message,
                });
            }
        }));
        return completion.Task;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _cancellation?.Cancel();
            _listener?.Close();
            _listener = null;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }
}
