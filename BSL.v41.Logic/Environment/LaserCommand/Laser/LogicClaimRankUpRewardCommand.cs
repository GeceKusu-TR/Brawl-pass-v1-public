using BSL.v41.Logic.Environment.LaserMessage.Sepo.Mode;
using BSL.v41.Logic.Database;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Supercell.Titan.CommonUtils.Utils;
using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.Mathematical.Data;
using BSL.v41.Titan.Diagnostics;
using BSL.v41.Tools.LaserCsv;

namespace BSL.v41.Logic.Environment.LaserCommand.Laser;

/// <summary>
/// V41 uses command 517 for the client-side reward/box claim action. Its
/// payload has the same common header and box index as command 500.
/// </summary>
public sealed class LogicClaimRankUpRewardCommand : LogicGatchaCommand
{
    protected override bool IssueDeliveryWhenMissing => true;

    // V41.150 Season 10 (the client labels server season 9 as "10. Sezon").
    // 10 = Brawl Box, 11 = Mega Box, 12 = Big Box.
    private static readonly IReadOnlyDictionary<int, int> FreeBoxDeliveryTypes =
        new Dictionary<int, int>
        {
            [0] = 12, [1] = 10, [3] = 12, [4] = 10, [5] = 12,
            [7] = 12, [8] = 10, [9] = 12, [10] = 11, [11] = 12,
            [12] = 10, [13] = 12, [15] = 12, [16] = 10, [17] = 12,
            [19] = 12, [20] = 11, [21] = 10, [23] = 12, [24] = 10,
            [25] = 12, [27] = 12, [28] = 10, [29] = 12, [30] = 11,
            [31] = 12, [33] = 10, [35] = 12, [37] = 12, [38] = 10,
            [39] = 12, [40] = 11, [41] = 10, [43] = 10, [45] = 11,
            [46] = 10, [47] = 12, [49] = 12, [51] = 11, [53] = 12,
            [54] = 10, [55] = 11, [57] = 12, [58] = 10, [59] = 12,
            [60] = 11, [61] = 12, [63] = 10, [65] = 11, [66] = 12,
            [68] = 11
        };

    private static readonly IReadOnlyDictionary<int, int> PremiumBoxDeliveryTypes =
        new Dictionary<int, int>
        {
            [2] = 11, [6] = 12, [14] = 12, [18] = 11, [22] = 12,
            [27] = 12, [32] = 12, [34] = 11, [36] = 12, [42] = 12,
            [44] = 12, [47] = 11, [50] = 12, [52] = 12, [56] = 12,
            [60] = 12, [62] = 11, [64] = 12
        };

    // Season 10 / server season 9 free-track gem cards. These are direct
    // rewards, not box deliveries, so they use command 202.
    private static readonly IReadOnlyDictionary<int, int> FreeGemRewards =
        new Dictionary<int, int>
        {
            [2] = 10, [14] = 10, [22] = 20, [36] = 10,
            [44] = 10, [52] = 10, [62] = 20
        };

    // Reward types from milestones.csv for server season 9 / client season 10.
    // Boxes remain on the tested box-delivery path above. These maps cover the
    // scalar rewards that previously fell through as non_box_reward_not_wired.
    private static readonly IReadOnlyDictionary<int, int> FreeGoldRewards =
        new Dictionary<int, int>
        {
            [6] = 50, [18] = 50, [32] = 100, [48] = 200,
            [56] = 200, [67] = 500
        };

    private static readonly IReadOnlyDictionary<int, int> FreePowerRewards =
        new Dictionary<int, int>
        {
            [26] = 50, [34] = 50, [42] = 75, [50] = 100,
            [64] = 100, [69] = 500
        };

    private static readonly IReadOnlyDictionary<int, int> PremiumGoldRewards =
        new Dictionary<int, int>
        {
            [3] = 50, [5] = 50, [7] = 50, [9] = 50, [11] = 50,
            [13] = 50, [15] = 50, [17] = 50, [19] = 50, [21] = 50,
            [24] = 50, [26] = 75, [28] = 75, [38] = 75, [40] = 100,
            [43] = 125, [54] = 200, [66] = 250
        };

    private static readonly IReadOnlyDictionary<int, int> PremiumPowerRewards =
        new Dictionary<int, int>
        {
            [1] = 25, [4] = 25, [8] = 50, [12] = 50, [16] = 50,
            [20] = 50, [23] = 50, [25] = 75, [29] = 125, [39] = 125,
            [46] = 125, [58] = 125, [68] = 150
        };

    private static readonly IReadOnlyDictionary<int, string> PremiumEmoteRewards =
        new Dictionary<int, string>
        {
            [31] = "emoji_fang_happy", [33] = "emoji_fang_sad",
            [35] = "emoji_fang_clap", [37] = "emoji_fang_angry",
            [41] = "emoji_fang_thanks", [45] = "emoji_fang_gg",
            [49] = "emoji_fang_phew", [51] = "emoji_fang_special",
            [53] = "emoji_fang_sayan", [55] = "emoji_fang_sayan_clap",
            [57] = "emoji_fang_sayan_angry", [59] = "emoji_fang_sayan_sad",
            [61] = "emoji_fang_sayan_happy", [63] = "emoji_fang_sayan_thanks",
            [65] = "emoji_fang_sayan_gg", [67] = "emoji_fang_sayan_phew",
            [69] = "emoji_fang_sayan_special"
        };

    private int _selectedDataId;
    private int _seasonId;
    private int _requiredMilestone;

    public override void Decode(ByteStream byteStream)
    {
        // LogicGatchaCommand consumes the common command header and the first
        // command-specific field. For command 517 that field is the reward
        // track (9=premium, 10=free), not a box index.
        base.Decode(byteStream);

        _selectedDataId = HasField(byteStream)
            ? ReadDataReference(byteStream)
            : 0;
        _seasonId = HasField(byteStream) ? byteStream.ReadVInt() : 0;
        _requiredMilestone = HasField(byteStream) ? byteStream.ReadVInt() : 0;
    }

    public override int Execute(LogicHomeMode logicHomeMode)
    {
        var accountId = logicHomeMode.AccountModel.GetAccountId();
        AccountTraceLogger.Log(accountId,
            "BRAWL-PASS-CLAIM-REQUEST",
            $"track={BoxIndex} seasonId={_seasonId} milestone={_requiredMilestone} " +
            $"selectedDataId={_selectedDataId}");

        // A box can contain a power-point drop. After the player confirms the
        // brawler selection, V41 sends another 517 command with a non-zero
        // data reference. This is not a second Brawl Pass tier claim.
        if (_selectedDataId != 0 && BoxIndex is 9 or 10)
        {
            if (TryGetDirectPowerReward(out var selectedPower))
            {
                var selectedSeasonId = _seasonId > 0
                    ? _seasonId
                    : MySqlPersistence.GetBrawlPassSeasonState().SeasonId;
                var selectedBrawler = _selectedDataId;
                MySqlPersistence.RecordBrawlPassBrawlerPower(
                    accountId, selectedSeasonId, _requiredMilestone,
                    selectedBrawler, selectedPower.Amount);
                AccountTraceLogger.Log(accountId, "BRAWL-PASS-POWER-SELECTED",
                    $"seasonId={selectedSeasonId} milestone={_requiredMilestone} " +
                    $"brawlerGlobalId={selectedBrawler} amount={selectedPower.Amount} " +
                    "persisted=True duplicateGrant=False");
            }

            var entries = logicHomeMode.AccountModel
                .GetFieldValueByAccountStructureParameterFromAccountModel(
                    AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
            var currentPowerPoints = entries.TryGetValue(12, out var powerEntry)
                ? powerEntry.Value
                : 0;
            entries[12] = new IntValueEntry(12, checked(currentPowerPoints));
            logicHomeMode.AccountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.IntValueEntryX, entries);
            AccountTraceLogger.Log(accountId, "BOX-REWARD-SELECTION",
                $"selectedDataId={_selectedDataId} track={BoxIndex} " +
                $"milestone={_requiredMilestone} powerPointsPersisted=True " +
                "duplicateGrant=False");
            logicHomeMode.GetGameListener()?.Send(
                new OwnHomeDataMessage(logicHomeMode.AccountModel, false));
            return 0;
        }

        // Command 517 also carries non-box Brawl Pass cards. Do not turn a
        // gem or other typed card into a box claim just because its track is
        // 9 or 10.
        if (!TryGetBrawlPassBox(out var deliveryType))
        {
            if (TryGetBrawlPassGem(out var gemAmount))
                return ClaimBrawlPassGems(logicHomeMode, gemAmount);

            if (TryGetDirectReward(out var directReward))
                return ClaimDirectReward(logicHomeMode, directReward);

            if (BoxIndex is 9 or 10)
            {
                AccountTraceLogger.Log(accountId, "BRAWL-PASS-CLAIM-REJECT",
                    $"track={BoxIndex} milestone={_requiredMilestone} " +
                    "reason=non_box_reward_not_wired");
                return 0;
            }

            return base.Execute(logicHomeMode);
        }

        // A pending entry is a transport retry. Let the base command resend
        // it without granting resources again. Once acknowledged, the
        // durable MySQL claim below rejects every later click.
        if (!BoxDeliveryLedger.TryGetPending(accountId, deliveryType))
        {
            var seasonId = _seasonId > 0
                ? _seasonId
                : MySqlPersistence.GetBrawlPassSeasonState().SeasonId;
            var track = BoxIndex == 9 ? 1 : 0;
            if (!MySqlPersistence.TryClaimBrawlPassLevel(
                    accountId, seasonId, track, _requiredMilestone))
            {
                var existingClaimKey = $"brawlpass:{seasonId}:{track}:{_requiredMilestone}";
                if (MySqlPersistence.HasBrawlPassClaim(
                        accountId, seasonId, track, _requiredMilestone))
                {
                    var persisted = MySqlPersistence.RecordBoxClaim(
                        accountId, existingClaimKey, deliveryType, "server_error_100_suppressed",
                        _requiredMilestone, status: "claimed");
                    AccountTraceLogger.Log(accountId, "BRAWL-PASS-ALREADY-CLAIMED",
                        $"claimKey={existingClaimKey} track={track} seasonId={seasonId} " +
                        $"milestone={_requiredMilestone} deliveryType={deliveryType} " +
                        $"status=claimed persisted={persisted} reason=already_claimed " +
                        "serverError=100");
                    // The durable claim prevents a second grant. Do not send
                    // a full 24101 home frame during the V41.150 box-close
                    // transition: both inline and delayed refreshes have been
                    // observed to trigger a native client crash. V41's
                    // idempotent duplicate-claim result is ServerError 100;
                    // it terminates the retry without minting a second box.
                    logicHomeMode.GetGameListener()?.Send(new ServerErrorMessage(100));
                    return 0;
                }

                AccountTraceLogger.Log(accountId, "BRAWL-PASS-CLAIM-REJECT",
                    $"track={track} seasonId={seasonId} milestone={_requiredMilestone} " +
                    "reason=already_claimed_or_persistence_unavailable");
                // Do not send OwnHomeData here: the V41.150 box-close native
                // path dereferences a null reward object when a full home
                // frame arrives during this transition. Return a generic
                // protocol error instead; no second delivery is created.
                logicHomeMode.GetGameListener()?.Send(new ServerErrorMessage(100));
                return 0;
            }

            AccountTraceLogger.Log(accountId, "BRAWL-PASS-CLAIM-RECORDED",
                $"track={track} seasonId={seasonId} milestone={_requiredMilestone} " +
                $"deliveryType={deliveryType}");

            var claimKey = $"brawlpass:{seasonId}:{track}:{_requiredMilestone}";
            var boxPersisted = MySqlPersistence.RecordBoxClaim(
                accountId, claimKey, deliveryType, "brawl_pass_claim", _requiredMilestone);
            AccountTraceLogger.Log(accountId, "BOX-CLAIM-MYSQL",
                $"claimKey={claimKey} track={track} seasonId={seasonId} " +
                $"milestone={_requiredMilestone} deliveryType={deliveryType} " +
                $"status=claimed persisted={boxPersisted}");

            if (deliveryType == 12)
            {
                // Big Box = three sequential one-drop deliveries. The
                // ledger is populated before the first frame so a reconnect
                // can resume at the correct draw without minting again.
                BoxDeliveryLedger.IssueMany(accountId, deliveryType, 3);
                var first = new AvailableServerCommandMessage();
                first.SetServerCommand(new LogicGiveDeliveryItemsCommand(
                    logicHomeMode.AccountModel, deliveryType,
                    brawlPassReward: true, issueLedger: false, drawIndex: 0));
                logicHomeMode.GetGameListener()?.Send(first);
                AccountTraceLogger.Log(accountId, "BOX-CLAIM-SEQUENCE",
                    "deliveryType=12 draws=3 drawIndex=0 sequential=true");
                return 0;
            }
        }

        return base.Execute(logicHomeMode);
    }

    protected override int NormalizeDeliveryType(int boxIndex)
    {
        if (TryGetBrawlPassBox(out var deliveryType)) return deliveryType;
        return base.NormalizeDeliveryType(boxIndex);
    }

    public override int GetCommandType() => 517;

    protected override int GetDeliveryDrawIndex(long accountId, int deliveryType)
    {
        if (deliveryType != 12) return 0;
        var pending = BoxDeliveryLedger.GetPendingCount(accountId, deliveryType);
        return Math.Clamp(3 - pending, 0, 2);
    }

    private bool TryGetBrawlPassBox(out int deliveryType)
    {
        deliveryType = 0;
        var boxes = BoxIndex == 9
            ? PremiumBoxDeliveryTypes
            : BoxIndex == 10
                ? FreeBoxDeliveryTypes
                : null;
        return boxes != null && boxes.TryGetValue(_requiredMilestone, out deliveryType);
    }

    private bool TryGetBrawlPassGem(out int gemAmount)
    {
        gemAmount = 0;
        // The season-10 premium track has no gem cards in V41.150.
        return BoxIndex == 10 && FreeGemRewards.TryGetValue(_requiredMilestone, out gemAmount);
    }

    private bool TryGetDirectPowerReward(out DirectReward reward)
    {
        reward = default;
        var amount = BoxIndex == 10
            ? FreePowerRewards.GetValueOrDefault(_requiredMilestone)
            : BoxIndex == 9
                ? PremiumPowerRewards.GetValueOrDefault(_requiredMilestone)
                : 0;
        if (amount <= 0) return false;

        reward = new DirectReward(
            amount,
            ValueType: 12,
            DataReference: GlobalId.CreateGlobalId(CsvHelperTable.Characters.GetId(), 0),
            IsPower: true);
        return true;
    }

    private bool TryGetDirectReward(out DirectReward reward)
    {
        reward = default;

        if (BoxIndex == 10 && FreeGoldRewards.TryGetValue(_requiredMilestone, out var freeGold))
        {
            reward = new DirectReward(freeGold, ValueType: 7);
            return true;
        }

        if (BoxIndex == 9 && PremiumGoldRewards.TryGetValue(_requiredMilestone, out var premiumGold))
        {
            reward = new DirectReward(premiumGold, ValueType: 7);
            return true;
        }

        if (TryGetDirectPowerReward(out reward)) return true;

        if (BoxIndex == 9 && _requiredMilestone == 0)
        {
            var rosa = ResolveDataReference(CsvHelperTable.Characters, "Rosa");
            var rosaSkin = ResolveDataReference(CsvHelperTable.Skins, "RosaBP");
            if (rosa > 0 && rosaSkin > 0)
            {
                reward = new DirectReward(1, 24, rosa, rosaSkin);
                return true;
            }
        }

        if (BoxIndex == 9 && _requiredMilestone == 30)
        {
            var fang = ResolveDataReference(CsvHelperTable.Characters, "KickerDude");
            if (fang > 0)
            {
                reward = new DirectReward(1, 3, fang);
                return true;
            }
        }

        if (BoxIndex == 9 && _requiredMilestone == 70)
        {
            var fang = ResolveDataReference(CsvHelperTable.Characters, "KickerDude");
            var fangSkin = ResolveDataReference(CsvHelperTable.Skins, "KickerDudeFurious");
            if (fang > 0 && fangSkin > 0)
            {
                reward = new DirectReward(1, 4, fang, fangSkin);
                return true;
            }
        }

        if (BoxIndex == 9 && PremiumEmoteRewards.TryGetValue(
                _requiredMilestone, out var emoteName))
        {
            var emote = ResolveDataReference(CsvHelperTable.Emotes, emoteName);
            if (emote > 0)
            {
                reward = new DirectReward(1, 19, emote);
                return true;
            }
        }

        return false;
    }

    private int ClaimDirectReward(LogicHomeMode logicHomeMode, DirectReward reward)
    {
        var account = logicHomeMode.AccountModel;
        var accountId = account.GetAccountId();
        var seasonId = _seasonId > 0
            ? _seasonId
            : MySqlPersistence.GetBrawlPassSeasonState().SeasonId;
        var track = BoxIndex == 9 ? 1 : 0;

        if (!MySqlPersistence.TryClaimBrawlPassLevel(
                accountId, seasonId, track, _requiredMilestone))
        {
            AccountTraceLogger.Log(accountId, "BRAWL-PASS-DIRECT-REJECT",
                $"seasonId={seasonId} track={track} milestone={_requiredMilestone} " +
                $"valueType={reward.ValueType} reason=already_claimed serverError=100");
            logicHomeMode.GetGameListener()?.Send(new ServerErrorMessage(100));
            return 0;
        }

        var entries = account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        if (reward.ValueType == 7)
        {
            var before = entries.TryGetValue(1, out var goldEntry) ? goldEntry.Value : 0;
            entries[1] = new IntValueEntry(1, checked(before + reward.Amount));
        }
        else if (reward.IsPower)
        {
            var before = entries.TryGetValue(12, out var powerEntry) ? powerEntry.Value : 0;
            entries[12] = new IntValueEntry(12, checked(before + reward.Amount));
        }

        account.SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX, entries);
        MySqlPersistence.EnqueueResources(account);

        if (reward.IsPower)
        {
            var homeBrawler = account
                .GetFieldValueByAccountStructureParameterFromAccountModel(
                    AccountStructure.HomeBrawlerGlobalId) is int homeBrawlerGlobalId
                ? homeBrawlerGlobalId
                : 0;
            var persistedPower = homeBrawler > 0 &&
                MySqlPersistence.RecordBrawlPassBrawlerPower(
                    accountId, seasonId, _requiredMilestone, homeBrawler, reward.Amount);
            AccountTraceLogger.Log(accountId, "BRAWL-PASS-POWER-GRANT",
                $"seasonId={seasonId} track={track} milestone={_requiredMilestone} " +
                $"amount={reward.Amount} brawlerGlobalId={homeBrawler} " +
                $"persisted={persistedPower} assignment=home_brawler");
        }

        AccountTraceLogger.Log(accountId, "BRAWL-PASS-DIRECT-GRANT",
            $"seasonId={seasonId} track={track} milestone={_requiredMilestone} " +
            $"amount={reward.Amount} valueType={reward.ValueType} " +
            $"dataReference={reward.DataReference} skinReference={reward.SkinReference} " +
            $"power={reward.IsPower} persistedClaim=True " +
            $"animationCommand={(reward.ValueType == 7 ? 0 : reward.IsClientAnimationSafe ? 203 : 0)}");

        // The V41.150 client does not reliably acknowledge the guessed 203
        // scalar-gold envelope on every Brawl Pass level, and an OwnHomeData
        // frame in the active claim turn can crash its native reward view.
        // Keep the claim durable, send no guessed animation, and refresh via
        // the already-tested orderly reconnect path.
        if (reward.ValueType == 7)
        {
            logicHomeMode.RequestBoxHomeRefresh(500);
            AccountTraceLogger.Log(accountId, "BRAWL-PASS-GOLD-REFRESH",
                $"seasonId={seasonId} track={track} milestone={_requiredMilestone} " +
                "refresh=fresh_login animationCommand=0 tick=expected safe=True");
            return 0;
        }

        // V41.150's 203 envelope is verified for scalar currency rewards
        // only. A guessed skin/power payload crashes libg.so in the native
        // reward view, so commit the claim and refresh through a fresh login.
        if (!reward.IsClientAnimationSafe)
        {
            AccountTraceLogger.Log(accountId, "BRAWL-PASS-REWARD-COMMITTED",
                $"seasonId={seasonId} track={track} milestone={_requiredMilestone} " +
                $"valueType={reward.ValueType} clientAnimation=disabled " +
                "reason=v41_native_reward_payload_unverified refresh=fresh_login");
            logicHomeMode.RequestBoxHomeRefresh(750);
            return 0;
        }

        var animation = new AvailableServerCommandMessage();
        animation.SetServerCommand(new LogicGiveDirectBrawlPassRewardCommand(
            reward.Amount,
            reward.ValueType,
            reward.DataReference,
            reward.SkinReference,
            reward.PinReference,
            reward.CardReference,
            _requiredMilestone));
        logicHomeMode.GetGameListener()?.Send(animation);
        // The reward animation is delivered through 24111, while the claim
        // bit list lives in OwnHomeData. Refresh only after the client has
        // finished the 203 animation; sending OwnHomeData in the same turn
        // races the native reward view and can crash V41.150. This gives the
        // pass card its tick on the next safe home snapshot instead of
        // waiting for a manual app restart.
        logicHomeMode.MarkDirectRewardAwaitingAck();
        AccountTraceLogger.Log(accountId, "BRAWL-PASS-DIRECT-REFRESH",
            $"seasonId={seasonId} track={track} milestone={_requiredMilestone} " +
            "refresh=pending_until_reward_view_close delayMs=500");
        return 0;
    }

    private static int ResolveDataReference(CsvHelperTable table, string name)
    {
        try
        {
            return LogicDataTables.GetDataFromTable(table.GetId())
                .GetDataByName(name)?.GetGlobalId() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private int ClaimBrawlPassGems(LogicHomeMode logicHomeMode, int gemAmount)
    {
        var account = logicHomeMode.AccountModel;
        var accountId = account.GetAccountId();
        var seasonId = _seasonId > 0
            ? _seasonId
            : MySqlPersistence.GetBrawlPassSeasonState().SeasonId;
        var claimKey = $"brawlpass:{seasonId}:0:{_requiredMilestone}";

        if (!MySqlPersistence.TryClaimBrawlPassLevel(accountId, seasonId, 0, _requiredMilestone))
        {
            var persisted = MySqlPersistence.RecordBoxClaim(
                accountId, claimKey, 0, "server_error_100_suppressed",
                _requiredMilestone, status: "claimed");
            AccountTraceLogger.Log(accountId, "BRAWL-PASS-GEM-REJECT",
                $"seasonId={seasonId} milestone={_requiredMilestone} amount={gemAmount} " +
                $"reason=already_claimed serverError=100 persisted={persisted}");
            logicHomeMode.GetGameListener()?.Send(new ServerErrorMessage(100));
            return 0;
        }

        var entries = account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        var before = entries.TryGetValue(16, out var gemEntry) ? gemEntry.Value : 0;
        entries[16] = new IntValueEntry(16, checked(before + gemAmount));
        account.SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX, entries);
        MySqlPersistence.EnqueueResources(account);

        AccountTraceLogger.Log(accountId, "BRAWL-PASS-GEM-GRANT",
            $"seasonId={seasonId} milestone={_requiredMilestone} amount={gemAmount} " +
            $"gemsBefore={before} gemsAfter={before + gemAmount} persistedClaim=True " +
            "animationCommand=202 directReward=true");

        var animation = new AvailableServerCommandMessage();
        // V41.150's native gem increment view is driven by command 202. The
        // 203 direct-reward envelope updates the balance but does not enter
        // the gem animation path for Brawl Pass claims.
        animation.SetServerCommand(new LogicDiamondsAddedCommand(gemAmount));
        logicHomeMode.GetGameListener()?.Send(animation);
        // Command 202 is acknowledged by ServerGemAcknowledgement. The claim
        // and resource update above are already durable; the acknowledgement
        // must never grant the reward a second time.
        return 0;
    }

    private static bool HasField(ByteStream byteStream)
    {
        // EndClientTurn frames are padded with 0xFF. Do not interpret that
        // padding as an optional VInt/data reference on short claim packets.
        return !byteStream.IsAtEnd() &&
               byteStream.GetByteArray()[byteStream.GetOffset()] != 0xFF;
    }

    private static int ReadDataReference(ByteStream byteStream)
    {
        var classId = byteStream.ReadVInt();
        if (classId <= 0 || !HasField(byteStream)) return 0;
        return GlobalId.CreateGlobalId(classId, byteStream.ReadVInt());
    }

    private readonly record struct DirectReward(
        int Amount,
        int ValueType,
        int DataReference = 0,
        int SkinReference = 0,
        int PinReference = 0,
        int CardReference = 0,
        bool IsPower = false)
    {
        public bool IsClientAnimationSafe => ValueType == 7;
    }
}
