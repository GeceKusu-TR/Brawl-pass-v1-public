using Newtonsoft.Json;
using BSL.v41.Titan.Graphic;

namespace BSL.v41.Logic.Database.Account;

public class AccountDatabase : AccountModel
{
    private readonly object _databaseGate = new();
    private readonly string? _filePath;
    private Dictionary<long, AccountModel> _onRamBase;
    private DateTime _lastLoadedWriteTimeUtc;

    public AccountDatabase(string? filePath, bool loadByBackup = false)
    {
        _filePath = filePath;
        _onRamBase = new Dictionary<long, AccountModel>();
    }

    public void CyclingLoadDataFromFile(int perSec)
    {
        Task.Run(() =>
        {
            while (true)
            {
                Thread.Sleep(perSec * 1000);

                // Do not replace live RAM state every 30 seconds. That race
                // used to make resource and tutorial updates appear to roll
                // back. Reload only when the file was changed externally.
                try
                {
                    if (File.Exists(_filePath!) && File.GetLastWriteTimeUtc(_filePath!) != _lastLoadedWriteTimeUtc)
                        LoadDataFromFile(true);
                }
                catch
                {
                    // A transient file error must not evict live accounts.
                }
            }
        });
    }

    public void LoadDataFromFile(bool reloaded = false)
    {
        lock (_databaseGate)
        {
            try
            {
                var data = File.ReadAllText(_filePath!);
                var loaded = ConvertJsonToDictionary(data);
                if (loaded.Count > 0)
                    _onRamBase = loaded;
                else if (AccountRedisStore.TryLoadAll(out var recovered))
                    _onRamBase = recovered;

                var restoredResourceCount = 0;
                foreach (var account in _onRamBase.Values)
                    if (MySqlPersistence.TryRestoreResources(account)) restoredResourceCount++;

                if (restoredResourceCount > 0)
                    WriteDataAtomically(_onRamBase);

                _lastLoadedWriteTimeUtc = File.GetLastWriteTimeUtc(_filePath!);
                AccountRedisStore.Mirror(_onRamBase);
                foreach (var account in _onRamBase.Values)
                {
                    MySqlPersistence.EnqueueLeaderboard(account);
                    MySqlPersistence.EnqueueResources(account);
                    MySqlPersistence.EnqueueOfferPurchases(account);
                }

                ConsoleLogger.WriteTextWithPrefix(!reloaded ? ConsoleLogger.Prefixes.Start : ConsoleLogger.Prefixes.Load,
                    $"Database-element {(reloaded ? "reloaded" : "started")}! Information: element name: {GetType().Name}; " +
                    $"database buffer length: {_onRamBase.Count}.");
            }
            catch (Exception exception)
            {
                if (AccountRedisStore.TryLoadAll(out var recovered))
                {
                    _onRamBase = recovered;
                    WriteDataAtomically(_onRamBase);
                    ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Load,
                        $"Account database recovered from Redis; account count: {_onRamBase.Count}.");
                }
                else
                    ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Error, exception);
            }
        }
    }

    public AccountModel CreateAccount(AccountModel accountModel, long accountId = -1)
    {
        accountId = accountId < 0 ? GetNewAccountId() : accountId;
        {
            try
            {
                OnAssistByAccountInFile(accountModel, false, accountId);
                {
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AccountId,
                        accountId);
                }
            }
            catch
            {
                // ignored.
            }
        }

        return accountModel;
    }

    public AccountModel LoadAccount(long id)
    {
        try
        {
            return _onRamBase[id];
        }
        catch (Exception)
        {
            // ignored.
        }

        return null!;
    }

    /// <summary>
    /// Returns a stable read-only snapshot for queries such as leaderboards.
    /// The caller must not mutate the returned account models.
    /// </summary>
    public IReadOnlyList<AccountModel> SnapshotAccounts()
    {
        lock (_databaseGate)
            return _onRamBase.Values.ToList();
    }

    public AccountModel SetAccountParameter(long accountId, AccountStructure accountStructure, object value)
    {
        var accountModel = LoadAccount(accountId);
        if (accountModel == null!) return null!;

        var parameterName = AccountHelper.GetNormalParameterNameFromAccountStructure(accountStructure);
        {
            if (accountModel != null!) ReplaceFieldValueByFieldName(accountModel, parameterName, value);
        }

        return OnAssistByAccountInFile(accountModel!, true, accountId);
    }

    public object GetAccountParameter(long accountId, AccountStructure accountStructure)
    {
        var accountModel = LoadAccount(accountId);
        {
            if (accountModel != null!)
                return accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(accountStructure);
        }

        return null!;
    }

    public AccountModel OnAssistByAccountInFile(AccountModel accountModel, bool wasReplace, long accountId = -1)
    {
        lock (_databaseGate)
        {
            accountId = Convert.ToInt64(accountId < 0
                ? accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AccountId)
                : accountId);

            var jsonData = File.Exists(_filePath!) ? File.ReadAllText(_filePath!) : "{}";
            var accountData = JsonConvert.DeserializeObject<Dictionary<long, AccountModel>>(jsonData)
                              ?? new Dictionary<long, AccountModel>();
            var newAccountModel = new AccountModel();
            switch (wasReplace)
            {
                case true:
                    _onRamBase[accountId] = accountModel;
                    accountData[accountId] = accountModel;
                    AbstractingAccountModelFromRootAccountModel(newAccountModel, accountModel);
                    break;
                case false:
                    _onRamBase[accountId] = accountModel;
                    accountData[accountId] = accountModel;
                    break;
            }

            WriteDataAtomically(accountData);
            _lastLoadedWriteTimeUtc = File.GetLastWriteTimeUtc(_filePath!);
            AccountRedisStore.Save(accountModel);
            MySqlPersistence.EnqueueLeaderboard(accountModel);
            MySqlPersistence.EnqueueResources(accountModel);
            MySqlPersistence.EnqueueOfferPurchases(accountModel);
            return newAccountModel;
        }
    }

    private void WriteDataAtomically(Dictionary<long, AccountModel> accountData)
    {
        var tempPath = $"{_filePath}.{System.Environment.ProcessId}.tmp";
        var json = JsonConvert.SerializeObject(accountData, Formatting.Indented);
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                   4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(true);
        }

        File.Move(tempPath, _filePath!, true);
    }

    public string ConvertDictionaryToJson(Dictionary<long, AccountModel>? onRamBase = null!)
    {
        return JsonConvert.SerializeObject(onRamBase ?? _onRamBase);
    }

    public Dictionary<long, AccountModel> ConvertJsonToDictionary(string json, bool localUs = false)
    {
        return localUs ? _onRamBase : JsonConvert.DeserializeObject<Dictionary<long, AccountModel>>(json)!;
    }

    public long GetMaxAccountId()
    {
        return _onRamBase.Count == 0 ? 0 : _onRamBase.Keys.Max();
    }

    public long GetNewAccountId()
    {
        return GetMaxAccountId() + 1;
    }
}
