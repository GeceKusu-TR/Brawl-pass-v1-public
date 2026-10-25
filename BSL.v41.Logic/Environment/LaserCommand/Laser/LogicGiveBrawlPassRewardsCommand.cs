using BSL.v41.Logic.Database;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.DataStream.Helps;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.Logic.Environment.LaserCommand.Laser;

/// <summary>
/// Direct reward delivery used by the V41 season-reward request. Claims are
/// inserted into MySQL before the delivery is sent, so the same level cannot
/// be paid twice after a reconnect or restart.
/// </summary>
public sealed class LogicGiveBrawlPassRewardsCommand : LogicCommand
{
    private const int TokensPerLevel = 500;
    private const int MaxPassTokens = 34500;
    private const int MaxLevel = 70;
    private const int PowerPointsPerLevel = 10;
    private readonly List<DeliveryReward> _rewards = [];

    public int GemsGranted { get; private set; }

    public LogicGiveBrawlPassRewardsCommand(AccountModel account)
    {
        var entries = account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        var season = MySqlPersistence.GetBrawlPassSeasonState();
        var passTokens = entries.TryGetValue(28, out var tokenEntry)
            ? Math.Clamp(tokenEntry.Value, 0, MaxPassTokens)
            : 0;
        var unlockedLevel = Math.Clamp(passTokens / TokensPerLevel, 0, MaxLevel);
        var claimed = MySqlPersistence.GetBrawlPassClaimedLevels(account.GetAccountId(), season.SeasonId, 0);
        var beforePowerPoints = GetResource(entries, 12, 29);

        var gold = 0;
        var gems = 0;
        var powerPoints = 0;
        for (var level = 0; level <= unlockedLevel; level++)
        {
            if (claimed.Contains(level) || !MySqlPersistence.TryClaimBrawlPassLevel(
                    account.GetAccountId(), season.SeasonId, 0, level)) continue;

            // Default V41 free-track rewards. The client renders the track
            // from its own season data; these are the durable server grants.
            var levelGold = 50 + level * 10;
            gold += levelGold;
            _rewards.Add(new DeliveryReward(levelGold, 0, 0, 7));
            powerPoints += PowerPointsPerLevel;
            // Power points are a typed brawler drop in V41, not a generic
            // resource. The client needs the character data reference to
            // render and apply the reward safely.
            _rewards.Add(new DeliveryReward(PowerPointsPerLevel, 16, 0, 6));
            if (level > 0 && level % 5 == 0)
            {
                gems += 5;
                _rewards.Add(new DeliveryReward(5, 0, 0, 8));
            }
        }

        if (gold > 0) AddResource(account, 1, gold);
        if (gems > 0) AddResource(account, 16, gems);
        if (powerPoints > 0) AddResource(account, 12, powerPoints);
        GemsGranted = gems;
        AccountTraceLogger.Log(account.GetAccountId(), "BRAWL-PASS-GRANT",
            $"tokens={passTokens} unlockedLevel={unlockedLevel} claimedBefore={claimed.Count} " +
            $"rewardEntries={_rewards.Count} gold={gold} gems={gems} powerPoints={powerPoints} " +
            $"powerPointsBefore={beforePowerPoints} powerPointsAfter={GetResource(account, 12, 29)}");
    }

    public bool HasRewards => _rewards.Count > 0;

    public override void Encode(ByteStream byteStream)
    {
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(1);
        byteStream.WriteVInt(100); // direct reward delivery, no box screen
        byteStream.WriteVInt(_rewards.Count);
        foreach (var reward in _rewards)
        {
            byteStream.WriteVInt(reward.Amount);
            ByteStreamHelper.WriteDataReference(byteStream, reward.DataClassId, reward.DataInstanceId);
            byteStream.WriteVInt(reward.ValueType);
            ByteStreamHelper.WriteDataReference(byteStream, 0); // skin data
            ByteStreamHelper.WriteDataReference(byteStream, 0); // card data
            byteStream.WriteVInt(0);
        }

        byteStream.WriteBoolean(true);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(DeliveryCommandId.Next());
        byteStream.WriteVInt(1);
        byteStream.WriteVInt(1);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(0);

    }

    public override int GetCommandType() => 203;

    private static void AddResource(AccountModel account, int key, int amount)
    {
        var entries = account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        var current = entries.TryGetValue(key, out var entry) ? entry.Value : 0;
        entries[key] = new IntValueEntry(key, checked(current + amount));
        account.SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX, entries);
    }

    private static int GetResource(Dictionary<int, IntValueEntry> entries, int primaryKey, int legacyKey)
    {
        return Math.Max(entries.TryGetValue(primaryKey, out var primary) ? primary.Value : 0,
            entries.TryGetValue(legacyKey, out var legacy) ? legacy.Value : 0);
    }

    private static int GetResource(AccountModel account, int primaryKey, int legacyKey)
    {
        var entries = account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        return GetResource(entries, primaryKey, legacyKey);
    }

    private sealed record DeliveryReward(int Amount, int DataClassId, int DataInstanceId, int ValueType);
}
