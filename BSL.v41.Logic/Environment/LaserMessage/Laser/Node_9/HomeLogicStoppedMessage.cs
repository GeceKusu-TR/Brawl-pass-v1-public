using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>
/// Client acknowledgement for StopHomeLogicMessage (24106).
/// </summary>
public sealed class HomeLogicStoppedMessage : PiranhaMessage
{
    public HomeLogicStoppedMessage()
    {
        Helper.Skip();
    }

    public override int GetMessageType() => 14105;

    public override int GetServiceNodeType() => 9;
}
