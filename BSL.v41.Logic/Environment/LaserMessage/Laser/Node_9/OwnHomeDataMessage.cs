using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Avatar;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Supercell.Titan.CommonUtils;
using BSL.v41.Titan.DataStream.Helps;
using BSL.v41.Titan.Diagnostics;
using BSL.v41.Titan.Utilities;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

public class OwnHomeDataMessage(AccountModel accountModel, bool includeOffers = false) : PiranhaMessage
{
    public OwnHomeDataMessage() : this(new AccountModel())
    {
        Helper.Skip();
    }

    public override void Encode()
    {
        base.Encode();

        var entries = accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        var gold = entries.TryGetValue(1, out var goldEntry) ? goldEntry.Value : 0;
        var gems = entries.TryGetValue(16, out var gemsEntry) ? gemsEntry.Value : 0;
        var passTokens = entries.TryGetValue(28, out var passEntry) ? passEntry.Value : 0;
        var powerPoints = Math.Max(
            entries.TryGetValue(12, out var powerEntry) ? powerEntry.Value : 0,
            entries.TryGetValue(29, out var legacyPowerEntry) ? legacyPowerEntry.Value : 0);
        AccountTraceLogger.Log(accountModel.GetAccountId(), "ACCOUNT-SNAPSHOT",
            $"event=ownHomeDataEncode includeOffers={includeOffers} accountId={accountModel.GetAccountId()} " +
            $"gold={gold} gems={gems} passTokens={passTokens} powerPoints={powerPoints} " +
            $"trophies={accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.Trophies)}");

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        new LogicClientHome(accountModel, includeOffers).Encode(ByteStream);
        new LogicClientAvatar(accountModel).Encode(ByteStream);
        // V41 uses this trailing value as the server clock for client-side
        // timers. Sending zero makes countdowns, including seasonal UI timers,
        // appear as "0 seconds" even when the server has a valid end time.
        ByteStream.WriteVInt(LogicTimeUtil.GetTimestamp());
    }

    public override int GetMessageType()
    {
        return 24101;
    }

    public override int GetServiceNodeType()
    {
        return 9;
    }
}
