using System.Net.Sockets;
using BSL.v41.General.Network;
using BSL.v41.Logic.Environment;
using BSL.v41.Logic.Environment.LaserMessage;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;
using BSL.v41.Supercell.Titan.CommonUtils;
using BSL.v41.Titan.Graphic;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.General.Networking.Msg;

public static class HttpMessaging
{
    public static void ReadMessage(int type, int length, int version, byte[] payload, ServerConnection serverConnection,
        int proxySessionId)
    {
        var account = serverConnection.GetAccountModelOrNull();
        var accountId = account?.GetAccountId() ?? 0;
        var packetName = Helper.GetPacketNameByType(type);
        AccountTraceLogger.Log(accountId, "WIRE-READ-BEGIN",
            $"type={type} name={packetName} payloadBytes={length} actualBytes={payload.Length} " +
            $"version={version} proxySessionId={proxySessionId}");

        try
        {
            var message = LogicLaserMessageFactory.CreateMessageByType(type);
            if (message == null)
            {
                AccountTraceLogger.Log(accountId, "WIRE-DROP",
                    $"type={type} name={packetName} reason=unknown_message_type proxySessionId={proxySessionId}");
                return;
            }

            message.SetProxySessionId(proxySessionId);
            if (length > 0)
            {
                message.ByteStream.SetByteArray(payload, payload.Length);
                message.Decode();
            }

            if (message is EndClientTurnMessage endClientTurn && !endClientTurn.IsDecodeValid())
            {
                AccountTraceLogger.Log(serverConnection.GetAccountModelOrNull()?.GetAccountId() ?? accountId,
                    "WIRE-REJECT",
                    $"type={type} name={message.GetMessageTypeName()} payloadBytes={length} " +
                    $"decodedBytes={endClientTurn.GetDecodedByteOffset()} " +
                    $"decodeError={endClientTurn.GetDecodeError()} " +
                    $"payloadHex={endClientTurn.GetPayloadHex()} proxySessionId={proxySessionId}");
                return;
            }

            AccountTraceLogger.Log(accountId, "WIRE-DECODE-OK",
                $"type={type} name={message.GetMessageTypeName()} payloadBytes={length} " +
                $"version={version} proxySessionId={proxySessionId}");

            var state = serverConnection.GetMessageManager()!.ReceiveMessage(message);
            AccountTraceLogger.Log(serverConnection.GetAccountModelOrNull()?.GetAccountId() ?? accountId,
                "WIRE-RESULT",
                $"type={type} name={message.GetMessageTypeName()} state={state} " +
                $"proxySessionId={proxySessionId}");

            if (state < 300 - 1)
                ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Info,
                    $"Message with type: {type} ({packetName}), " +
                    $"was processed and returned this state: '{(state >= 1 ? "successfully" : "failed")}'. PSI: {proxySessionId}");
        }
        catch (Exception exception)
        {
            // Keep the connection alive where possible, but retain enough
            // metadata to identify a malformed packet or encoder mismatch.
            AccountTraceLogger.Log(serverConnection.GetAccountModelOrNull()?.GetAccountId() ?? accountId,
                "WIRE-ERROR",
                $"type={type} name={packetName} payloadBytes={length} version={version} " +
                $"proxySessionId={proxySessionId} exception={exception.GetType().Name} " +
                $"message={Sanitize(exception.Message)}");
            ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Error,
                $"Failed to process message {type} ({packetName}): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string Sanitize(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }

    public static void WriteMessage(PiranhaMessage message, ServerConnection serverConnection)
    {
        message.Encode();

        var payload = new byte[message.GetEncodingLength()];
        {
            Buffer.BlockCopy(message.GetMessageBytes(), 0, payload, 0, payload.Length);
        }

        var stream = new byte[payload.Length + 7];
        {
            stream[0] = (byte)(message.GetMessageType() >> 8);
            stream[1] = (byte)message.GetMessageType();
            stream[2] = (byte)(payload.Length >> 16);
            stream[3] = (byte)(payload.Length >> 8);
            stream[4] = (byte)payload.Length;
            stream[5] = (byte)(message.GetMessageVersion() >> 8);
            stream[6] = (byte)message.GetMessageVersion();
        }

        Buffer.BlockCopy(payload, 0, stream, 7, payload.Length);
        {
            var buffer = stream.Concat(new byte[] { 0xFF, 0xFF, 0x0, 0x0, 0x0, 0x0, 0x0 }).ToArray(); // pornography
            {
                var account = serverConnection.GetAccountModelOrNull();
                var accountId = account?.GetAccountId() ?? 0;
                var socket = serverConnection.GetConnection()!.GetUserListener()!;
                if (message is LeaderboardMessage)
                {
                    // Keep a byte-level capture for the V41 leaderboard only.
                    // This contains no authentication material and lets a
                    // client crash report be compared with the exact payload
                    // that was sent without enabling a noisy global hex dump.
                    AccountTraceLogger.Log(accountId, "LEADERBOARD-WIRE-HEX",
                        $"payloadBytes={payload.Length} frameHeaderHex={Convert.ToHexString(stream.AsSpan(0, 7))} " +
                        $"payloadHex={Convert.ToHexString(payload)}");
                }
                AccountTraceLogger.Log(accountId, "WIRE-FRAME-BUILD",
                    $"type={message.GetMessageType()} name={message.GetMessageTypeName()} " +
                    $"version={message.GetMessageVersion()} serviceNode={message.GetServiceNodeType()} " +
                    $"payloadBytes={payload.Length} frameBytes={buffer.Length} trailerBytes=7 " +
                    $"socketConnected={socket.Connected}");
                socket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None,
                    TcpLaserSocketListener.OnSend, socket);
                AccountTraceLogger.Log(accountId, "WIRE-FRAME-QUEUED",
                    $"type={message.GetMessageType()} frameBytes={buffer.Length} socketConnected={socket.Connected}");
            }
        }
    }

    public static void WriteMessage(PiranhaMessage message, Socket socket)
    {
        message.Encode();

        var payload = new byte[message.GetEncodingLength()];
        {
            Buffer.BlockCopy(message.GetMessageBytes(), 0, payload, 0, payload.Length);
        }

        var stream = new byte[payload.Length + 7];
        {
            stream[0] = (byte)(message.GetMessageType() >> 8);
            stream[1] = (byte)message.GetMessageType();
            stream[2] = (byte)(payload.Length >> 16);
            stream[3] = (byte)(payload.Length >> 8);
            stream[4] = (byte)payload.Length;
            stream[5] = (byte)(message.GetMessageVersion() >> 8);
            stream[6] = (byte)message.GetMessageVersion();
        }

        Buffer.BlockCopy(payload, 0, stream, 7, payload.Length);
        {
            var buffer = stream.Concat(new byte[] { 0xFF, 0xFF, 0x0, 0x0, 0x0, 0x0, 0x0 }).ToArray(); // pornography
            {
                socket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, TcpLaserSocketListener.OnSend, socket);
            }
        }
    }
}
