using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Rhino;

namespace RhinoMCP;

internal sealed class RhinoBridgeService : IDisposable
{
    private const int MaxMessageBytes = 64 * 1024 * 1024;
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;
    private int _clients;

    public static RhinoBridgeService Instance { get; } = new();
    public bool Running => _listener is not null;
    public int ClientCount => Volatile.Read(ref _clients);

    public void Start(int port)
    {
        lock (_sync)
        {
            if (_listener is not null)
                return;
            try
            {
                _cancellation = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                _ = AcceptLoopAsync(_listener, _cancellation.Token);
                BridgeLog.Write($"Listening on 127.0.0.1:{port} (protocol v2).");
            }
            catch (Exception ex)
            {
                _listener?.Stop();
                _listener = null;
                _cancellation?.Dispose();
                _cancellation = null;
                BridgeLog.Write($"Could not start on port {port}: {ex.Message}");
            }
        }
    }

    public void Restart(int port)
    {
        Stop();
        Start(port);
    }

    public void Stop()
    {
        lock (_sync)
        {
            _cancellation?.Cancel();
            _listener?.Stop();
            _listener = null;
            _cancellation?.Dispose();
            _cancellation = null;
            BridgeLog.Write("Bridge stopped.");
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellation).ConfigureAwait(false);
                client.NoDelay = true;
                _ = HandleClientAsync(client, cancellation);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested)
                    BridgeLog.Write($"Accept failed: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellation)
    {
        Interlocked.Increment(ref _clients);
        BridgeLog.Write("AI server connected.");
        try
        {
            using (client)
            await using (NetworkStream stream = client.GetStream())
            {
                while (!cancellation.IsCancellationRequested)
                {
                    JsonDocument request;
                    try
                    {
                        request = await ReadFrameAsync(stream, cancellation).ConfigureAwait(false);
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }

                    using (request)
                    {
                        Dictionary<string, object?> response = await DispatchOnUiThreadAsync(
                            request.RootElement.Clone()).ConfigureAwait(false);
                        await WriteFrameAsync(stream, response, cancellation).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            BridgeLog.Write($"Connection ended: {ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _clients);
        }
    }

    private static Task<Dictionary<string, object?>> DispatchOnUiThreadAsync(JsonElement request)
    {
        TaskCompletionSource<Dictionary<string, object?>> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            object? id = null;
            try
            {
                if (request.TryGetProperty("id", out JsonElement idElement))
                    id = idElement.ValueKind == JsonValueKind.Number
                        ? idElement.GetInt64() : idElement.ToString();
                string type = request.GetProperty("type").GetString()
                    ?? throw new InvalidOperationException("Command type is required.");
                JsonElement parameters = request.TryGetProperty("params", out JsonElement value)
                    ? value : default;
                object data = RhinoCommandDispatcher.Dispatch(type, parameters);
                if (data is CapturedViewport capture)
                {
                    _ = EncodeCaptureAsync(completion, id, capture);
                    return;
                }
                completion.SetResult(new Dictionary<string, object?>
                {
                    ["protocol"] = 2,
                    ["id"] = id,
                    ["status"] = "ok",
                    ["data"] = data,
                });
            }
            catch (Exception ex)
            {
                completion.SetResult(new Dictionary<string, object?>
                {
                    ["protocol"] = 2,
                    ["id"] = id,
                    ["status"] = "error",
                    ["message"] = ex.Message,
                });
            }
        }));
        return completion.Task;
    }

    private static async Task EncodeCaptureAsync(
        TaskCompletionSource<Dictionary<string, object?>> completion,
        object? id,
        CapturedViewport capture)
    {
        try
        {
            Dictionary<string, object?> data = await Task.Run(capture.Encode).ConfigureAwait(false);
            completion.SetResult(new Dictionary<string, object?>
            {
                ["protocol"] = 2,
                ["id"] = id,
                ["status"] = "ok",
                ["data"] = data,
            });
        }
        catch (Exception ex)
        {
            capture.Dispose();
            completion.SetResult(new Dictionary<string, object?>
            {
                ["protocol"] = 2,
                ["id"] = id,
                ["status"] = "error",
                ["message"] = ex.Message,
            });
        }
    }

    private static async Task<JsonDocument> ReadFrameAsync(
        NetworkStream stream, CancellationToken cancellation)
    {
        byte[] header = new byte[4];
        await ReadExactlyAsync(stream, header, cancellation).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > MaxMessageBytes)
            throw new InvalidDataException("Invalid bridge frame length.");
        byte[] body = new byte[length];
        await ReadExactlyAsync(stream, body, cancellation).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    private static async Task ReadExactlyAsync(
        Stream stream, byte[] buffer, CancellationToken cancellation)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellation).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
    }

    private static async Task WriteFrameAsync(
        NetworkStream stream, object value, CancellationToken cancellation)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        await stream.WriteAsync(header, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    public void Dispose() => Stop();
}
