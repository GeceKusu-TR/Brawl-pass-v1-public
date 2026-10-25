using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>
/// Ends the active home-logic view before the server sends a fresh home
/// snapshot.  The V41 client acknowledges this with HomeLogicStoppedMessage
/// (14105); sending OwnHomeDataMessage while the box view is still active can
/// enter an unsafe native transition.
/// </summary>
public sealed class StopHomeLogicMessage : PiranhaMessage
{
    public StopHomeLogicMessage()
    {
        Helper.Skip();
    }

    public override int GetMessageType() => 24106;

    public override int GetServiceNodeType() => 9;
}
