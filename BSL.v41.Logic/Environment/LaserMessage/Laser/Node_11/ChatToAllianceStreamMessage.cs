using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_11;

/// <summary>V41 client packet 14315: a text message sent to the alliance stream.</summary>
public sealed class ChatToAllianceStreamMessage : PiranhaMessage
{
    private string _message = string.Empty;

    public ChatToAllianceStreamMessage()
    {
        Helper.Skip();
    }

    public override void Decode()
    {
        base.Decode();
        _message = ByteStream.ReadString(2000);
    }

    public string GetMessage() => _message;

    public override int GetMessageType() => 14315;

    public override int GetServiceNodeType() => 11;
}
