using Newtonsoft.Json;

namespace BSL.v41.General.Network;

/// <summary>
/// Persistent rewards queued by the local admin panel for the next login.
/// Values are delivery types: 10 Brawl/Savaş, 11 Büyük, 12 Mega.
/// </summary>
public static class AdminRewardStore
{
    private static readonly object Gate = new();
    private static readonly string FilePath = "SaveBase/admin_inbox_rewards.json";
    private static Dictionary<long, List<int>> _rewards = new();

    public static void Enqueue(long accountId, int deliveryType)
    {
        lock (Gate)
        {
            if (!_rewards.TryGetValue(accountId, out var list))
                _rewards[accountId] = list = [];

            list.Add(deliveryType);
            Save();
        }
    }

    public static List<int> Take(long accountId)
    {
        lock (Gate)
        {
            if (!_rewards.Remove(accountId, out var list)) return [];
            Save();
            return list;
        }
    }

    public static void Initialize()
    {
        lock (Gate)
        {
            if (!File.Exists(FilePath))
            {
                _rewards = new Dictionary<long, List<int>>();
                return;
            }

            _rewards = JsonConvert.DeserializeObject<Dictionary<long, List<int>>>(
                File.ReadAllText(FilePath)) ?? new Dictionary<long, List<int>>();
        }
    }

    private static void Save()
    {
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(_rewards, Formatting.Indented));
    }
}
