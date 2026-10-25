using BSL.v41.Titan.DataStream;
using BSL.v41.Logic.Environment.LaserCommand;

namespace BSL.v41.Logic.Environment.LaserCommand.Laser;

/// <summary>
/// V41 server command 202 (LogicDiamondsAddedCommand).
/// </summary>
public sealed class LogicDiamondsAddedCommand(int amount) : LogicCommand
{
    private readonly int _amount = Math.Max(0, amount);

    public override void Encode(ByteStream byteStream)
    {
        // V41.150 reference layout:
        // AllianceGift, Diamonds, three reserved Int32 fields, transaction id.
        byteStream.WriteBoolean(false);
        byteStream.WriteInt(_amount);
        byteStream.WriteInt(0);
        byteStream.WriteInt(0);
        byteStream.WriteInt(0);
        byteStream.WriteString("GPA.0000-0000-0000-00000");
    }

    public override int GetCommandType() => 202;
}
