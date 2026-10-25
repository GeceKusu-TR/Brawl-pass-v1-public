using System.Net.Sockets;
using BSL.v41.General.Cloud;
using BSL.v41.General.NetIsland;
using BSL.v41.General.Networking.Msg;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Proxy.Cloud;
using BSL.v41.Supercell.Titan.CommonUtils;
using BSL.v41.Titan.Graphic;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.General.Networking;

public class ServerConnection(Connection connection)
{
    private readonly Connection? _connection = connection;
    private AccountModel? _accountModel;
    private MessageManager? _messageManager;
    private Messaging? _messaging;
    private string? _loginDevice;
    private int _proxySessionId;

    public Task OwnTask { get; set; } = null!;

    public void Initialize()
    {
        _messaging = new Messaging(_connection!.GetUserListener(), _connection, this);
        _messageManager =
            new MessageManager(_connection!.GetListener(), _connection.GetUserListener(), _connection, this);

        _connection.SetMessaging(_messaging);
        _proxySessionId = ProxySessionManager.CreateNewProxySession(_connection.GetUserListener()!);

        ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Tcp, "New user-connected! Information: " +
                                                                      $"server address: {Helper.GetIpFromDomain(_connection?.GetListener()!.LocalEndPoint!.ToString()!)}; " +
                                                                      $"server port: {Helper.GetPortFromDomain(_connection?.GetListener()!.LocalEndPoint!.ToString()!)}; " +
                                                                      $"client address: {Helper.GetIpBySocket(_connection?.GetUserListener()!)}; " +
                                                                      $"client port: {Helper.GetPortBySocket(_connection?.GetUserListener()!)}. PSI: {_proxySessionId}.");
    }

    public void Update(object? state)
    {
        try
        {
            if (_messaging!.IsConnected(_proxySessionId))
                _messaging!.SetIsConnected(_messageManager!.IsActive());
            else
                Disconnect();

            foreach (var piranhaMessage in GetMessageManager()!.ToProxyMessageDictionary)
            {
                ProxySessionManager.WriteMessageToProxySocket(_proxySessionId, piranhaMessage.Value);
                GetMessageManager()!.ToProxyMessageDictionary.Remove(piranhaMessage.Key);
            }
        }
        catch
        {
            // ignored.
        }
    }

    public Messaging? GetMessaging(Messaging? messaging = null!)
    {
        return messaging ?? _messaging;
    }

    public Connection? GetConnection(Connection? connection100 = null!)
    {
        return connection100 ?? _connection;
    }

    public string? GetServerTypeString()
    {
        return Enum.GetName(typeof(SocketType), _connection!.GetListener()!.SocketType);
    }

    public MessageManager? GetMessageManager()
    {
        return _messageManager;
    }

    public AccountModel GetAccountModel()
    {
        return _accountModel!;
    }

    public AccountModel? GetAccountModelOrNull()
    {
        return _accountModel;
    }

    public AccountModel SetAccountModel(AccountModel accountModel)
    {
        _accountModel = accountModel;
        AccountTraceLogger.Log(accountModel.GetAccountId(), "SESSION-BOUND",
            $"sessionId={_proxySessionId} remote={GetRemoteAddress() ?? "unknown"} " +
            $"loginDevicePresent={!string.IsNullOrWhiteSpace(_loginDevice)}");
        return _accountModel;
    }

    public void SetLoginDevice(string device)
    {
        _loginDevice = device;
    }

    public string? GetLoginDevice()
    {
        return _loginDevice;
    }

    public string? GetRemoteAddress()
    {
        return (_connection?.GetUserListener()?.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString();
    }

    public int GetSessionId()
    {
        return _proxySessionId;
    }

    public bool Disconnect()
    {
        if (_accountModel != null!)
        {
            AccountTraceLogger.Log(_accountModel.GetAccountId(), "DISCONNECT",
                "server connection disconnect requested");
            InteractiveModule.UnregisterPlayerSession(_accountModel.GetAccountId(), this);
        }

        _connection!.Timer!.Dispose();
        OwnTask.Dispose();

        ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Tcp, "New user-disconnected! Information: " +
                                                                      $"server address: {Helper.GetIpFromDomain(_connection?.GetListener()!.LocalEndPoint!.ToString()!)}; " +
                                                                      $"server port: {Helper.GetPortFromDomain(_connection?.GetListener()!.LocalEndPoint!.ToString()!)}; " +
                                                                      $"client address: {Helper.GetIpBySocket(_connection?.GetUserListener()!)}; " +
                                                                      $"client port: {Helper.GetPortBySocket(_connection?.GetUserListener()!)}.");
        return _messaging!.Disconnect();
    }
}
