using System.Net;
using System.Net.Sockets;
using BSL.v41.General.Cloud;
using BSL.v41.General.Networking.LaserUdp;
using BSL.v41.Titan.DataStream;
using BSL.v41.Titan.Graphic;

namespace BSL.v41.General.Network;

public class UdpLaserSocketListener(int port)
{
    public readonly Dictionary<long, IPEndPoint>? Sessions = new();
    private Thread? _thread;
    private int _diagnosticPacketCount;
    public Socket? UdpAdministrator;

    public void StartSocket()
    {
        UdpAdministrator = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        UdpAdministrator!.Bind(new IPEndPoint(IPAddress.Any, port));

        ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Start, $"Network-server started! Information: " +
                                                                        $"server protocol type: {UdpAdministrator.ProtocolType}; " +
                                                                        $"server port: {port}.");

        _thread = new Thread(Receive);
        _thread.Start();
    }

    [Obsolete("Obsolete")]
    public void StopSocket()
    {
        _thread!.Abort();
        UdpAdministrator!.Close();
        ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Stop, $"Network-server stopped! Information: " +
                                                                       $"server protocol type: {UdpAdministrator.ProtocolType}; " +
                                                                       $"server port: {port}.");
    }

    private void Receive()
    {
        while (true)
        {
            var buffer = new byte[1024];
            EndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 0);
            var bytesRead = UdpAdministrator!.ReceiveFrom(buffer, ref ipEndPoint);

            var byteStream = new ByteStream(buffer, bytesRead);
            try
            {
                var diagnosticPacket = Interlocked.Increment(ref _diagnosticPacketCount) <= 8;
                if (diagnosticPacket)
                    ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Udp,
                        $"UDP datagram received: port={port}; bytes={bytesRead}; endpoint={ipEndPoint}; " +
                        $"head={Convert.ToHexString(buffer.AsSpan(0, Math.Min(bytesRead, 24)))}.");

                long sessionId = byteStream.ReadLong();
                byteStream.ReadShort();

                // A stray or stale datagram must not terminate the receive
                // loop for the whole battle port. This used to return from
                // Receive(), leaving every later battle on that UDP port
                // without input or vision traffic.
                if (sessionId < Saver.LastUdpSessionId / 2) continue;

                if (Sessions!.TryAdd(sessionId, (IPEndPoint)ipEndPoint))
                    ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Udp,
                        $"UDP session registered: session={sessionId}; port={port}; endpoint={ipEndPoint}.");
                Task.Run(() => { new UdpPacketHandler(sessionId, byteStream).Receive(); });
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref _diagnosticPacketCount) <= 8)
                    ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Warn,
                        $"UDP datagram rejected on port {port}: {exception.Message}.");
            }
        }
    }
}
