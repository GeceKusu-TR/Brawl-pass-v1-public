using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Mode;
using BSL.v41.Titan.DataStream;

namespace BSL.v41.Logic.Environment.LaserCommand.Laser;

/// <summary>
/// V41's Brawl Pass claim action arrives inside EndClientTurn as command 535
/// rather than as top-level message 14277. The claim is idempotent because
/// LogicGiveBrawlPassRewardsCommand records each level in MySQL first.
/// </summary>
public sealed class LogicClaimBrawlPassRewardCommand : LogicCommand
{
    public override void Decode(ByteStream byteStream)
    {
        // The command-specific fields are not needed for this server's single
        // active season. Consume the common command header only.
        base.Decode(byteStream);
    }

    public override bool CanExecute(LogicHomeMode logicHomeMode)
    {
        return logicHomeMode.AccountModel != null!;
    }

    public override int Execute(LogicHomeMode logicHomeMode)
    {
        var account = logicHomeMode.AccountModel;
        var rewardCommand = new LogicGiveBrawlPassRewardsCommand(account);
        if (rewardCommand.HasRewards)
        {
            var availableRewards = new AvailableServerCommandMessage();
            availableRewards.SetServerCommand(rewardCommand);
            logicHomeMode.GetGameListener()?.Send(availableRewards);

            // Direct Brawl Pass gem rewards also use the dedicated V41
            // diamonds-added command. This updates the top-bar balance and
            // lets the client play its normal gem increment animation; box
            // deliveries continue to use their tested ValueType=8 drop.
            if (rewardCommand.GemsGranted > 0)
            {
                var gemsAdded = new AvailableServerCommandMessage();
                gemsAdded.SetServerCommand(new LogicDiamondsAddedCommand(
                    rewardCommand.GemsGranted));
                logicHomeMode.GetGameListener()?.Send(gemsAdded);
            }
        }

        // Do not send 24101 in the same turn as the reward delivery. The
        // V41.150 client is still consuming the delivery screen and crashes
        // on the concurrent home refresh; the account state is already
        // persisted and will be refreshed by the next home request.
        return 0;
    }

    public override int GetCommandType() => 535;
}
