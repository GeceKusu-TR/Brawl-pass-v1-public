using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.DataStream.Helps;
using BSL.v41.Titan.Mathematical;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.Logic.Environment.LaserMessage.Sepo.Avatar;

public class LogicClientAvatar(AccountModel accountModel)
{
    public void Encode(ByteStream byteStream)
    {
        var resources = accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        var gold = resources.TryGetValue(1, out var goldEntry) ? goldEntry.Value : 0;
        var gems = resources.TryGetValue(16, out var gemsEntry) ? gemsEntry.Value : 0;
        var powerPoints = Math.Max(
            resources.TryGetValue(12, out var powerPointsEntry) ? powerPointsEntry.Value : 0,
            resources.TryGetValue(29, out var legacyPowerPointsEntry) ? legacyPowerPointsEntry.Value : 0);
        AccountTraceLogger.Log(accountModel.GetAccountId(), "AVATAR-RESOURCES",
            $"gold={gold} gems={gems} powerPoints={powerPoints} homeBrawler=" +
            $"{accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.HomeBrawlerGlobalId)}");

        ByteStreamHelper.EncodeLogicLong(byteStream, new LogicLong(0, Convert.ToInt32(accountModel.GetAccountId())));
        ByteStreamHelper.EncodeLogicLong(byteStream, new LogicLong(0, Convert.ToInt32(accountModel.GetAccountId())));
        ByteStreamHelper.EncodeLogicLong(byteStream, new LogicLong(0,
            Convert.ToInt16(
                accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.HomeId))));

        byteStream.WriteStringReference(
            accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AvatarName)
                .ToString()!);
        byteStream.WriteBoolean(
            (bool)accountModel
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.NameSetByUser));

        byteStream.WriteInt(0);

        byteStream.WriteVInt(15);

        byteStream.WriteVInt(2 + 2);

        ByteStreamHelper.WriteDataReference(byteStream, 23, 0);
        byteStream.WriteVInt(1);

        ByteStreamHelper.WriteDataReference(byteStream, 5, 8);
        byteStream.WriteVInt(gold);

        ByteStreamHelper.WriteDataReference(byteStream, 5, 10);
        // 5000010 is the star-point resource in V41, not diamonds.
        byteStream.WriteVInt(0);

        ByteStreamHelper.WriteDataReference(byteStream, 5, 12);
        byteStream.WriteVInt(powerPoints);

        for (var i = 0; i < 14; i++) byteStream.WriteVInt(0);

        // The first value in this tail is the diamond balance. Keep the
        // remaining ten placeholders so the tutorial counter stays aligned.
        byteStream.WriteVInt(gems);
        for (var i = 0; i < 10; i++) byteStream.WriteVInt(0);
        byteStream.WriteVInt(2);

    }
}
