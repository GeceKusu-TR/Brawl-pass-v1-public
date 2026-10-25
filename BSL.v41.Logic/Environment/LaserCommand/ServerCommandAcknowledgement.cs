using BSL.v41.Logic.Database;
using BSL.v41.Logic.Environment.LaserCommand.Laser;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Mode;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.Logic.Environment.LaserCommand;

/// <summary>
/// Command 203 is server-to-client delivery data. If a client echoes its
/// command component in EndClientTurn, consume no client payload and ignore
/// it rather than decoding the delivery body as client commands.
/// </summary>
public sealed class ServerCommandAcknowledgement : LogicCommand
{
    public override bool CanExecute(LogicHomeMode logicHomeMode) => logicHomeMode.AccountModel != null!;

    public override int Execute(LogicHomeMode logicHomeMode)
    {
        var accountId = logicHomeMode.AccountModel.GetAccountId();
        var acknowledged = BoxDeliveryLedger.TryAcknowledgeAny(accountId, out var deliveryType);
        AccountTraceLogger.Log(accountId, "BOX-DELIVERY-ACK",
            $"command=203 deliveryType={deliveryType} acknowledged={acknowledged} phase=client_box_close");

        if (!acknowledged && logicHomeMode.AcknowledgeDirectReward())
        {
            AccountTraceLogger.Log(accountId, "BRAWL-PASS-DIRECT-ACK",
                "command=203 acknowledged=True duplicateGrant=False refresh=deferred_until_empty_turn");
        }

        if (acknowledged && deliveryType == 12)
        {
            var pendingDraws = BoxDeliveryLedger.GetPendingCount(accountId, deliveryType);
            if (pendingDraws > 0)
            {
                var drawIndex = Math.Clamp(3 - pendingDraws, 0, 2);
                var next = new AvailableServerCommandMessage();
                next.SetServerCommand(new LogicGiveDeliveryItemsCommand(
                    logicHomeMode.AccountModel, deliveryType, brawlPassReward: true,
                    issueLedger: false, drawIndex: drawIndex));
                logicHomeMode.GetGameListener()?.Send(next);
                BoxDeliveryLedger.MarkActive(accountId, deliveryType);
                AccountTraceLogger.Log(accountId, "BOX-DRAW-NEXT",
                    $"deliveryType={deliveryType} drawIndex={drawIndex} " +
                    $"pendingAfterAck={pendingDraws} sequential=true");
            }

            if (pendingDraws == 0)
            {
                logicHomeMode.RequestBoxHomeRefresh();
                AccountTraceLogger.Log(accountId, "BOX-CLAIM-REFRESH",
                    "deliveryType=12 pendingAfterAck=0 refresh=pendingUntilHomeLogicClose " +
                    "reason=wait_for_safe_client_state_transition");
            }
        }
        else if (acknowledged && deliveryType is 10 or 11)
        {
            logicHomeMode.RequestBoxHomeRefresh();
            AccountTraceLogger.Log(accountId, "BOX-CLAIM-REFRESH",
                $"deliveryType={deliveryType} refresh=pendingUntilHomeLogicClose " +
                "reason=wait_for_safe_client_state_transition");
        }

        // Do not send OwnHomeData directly from the acknowledgement. The V41
        // client may still be inside the native box-reward view; a full 24101
        // frame there was proven to trigger a native crash. LogicHomeMode
        // refreshes through a controlled reconnect after a later idle turn.

        return 0;
    }

    public override int GetCommandType() => 203;
}
