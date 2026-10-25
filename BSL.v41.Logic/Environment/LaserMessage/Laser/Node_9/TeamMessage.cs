using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Player;
using BSL.v41.Supercell.Titan.CommonUtils;
using BSL.v41.Titan.DataStream.Helps;
using BSL.v41.Titan.Mathematical;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

public class TeamMessage(AccountModel accountModel, long teamId) : PiranhaMessage
{
    public TeamMessage() : this(new AccountModel(), 0)
    {
        Helper.Skip();
    }

    public override void Encode()
    {
        base.Encode();

        // V41 TeamEntry. This is a one-member room; later members are added
        // through the normal team/invite messages.
        ByteStream.WriteVInt(0);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteVInt(3);
        ByteStream.WriteLong((LogicLong)teamId);
        ByteStream.WriteVInt(0);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);
        ByteStreamHelper.WriteDataReference(ByteStream, 0);
        ByteStream.WriteBoolean(false); // no custom battle-player map

        ByteStream.WriteVInt(1);
        ByteStream.WriteBoolean(true);
        ByteStream.WriteLong((LogicLong)accountModel.GetAccountId());
        ByteStreamHelper.WriteDataReference(ByteStream, Convert.ToInt32(
            accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.HomeBrawlerGlobalId)));
        ByteStreamHelper.WriteDataReference(ByteStream, 0);
        ByteStream.WriteVInt(1000);
        ByteStream.WriteVInt(Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.Trophies)));
        ByteStream.WriteVInt(Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.MaxTrophies)));
        ByteStream.WriteVInt(11);
        ByteStream.WriteVInt(0);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteVInt(0);
        for (var i = 0; i < 7; i++) ByteStream.WriteVInt(0);

        var display = new PlayerDisplayData();
        display.SetAvatarName(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.AvatarName).ToString()!);
        display.SetPlayerThumbnail(Convert.ToInt32(accountModel
            .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.PlayerThumbnailGlobalId)));
        display.SetNameColor(Convert.ToInt32(accountModel
            .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.NameColorGlobalId)));
        display.Encode(ByteStream);

        for (var i = 0; i < 4; i++) ByteStreamHelper.WriteDataReference(ByteStream, 0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteVInt(0); // invites
        ByteStream.WriteVInt(0); // join requests
        ByteStream.WriteVInt(0); // disabled bots
        ByteStream.WriteBoolean(true);
        ByteStream.WriteBoolean(true);
        ByteStream.WriteBoolean(true);
        ByteStream.WriteVInt(0); // custom modifiers
    }

    public override int GetMessageType()
    {
        return 24124;
    }

    public override int GetServiceNodeType()
    {
        return 9;
    }
}
