using BSL.v41.Logic.Database;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.DataStream.Helps;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.Logic.Environment.LaserCommand.Laser;

/// <summary>
/// Server command 203. This is the V41 delivery/gacha payload consumed by the
/// client's box-opening screen. A text notification alone does not open a box.
/// </summary>
public sealed class LogicGiveDeliveryItemsCommand : LogicCommand
{
    private readonly int _deliveryType;
    private readonly bool _brawlPassReward;
    private readonly int _drawIndex;
    private readonly List<DeliveryReward> _rewards;
    private readonly long _accountId;
    private readonly string _claimKey;

    public LogicGiveDeliveryItemsCommand(AccountModel accountModel, int deliveryType,
        bool brawlPassReward = false, bool grantRewards = true, bool issueLedger = true,
        int drawIndex = 0)
    {
        _accountId = accountModel.GetAccountId();
        _deliveryType = deliveryType;
        _brawlPassReward = brawlPassReward;
        _drawIndex = Math.Max(0, drawIndex);
        _claimKey = $"delivery:{Guid.NewGuid():N}";
        var beforeGold = ReadResource(accountModel, 1);
        var beforeGems = ReadResource(accountModel, 16);
        _rewards = CreateRewards(accountModel, deliveryType, brawlPassReward, grantRewards, _drawIndex);
        if (issueLedger)
        {
            BoxDeliveryLedger.Issue(_accountId, _deliveryType);
            var persisted = MySqlPersistence.RecordBoxClaim(
                _accountId, _claimKey, _deliveryType, "delivery_issued", status: "claimed");
            AccountTraceLogger.Log(_accountId, "BOX-CLAIM-MYSQL",
                $"claimKey={_claimKey} deliveryType={_deliveryType} " +
                $"status=claimed persisted={persisted}");
        }
        AccountTraceLogger.Log(_accountId, "BOX-BUILD",
            $"deliveryType={_deliveryType} rewards={string.Join(';', _rewards.Select(reward =>
                $"amount={reward.Amount},valueType={reward.ValueType},data={reward.DataClassId}:{reward.DataInstanceId}"))} " +
                $"goldBefore={beforeGold} goldAfter={ReadResource(accountModel, 1)} " +
                $"gemsBefore={beforeGems} gemsAfter={ReadResource(accountModel, 16)} " +
                $"brawlPassReward={_brawlPassReward} drawIndex={_drawIndex}");
    }

    public override void Encode(ByteStream byteStream)
    {
        // Command 203 is a list of DeliveryUnit/GatchaDrop records. The
        // V41.150 client path tested on the real device consumes three data
        // references after ValueType (skin, pin, card), followed by one
        // trailing VInt. The shorter two-reference variant leaves the client
        // on its loading screen, so keep the proven wire shape here.
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(1);
        // 10 = Brawl/Savaş, 11 = Mega and 12 = Büyük.  Type 100 is only a
        // generic direct-reward unit and does not start the box screen.
        byteStream.WriteVInt(_deliveryType);
        byteStream.WriteVInt(_rewards.Count);

        foreach (var reward in _rewards)
        {
            byteStream.WriteVInt(reward.Amount);
            ByteStreamHelper.WriteDataReference(byteStream, reward.DataClassId, reward.DataInstanceId);
            byteStream.WriteVInt(reward.ValueType);
            ByteStreamHelper.WriteDataReference(byteStream, 0); // skin data
            ByteStreamHelper.WriteDataReference(byteStream, 0); // pin data
            ByteStreamHelper.WriteDataReference(byteStream, 0); // card data
            byteStream.WriteVInt(0);
        }

        // ForcedDrops + V41 delivery metadata. The device-tested layout ends
        // with six VInts and a zero LogicLong (two VInts); adding generated
        // command/base fields here makes the box screen wait indefinitely.
        byteStream.WriteBoolean(true);
        byteStream.WriteVInt(1);
        byteStream.WriteVInt(1);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(0);

        AccountTraceLogger.Log(_accountId, "BOX-ENCODE",
            $"deliveryType={_deliveryType} rewardCount={_rewards.Count} " +
            $"drawIndex={_drawIndex} layout=v41_150_count_data_type_skin_pin_card_footer " +
            $"payloadBytes={byteStream.GetLength()}");
    }

    public override int GetCommandType()
    {
        return 203;
    }

    private static List<DeliveryReward> CreateRewards(AccountModel accountModel, int deliveryType,
        bool brawlPassReward, bool grantRewards, int drawIndex)
    {
        var (gold, secondary) = deliveryType switch
        {
            11 => (Random.Shared.Next(250, 501), Random.Shared.Next(30, 51)), // Mega Box
            12 => (Random.Shared.Next(100, 251), Random.Shared.Next(20, 41)), // Big Box
            _ => (Random.Shared.Next(50, 101), Random.Shared.Next(10, 31)) // Brawl/Savaş Box
        };

        if (brawlPassReward)
        {
            // Each Big Box draw is sent as a separate command 203. This keeps
            // the client-safe one-drop wire shape while still implementing
            // the three draws expected for a Big Box.
            if (deliveryType == 12)
            {
                if (drawIndex == 1)
                {
                    // ValueType 8 is the V41 gem drop. Resource key 12 is
                    // power points; writing the gem amount there made the
                    // reward animation and the persisted balance disagree.
                    if (grantRewards) AddResource(accountModel, 16, secondary);
                    return [new DeliveryReward(secondary, 0, 0, 8)];
                }

                var drawGold = drawIndex == 2 ? Random.Shared.Next(50, 101) : gold;
                if (grantRewards) AddResource(accountModel, 1, drawGold);
                return [new DeliveryReward(drawGold, 0, 0, 7)];
            }

            if (grantRewards) AddResource(accountModel, 1, gold);
            // Keep the previously device-tested single scalar reward for
            // Brawl and Mega deliveries until their native draw layouts are
            // separately verified.
            if (grantRewards)
                AddResource(accountModel, 1, secondary);
            return
            [
                new DeliveryReward(checked(gold + secondary), 0, 0, 7)
            ];
        }

        if (grantRewards)
            AddResource(accountModel, 1, gold);  // Coin/gold
        if (grantRewards)
            AddResource(accountModel, 16, secondary); // Gems for shop boxes

        return
        [
            new DeliveryReward(gold, 0, 0, 7),
            new DeliveryReward(secondary, 0, 0, 8)
        ];
    }

    private static void AddResource(AccountModel accountModel, int key, int amount)
    {
        var entries = accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        var current = entries.TryGetValue(key, out var entry) ? entry.Value : 0;
        entries[key] = new IntValueEntry(key, checked(current + amount));
        accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX, entries);
    }

    private static int ReadResource(AccountModel accountModel, int key)
    {
        var entries = accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        return entries.TryGetValue(key, out var entry) ? entry.Value : 0;
    }

    private sealed record DeliveryReward(int Amount, int DataClassId, int DataInstanceId, int ValueType);
}

internal static class DeliveryCommandId
{
    private static int _nextId;

    public static int Next() => System.Threading.Interlocked.Increment(ref _nextId);
}
