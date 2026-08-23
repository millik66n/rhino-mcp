using Rhino;
using Rhino.DocObjects;

namespace RhinoMCP;

internal static class SceneChangeTracker
{
    internal sealed record Change(Guid Id, long Version);
    private static readonly object Sync = new();
    private static readonly Dictionary<Guid, long> Changed = new();
    private static readonly Dictionary<Guid, long> Deleted = new();
    private static long _version;

    public static long Version => Interlocked.Read(ref _version);

    public static void Start()
    {
        RhinoDoc.AddRhinoObject += OnChanged;
        RhinoDoc.DeleteRhinoObject += OnDeleted;
        RhinoDoc.ReplaceRhinoObject += OnReplaced;
        RhinoDoc.ModifyObjectAttributes += OnAttributesChanged;
    }

    public static void Stop()
    {
        RhinoDoc.AddRhinoObject -= OnChanged;
        RhinoDoc.DeleteRhinoObject -= OnDeleted;
        RhinoDoc.ReplaceRhinoObject -= OnReplaced;
        RhinoDoc.ModifyObjectAttributes -= OnAttributesChanged;
    }

    public static (long Version, IReadOnlyCollection<Change> Changed, IReadOnlyCollection<Change> Deleted)
        Since(long since)
    {
        lock (Sync)
        {
            return (
                Version,
                Changed.Where(pair => pair.Value > since)
                    .Select(pair => new Change(pair.Key, pair.Value)).ToArray(),
                Deleted.Where(pair => pair.Value > since)
                    .Select(pair => new Change(pair.Key, pair.Value)).ToArray());
        }
    }

    private static void MarkChanged(Guid id)
    {
        long version = Interlocked.Increment(ref _version);
        lock (Sync)
        {
            Changed[id] = version;
            Deleted.Remove(id);
            Trim(version);
        }
    }

    private static void MarkDeleted(Guid id)
    {
        long version = Interlocked.Increment(ref _version);
        lock (Sync)
        {
            Deleted[id] = version;
            Changed.Remove(id);
            Trim(version);
        }
    }

    private static void Trim(long current)
    {
        long minimum = Math.Max(0, current - 100_000);
        foreach (Guid id in Changed.Where(pair => pair.Value < minimum).Select(pair => pair.Key).ToArray())
            Changed.Remove(id);
        foreach (Guid id in Deleted.Where(pair => pair.Value < minimum).Select(pair => pair.Key).ToArray())
            Deleted.Remove(id);
    }

    private static void OnChanged(object? sender, RhinoObjectEventArgs args) => MarkChanged(args.ObjectId);
    private static void OnDeleted(object? sender, RhinoObjectEventArgs args) => MarkDeleted(args.ObjectId);
    private static void OnReplaced(object? sender, RhinoReplaceObjectEventArgs args) => MarkChanged(args.NewRhinoObject.Id);
    private static void OnAttributesChanged(object? sender, RhinoModifyObjectAttributesEventArgs args) =>
        MarkChanged(args.RhinoObject.Id);
}
