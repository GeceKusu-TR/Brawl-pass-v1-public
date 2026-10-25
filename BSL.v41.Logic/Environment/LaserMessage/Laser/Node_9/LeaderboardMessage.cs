using BSL.v41.Logic.Database;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Database.Alliance;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Alliance;
using BSL.v41.Supercell.Titan.CommonUtils;
using BSL.v41.Titan.DataStream.Helps;
using BSL.v41.Titan.Diagnostics;
using BSL.v41.Titan.Mathematical;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>
/// V41 AvatarRankingList response (24403).
/// The wire layout follows the V41 player/club ranking entry layout.
/// </summary>
public sealed class LeaderboardMessage : PiranhaMessage
{
    private readonly int _leaderboardType;
    private readonly int _brawlerGlobalId;
    private readonly bool _isRegional;
    private readonly IReadOnlyList<AccountModel> _accounts;
    private readonly IReadOnlyList<AllianceModel> _alliances;
    private readonly long _requesterAccountId;

    public LeaderboardMessage(
        int leaderboardType,
        int brawlerGlobalId,
        bool isRegional,
        IReadOnlyList<AccountModel> accounts,
        IReadOnlyList<AllianceModel> alliances,
        int myIndex = 0,
        long requesterAccountId = 0)
    {
        _leaderboardType = leaderboardType;
        _brawlerGlobalId = brawlerGlobalId;
        _isRegional = isRegional;
        _accounts = accounts;
        _alliances = alliances;
        _myIndex = myIndex;
        _requesterAccountId = requesterAccountId;
    }

    private readonly int _myIndex;

    public LeaderboardMessage() : this(1, 0, false, [], [], 0, 0)
    {
        Helper.Skip();
    }

    public override void Encode()
    {
        Trace("LEADERBOARD-ENCODE-BEGIN",
            $"type={_leaderboardType} brawlerGlobalId={_brawlerGlobalId} isRegional={_isRegional} " +
            $"accountCount={_accounts.Count} allianceCount={_alliances.Count} myIndex={_myIndex}");

        try
        {
            base.Encode();
            Trace("LEADERBOARD-ENCODE-BASE", $"offset={ByteStream.GetOffset()}");

            // Header: type, season/variant, brawler reference, region.
            ByteStream.WriteVInt(_leaderboardType);
            ByteStream.WriteVInt(0);
            ByteStreamHelper.WriteDataReference(ByteStream, _brawlerGlobalId);
            ByteStream.WriteString(_isRegional ? "TR" : null);
            Trace("LEADERBOARD-ENCODE-HEADER",
                $"offset={ByteStream.GetOffset()} type={_leaderboardType} " +
                $"brawlerGlobalId={_brawlerGlobalId} region={(_isRegional ? "TR" : "null")}");

            switch (_leaderboardType)
            {
                case 2:
                    EncodeAllianceEntries();
                    break;
                default:
                    // Type 1 is total player trophies. Type 0/other values are
                    // brawler rankings; the current account model has no
                    // per-brawler trophy table, so keep the same wire shape and
                    // use the persisted total as a safe fallback.
                    EncodePlayerEntries();
                    break;
            }

            Trace("LEADERBOARD-ENCODE-LIST-END", $"offset={ByteStream.GetOffset()}");

            // Pagination/next-page fields used by the V41 client.
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(_resolvedMyIndex);
            ByteStream.WriteVInt(0);
            ByteStream.WriteVInt(0);
            ByteStream.WriteString("TR");
            Trace("LEADERBOARD-ENCODE-TRAILER",
                $"offset={ByteStream.GetOffset()} first=0 myIndex={_resolvedMyIndex} third=0 fourth=0 region=TR");
            Trace("LEADERBOARD-ENCODE-END", $"payloadBytes={ByteStream.GetLength()}");
        }
        catch (Exception exception)
        {
            Trace("LEADERBOARD-ENCODE-ERROR",
                $"offset={ByteStream.GetOffset()} payloadBytes={ByteStream.GetLength()} " +
                $"exception={exception.GetType().Name} message={Sanitize(exception.Message)}");
            throw;
        }
    }

    private void EncodePlayerEntries()
    {
        var ranked = _accounts
            .Select(account => new
            {
                Account = account,
                Trophies = ReadInt(account, AccountStructure.Trophies),
                MaxTrophies = ReadInt(account, AccountStructure.MaxTrophies),
                AccountId = account.GetAccountId()
            })
            .OrderByDescending(item => item.Trophies)
            .ThenByDescending(item => item.MaxTrophies)
            // Keep deterministic ordering for equal trophies. The client list
            // displays the higher account id first for this tie case.
            .ThenByDescending(item => item.AccountId)
            .Take(200)
            .ToList();

        // The client uses the trailer index to highlight the requesting player.
        // A zero index is reserved for "not present"; sending it for an account
        // that is in the returned page makes the client treat its own row as
        // missing. Resolve it from the exact same ordering used for the rows so
        // the value cannot drift from the encoded list.
        var resolvedMyIndex = _myIndex > 0
            ? _myIndex
            : _requesterAccountId > 0
                ? ranked.FindIndex(item => item.AccountId == _requesterAccountId) + 1
                : 0;
        if (resolvedMyIndex > ranked.Count)
            resolvedMyIndex = 0;
        _resolvedMyIndex = resolvedMyIndex;

        ByteStream.WriteVInt(ranked.Count);
        Trace("LEADERBOARD-PLAYER-LIST",
            $"rankedCount={ranked.Count} myIndex={resolvedMyIndex} offset={ByteStream.GetOffset()}");
        for (var index = 0; index < ranked.Count; index++)
        {
            var item = ranked[index];
            var allianceName = ReadAccountAllianceName(item.Account);
            var avatarName = ReadString(item.Account, AccountStructure.AvatarName, "Brawler");
            var thumbnail = ReadInt(item.Account, AccountStructure.PlayerThumbnailGlobalId);
            var nameColor = ReadInt(item.Account, AccountStructure.NameColorGlobalId);
            var displayIndex = ranked.Count - index;
            Trace("LEADERBOARD-PLAYER-ENTRY-BEGIN",
                $"index={displayIndex} accountId={item.AccountId} trophies={item.Trophies} " +
                $"alliancePresent={!string.IsNullOrWhiteSpace(allianceName)} nameChars={avatarName.Length} " +
                $"allianceStringBytes={allianceName.Length} " +
                $"thumbnail={thumbnail} nameColor={nameColor} offset={ByteStream.GetOffset()}");
            // V41 AvatarRankingList uses VInt64/LogicLong (two VInts), not
            // the fixed-width 8-byte WriteLong representation.
            ByteStreamHelper.EncodeLogicLong(ByteStream, (LogicLong)item.Account.GetAccountId());
            ByteStream.WriteVInt(1);
            ByteStream.WriteVInt(item.Trophies);

            // Player entry: the club section is optional.  Sending
            // hasAlliance=true with an empty string is not equivalent to
            // "no club" for the V41 client: the native decoder constructs a
            // club object for that branch and later dereferences it.  That
            // was the source of the reproducible libg.so null dereference on
            // accounts without a club.  Omit the optional section entirely
            // when there is no resolved club.
            var hasAlliance = !string.IsNullOrWhiteSpace(allianceName);
            ByteStream.WriteBoolean(hasAlliance);
            if (hasAlliance)
                ByteStream.WriteString(allianceName);
            ByteStream.WriteString(avatarName);
            ByteStream.WriteVInt(100);
            ByteStream.WriteVInt(thumbnail);
            ByteStream.WriteVInt(nameColor);
            ByteStream.WriteBoolean(false);
            Trace("LEADERBOARD-PLAYER-ENTRY-END",
                $"index={displayIndex} accountId={item.AccountId} hasAlliance={hasAlliance} " +
                $"offset={ByteStream.GetOffset()}");
        }
    }

    private int _resolvedMyIndex;

    private void EncodeAllianceEntries()
    {
        var ranked = _alliances
            .Select(alliance =>
            {
                var members = alliance.GetFieldValueByAllianceStructureParameterFromAllianceModel(
                    AllianceStructure.AllianceMemberEntryList) as Dictionary<long, AllianceMemberEntry>
                               ?? new Dictionary<long, AllianceMemberEntry>();
                var score = members.Values
                    .Where(member => member != null)
                    .Sum(member => ReadMemberTrophies(member!));
                return new { Alliance = alliance, Members = members, Score = score };
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Alliance.GetAllianceId())
            .Take(200)
            .ToList();

        ByteStream.WriteVInt(ranked.Count);
        Trace("LEADERBOARD-ALLIANCE-LIST", $"rankedCount={ranked.Count} offset={ByteStream.GetOffset()}");
        for (var index = 0; index < ranked.Count; index++)
        {
            var item = ranked[index];
            var allianceName = ReadAllianceString(item.Alliance, AllianceStructure.AllianceName, "Club");
            var badge = ReadInt(item.Alliance, AllianceStructure.AllianceBadgeData);
            Trace("LEADERBOARD-ALLIANCE-ENTRY-BEGIN",
                $"index={index} allianceId={item.Alliance.GetAllianceId()} score={item.Score} " +
                $"memberCount={item.Members.Count} nameChars={allianceName.Length} badge={badge} " +
                $"offset={ByteStream.GetOffset()}");
            ByteStreamHelper.EncodeLogicLong(ByteStream, (LogicLong)item.Alliance.GetAllianceId());
            ByteStream.WriteVInt(1);
            ByteStream.WriteVInt(item.Score);
            ByteStream.WriteBoolean(false);
            ByteStream.WriteBoolean(true);
            ByteStream.WriteString(allianceName);
            ByteStream.WriteVInt(item.Members.Count);
            ByteStreamHelper.WriteDataReference(ByteStream, 8, badge);
            Trace("LEADERBOARD-ALLIANCE-ENTRY-END",
                $"index={index} allianceId={item.Alliance.GetAllianceId()} offset={ByteStream.GetOffset()}");
        }
    }

    private static int ReadMemberTrophies(AllianceMemberEntry member)
    {
        var account = Databases.AccountDatabase.LoadAccount(member.AccountId);
        return account == null ? 0 : ReadInt(account, AccountStructure.Trophies);
    }

    private static int ReadInt(AccountModel account, AccountStructure field)
    {
        var value = account.GetFieldValueByAccountStructureParameterFromAccountModel(field);
        return value == null ? 0 : Convert.ToInt32(value);
    }

    private static int ReadInt(AllianceModel alliance, AllianceStructure field)
    {
        var value = alliance.GetFieldValueByAllianceStructureParameterFromAllianceModel(field);
        return value == null ? 0 : Convert.ToInt32(value);
    }

    private static string ReadString(AccountModel account, AccountStructure field, string fallback)
    {
        var value = account.GetFieldValueByAccountStructureParameterFromAccountModel(field)?.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string ReadAccountAllianceName(AccountModel account)
    {
        var allianceId = Convert.ToInt64(account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.AllianceId) ?? 0);
        // AllianceName is a required string in the V41 ranking entry. An
        // account without a club uses an empty string, not a null string
        // sentinel; the latter shifts the client decoder on some builds.
        if (allianceId <= 0) return string.Empty;

        var alliance = Databases.AllianceDatabase.LoadAlliance(allianceId);
        return alliance?.GetFieldValueByAllianceStructureParameterFromAllianceModel(
            AllianceStructure.AllianceName)?.ToString() ?? string.Empty;
    }

    private static string ReadAllianceString(AllianceModel alliance, AllianceStructure field, string fallback)
    {
        var value = alliance.GetFieldValueByAllianceStructureParameterFromAllianceModel(field)?.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private void Trace(string phase, string details)
    {
        if (_requesterAccountId > 0)
            AccountTraceLogger.Log(_requesterAccountId, phase, details);
    }

    private static string Sanitize(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }

    public override int GetMessageType() => 24403;
    public override int GetServiceNodeType() => 13;
}
