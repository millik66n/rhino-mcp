using System.Text.Json;
using Grasshopper.Kernel;

namespace RhinoMCP.Grasshopper;

internal static class GrasshopperDispatcher
{
    public static object Dispatch(string command, JsonElement parameters) => command switch
    {
        "get_context" => Context(parameters),
        "get_objects" => Objects(parameters),
        "get_selected" => Selected(parameters),
        "expire_objects" => Expire(parameters),
        "execute_code" => ExecutePython(parameters),
        _ => throw new ArgumentException($"Unknown Grasshopper command: {command}"),
    };

    private static GH_Document Document => global::Grasshopper.Instances.ActiveCanvas?.Document
        ?? throw new InvalidOperationException("Open a Grasshopper definition and try again.");

    private static Dictionary<string, object?> Context(JsonElement parameters)
    {
        GH_Document document = Document;
        bool simplified = GetBool(parameters, "simplified", true);
        int page = Math.Max(1, GetInt(parameters, "page", 1));
        int size = Math.Clamp(GetInt(parameters, "page_size", 100), 1, 500);
        IGH_DocumentObject[] all = document.Objects.ToArray();
        object[] items = all.Skip((page - 1) * size).Take(size)
            .Select(item => (object)Summary(item, simplified)).ToArray();
        return Page(items, all.Length, page, size, document.SolutionState.ToString());
    }

    private static Dictionary<string, object?> Objects(JsonElement parameters)
    {
        bool simplified = GetBool(parameters, "simplified", true);
        HashSet<Guid> ids = GetGuids(parameters, "guids").ToHashSet();
        object[] items = Document.Objects.Where(item => ids.Contains(item.InstanceGuid))
            .Select(item => (object)Summary(item, simplified)).ToArray();
        return new() { ["items"] = items, ["total"] = items.Length };
    }

    private static Dictionary<string, object?> Selected(JsonElement parameters)
    {
        bool simplified = GetBool(parameters, "simplified", true);
        object[] items = Document.Objects.Where(item => item.Attributes.Selected)
            .Select(item => (object)Summary(item, simplified)).ToArray();
        return new() { ["items"] = items, ["total"] = items.Length };
    }

    private static Dictionary<string, object?> Expire(JsonElement parameters)
    {
        HashSet<Guid> ids = GetGuids(parameters, "guids").ToHashSet();
        int expired = 0;
        foreach (IGH_DocumentObject item in Document.Objects.Where(item => ids.Contains(item.InstanceGuid)))
        {
            item.ExpireSolution(false);
            expired++;
        }
        if (expired > 0)
            Document.NewSolution(false);
        return new() { ["expired"] = expired, ["message"] = $"Recomputed {expired} objects." };
    }

    private static Dictionary<string, object?> ExecutePython(JsonElement parameters)
    {
        if (!string.Equals(GrasshopperUserSettings.Profile, "developer",
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Arbitrary code is disabled. Select the Developer profile explicitly.");
        string code = parameters.TryGetProperty("code", out JsonElement value)
            ? value.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("code is required");
        string path = Path.Combine(Path.GetTempPath(), $"rhino_mcp_gh_{Guid.NewGuid():N}.py");
        File.WriteAllText(path, code);
        try
        {
            Rhino.RhinoApp.CommandWindowCaptureEnabled = true;
            Rhino.RhinoApp.RunScript(
                Rhino.RhinoDoc.ActiveDoc.RuntimeSerialNumber,
                $"_-ScriptEditor _Run \"{path}\"",
                false);
            string[] output = Rhino.RhinoApp.CapturedCommandWindowStrings(true)
                ?? Array.Empty<string>();
            return new() { ["stdout"] = string.Concat(output), ["error"] = null };
        }
        finally
        {
            Rhino.RhinoApp.CommandWindowCaptureEnabled = false;
            try { File.Delete(path); } catch { }
        }
    }

    private static Dictionary<string, object?> Summary(IGH_DocumentObject item, bool simplified)
    {
        Dictionary<string, object?> result = new()
        {
            ["id"] = item.InstanceGuid,
            ["name"] = item.Name,
            ["nickname"] = item.NickName,
            ["type"] = item.GetType().Name,
            ["selected"] = item.Attributes.Selected,
        };
        if (simplified)
            return result;
        result["description"] = item.Description;
        result["category"] = item.Category;
        result["subcategory"] = item.SubCategory;
        if (item is IGH_Component component)
        {
            result["inputs"] = component.Params.Input.Select(Parameter).ToArray();
            result["outputs"] = component.Params.Output.Select(Parameter).ToArray();
        }
        else if (item is IGH_Param parameter)
        {
            result["sources"] = parameter.Sources.Select(source => source.InstanceGuid).ToArray();
            result["recipients"] = parameter.Recipients.Select(recipient => recipient.InstanceGuid).ToArray();
        }
        return result;
    }

    private static Dictionary<string, object?> Parameter(IGH_Param parameter) => new()
    {
        ["id"] = parameter.InstanceGuid,
        ["name"] = parameter.Name,
        ["nickname"] = parameter.NickName,
        ["sources"] = parameter.Sources.Select(source => source.InstanceGuid).ToArray(),
        ["recipients"] = parameter.Recipients.Select(recipient => recipient.InstanceGuid).ToArray(),
    };

    private static Dictionary<string, object?> Page(
        object[] items, int total, int page, int size, string state) => new()
    {
        ["items"] = items,
        ["page"] = page,
        ["page_size"] = size,
        ["total"] = total,
        ["next_page"] = page * size < total ? page + 1 : null,
        ["solution_state"] = state,
        ["simplified_default"] = true,
    };

    private static Guid[] GetGuids(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"{name} must be an array");
        return values.EnumerateArray().Select(item => Guid.Parse(item.GetString() ?? "")).ToArray();
    }

    private static int GetInt(JsonElement parent, string name, int fallback) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            && value.TryGetInt32(out int result) ? result : fallback;

    private static bool GetBool(JsonElement parent, string name, bool fallback) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
}
