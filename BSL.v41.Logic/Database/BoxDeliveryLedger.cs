using Newtonsoft.Json;

namespace BSL.v41.Logic.Database;

/// <summary>
/// Durable acknowledgement ledger for server-issued box deliveries.
/// Rewards are created when the server delivery is built; command 517 only
/// acknowledges that delivery and must never mint it again on a new session.
/// </summary>
public static class BoxDeliveryLedger
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine("SaveBase", "box_delivery_ledger.json");
    private static readonly Dictionary<long, List<int>> Pending = new();
    private static readonly Dictionary<long, int> Active = new();
    private static bool _loaded;

    public static void Issue(long accountId, int deliveryType)
    {
        IssueMany(accountId, deliveryType, 1);
    }

    public static void IssueMany(long accountId, int deliveryType, int count)
    {
        if (accountId <= 0 || deliveryType is < 10 or > 12 || count <= 0) return;

        lock (Gate)
        {
            EnsureLoaded();
            if (!Pending.TryGetValue(accountId, out var deliveries))
                Pending[accountId] = deliveries = [];
            for (var index = 0; index < count; index++)
                deliveries.Add(deliveryType);
            Active[accountId] = deliveryType;
            Save();
        }
    }

    public static bool TryGetPending(long accountId, int deliveryType)
    {
        if (accountId <= 0 || deliveryType is < 10 or > 12) return false;

        lock (Gate)
        {
            EnsureLoaded();
            return Pending.TryGetValue(accountId, out var deliveries) &&
                   deliveries.Contains(deliveryType);
        }
    }

    public static void MarkActive(long accountId, int deliveryType)
    {
        if (accountId <= 0 || deliveryType is < 10 or > 12) return;
        lock (Gate)
        {
            EnsureLoaded();
            Active[accountId] = deliveryType;
        }
    }

    public static int GetPendingCount(long accountId, int deliveryType)
    {
        if (accountId <= 0 || deliveryType is < 10 or > 12) return 0;

        lock (Gate)
        {
            EnsureLoaded();
            return Pending.TryGetValue(accountId, out var deliveries)
                ? deliveries.Count(type => type == deliveryType)
                : 0;
        }
    }

    public static bool TryAcknowledge(long accountId, int deliveryType)
    {
        if (accountId <= 0 || deliveryType is < 10 or > 12) return false;

        lock (Gate)
        {
            EnsureLoaded();
            if (!Pending.TryGetValue(accountId, out var deliveries)) return false;

            var index = deliveries.IndexOf(deliveryType);
            if (index < 0) return false;

            deliveries.RemoveAt(index);
            if (Active.TryGetValue(accountId, out var active) && active == deliveryType)
                Active.Remove(accountId);
            if (deliveries.Count == 0) Pending.Remove(accountId);
            Save();
            return true;
        }
    }

    public static bool TryAcknowledgeAny(long accountId, out int deliveryType)
    {
        deliveryType = 0;
        if (accountId <= 0) return false;

        lock (Gate)
        {
            EnsureLoaded();
            if (!Pending.TryGetValue(accountId, out var deliveries) || deliveries.Count == 0)
                return false;

            var index = Active.TryGetValue(accountId, out var active)
                ? deliveries.IndexOf(active)
                : 0;
            if (index < 0) index = 0;
            deliveryType = deliveries[index];
            deliveries.RemoveAt(index);
            Active.Remove(accountId);
            if (deliveries.Count == 0) Pending.Remove(accountId);
            Save();
            return true;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (!File.Exists(FilePath)) return;
            var loaded = JsonConvert.DeserializeObject<Dictionary<long, List<int>>>(
                File.ReadAllText(FilePath));
            if (loaded == null) return;
            foreach (var pair in loaded)
                Pending[pair.Key] = pair.Value ?? [];
        }
        catch
        {
            // A corrupt ledger must not prevent the game from starting. No
            // existing account data is touched; a fresh ledger is used.
        }
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = $"{FilePath}.{System.Environment.ProcessId}.tmp";
        File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(Pending, Formatting.Indented));
        File.Move(temporaryPath, FilePath, true);
    }
}
