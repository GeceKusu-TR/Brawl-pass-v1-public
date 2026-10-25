using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>
/// Client notification that the offline/tutorial battle has ended.
/// The battle is client-side while server battles are disabled; accepting the
/// message lets the server return the client to the home screen.
/// </summary>
public class AskForBattleEndMessage : PiranhaMessage
{
    public AskForBattleEndMessage()
    {
        Helper.Skip();
    }

    public override void Decode()
    {
        // The message body is not needed for the home-screen transition.
        base.Decode();
    }

    public override int GetMessageType()
    {
        return 14110;
    }

    public override int GetServiceNodeType()
    {
        return 9;
    }
}
