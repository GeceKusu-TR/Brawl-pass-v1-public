using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>V41 client request 14277 for eligible season/Brawl Pass rewards.</summary>
public sealed class GetSeasonRewardsMessage : PiranhaMessage
{
    public GetSeasonRewardsMessage()
    {
        Helper.Skip();
    }

    public override int GetMessageType() => 14277;

    public override int GetServiceNodeType() => 9;
}
