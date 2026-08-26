using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace RhinoMCP;

/// <summary>
/// A tiny HTTP server for the local connection dashboard. It binds only to
/// loopback, ships every asset inside the plug-in, and protects its single
/// launch action with a per-process token embedded in the local page.
/// </summary>
internal sealed class RhinoMcpDashboardService : IDisposable
{
    private const int MaxRequestBytes = 16 * 1024;
    private const string DashboardResource = "RhinoMCP.Dashboard.index.html";
    private const string ActionTokenPlaceholder = "__RHINO_MCP_ACTION_TOKEN__";
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;
    private byte[] _dashboardHtml = Array.Empty<byte>();
    private string _actionToken = "";
    private bool _browserWasOpened;

    public bool Running
    {
        get
        {
            lock (_sync)
                return _listener is not null;
        }
    }

    public int Port { get; private set; }
    public string Url => $"http://127.0.0.1:{Port}/";
    public string LastBrowser { get; private set; } = "Not opened";

    public void Start(int preferredPort)
    {
        lock (_sync)
        {
            if (_listener is not null)
                return;

            try
            {
                _actionToken = Guid.NewGuid().ToString("N");
                _dashboardHtml = LoadDashboardHtml(_actionToken);
                _cancellation = new CancellationTokenSource();
                _listener = StartListener(preferredPort);
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _ = AcceptLoopAsync(_listener, _cancellation.Token);
                BridgeLog.Write($"Connection dashboard ready at {Url}");
            }
            catch (Exception exception)
            {
                _listener?.Stop();
                _listener = null;
                _cancellation?.Dispose();
                _cancellation = null;
                _actionToken = "";
                Port = 0;
                BridgeLog.Write($"Could not start the connection dashboard: {exception.Message}");
            }
        }
    }

    public bool OpenBrowser(bool force = false, bool preferChrome = true)
    {
        if (!Running)
        {
            BridgeLog.Write("The connection dashboard is not running.");
            return false;
        }

        if (_browserWasOpened && !force)
            return true;

        if (preferChrome && TryOpenChrome())
        {
            _browserWasOpened = true;
            LastBrowser = "Google Chrome";
            BridgeLog.Write("Opened the connection dashboard in Google Chrome.");
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Url,
                UseShellExecute = true,
            });
            _browserWasOpened = true;
            LastBrowser = "Default browser";
            BridgeLog.Write("Opened the connection dashboard in the default browser.");
            return true;
        }
        catch (Exception exception)
        {
            BridgeLog.Write($"Could not open the default browser: {exception.Message}");
            return false;
        }
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
            _actionToken = "";
            _browserWasOpened = false;
            LastBrowser = "Not opened";
            Port = 0;
        }
    }

    public void Dispose() => Stop();

    private bool TryOpenChrome()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
        string[] candidates =
        {
            Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
        };

        foreach (string executable in candidates.Where(File.Exists))
        {
            if (TryStartChrome(executable))
                return true;
        }
        return TryStartChrome("chrome.exe");
    }

    private bool TryStartChrome(string executable)
    {
        try
        {
            Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--new-tab \"{Url}\"",
                UseShellExecute = true,
            });
            process?.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TcpListener StartListener(int preferredPort)
    {
        TcpListener preferred = new(IPAddress.Loopback, preferredPort);
        try
        {
            preferred.Start();
            return preferred;
        }
        catch (SocketException) when (preferredPort != 0)
        {
            preferred.Stop();
            TcpListener fallback = new(IPAddress.Loopback, 0);
            fallback.Start();
            BridgeLog.Write(
                $"Dashboard port {preferredPort} was busy; using a private local fallback port.");
            return fallback;
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                _ = HandleClientAsync(client, cancellation);
            }
            catch (Exception exception)
            {
                if (!cancellation.IsCancellationRequested)
                    BridgeLog.Write($"Dashboard connection failed: {exception.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellation)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                string request = await ReadRequestAsync(stream, cancellation).ConfigureAwait(false);
                string firstLine = request.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                string[] parts = firstLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    await WriteTextAsync(stream, 400, "Bad Request", "Bad request.", false, cancellation)
                        .ConfigureAwait(false);
                    return;
                }

                string host = HeaderValue(request, "Host");
                if (!string.Equals(
                    host, $"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteTextAsync(
                        stream, 421, "Misdirected Request",
                        "Use the private Rhino MCP dashboard address opened by Rhino.",
                        false, cancellation).ConfigureAwait(false);
                    return;
                }

                string method = parts[0].ToUpperInvariant();
                string path = parts[1];
                int queryStart = path.IndexOf('?');
                if (queryStart >= 0)
                    path = path.Substring(0, queryStart);

                if (method == "POST" && path == "/api/open-codex")
                {
                    await OpenCodexAsync(stream, request, cancellation).ConfigureAwait(false);
                    return;
                }

                bool headOnly = method == "HEAD";
                if (method != "GET" && !headOnly)
                {
                    await WriteTextAsync(
                        stream, 405, "Method Not Allowed",
                        "Only GET, HEAD, and the protected Codex launch action are supported.",
                        false, cancellation, "Allow: GET, HEAD, POST\r\n").ConfigureAwait(false);
                    return;
                }

                if (path == "/" || path == "/index.html")
                {
                    await WriteResponseAsync(
                        stream, 200, "OK", "text/html; charset=utf-8", _dashboardHtml,
                        headOnly, cancellation).ConfigureAwait(false);
                }
                else if (path == "/api/status")
                {
                    Dictionary<string, object?> status =
                        RhinoMcpPlugin.Instance?.StatusHud.DashboardStatus()
                        ?? RhinoMcpStatusHud.UnavailableDashboardStatus();
                    byte[] body = JsonSerializer.SerializeToUtf8Bytes(status);
                    await WriteResponseAsync(
                        stream, 200, "OK", "application/json; charset=utf-8", body,
                        headOnly, cancellation).ConfigureAwait(false);
                }
                else if (path == "/favicon.ico")
                {
                    await WriteResponseAsync(
                        stream, 204, "No Content", "image/x-icon", Array.Empty<byte>(),
                        true, cancellation).ConfigureAwait(false);
                }
                else
                {
                    await WriteTextAsync(stream, 404, "Not Found", "Not found.", headOnly, cancellation)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Rhino is closing.
            }
            catch (Exception exception)
            {
                if (!cancellation.IsCancellationRequested)
                    BridgeLog.Write($"Dashboard request failed: {exception.Message}");
            }
        }
    }

    private async Task OpenCodexAsync(
        NetworkStream stream,
        string request,
        CancellationToken cancellation)
    {
        string suppliedToken = HeaderValue(request, "X-Rhino-MCP-Action");
        string origin = HeaderValue(request, "Origin");
        string expectedToken;
        lock (_sync)
            expectedToken = _actionToken;
        if ((!string.IsNullOrWhiteSpace(origin)
                && !string.Equals(origin, Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            || string.IsNullOrWhiteSpace(expectedToken)
            || !string.Equals(suppliedToken, expectedToken, StringComparison.Ordinal))
        {
            await WriteTextAsync(
                stream, 403, "Forbidden", "The local dashboard action token is invalid.",
                false, cancellation).ConfigureAwait(false);
            return;
        }

        RhinoMcpClientLauncher? launcher = RhinoMcpPlugin.Instance?.ClientLauncher;
        if (launcher is null)
        {
            await WriteTextAsync(
                stream, 503, "Service Unavailable", "Rhino MCP is still starting.",
                false, cancellation).ConfigureAwait(false);
            return;
        }

        ClientLaunchSnapshot result = await launcher
            .OpenConfiguredCodexAsync(automatic: false).ConfigureAwait(false);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(launcher.DashboardStatus());
        await WriteResponseAsync(
            stream, result.Succeeded ? 200 : 409,
            result.Succeeded ? "OK" : "Conflict",
            "application/json; charset=utf-8", body, false, cancellation)
            .ConfigureAwait(false);
    }

    private static async Task<string> ReadRequestAsync(
        NetworkStream stream, CancellationToken cancellation)
    {
        byte[] bytes = new byte[MaxRequestBytes];
        int length = 0;
        while (length < bytes.Length)
        {
            int read = await stream.ReadAsync(
                bytes, length, bytes.Length - length, cancellation).ConfigureAwait(false);
            if (read == 0)
                break;
            length += read;
            if (HeadersComplete(bytes, length))
                return Encoding.ASCII.GetString(bytes, 0, length);
        }

        throw new InvalidDataException("Dashboard request headers were incomplete or too large.");
    }

    private static bool HeadersComplete(byte[] bytes, int length)
    {
        for (int index = 3; index < length; index++)
        {
            if (bytes[index - 3] == '\r' && bytes[index - 2] == '\n'
                && bytes[index - 1] == '\r' && bytes[index] == '\n')
                return true;
        }
        return false;
    }

    private static string HeaderValue(string request, string name)
    {
        string prefix = name + ":";
        foreach (string line in request.Split(new[] { "\r\n" }, StringSplitOptions.None))
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line.Substring(prefix.Length).Trim();
        }
        return "";
    }

    private static Task WriteTextAsync(
        NetworkStream stream,
        int status,
        string reason,
        string text,
        bool headOnly,
        CancellationToken cancellation,
        string extraHeaders = "") =>
        WriteResponseAsync(
            stream, status, reason, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text),
            headOnly, cancellation, extraHeaders);

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int status,
        string reason,
        string contentType,
        byte[] body,
        bool headOnly,
        CancellationToken cancellation,
        string extraHeaders = "")
    {
        string headers =
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Referrer-Policy: no-referrer\r\n" +
            "Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; " +
            "script-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; " +
            "base-uri 'none'; frame-ancestors 'none'\r\n" +
            extraHeaders +
            "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(
            headerBytes, 0, headerBytes.Length, cancellation).ConfigureAwait(false);
        if (!headOnly && body.Length > 0)
            await stream.WriteAsync(body, 0, body.Length, cancellation).ConfigureAwait(false);
    }

    private static byte[] LoadDashboardHtml(string actionToken)
    {
        Assembly assembly = typeof(RhinoMcpDashboardService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(DashboardResource);
        if (stream is null)
            throw new InvalidOperationException("The embedded dashboard page is missing.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        string html = reader.ReadToEnd();
        if (!html.Contains(ActionTokenPlaceholder))
            throw new InvalidOperationException("The dashboard action token placeholder is missing.");
        return Encoding.UTF8.GetBytes(html.Replace(ActionTokenPlaceholder, actionToken));
    }
}
