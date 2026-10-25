using BSL.v41.Logic.Environment.LaserCommand;
using BSL.v41.Logic.Environment.LaserCommand.Laser;
using BSL.v41.Titan.DataStream;

namespace BSL.v41.Logic.Environment;

public abstract class LogicCommandManager
{
    private static readonly Dictionary<int, Type> CommandTypes = new()
    {
        { new ServerCommandAcknowledgement().GetCommandType(), typeof(ServerCommandAcknowledgement) },
        { new ServerGemAcknowledgement().GetCommandType(), typeof(ServerGemAcknowledgement) },
        { new LogicSetPlayerAgeCommand().GetCommandType(), typeof(LogicSetPlayerAgeCommand) },
        { new LogicGatchaCommand().GetCommandType(), typeof(LogicGatchaCommand) },
        { new LogicClaimRankUpRewardCommand().GetCommandType(), typeof(LogicClaimRankUpRewardCommand) },
        { new LogicPurchaseOfferCommand().GetCommandType(), typeof(LogicPurchaseOfferCommand) }
        , { new LogicPurchaseBrawlPassCommand().GetCommandType(), typeof(LogicPurchaseBrawlPassCommand) }
        , { new LogicClaimBrawlPassRewardCommand().GetCommandType(), typeof(LogicClaimBrawlPassRewardCommand) }
    };

    public static LogicCommand? DecodeCommand(ByteStream byteStream)
    {
        var type = byteStream.ReadVInt();
        var command = CreateCommand(type);

        if (command == null)
        {
            // Keep unknown commands in the pipeline for account-scoped
            // diagnostics. They remain non-executable by design.
            command = new UnknownLogicCommand(type);
        }

        command.Decode(byteStream);
        return command;
    }

    public static void EncodeCommand(LogicCommand logicCommand, ByteStream byteStream)
    {
        byteStream.WriteVInt(logicCommand.GetCommandType());
        {
            logicCommand.Encode(byteStream);
        }
    }

    public static LogicCommand? CreateCommand(int type)
    {
        if (CommandTypes.TryGetValue(type, out var commandType))
            return (LogicCommand)Activator.CreateInstance(commandType)!;

        return null;
    }
}
