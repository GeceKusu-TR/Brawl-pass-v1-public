using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>V41 server acknowledgement that the player left a team room.</summary>
public class TeamLeftMessage : PiranhaMessage
{
    public TeamLeftMessage()
    {
        Helper.Skip();
    }

    public override int GetMessageType()
    {
        return 24125;
    }

    public override int GetServiceNodeType()
    {
        return 9;
    }
}
