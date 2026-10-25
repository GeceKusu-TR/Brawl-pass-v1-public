using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>
/// V41 battle-log request. Battle/replay services are intentionally disabled
/// in this deployment, but the request is handled explicitly so it is not
/// reported as an unknown wire message.
/// </summary>
public sealed class GetBattleLogMessage : PiranhaMessage
{
    public GetBattleLogMessage()
    {
        Helper.Skip();
    }

    public override int GetMessageType() => 14114;
    public override int GetServiceNodeType() => 9;
}
