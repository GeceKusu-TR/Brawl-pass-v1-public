using BSL.v41.Logic.Database;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Mode;
using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.Logic.Environment.LaserCommand.Laser;

/// <summary>
/// V41 Brawl Pass activation command. This is an entitlement update, not a
/// box delivery. Keeping it separate prevents a pass purchase from opening
/// the 24111 delivery/box flow.
/// </summary>
public sealed class LogicPurchaseBrawlPassCommand : LogicCommand
{
    public override void Decode(ByteStream byteStream)
    {
        base.Decode(byteStream);

        // The V41.150 purchase packet ends immediately after the common
        // command header.  The old decoder tried to consume two VInts and a
        // LogicLong that are not present in this packet, causing a valid
        // 534 command to be rejected with IndexOutOfRangeException before
        // Execute() could persist the entitlement.  The purchase reference
        // is not needed to authorize this local server's fixed season, so
        // leave any future extension bytes unread rather than guessing their
        // shape.
    }

    public override bool CanExecute(LogicHomeMode logicHomeMode)
    {
        return logicHomeMode.AccountModel != null!;
    }

    public override int Execute(LogicHomeMode logicHomeMode)
    {
        var account = logicHomeMode.AccountModel;
        var season = MySqlPersistence.GetBrawlPassSeasonState();
        if (MySqlPersistence.HasBrawlPassPremium(account.GetAccountId(), season.SeasonId))
        {
            AccountTraceLogger.Log(account.GetAccountId(), "BRAWL-PASS-PURCHASE",
                $"command={GetCommandType()} seasonId={season.SeasonId} persisted=true " +
                "result=already_active delivery=none boxDelivery=none");
            return 0;
        }

        var persisted = MySqlPersistence.SetBrawlPassPremium(account.GetAccountId(), season.SeasonId);

        AccountTraceLogger.Log(account.GetAccountId(), "BRAWL-PASS-PURCHASE",
            $"command={GetCommandType()} seasonId={season.SeasonId} persisted={persisted} " +
            "delivery=none boxDelivery=none");

        // Do not send 24101 from inside the purchase turn. The client has not
        // finished closing the purchase dialog and crashes in libg.so when a
        // full home frame arrives here. The entitlement is durable; the next
        // ordinary home request exposes the premium flag.
        return 0;
    }

    public override int GetCommandType() => 534;
}
