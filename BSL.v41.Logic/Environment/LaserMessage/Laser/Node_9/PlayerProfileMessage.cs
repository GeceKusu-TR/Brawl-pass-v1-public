using BSL.v41.Logic.Database.Account;
using BSL.v41.Supercell.Titan.CommonUtils;
using BSL.v41.Titan.DataStream.Helps;
using BSL.v41.Titan.Mathematical;
using BSL.v41.Titan.Mathematical.Data;
using BSL.v41.Supercell.Titan.CommonUtils.Utils;
using BSL.v41.Tools.LaserCsv;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Player;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

public class PlayerProfileMessage(AccountModel accountModel) : PiranhaMessage
{
    public PlayerProfileMessage() : this(new AccountModel())
    {
        Helper.Skip();
    }

    public override void Encode()
    {
        base.Encode();

        var accountId = accountModel.GetAccountId();
        ByteStreamHelper.EncodeLogicLong(ByteStream, (LogicLong)accountId);
        ByteStreamHelper.WriteDataReference(ByteStream,
            Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.HomeBrawlerGlobalId)));

        // V41 expects the complete character list in a profile, even if the
        // account has not unlocked every character yet.
        ByteStream.WriteVInt(70);
        for (var i = 0; i < 70; i++)
        {
            ByteStreamHelper.WriteDataReference(ByteStream, CsvHelperTable.Characters.GetId(), i);
            ByteStreamHelper.WriteDataReference(ByteStream, 0);
            ByteStream.WriteVInt(i == 0
                ? Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                    AccountStructure.Trophies))
                : 0);
            ByteStream.WriteVInt(i == 0
                ? Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                    AccountStructure.MaxTrophies))
                : 0);
            ByteStream.WriteVInt(i == 0 ? 11 : 0);
        }

        var trophies = Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.Trophies));
        var maxTrophies = Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.MaxTrophies));
        ByteStream.WriteVInt(8);
        WriteStat(1, 0);
        WriteStat(2, 100);
        WriteStat(3, trophies);
        WriteStat(4, maxTrophies);
        WriteStat(5, 1);
        WriteStat(7, 0);
        WriteStat(8, 0);
        WriteStat(11, 0);

        var display = new PlayerDisplayData();
        display.SetAvatarName(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.AvatarName).ToString()!);
        display.SetPlayerThumbnail(Convert.ToInt32(accountModel
            .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.PlayerThumbnailGlobalId)));
        display.SetNameColor(Convert.ToInt32(accountModel
            .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.NameColorGlobalId)));
        display.Encode(ByteStream);

        // Optional battle-card/alliance section. The account's alliance data is
        // served through the existing alliance messages; keeping this section
        // empty makes profile lookup safe for accounts without an alliance.
        ByteStream.WriteBoolean(false);
        ByteStreamHelper.WriteDataReference(ByteStream, 0);
        ByteStream.WriteVInt(16);
    }

    private void WriteStat(int key, int value)
    {
        ByteStream.WriteVInt(key);
        ByteStream.WriteVInt(value);
    }

    public override int GetMessageType()
    {
        return 24113;
    }

    public override int GetServiceNodeType()
    {
        return 9;
    }
}
