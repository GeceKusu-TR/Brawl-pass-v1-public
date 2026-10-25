using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.DataStream.Helps;

namespace BSL.v41.Logic.Environment.LaserCommand.Laser;

/// <summary>
/// V41.150's short deliveryType=100 envelope for a single non-box Brawl Pass
/// reward. It deliberately has the same tested shape as the gem delivery, but
/// keeps the optional data references needed by skins, pins and power drops.
/// </summary>
public sealed class LogicGiveDirectBrawlPassRewardCommand(
    int amount,
    int valueType,
    int dataReference = 0,
    int skinReference = 0,
    int pinReference = 0,
    int cardReference = 0,
    int progressionMarker = 0) : LogicCommand
{
    private readonly int _amount = Math.Max(0, amount);
    private readonly int _valueType = Math.Max(0, valueType);
    private readonly int _dataReference = Math.Max(0, dataReference);
    private readonly int _skinReference = Math.Max(0, skinReference);
    private readonly int _pinReference = Math.Max(0, pinReference);
    private readonly int _cardReference = Math.Max(0, cardReference);
    private readonly int _progressionMarker = Math.Max(0, progressionMarker);

    public override void Encode(ByteStream byteStream)
    {
        byteStream.WriteVInt(0);
        byteStream.WriteVInt(1);
        byteStream.WriteVInt(100); // one direct reward, no box screen
        byteStream.WriteVInt(1);

        byteStream.WriteVInt(_amount);
        ByteStreamHelper.WriteDataReference(byteStream, _dataReference);
        byteStream.WriteVInt(_valueType);
        ByteStreamHelper.WriteDataReference(byteStream, _skinReference);
        ByteStreamHelper.WriteDataReference(byteStream, _pinReference);
        ByteStreamHelper.WriteDataReference(byteStream, _cardReference);
        byteStream.WriteVInt(_progressionMarker);
    }

    public override int GetCommandType() => 203;
}
