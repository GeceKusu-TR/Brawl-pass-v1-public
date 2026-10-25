using BSL.v41.Logic.Environment.LaserCommand;
using BSL.v41.Supercell.Titan.CommonUtils;

namespace BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;

public class EndClientTurnMessage : PiranhaMessage
{
    private readonly List<LogicCommand> _logicCommands = [];
    private int _checksum;
    private int _tick;
    private bool _decodeValid = true;
    private string _decodeError = string.Empty;

    public EndClientTurnMessage()
    {
        Helper.Skip();
    }

    public override void Decode()
    {
        base.Decode();

        // Defensive reset also protects callers that explicitly reuse an
        // instance. The factory normally supplies a fresh message now.
        _logicCommands.Clear();
        _tick = 0;
        _checksum = 0;
        _decodeValid = true;
        _decodeError = string.Empty;

        try
        {
            ByteStream.ReadBoolean();
            _tick = ByteStream.ReadVInt();
            _checksum = ByteStream.ReadVInt();
            var commands = ByteStream.ReadVInt();
            if (commands < 0 || commands > 128)
                throw new FormatException($"Invalid command count={commands} offset={ByteStream.GetOffset()}.");

            var parsedCommands = new List<LogicCommand>(commands);
            for (var i = 0; i < commands; i++)
            {
                var commandOffset = ByteStream.GetOffset();
                var commandType = PeekCommandType(commandOffset);
                if (ByteStream.IsAtEnd())
                    throw new FormatException(
                        $"Missing command index={i} type={commandType} offset={commandOffset} " +
                        $"payloadBytes={ByteStream.GetLength()}.");

                try
                {
                    var command = LogicCommandManager.DecodeCommand(ByteStream);
                    if (command != null) parsedCommands.Add(command);
                }
                catch (Exception exception)
                {
                    throw new FormatException(
                        $"Command decode failed index={i} type={commandType} " +
                        $"offsetBefore={commandOffset} offsetAfter={ByteStream.GetOffset()} " +
                        $"payloadBytes={ByteStream.GetLength()} exception={exception.GetType().Name}.",
                        exception);
                }
            }

            _logicCommands.AddRange(parsedCommands);
        }
        catch (Exception exception)
        {
            // Reject the whole EndClientTurn atomically. No partially decoded
            // command is exposed to MessageManager for execution.
            _logicCommands.Clear();
            _decodeValid = false;
            _decodeError = exception.Message.Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    public override void Clear()
    {
        base.Clear();
        _logicCommands.Clear();
        _tick = 0;
        _checksum = 0;
        _decodeValid = true;
        _decodeError = string.Empty;
    }

    public int GetTick()
    {
        return _tick;
    }

    public int GetChecksum()
    {
        return _checksum;
    }

    public IEnumerable<LogicCommand> GetCommands()
    {
        return _logicCommands;
    }

    public int GetDecodedByteOffset() => ByteStream.GetOffset();

    public int GetPayloadLength() => ByteStream.GetLength();

    public string GetPayloadHex(int maxBytes = 96)
    {
        var length = Math.Min(ByteStream.GetLength(), Math.Max(maxBytes, 0));
        return Convert.ToHexString(ByteStream.GetByteArray(), 0, length);
    }

    public bool IsDecodeValid() => _decodeValid;

    public string GetDecodeError() => _decodeError;

    private int PeekCommandType(int offset)
    {
        var savedOffset = ByteStream.GetOffset();
        try
        {
            ByteStream.SetOffset(offset);
            return ByteStream.IsAtEnd() ? -1 : ByteStream.ReadVInt();
        }
        catch
        {
            return -1;
        }
        finally
        {
            ByteStream.SetOffset(savedOffset);
        }
    }

    public override int GetMessageType()
    {
        return 14102;
    }

    public override int GetServiceNodeType()
    {
        return 9;
    }
}
