using System.Collections;
using System.Runtime.CompilerServices;

namespace RefreshAncient.Compat2;

public static class AncientHistory
{
    private sealed class RunHistory
    {
        public readonly Dictionary<(ulong, object), RoomHistory> Rooms = new();
    }
    private sealed class RoomHistory
    {
        public readonly HashSet<string> Seen = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, object> Known = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Eligible = new(StringComparer.OrdinalIgnoreCase);
    }
    private static readonly ConditionalWeakTable<object, RunHistory> Runs = new();
    private static readonly object Gate = new();

    internal static string Id(object ancient)
    {
        string? id = Convert.ToString(UniqueOptions.Get(UniqueOptions.Get(ancient, "Id"), "Entry"));
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Ancient event has no model ID.");
        return id;
    }

    private static RoomHistory History(object run, object current)
    {
        object owner = UniqueOptions.Get(current, "Owner") ?? throw new InvalidOperationException("Ancient event has no owner.");
        ulong player = Convert.ToUInt64(UniqueOptions.Get(owner, "NetId"));
        object location = UniqueOptions.Get(run, "RunLocation") ?? UniqueOptions.Get(run, "CurrentRoom")
            ?? throw new InvalidOperationException("Current room identity is unavailable.");
        if (!Runs.TryGetValue(run, out RunHistory? history)) { history = new RunHistory(); Runs.Add(run, history); }
        var key = (player, location);
        if (!history.Rooms.TryGetValue(key, out RoomHistory? room)) { room = new RoomHistory(); history.Rooms.Add(key, room); }
        string id = Id(current);
        room.Seen.Add(id);
        room.Known[id] = current;
        return room;
    }
    private static object Run(object current) => UniqueOptions.Get(UniqueOptions.Get(current, "Owner"), "RunState")
        ?? throw new InvalidOperationException("Run state is unavailable.");

    public static object Filter(object run, object current, object candidates)
    {
        lock (Gate)
        {
            RoomHistory history = History(run, current);
            history.Eligible.Clear();
            history.Eligible.Add(Id(current));
            IList result = (IList)Activator.CreateInstance(candidates.GetType())!;
            HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
            foreach (object candidate in UniqueOptions.Items(candidates))
            {
                string id = Id(candidate);
                history.Known[id] = candidate;
                history.Eligible.Add(id);
                if (!history.Seen.Contains(id) && unique.Add(id)) result.Add(candidate);
            }
            return result;
        }
    }

    public static bool CanReplace(object current, object replacement)
    {
        try
        {
            lock (Gate)
            {
                RoomHistory history = History(Run(current), current);
                string id = Id(replacement);
                return history.Eligible.Contains(id) && !history.Seen.Contains(id);
            }
        }
        catch (Exception error) { UniqueOptions.Log(error); return false; }
    }

    public static bool CommitIfSuccessful(bool success, object current, object replacement)
    {
        if (!success) return false;
        lock (Gate)
        {
            RoomHistory history = History(Run(current), current);
            string id = Id(replacement);
            history.Seen.Add(id);
            history.Known[id] = replacement;
        }
        return true;
    }

    public static int Remaining(object current)
    {
        lock (Gate)
        {
            RoomHistory history = History(Run(current), current);
            int count = 0;
            foreach (string id in history.Eligible) if (!history.Seen.Contains(id)) count++;
            return count;
        }
    }

    public static bool CanPress(object layout)
    {
        if (CatalogUi.IsOpen() || CatalogUi.ButtonsHidden(layout)) return false;
        try
        {
            object? current = UniqueOptions.CurrentAncient();
            if (current != null && Remaining(current) > 0) return true;
        }
        catch (Exception error) { UniqueOptions.Log(error); }
        CatalogUi.Update(layout);
        return false;
    }

    public static string[][] Catalog(object current) => CatalogCore(current, false);
    internal static string[][] CatalogRich(object current) => CatalogCore(current, true);
    internal static object[] CatalogModels(object current)
    {
        lock (Gate) return History(Run(current), current).Known.Values.ToArray();
    }
    private static string[][] CatalogCore(object current, bool rich)
    {
        lock (Gate)
        {
            RoomHistory history = History(Run(current), current);
            string currentId = Id(current);
            List<string[]> rows = new();
            foreach (KeyValuePair<string, object> entry in history.Known)
            {
                string status = string.Equals(currentId, entry.Key, StringComparison.OrdinalIgnoreCase) ? "current"
                    : history.Seen.Contains(entry.Key) ? "seen" : history.Eligible.Contains(entry.Key) ? "available" : "unavailable";
                rows.Add(new[]
                {
                    CatalogUi.ModelDisplayText(entry.Value, "Title", entry.Key, rich),
                    CatalogUi.ModelDisplayText(entry.Value, "InitialDescription", string.Empty, rich), status
                });
            }
            return rows.ToArray();
        }
    }
}
