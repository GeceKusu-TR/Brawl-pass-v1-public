using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.DataStream.Helps;

namespace BSL.v41.Logic.Environment.LaserCommand.Laser;

/// <summary>
/// V41 direct-reward delivery containing exactly one gem drop. Delivery type
/// 100 has a shorter wire shape than the box (10/11/12) envelope: after the
/// drop's three reserved data fields it ends with the current progression
/// marker. Sending the box footer here leaves V41.150 in its loading state.
/// </summary>
public sealed class LogicGiveDirectGemRewardCommand(int amount, int progressionMarker = 0) : LogicCommand
{
    private readonly int _amount = Math.Max(0, amount);
    private readonly int _progressionMarker = Math.Max(0, progressionMarker);

    public override void Encode(ByteStream byteStream)
    {
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(1);
        byteStream.WriteVInt(100); // direct reward, no box screen
        byteStream.WriteVInt(1);   // one reward unit

        byteStream.WriteVInt(_amount);
        ByteStreamHelper.WriteDataReference(byteStream, 0);
        byteStream.WriteVInt(8);   // V41 gem reward value type
        byteStream.WriteVInt(0); // reserved reward field
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(_progressionMarker);
    }

    public override int GetCommandType() => 203;
}
