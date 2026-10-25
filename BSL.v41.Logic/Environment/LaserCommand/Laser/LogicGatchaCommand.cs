using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Mode;
using BSL.v41.Logic.Database;
using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.Logic.Environment.LaserCommand.Laser;

public class LogicGatchaCommand : LogicCommand
{
    private int _boxIndex;

    protected int BoxIndex => _boxIndex;

    public override void Decode(ByteStream byteStream)
    {
        base.Decode(byteStream);
        // Some V41 clients retry the claim with only the common command
        // header. Keep that retry harmless instead of reading past the PDU.
        _boxIndex = byteStream.IsAtEnd() ? 0 : byteStream.ReadVInt();
    }

    public override bool CanExecute(LogicHomeMode logicHomeMode)
    {
        return logicHomeMode.AccountModel != null!;
    }

    public override int Execute(LogicHomeMode logicHomeMode)
    {
        var deliveryType = NormalizeDeliveryType(_boxIndex);
        var accountId = logicHomeMode.AccountModel.GetAccountId();

        if (IssueDeliveryWhenMissing && BoxDeliveryLedger.TryGetPending(accountId, deliveryType))
        {
            // A prior 24111 may have reached the socket but left the client
            // on its loading screen before it could acknowledge command 203.
            // Re-send the same delivery type without crediting resources a
            // second time; the subsequent 203 ack consumes the pending entry.
            var retry = new AvailableServerCommandMessage();
            retry.SetServerCommand(new LogicGiveDeliveryItemsCommand(
                logicHomeMode.AccountModel, deliveryType, brawlPassReward: true,
                grantRewards: false, issueLedger: false,
                drawIndex: GetDeliveryDrawIndex(accountId, deliveryType)));
            logicHomeMode.GetGameListener()?.Send(retry);
            BoxDeliveryLedger.MarkActive(accountId, deliveryType);
            AccountTraceLogger.Log(accountId, "BOX-CLAIM-REDELIVERY",
                $"command={GetCommandType()} boxIndex={_boxIndex} sessionId={logicHomeMode.SessionId} " +
                $"deliveryType={deliveryType} result=resent_without_duplicate_grant");
            return 0;
        }

        if (!BoxDeliveryLedger.TryAcknowledge(accountId, deliveryType))
        {
            if (IssueDeliveryWhenMissing)
            {
                // Rank/Brawl Pass cards do not have a pre-issued shop
                // delivery. Create exactly one durable delivery at claim
                // time, then let the client acknowledge it with its retry.
                // This path is intentionally opt-in for command 517's
                // Brawl Pass subclass; ordinary shop claims still reject.
                var delivery = new AvailableServerCommandMessage();
                delivery.SetServerCommand(new LogicGiveDeliveryItemsCommand(
                    logicHomeMode.AccountModel, deliveryType, brawlPassReward: true));
                logicHomeMode.GetGameListener()?.Send(delivery);
                AccountTraceLogger.Log(accountId, "BOX-CLAIM-FALLBACK",
                    $"command={GetCommandType()} boxIndex={_boxIndex} sessionId={logicHomeMode.SessionId} " +
                    $"deliveryType={deliveryType} result=issued_on_brawl_pass_claim");
                return 0;
            }

            AccountTraceLogger.Log(accountId, "BOX-CLAIM-REJECT",
                $"command={GetCommandType()} boxIndex={_boxIndex} sessionId={logicHomeMode.SessionId} " +
                $"deliveryType={deliveryType} reason=no_pending_server_delivery");
            // Do not inject a full 24101 frame while the client is closing
            // the box/claim screen. V41.150 dereferences a null delivery
            // object in that transition (libg.so fault address 0x8). The
            // rejected claim changes no resources; the next normal home
            // refresh remains the source of truth.
            return 0;
        }

        AccountTraceLogger.Log(accountId, "BOX-CLAIM",
            $"command={GetCommandType()} boxIndex={_boxIndex} deliveryType={deliveryType} " +
            $"sessionId={logicHomeMode.SessionId} result=acknowledged_existing_delivery");
        // LogicGiveDeliveryItemsCommand already created and persisted the
        // reward before sending 24111. Command 517 is only its acknowledgement.
        // Sending 24101 here races the V41 box-close state and causes the
        // same native null dereference. The reward was already persisted and
        // was delivered through 24111, so defer the full home refresh to the
        // next ordinary home request.
        return 0;
    }

    protected virtual int NormalizeDeliveryType(int boxIndex)
    {
        return boxIndex switch
        {
            10 or 3 => 12, // Mega Box item id / shop mega id
            14 or 4 or 2 or 1 => 11, // Big/Medium Box item id / offer index
            6 or 5 or 0 => 10, // Brawl Box item id / legacy box id / free offer
            11 or 12 => boxIndex,
            _ => 10
        };
    }

    protected virtual bool IssueDeliveryWhenMissing => false;

    protected virtual int GetDeliveryDrawIndex(long accountId, int deliveryType) => 0;

    public override int GetCommandType()
    {
        return 500;
    }
}
