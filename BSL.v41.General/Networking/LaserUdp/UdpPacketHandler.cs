using BSL.v41.General.Cloud;
using BSL.v41.Logic.Environment;
using BSL.v41.Logic.Environment.LaserMessage;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_27;
using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.Graphic;

namespace BSL.v41.General.Networking.LaserUdp;

public class UdpPacketHandler(long sessionId, ByteStream byteStream)
{
    public void Receive()
    {
        try
        {
            var type = byteStream.ReadVInt();
            var length = byteStream.ReadVInt();
            var data = byteStream.ReadBytes(length, 2048);

            if (data == null) return;

            var message = LogicLaserMessageFactory.CreateMessageByType(type);
            if (message == null) return;

            message.ByteStream.SetByteArray(data, length);
            message.Decode();

            HandleMessage(message);
        }
        catch (Exception exception)
        {
            ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Warn,
                $"UDP packet rejected for session {sessionId}: {exception.Message}.");
        }
    }

    private void HandleMessage(PiranhaMessage piranhaMessage)
    {
        if (piranhaMessage.GetMessageType() != 10555) return;

        if (!InteractiveModule.LogicBattleModeServersMassive!.TryGetValue(sessionId, out var battleServer))
            return;

        while (((ClientInputMessage)piranhaMessage).ClientInputManager!.GetOverrideGroup()
               .TryDequeue(out var clientInput))
        {
            clientInput.OwnSessionId = sessionId;
            battleServer.HandleClientInput(clientInput);
        }
    }
}
