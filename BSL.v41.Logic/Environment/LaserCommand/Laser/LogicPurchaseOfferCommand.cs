using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Mode;
using BSL.v41.Logic.Environment.LaserNotification.Laser;
using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.DataStream.Helps;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.Logic.Environment.LaserCommand.Laser;

public class LogicPurchaseOfferCommand : LogicCommand
{
    private int _offerIndex;
    private int _unknown;

    public override void Decode(ByteStream byteStream)
    {
        base.Decode(byteStream);
        _offerIndex = byteStream.ReadVInt();
        _unknown = ByteStreamHelper.ReadDataReference(byteStream);
    }

    public override bool CanExecute(LogicHomeMode logicHomeMode)
    {
        var offers = logicHomeMode.AccountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.LogicOfferBundleList) as List<LogicOfferBundle>;
        var canExecute = offers != null && _offerIndex >= 0 && _offerIndex < offers.Count &&
                         !offers[_offerIndex].GetPurchased();
        AccountTraceLogger.Log(logicHomeMode.AccountModel.GetAccountId(), "OFFER-PURCHASE-REQUEST",
            $"command={GetCommandType()} offerIndex={_offerIndex} dataReference={_unknown} " +
            $"offerCount={offers?.Count ?? 0} canExecute={canExecute}");
        return canExecute;
    }

    public override int Execute(LogicHomeMode logicHomeMode)
    {
        var offers = (List<LogicOfferBundle>)logicHomeMode.AccountModel
            .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.LogicOfferBundleList);

        if (_offerIndex < 0 || _offerIndex >= offers.Count)
        {
            AccountTraceLogger.Log(logicHomeMode.AccountModel.GetAccountId(), "OFFER-PURCHASE-REJECT",
                $"command={GetCommandType()} offerIndex={_offerIndex} offerCount={offers.Count} reason=index_out_of_range");
            logicHomeMode.GetGameListener()?.Send(new OwnHomeDataMessage(logicHomeMode.AccountModel, false));
            return 0;
        }

        offers[_offerIndex].SetPurchased(true);
        logicHomeMode.AccountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.LogicOfferBundleList, offers);

        // The shop offer is a box offer in the V41 client. Sending only a
        // notification leaves the client waiting for the box-opening screen.
        // Convert the starter-offer slot to the delivery type expected by
        // LogicGiveDeliveryItemsCommand and send the actual server command.
        var deliveryType = _offerIndex switch
        {
            3 => 12, // BigBox / Mega delivery
            2 => 11, // MediumBox / Big delivery
            1 or 0 => 10, // BrawlBox / FreeBox delivery
            _ => 0
        };

        if (deliveryType == 0)
        {
            AccountTraceLogger.Log(logicHomeMode.AccountModel.GetAccountId(), "OFFER-PURCHASE-REJECT",
                $"command={GetCommandType()} offerIndex={_offerIndex} reason=not_a_box_offer delivery=none");
            logicHomeMode.GetGameListener()?.Send(new OwnHomeDataMessage(logicHomeMode.AccountModel, false));
            return 0;
        }

        AccountTraceLogger.Log(logicHomeMode.AccountModel.GetAccountId(), "OFFER-PURCHASE-DELIVERY",
            $"command={GetCommandType()} offerIndex={_offerIndex} deliveryType={deliveryType} boxDelivery=true");

        var delivery = new AvailableServerCommandMessage();
        delivery.SetServerCommand(new LogicGiveDeliveryItemsCommand(logicHomeMode.AccountModel, deliveryType));
        logicHomeMode.GetGameListener()?.Send(delivery);
        return 0;
    }

    public override int GetCommandType()
    {
        return 519;
    }
}
