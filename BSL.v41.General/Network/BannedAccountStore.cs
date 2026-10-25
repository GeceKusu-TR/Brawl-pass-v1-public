using Newtonsoft.Json;

namespace BSL.v41.General.Network;

/// <summary>
/// Small persistent ban list used by the local administrator panel and login gate.
/// The server keeps this separate from account_database.json so an unban never
/// rewrites or risks corrupting player data.
/// </summary>
public static class BannedAccountStore
{
    private static readonly object Gate = new();
    private static readonly string FilePath = "SaveBase/banned_accounts.json";
    private static Dictionary<long, string> _bannedAccounts = new();

    public static void Initialize()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    Save();
                    return;
                }

                _bannedAccounts = JsonConvert.DeserializeObject<Dictionary<long, string>>(
                    File.ReadAllText(FilePath)) ?? new Dictionary<long, string>();
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[ADMIN] Ban list could not be loaded: {exception.Message}");
                _bannedAccounts = new Dictionary<long, string>();
            }
        }
    }

    public static bool IsBanned(long accountId)
    {
        lock (Gate) return _bannedAccounts.ContainsKey(accountId);
    }

    public static string GetReason(long accountId)
    {
        lock (Gate)
            return _bannedAccounts.TryGetValue(accountId, out var reason) && !string.IsNullOrWhiteSpace(reason)
                ? reason
                : "Yönetici tarafından erişiminiz engellendi.";
    }

    public static bool Ban(long accountId, string reason)
    {
        lock (Gate)
        {
            var existed = _bannedAccounts.ContainsKey(accountId);
            _bannedAccounts[accountId] = string.IsNullOrWhiteSpace(reason) ? "Yönetici tarafından banlandı." : reason;
            Save();
            return !existed;
        }
    }

    public static bool Unban(long accountId)
    {
        lock (Gate)
        {
            var removed = _bannedAccounts.Remove(accountId);
            if (removed) Save();
            return removed;
        }
    }

    public static IReadOnlyDictionary<long, string> Snapshot()
    {
        lock (Gate) return new Dictionary<long, string>(_bannedAccounts);
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(_bannedAccounts, Formatting.Indented));
    }
}
