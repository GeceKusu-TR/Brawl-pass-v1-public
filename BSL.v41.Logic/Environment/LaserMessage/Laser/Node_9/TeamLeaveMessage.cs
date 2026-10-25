using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>V41 client request to leave the current team room.</summary>
public class TeamLeaveMessage : PiranhaMessage
{
    public TeamLeaveMessage()
    {
        Helper.Skip();
    }

    public override int GetMessageType()
    {
        return 14353;
    }

    public override int GetServiceNodeType()
    {
        return 9;
    }
}
