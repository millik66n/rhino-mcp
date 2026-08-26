using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoMCP;

internal static class RhinoCommandDispatcher
{
    public static object Dispatch(string command, JsonElement parameters) => command switch
    {
        "health" => Health(),
        "open_dashboard" => OpenDashboard(parameters),
        "get_scene_info" => SceneInfo(),
        "get_layers" => Layers(),
        "list_objects" => ListObjects(parameters),
        "get_scene_changes" => SceneChanges(parameters),
        "create_geometry" => Mutate("Create geometry", parameters, () => Create(parameters)),
        "modify_objects" => Mutate("Modify objects", parameters, () => Modify(parameters)),
        "delete_objects" => Mutate("Delete objects", parameters, () => Delete(parameters)),
        "organize_layers" => Mutate("Organize layers", parameters, () => OrganizeLayers(parameters)),
        "batch_geometry" => Batch(parameters),
        "test_connection" => TestConnection(GetBool(parameters, "cleanup", true)),
        "capture_viewport" => CaptureViewport(parameters),
        "execute_code" => ExecuteDeveloperPython(parameters),
        _ => throw new ArgumentException($"Unknown Rhino MCP command: {command}"),
    };

    private static RhinoDoc Document => RhinoDoc.ActiveDoc
        ?? throw new InvalidOperationException("Open a Rhino document and try again.");

    private static Dictionary<string, object?> Health()
    {
        RhinoDoc? document = RhinoDoc.ActiveDoc;
        return new()
        {
            ["connected"] = true,
            ["protocol"] = 2,
            ["rhino_version"] = RhinoApp.Version.ToString(),
            ["document"] = document?.Name ?? "Untitled",
            ["document_open"] = document is not null,
            ["scene_version"] = SceneChangeTracker.Version,
            ["clients"] = RhinoBridgeService.Instance.ClientCount,
        };
    }

    private static Dictionary<string, object?> OpenDashboard(JsonElement parameters)
    {
        RhinoMcpDashboardService dashboard = RhinoMcpPlugin.Instance?.Dashboard
            ?? throw new InvalidOperationException("Rhino MCP is still starting.");
        dashboard.Start(UserSettings.DashboardPort);
        bool opened = dashboard.OpenBrowser(
            force: GetBool(parameters, "force", false),
            preferChrome: GetBool(parameters, "prefer_chrome", true));
        if (!opened)
            throw new InvalidOperationException("The connection dashboard could not be opened.");
        return new Dictionary<string, object?>
        {
            ["opened"] = true,
            ["browser"] = dashboard.LastBrowser,
            ["url"] = dashboard.Url,
        };
    }

    private static Dictionary<string, object?> SceneInfo()
    {
        RhinoDoc doc = Document;
        RhinoObject[] objects = doc.Objects.GetObjectList(ObjectType.AnyObject).ToArray();
        return new()
        {
            ["document"] = doc.Name ?? "Untitled",
            ["units"] = doc.ModelUnitSystem.ToString(),
            ["object_count"] = objects.Length,
            ["layer_count"] = doc.Layers.Count(layer => !layer.IsDeleted),
            ["selected_count"] = objects.Count(item => item.IsSelected(false) > 0),
            ["scene_version"] = SceneChangeTracker.Version,
            ["sample"] = objects.Take(5).Select(item => ObjectSummary(doc, item, null)).ToArray(),
        };
    }

    private static Dictionary<string, object?> Layers()
    {
        RhinoDoc doc = Document;
        RhinoObject[] objects = doc.Objects.GetObjectList(ObjectType.AnyObject).ToArray();
        object[] layers = doc.Layers
            .Where(layer => !layer.IsDeleted)
            .Select(layer => (object)new Dictionary<string, object?>
            {
                ["id"] = layer.Id,
                ["name"] = layer.Name,
                ["full_path"] = layer.FullPath,
                ["visible"] = layer.IsVisible,
                ["locked"] = layer.IsLocked,
                ["color"] = $"#{layer.Color.R:X2}{layer.Color.G:X2}{layer.Color.B:X2}",
                ["object_count"] = objects.Count(item => item.Attributes.LayerIndex == layer.Index),
            })
            .ToArray();
        return new() { ["items"] = layers, ["total"] = layers.Length, ["scene_version"] = SceneChangeTracker.Version };
    }

    private static Dictionary<string, object?> ListObjects(JsonElement parameters)
    {
        RhinoDoc doc = Document;
        int page = Math.Max(1, GetInt(parameters, "page", 1));
        int size = Clamp(GetInt(parameters, "page_size", 100), 1, 500);
        string? layer = GetString(parameters, "layer");
        string? objectType = GetString(parameters, "object_type");
        HashSet<string>? fields = GetStringArray(parameters, "fields") is { Length: > 0 } requested
            ? new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase) : null;

        IEnumerable<RhinoObject> query = doc.Objects.GetObjectList(ObjectType.AnyObject);
        if (!string.IsNullOrWhiteSpace(layer))
            query = query.Where(item => string.Equals(
                doc.Layers[item.Attributes.LayerIndex].FullPath, layer, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(objectType))
            query = query.Where(item => string.Equals(
                item.ObjectType.ToString(), objectType, StringComparison.OrdinalIgnoreCase));
        RhinoObject[] all = query.ToArray();
        object[] items = all.Skip((page - 1) * size).Take(size)
            .Select(item => (object)ObjectSummary(doc, item, fields)).ToArray();
        return Page(items, all.Length, page, size, SceneChangeTracker.Version);
    }

    private static Dictionary<string, object?> SceneChanges(JsonElement parameters)
    {
        RhinoDoc doc = Document;
        long since = Math.Max(0, GetLong(parameters, "since_version", 0));
        int page = Math.Max(1, GetInt(parameters, "page", 1));
        int size = Clamp(GetInt(parameters, "page_size", 100), 1, 500);
        var changes = SceneChangeTracker.Since(since);
        object[] events = changes.Changed.Select(change =>
        {
            RhinoObject? item = doc.Objects.FindId(change.Id);
            return item is null
                ? (Version: change.Version, Item: (object)new Dictionary<string, object?>
                {
                    ["id"] = change.Id, ["change"] = "deleted", ["version"] = change.Version,
                })
                : (Version: change.Version, Item: (object)new Dictionary<string, object?>
                {
                    ["change"] = "upserted",
                    ["version"] = change.Version,
                    ["object"] = ObjectSummary(doc, item, null),
                });
        }).Concat(changes.Deleted.Select(change =>
            (Version: change.Version, Item: (object)new Dictionary<string, object?>
            {
                ["id"] = change.Id, ["change"] = "deleted", ["version"] = change.Version,
            })))
            .OrderBy(change => change.Version)
            .Select(change => change.Item)
            .ToArray();
        object[] items = events.Skip((page - 1) * size).Take(size).ToArray();
        Dictionary<string, object?> result = Page(items, events.Length, page, size, changes.Version);
        result["since_version"] = since;
        return result;
    }

    private static Dictionary<string, object?> Create(JsonElement parameters)
    {
        RhinoDoc doc = Document;
        string kind = (GetString(parameters, "kind") ?? "").ToLowerInvariant();
        JsonElement geometry = GetObject(parameters, "geometry");
        bool dryRun = GetBool(parameters, "dry_run", false);
        string? name = GetString(parameters, "name");
        string? layer = GetString(parameters, "layer");
        ObjectAttributes attributes = dryRun
            ? NewPreviewAttributes(name)
            : NewAttributes(doc, name, layer);
        ValidateGeometry(kind, geometry);
        Guid id = kind switch
        {
            "point" => dryRun ? Guid.Empty : doc.Objects.AddPoint(Point(geometry, "point"), attributes),
            "line" => dryRun ? Guid.Empty : doc.Objects.AddLine(
                Point(geometry, "from"), Point(geometry, "to"), attributes),
            "box" => dryRun ? Guid.Empty : doc.Objects.AddBox(
                new Box(new BoundingBox(Point(geometry, "min"), Point(geometry, "max"))), attributes),
            "sphere" => dryRun ? Guid.Empty : doc.Objects.AddSphere(
                new Sphere(Point(geometry, "center"), GetDouble(geometry, "radius", 1)), attributes),
            "cylinder" => dryRun ? Guid.Empty : doc.Objects.AddBrep(
                new Cylinder(
                    new Circle(new Plane(Point(geometry, "base"), Vector3d.ZAxis),
                        GetDouble(geometry, "radius", 1)),
                    GetDouble(geometry, "height", 1)).ToBrep(true, true), attributes),
            "polyline" => dryRun ? Guid.Empty : doc.Objects.AddPolyline(Points(geometry, "points"), attributes),
            _ => throw new ArgumentException("kind must be point, line, box, sphere, cylinder, or polyline"),
        };
        if (!dryRun && id == Guid.Empty)
            throw new InvalidOperationException($"Rhino could not create the {kind}.");
        return new()
        {
            ["dry_run"] = dryRun,
            ["kind"] = kind,
            ["created_ids"] = dryRun ? Array.Empty<Guid>() : new[] { id },
            ["message"] = dryRun ? $"Preview valid: create one {kind}." : $"Created one {kind}.",
        };
    }

    private static Dictionary<string, object?> Modify(JsonElement parameters)
    {
        RhinoDoc doc = Document;
        Guid[] ids = Guids(parameters, "object_ids");
        JsonElement transform = GetObject(parameters, "transform");
        bool dryRun = GetBool(parameters, "dry_run", false);
        int found = ids.Count(id => doc.Objects.FindId(id) is not null);
        if (dryRun)
        {
            foreach (Guid id in ids)
            {
                RhinoObject? item = doc.Objects.FindId(id);
                if (item is not null)
                    _ = BuildTransform(transform, item.Geometry.GetBoundingBox(true).Center);
            }
            return new() { ["dry_run"] = true, ["matched"] = found, ["message"] = $"Preview valid for {found} objects." };
        }

        int changed = 0;
        List<Guid> modifiedIds = new();
        foreach (Guid id in ids)
        {
            RhinoObject? item = doc.Objects.FindId(id);
            if (item is null)
                continue;
            Transform xform = BuildTransform(transform, item.Geometry.GetBoundingBox(true).Center);
            Guid currentId = id;
            if (!xform.Equals(Transform.Identity))
            {
                Guid transformedId = doc.Objects.Transform(id, xform, true);
                if (transformedId != Guid.Empty)
                {
                    currentId = transformedId;
                    changed++;
                }
            }

            RhinoObject? current = doc.Objects.FindId(currentId);
            if (current is null)
                continue;
            ObjectAttributes attributes = current.Attributes.Duplicate();
            bool attributesChanged = false;
            if (GetString(transform, "name") is { } name)
            {
                attributes.Name = name;
                attributesChanged = true;
            }
            if (GetString(transform, "layer") is { } layer)
            {
                attributes.LayerIndex = EnsureLayer(doc, layer);
                attributesChanged = true;
            }
            if (attributesChanged && doc.Objects.ModifyAttributes(current.Id, attributes, true))
                changed++;
            modifiedIds.Add(current.Id);
        }
        return new()
        {
            ["modified"] = changed,
            ["matched"] = found,
            ["object_ids"] = modifiedIds,
            ["message"] = $"Modified {changed} object values.",
        };
    }

    private static Dictionary<string, object?> Delete(JsonElement parameters)
    {
        RhinoDoc doc = Document;
        Guid[] ids = Guids(parameters, "object_ids");
        bool dryRun = GetBool(parameters, "dry_run", false);
        int found = ids.Count(id => doc.Objects.FindId(id) is not null);
        if (dryRun)
            return new() { ["dry_run"] = true, ["matched"] = found, ["message"] = $"Would delete {found} objects." };
        int deleted = ids.Count(id => doc.Objects.Delete(id, true));
        return new() { ["deleted"] = deleted, ["message"] = $"Deleted {deleted} objects. Use Undo to restore them." };
    }

    private static Dictionary<string, object?> OrganizeLayers(JsonElement parameters)
    {
        RhinoDoc doc = Document;
        JsonElement actions = GetArray(parameters, "actions");
        bool dryRun = GetBool(parameters, "dry_run", false);
        int changed = 0;
        foreach (JsonElement action in actions.EnumerateArray())
        {
            string type = (GetString(action, "type") ?? "").ToLowerInvariant();
            string name = GetString(action, "name")
                ?? throw new ArgumentException("Each layer action requires name.");
            if (type is not ("create" or "rename" or "recolor" or "delete"))
                throw new ArgumentException("Layer action type must be create, rename, recolor, or delete.");
            if (type == "rename" && string.IsNullOrWhiteSpace(GetString(action, "new_name")))
                throw new ArgumentException("rename requires new_name");
            if (type == "recolor")
                _ = ParseColor(GetString(action, "color")
                    ?? throw new ArgumentException("recolor requires color"));
            if (type == "create" && GetString(action, "color") is { } previewColor)
                _ = ParseColor(previewColor);
            if (dryRun)
            {
                changed++;
                continue;
            }
            int index = FindLayer(doc, name);
            if (type == "create")
            {
                if (index < 0)
                {
                    Layer layer = new() { Name = name };
                    if (GetString(action, "color") is { } color)
                        layer.Color = ParseColor(color);
                    doc.Layers.Add(layer);
                    changed++;
                }
            }
            else if (index >= 0 && type is "rename" or "recolor")
            {
                Layer original = doc.Layers[index];
                Layer layer = new()
                {
                    Name = original.Name,
                    Color = original.Color,
                    IsVisible = original.IsVisible,
                    IsLocked = original.IsLocked,
                    ParentLayerId = original.ParentLayerId,
                };
                if (type == "rename")
                    layer.Name = GetString(action, "new_name")
                        ?? throw new ArgumentException("rename requires new_name");
                else
                    layer.Color = ParseColor(GetString(action, "color")
                        ?? throw new ArgumentException("recolor requires color"));
                if (doc.Layers.Modify(layer, index, true))
                    changed++;
            }
            else if (index >= 0 && type == "delete" && doc.Layers.Delete(index, true))
                changed++;
        }
        return new()
        {
            ["dry_run"] = dryRun,
            ["changed"] = changed,
            ["message"] = dryRun ? $"Preview valid for {changed} layer actions." : $"Applied {changed} layer actions.",
        };
    }

    private static Dictionary<string, object?> Batch(JsonElement parameters)
    {
        RhinoDoc doc = Document;
        bool dryRun = GetBool(parameters, "dry_run", false);
        JsonElement[] operations = GetArray(parameters, "operations").EnumerateArray()
            .Select(item => item.Clone()).ToArray();
        List<object> previews = operations.Select(operation =>
            (object)RunBatchOperation(operation, true)).ToList();
        if (dryRun)
        {
            return new()
            {
                ["dry_run"] = true,
                ["operations"] = previews,
                ["message"] = "Batch preview is valid.",
            };
        }

        uint undo = doc.BeginUndoRecord("Rhino MCP batch");
        List<object> results = new();
        try
        {
            foreach (JsonElement operation in operations)
                results.Add(RunBatchOperation(operation, false));
        }
        finally
        {
            doc.EndUndoRecord(undo);
            doc.Views.Redraw();
        }
        return new()
        {
            ["dry_run"] = false,
            ["operations"] = results,
            ["message"] = $"Applied {results.Count} operations in one undo record.",
        };
    }

    private static Dictionary<string, object?> RunBatchOperation(JsonElement operation, bool dryRun)
    {
        string type = (GetString(operation, "type") ?? "create").ToLowerInvariant();
        return type switch
        {
            "create" => Create(WithDryRun(operation, dryRun)),
            "modify" => Modify(WithDryRun(operation, dryRun)),
            "delete" => Delete(WithDryRun(operation, dryRun)),
            "layers" => OrganizeLayers(WithDryRun(operation, dryRun)),
            _ => throw new ArgumentException($"Unknown batch operation type: {type}"),
        };
    }

    public static Dictionary<string, object?> TestConnection(bool cleanup)
    {
        RhinoDoc doc = Document;
        uint undo = doc.BeginUndoRecord("Rhino MCP connection test");
        Guid id = Guid.Empty;
        try
        {
            ObjectAttributes attributes = NewAttributes(doc, "Rhino MCP test cube", null);
            id = doc.Objects.AddBox(new Box(new BoundingBox(Point3d.Origin, new Point3d(1, 1, 1))), attributes);
            if (id == Guid.Empty || doc.Objects.FindId(id) is null)
                throw new InvalidOperationException("Rhino could not create the test cube.");
            if (cleanup)
                doc.Objects.Delete(id, true);
            doc.Views.Redraw();
            return new()
            {
                ["ok"] = true,
                ["created_id"] = id,
                ["cleaned_up"] = cleanup,
                ["message"] = cleanup ? "Connection works. Test cube created, verified, and removed." : "Connection works. Test cube created.",
            };
        }
        finally
        {
            doc.EndUndoRecord(undo);
        }
    }

    private static CapturedViewport CaptureViewport(JsonElement parameters)
    {
        RhinoDoc doc = Document;
        Rhino.Display.RhinoView view = doc.Views.ActiveView
            ?? throw new InvalidOperationException("No active Rhino viewport.");
        int maximum = Clamp(GetInt(parameters, "max_size", 1024), 256, 4096);
        int width = view.ActiveViewport.Size.Width;
        int height = view.ActiveViewport.Size.Height;
        double scale = Math.Min(1.0, maximum / (double)Math.Max(width, height));
        Rhino.Display.ViewCapture capture = new()
        {
            Width = Math.Max(1, (int)(width * scale)),
            Height = Math.Max(1, (int)(height * scale)),
            ScaleScreenItems = false,
            DrawAxes = true,
            DrawGrid = true,
            DrawGridAxes = true,
            TransparentBackground = false,
        };
        Bitmap? captured = RhinoMcpPlugin.Instance?.StatusHud.WithoutOverlay(
            () => capture.CaptureToBitmap(view));
        Bitmap bitmap = (captured ?? capture.CaptureToBitmap(view))
            ?? throw new InvalidOperationException("Viewport capture failed.");
        string format = (GetString(parameters, "format") ?? "jpeg").ToLowerInvariant();
        int quality = Clamp(GetInt(parameters, "quality", 80), 20, 95);
        return new CapturedViewport(bitmap, format == "png" ? "png" : "jpeg",
            quality, capture.Width, capture.Height);
    }

    private static Dictionary<string, object?> ExecuteDeveloperPython(JsonElement parameters)
    {
        if (!string.Equals(UserSettings.Profile, "developer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Arbitrary code is disabled. Select the Developer profile explicitly.");
        string code = GetString(parameters, "code")
            ?? throw new ArgumentException("code is required");
        string path = Path.Combine(Path.GetTempPath(), $"rhino_mcp_{Guid.NewGuid():N}.py");
        File.WriteAllText(path, code);
        try
        {
            RhinoApp.CommandWindowCaptureEnabled = true;
            RhinoApp.RunScript(Document.RuntimeSerialNumber, $"_-ScriptEditor _Run \"{path}\"", false);
            string[] output = RhinoApp.CapturedCommandWindowStrings(true) ?? Array.Empty<string>();
            return new() { ["stdout"] = string.Concat(output), ["error"] = null };
        }
        finally
        {
            RhinoApp.CommandWindowCaptureEnabled = false;
            try { File.Delete(path); } catch { }
        }
    }

    private static Dictionary<string, object?> Mutate(
        string name, JsonElement parameters, Func<Dictionary<string, object?>> operation)
    {
        RhinoDoc doc = Document;
        bool dryRun = GetBool(parameters, "dry_run", false);
        uint undo = dryRun ? 0 : doc.BeginUndoRecord($"Rhino MCP: {name}");
        try
        {
            return operation();
        }
        finally
        {
            if (!dryRun)
            {
                doc.EndUndoRecord(undo);
                doc.Views.Redraw();
            }
        }
    }

    private static Dictionary<string, object?> ObjectSummary(
        RhinoDoc doc, RhinoObject item, HashSet<string>? fields)
    {
        BoundingBox box = item.Geometry.GetBoundingBox(true);
        Dictionary<string, object?> all = new()
        {
            ["id"] = item.Id,
            ["type"] = item.ObjectType.ToString(),
            ["name"] = item.Attributes.Name,
            ["layer"] = doc.Layers[item.Attributes.LayerIndex].FullPath,
            ["visible"] = item.Visible,
            ["selected"] = item.IsSelected(false) > 0,
            ["bbox"] = box.IsValid
                ? new Dictionary<string, object?>
                {
                    ["min"] = Coordinates(box.Min), ["max"] = Coordinates(box.Max),
                }
                : null,
        };
        return fields is null
            ? all
            : all.Where(pair => pair.Key == "id" || fields.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static Dictionary<string, object?> Page(
        object[] items, int total, int page, int size, long version) => new()
    {
        ["items"] = items,
        ["page"] = page,
        ["page_size"] = size,
        ["total"] = total,
        ["next_page"] = page * size < total ? page + 1 : null,
        ["scene_version"] = version,
    };

    private static ObjectAttributes NewAttributes(RhinoDoc doc, string? name, string? layer)
    {
        ObjectAttributes attributes = new();
        if (!string.IsNullOrWhiteSpace(name))
            attributes.Name = name;
        if (!string.IsNullOrWhiteSpace(layer))
            attributes.LayerIndex = EnsureLayer(doc, layer!);
        return attributes;
    }

    private static ObjectAttributes NewPreviewAttributes(string? name)
    {
        ObjectAttributes attributes = new();
        if (!string.IsNullOrWhiteSpace(name))
            attributes.Name = name;
        return attributes;
    }

    private static void ValidateGeometry(string kind, JsonElement geometry)
    {
        switch (kind)
        {
            case "point":
                _ = Point(geometry, "point");
                break;
            case "line":
                _ = Point(geometry, "from");
                _ = Point(geometry, "to");
                break;
            case "box":
                BoundingBox box = new(Point(geometry, "min"), Point(geometry, "max"));
                if (!box.IsValid)
                    throw new ArgumentException("box min and max do not form a valid box");
                break;
            case "sphere":
                _ = Point(geometry, "center");
                RequirePositive(geometry, "radius");
                break;
            case "cylinder":
                _ = Point(geometry, "base");
                RequirePositive(geometry, "radius");
                RequirePositive(geometry, "height");
                break;
            case "polyline":
                _ = Points(geometry, "points").ToArray();
                break;
            default:
                throw new ArgumentException(
                    "kind must be point, line, box, sphere, cylinder, or polyline");
        }
    }

    private static void RequirePositive(JsonElement parent, string name)
    {
        double value = GetDouble(parent, name, double.NaN);
        if (double.IsNaN(value) || value <= 0)
            throw new ArgumentException($"{name} must be greater than zero");
    }

    private static int EnsureLayer(RhinoDoc doc, string name)
    {
        int index = FindLayer(doc, name);
        if (index >= 0)
            return index;
        index = doc.Layers.Add(new Layer { Name = name });
        if (index < 0)
            throw new InvalidOperationException($"Could not create layer '{name}'.");
        return index;
    }

    private static int FindLayer(RhinoDoc doc, string name)
    {
        Layer? layer = doc.Layers.FirstOrDefault(item =>
            !item.IsDeleted && (string.Equals(item.FullPath, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));
        return layer?.Index ?? -1;
    }

    private static Transform BuildTransform(JsonElement value, Point3d fallbackCenter)
    {
        Transform result = Transform.Identity;
        if (TryPoint(value, "translation", out Point3d translation))
            result = Transform.Translation(new Vector3d(translation.X, translation.Y, translation.Z)) * result;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("scale", out JsonElement scale)
            && scale.TryGetDouble(out double factor))
        {
            Point3d center = TryPoint(value, "center", out Point3d requested) ? requested : fallbackCenter;
            result = Transform.Scale(center, factor) * result;
        }
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("rotation_degrees", out JsonElement rotation)
            && rotation.TryGetDouble(out double degrees))
        {
            Point3d center = TryPoint(value, "center", out Point3d requested) ? requested : fallbackCenter;
            Vector3d axis = TryPoint(value, "axis", out Point3d requestedAxis)
                ? new Vector3d(requestedAxis.X, requestedAxis.Y, requestedAxis.Z) : Vector3d.ZAxis;
            result = Transform.Rotation(RhinoMath.ToRadians(degrees), axis, center) * result;
        }
        if (!result.IsValid)
            throw new ArgumentException("transform is not valid");
        return result;
    }

    private static Point3d Point(JsonElement parent, string name)
    {
        if (!TryPoint(parent, name, out Point3d point))
            throw new ArgumentException($"{name} must be [x, y, z]");
        return point;
    }

    private static bool TryPoint(JsonElement parent, string name, out Point3d point)
    {
        point = Point3d.Unset;
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
            return false;
        double[] values = value.EnumerateArray().Select(item => item.GetDouble()).ToArray();
        if (values.Length != 3)
            return false;
        point = new Point3d(values[0], values[1], values[2]);
        return true;
    }

    private static IEnumerable<Point3d> Points(JsonElement parent, string name)
    {
        JsonElement array = GetArray(parent, name);
        Point3d[] points = array.EnumerateArray().Select(item =>
        {
            double[] values = item.EnumerateArray().Select(number => number.GetDouble()).ToArray();
            if (values.Length != 3)
                throw new ArgumentException("Each point must be [x, y, z].");
            return new Point3d(values[0], values[1], values[2]);
        }).ToArray();
        if (points.Length < 2)
            throw new ArgumentException("A polyline requires at least two points.");
        return points;
    }

    private static double[] Coordinates(Point3d point) => new[] { point.X, point.Y, point.Z };

    private static Color ParseColor(string value)
    {
        try { return ColorTranslator.FromHtml(value); }
        catch { throw new ArgumentException("color must be a CSS hex color such as #4F8EF7"); }
    }

    private static Guid[] Guids(JsonElement parent, string name) => GetArray(parent, name)
        .EnumerateArray().Select(value => Guid.Parse(value.GetString()
            ?? throw new ArgumentException($"{name} contains an invalid ID"))).ToArray();

    private static JsonElement GetObject(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Object)
            return value;
        throw new ArgumentException($"{name} must be an object");
    }

    private static JsonElement GetArray(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Array)
            return value;
        throw new ArgumentException($"{name} must be an array");
    }

    private static string? GetString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string[]? GetStringArray(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetString() ?? "").ToArray() : null;

    private static int GetInt(JsonElement parent, string name, int fallback) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            && value.TryGetInt32(out int result) ? result : fallback;

    private static long GetLong(JsonElement parent, string name, long fallback) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            && value.TryGetInt64(out long result) ? result : fallback;

    private static double GetDouble(JsonElement parent, string name, double fallback) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            && value.TryGetDouble(out double result) ? result : fallback;

    private static bool GetBool(JsonElement parent, string name, bool fallback) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    private static JsonElement WithDryRun(JsonElement operation, bool dryRun)
    {
        Dictionary<string, JsonElement> properties = operation.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        using JsonDocument boolean = JsonDocument.Parse(dryRun ? "true" : "false");
        properties["dry_run"] = boolean.RootElement.Clone();
        return JsonSerializer.SerializeToElement(properties);
    }
}

internal sealed class CapturedViewport : IDisposable
{
    private readonly Bitmap _bitmap;
    private readonly string _format;
    private readonly int _quality;
    private readonly int _width;
    private readonly int _height;

    public CapturedViewport(Bitmap bitmap, string format, int quality, int width, int height)
    {
        _bitmap = bitmap;
        _format = format;
        _quality = quality;
        _width = width;
        _height = height;
    }

    public Dictionary<string, object?> Encode()
    {
        using (_bitmap)
        using (MemoryStream stream = new())
        {
            if (_format == "png")
            {
                _bitmap.Save(stream, ImageFormat.Png);
            }
            else
            {
                ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders()
                    .First(item => item.FormatID == ImageFormat.Jpeg.Guid);
                using EncoderParameters encoder = new(1);
                encoder.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, _quality);
                _bitmap.Save(stream, codec, encoder);
            }
            return new Dictionary<string, object?>
            {
                ["source"] = new Dictionary<string, object?>
                {
                    ["format"] = _format,
                    ["data"] = Convert.ToBase64String(stream.ToArray()),
                },
                ["width"] = _width,
                ["height"] = _height,
            };
        }
    }

    public void Dispose() => _bitmap.Dispose();
}
