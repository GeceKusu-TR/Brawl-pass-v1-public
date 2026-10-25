using BSL.v41.Logic.Database;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;

public class BrawlPassSeasonData(AccountModel accountModel)
{
    private const int MaxPassTokens = 34500;
    private const int MaxBitListLevels = 128;

    public void Encode(ByteStream byteStream)
    {
        var wireStartOffset = byteStream.GetOffset();
        var entries = accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        var season = MySqlPersistence.GetBrawlPassSeasonState();
        var passTokens = entries.TryGetValue(28, out var tokenEntry)
            ? Math.Clamp(tokenEntry.Value, 0, MaxPassTokens)
            : 0;
        var freeClaims = MySqlPersistence.GetBrawlPassClaimedLevels(accountModel.GetAccountId(), season.SeasonId, 0);
        var premiumClaims = MySqlPersistence.GetBrawlPassClaimedLevels(accountModel.GetAccountId(), season.SeasonId, 1);

        AccountTraceLogger.Log(accountModel.GetAccountId(), "BRAWL-PASS-ENCODE",
            $"seasonId={season.SeasonId} passTokens={passTokens} " +
            $"premium={MySqlPersistence.HasBrawlPassPremium(accountModel.GetAccountId(), season.SeasonId)} " +
            $"freeClaims={string.Join(',', freeClaims.OrderBy(level => level))} " +
            $"premiumClaims={string.Join(',', premiumClaims.OrderBy(level => level))}");

        // V41 uses a track marker byte followed by the claimed-level bit
        // words. Keep this wire shape: the V41.150 client rejects the
        // boolean-prefixed variant during login.
        byteStream.WriteVInt(season.SeasonId);
        byteStream.WriteVInt(passTokens);
        byteStream.WriteBoolean(MySqlPersistence.HasBrawlPassPremium(
            accountModel.GetAccountId(), season.SeasonId));
        byteStream.WriteVInt(0);

        // Device verification confirms the V41 order: marker 2 is the
        // premium track and marker 1 is the free track. The V41.150
        // LogicBitList reserves its first two bits, so a zero-based reward
        // level n is encoded at global bit n+2.
        WriteClaimedBitList(byteStream, 2, premiumClaims);
        WriteClaimedBitList(byteStream, 1, freeClaims);

        var wireEndOffset = byteStream.GetOffset();
        var wireBytes = byteStream.GetByteArray();
        var wireHex = wireEndOffset > wireStartOffset
            ? Convert.ToHexString(wireBytes.AsSpan(wireStartOffset, wireEndOffset - wireStartOffset))
            : string.Empty;
        AccountTraceLogger.Log(accountModel.GetAccountId(), "BRAWL-PASS-WIRE",
            $"seasonId={season.SeasonId} startOffset={wireStartOffset} endOffset={wireEndOffset} " +
            $"byteCount={wireEndOffset - wireStartOffset} freeClaims={string.Join(',', freeClaims.OrderBy(level => level))} " +
            $"premiumClaims={string.Join(',', premiumClaims.OrderBy(level => level))} hex={wireHex}");
    }

    private static void WriteClaimedBitList(ByteStream byteStream, byte marker, HashSet<int> claimedLevels)
    {
        byteStream.WriteByte(marker);
        for (var word = 0; word < MaxBitListLevels / 32; word++)
        {
            var bits = 0;
            for (var bit = 0; bit < 32; bit++)
            {
                var globalBit = word * 32 + bit;
                var claimedLevel = globalBit - 2;
                if (claimedLevel >= 0 && claimedLevels.Contains(claimedLevel))
                    bits |= 1 << bit;
            }
            byteStream.WriteInt(bits);
        }
    }
}
