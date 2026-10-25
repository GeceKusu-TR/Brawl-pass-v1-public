using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

public class TeamCreateMessage : PiranhaMessage
{
    public long TeamOwnerId { get; private set; }
    public int TeamType { get; private set; }
    public int EventSlot { get; private set; }

    public TeamCreateMessage()
    {
        Helper.Skip();
    }

    public override void Decode()
    {
        base.Decode();
        // The V41 client sends optional team configuration fields after the
        // header and their layout varies by game mode. The server creates a
        // private room from the authenticated connection, so it must not
        // reject the request because an unused optional field is absent.
        TeamOwnerId = 0;
        TeamType = 0;
        EventSlot = 0;
    }

    public override int GetMessageType()
    {
        return 14350;
    }

    public override int GetServiceNodeType()
    {
        return 9;
    }
}
