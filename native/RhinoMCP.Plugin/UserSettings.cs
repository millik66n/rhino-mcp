using System.Text.Json;

namespace RhinoMCP;

internal static class UserSettings
{
    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rhino-mcp", "config.json");

    public static int RhinoPort => ReadInt("rhino_port", 9876);
    public static int GrasshopperPort => ReadInt("grasshopper_port", 9999);
    public static string Profile => ReadString("profile", "basic");
    public static string Client => ReadClient();
    public static string RegulationsDatabase => ReadString("regulations_db", "");
    public static bool RegulationsAvailable => File.Exists(RegulationsDatabase);

    private static JsonElement? ReadRoot()
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static int ReadInt(string key, int fallback)
    {
        JsonElement? root = ReadRoot();
        return root is { } value && value.TryGetProperty(key, out JsonElement property)
            && property.TryGetInt32(out int result) ? result : fallback;
    }

    private static string ReadString(string key, string fallback)
    {
        JsonElement? root = ReadRoot();
        return root is { } value && value.TryGetProperty(key, out JsonElement property)
            ? property.GetString() ?? fallback : fallback;
    }

    private static string ReadClient()
    {
        JsonElement? root = ReadRoot();
        if (root is not { } value || !value.TryGetProperty("configured_clients", out JsonElement clients)
            || clients.ValueKind != JsonValueKind.Array)
            return "Not configured";
        string[] names = clients.EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => char.ToUpperInvariant(item![0]) + item.Substring(1))
            .ToArray();
        return names.Length == 0 ? "Not configured" : string.Join(", ", names);
    }
}
