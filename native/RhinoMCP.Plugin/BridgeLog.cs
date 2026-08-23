namespace RhinoMCP;

internal static class BridgeLog
{
    private static readonly object Sync = new();
    private static readonly Queue<string> Lines = new();
    public static event Action? Changed;

    public static string Text
    {
        get
        {
            lock (Sync)
                return string.Join(Environment.NewLine, Lines);
        }
    }

    public static void Write(string message)
    {
        lock (Sync)
        {
            Lines.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
            while (Lines.Count > 100)
                Lines.Dequeue();
        }
        Changed?.Invoke();
        Rhino.RhinoApp.WriteLine($"[Rhino MCP] {message}");
    }
}
