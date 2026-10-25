using BSL.v41.Supercell.Titan.CommonUtils;
using BSL.v41.Titan.DataStream.Helps;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

/// <summary>V41 request for player, brawler and club leaderboards.</summary>
public sealed class GetLeaderboardMessage : PiranhaMessage
{
    private bool _isRegional;
    private int _brawlerGlobalId;
    private int _leaderboardType = 1;
    private int _unknownVInt;
    private bool _decodeValid = true;

    public GetLeaderboardMessage()
    {
        Helper.Skip();
    }

    public override void Decode()
    {
        base.Decode();
        _isRegional = false;
        _brawlerGlobalId = 0;
        _leaderboardType = 1;
        _unknownVInt = 0;
        _decodeValid = true;

        try
        {
            // V41 order: regional flag, leaderboard type, brawler data
            // reference, trailing value. Reading the data reference before
            // the type made every request look like leaderboardType=0 and
            // prevented the client from selecting the correct tab.
            if (ByteStream.GetLength() - ByteStream.GetOffset() < 1)
            {
                _decodeValid = false;
                return;
            }

            _isRegional = ByteStream.ReadBoolean();
            if (!ByteStream.IsAtEnd())
                _leaderboardType = ByteStream.ReadVInt();
            if (!ByteStream.IsAtEnd())
                _brawlerGlobalId = ByteStreamHelper.ReadDataReference(ByteStream);
            if (!ByteStream.IsAtEnd())
                _unknownVInt = ByteStream.ReadVInt();
        }
        catch
        {
            // A malformed optional request must not tear down the session.
            // Default to the safe global player leaderboard.
            _isRegional = false;
            _brawlerGlobalId = 0;
            _leaderboardType = 1;
            _decodeValid = false;
        }
    }

    public bool IsRegional() => _isRegional;
    public int GetBrawlerGlobalId() => _brawlerGlobalId;
    public int GetLeaderboardType() => _leaderboardType;
    public bool IsDecodeValid() => _decodeValid;

    public override int GetMessageType() => 14403;
    public override int GetServiceNodeType() => 9;
}
