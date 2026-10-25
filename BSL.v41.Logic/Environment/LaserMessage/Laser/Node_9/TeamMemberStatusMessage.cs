using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>Periodic V41 team-room status ping. The local room has no state to persist.</summary>
public class TeamMemberStatusMessage : PiranhaMessage
{
    public TeamMemberStatusMessage()
    {
        Helper.Skip();
    }

    public override int GetMessageType()
    {
        return 14361;
    }

    public override int GetServiceNodeType()
    {
        return 9;
    }
}
