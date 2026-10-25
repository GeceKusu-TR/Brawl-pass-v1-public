using BSL.v41.General.NetIsland.LaserBattle;
using BSL.v41.General.Networking;
using BSL.v41.Logic.Environment.LaserCommand.Laser;
using BSL.v41.Logic.Environment.LaserMessage;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_1;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;
using BSL.v41.Logic.Environment.LaserNotification.Laser;

namespace BSL.v41.General.Cloud;

public static class InteractiveModule
{
    public static readonly Dictionary<long, ServerConnection>? ServerConnections = new();
    public static readonly Dictionary<long, long> UdpSessionIdsMassive = new();
    public static readonly Dictionary<long, LogicBattleModeServer>? LogicBattleModeServersMassive = new();

    public static void SendMessageToAllActivePlayersByTcpProtocol(PiranhaMessage piranhaMessage)
    {
        if (ServerConnections == null) return;
        foreach (var serverConnection in
                 ServerConnections.Where(serverConnection => serverConnection.Key > 0))
            serverConnection.Value.GetMessaging()!.Send(piranhaMessage);
    }

    public static void CloseAllPlayersActiveSessions()
    {
        if (ServerConnections == null) return;
        foreach (var serverConnection in
                 ServerConnections.Where(serverConnection => serverConnection.Key > 0).ToArray())
            serverConnection.Value.Disconnect();
    }

    public static void RegisterPlayerSession(long accountId, ServerConnection serverConnection)
    {
        if (ServerConnections == null) return;
        lock (ServerConnections) ServerConnections[accountId] = serverConnection;
    }

    public static bool DisconnectPlayer(long accountId)
    {
        if (ServerConnections == null) return false;

        ServerConnection? serverConnection;
        lock (ServerConnections)
        {
            if (!ServerConnections.Remove(accountId, out serverConnection)) return false;
        }

        try
        {
            // Tell the client why the session is ending before closing the TCP
            // socket. Abrupt closure makes some V41 clients crash.
            var notification = new AvailableServerCommandMessage();
            notification.SetServerCommand(new LogicAddNotificationCommand(
                new FloaterTextNotification("Bu bağlantı yönetici tarafından sonlandırıldı.")));
            serverConnection.GetMessaging()?.Send(notification);

            var disconnectedMessage = new DisconnectedMessage();
            disconnectedMessage.SetReason(2);
            serverConnection.GetMessaging()?.Send(disconnectedMessage);

            _ = Task.Run(async () =>
            {
                await Task.Delay(250);
                try
                {
                    serverConnection.Disconnect();
                }
                catch
                {
                    // The client may have already closed the socket.
                }
            });
        }
        catch
        {
            // The socket may already have closed between the lookup and disconnect.
        }

        return true;
    }

    public static void UnregisterPlayerSession(long accountId, ServerConnection serverConnection)
    {
        if (ServerConnections == null) return;
        lock (ServerConnections)
        {
            if (ServerConnections.TryGetValue(accountId, out var current) && ReferenceEquals(current, serverConnection))
                ServerConnections.Remove(accountId);
        }
    }

    public static void ClearUdpSessionIdsMassive()
    {
        UdpSessionIdsMassive.Clear();
    }

    public static void ClearLogicBattleModeServersMassive()
    {
        LogicBattleModeServersMassive!.Clear();
    }
}
