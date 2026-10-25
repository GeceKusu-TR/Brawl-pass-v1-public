using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>V41 client packet 14359: a text message sent to a team.</summary>
public sealed class TeamChatMessage : PiranhaMessage
{
    private string _message = string.Empty;

    public TeamChatMessage()
    {
        Helper.Skip();
    }

    public override void Decode()
    {
        base.Decode();
        _message = ByteStream.ReadString(2000);
    }

    public string GetMessage() => _message;

    public override int GetMessageType() => 14359;

    public override int GetServiceNodeType() => 9;
}
