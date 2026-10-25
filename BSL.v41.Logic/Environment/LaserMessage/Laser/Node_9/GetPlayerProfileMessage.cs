using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

public class GetPlayerProfileMessage : PiranhaMessage
{
    private long _accountId;

    public GetPlayerProfileMessage()
    {
        Helper.Skip();
    }

    public override void Decode()
    {
        base.Decode();

        // 14113 starts with the target LogicLong as two big-endian int32
        // values (high, low). The remaining bytes are an optional client tag
        // field and are not part of the account key.
        if (ByteStream.GetLength() - ByteStream.GetOffset() < 8)
        {
            _accountId = 0;
            return;
        }

        _accountId = (long)ByteStream.ReadLong();
    }

    public long GetAccountId()
    {
        return _accountId;
    }

    public override int GetMessageType()
    {
        return 14113;
    }

    public override int GetServiceNodeType()
    {
        return 9;
    }
}
