using BSL.v41.Logic.Environment.LaserMessage.Sepo.Mode;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.Logic.Environment.LaserCommand;

/// <summary>
/// The V41 client echoes server command 202 in the next EndClientTurn after
/// playing the gem increment animation. It is an acknowledgement only: the
/// durable claim and resource grant already happened before command 202 was
/// sent, so this handler must never grant anything a second time.
/// </summary>
public sealed class ServerGemAcknowledgement : LogicCommand
{
    public override bool CanExecute(LogicHomeMode logicHomeMode)
    {
        return logicHomeMode.AccountModel != null!;
    }

    public override int Execute(LogicHomeMode logicHomeMode)
    {
        var accountId = logicHomeMode.AccountModel.GetAccountId();
        AccountTraceLogger.Log(accountId, "BRAWL-PASS-GEM-ACK",
            "command=202 acknowledged=True duplicateGrant=False " +
            "phase=client_gem_increment_close");

        return 0;
    }

    public override int GetCommandType() => 202;
}
