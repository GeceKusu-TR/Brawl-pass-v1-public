using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserCommand;
using BSL.v41.Logic.Environment.LaserListener;
using BSL.v41.Logic.Environment.LaserMessage;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Avatar;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.Logic.Environment.LaserMessage.Sepo.Mode;

public class LogicHomeMode
{
    private LogicClientAvatar _logicClientAvatar = null!;
    private LogicClientHome _logicClientHome = null!;
    private LogicGameListener? _logicGameListener;
    private Action<PiranhaMessage>? _serverMessageSender;
    private bool _boxHomeRefreshPending;
    private bool _boxHomeReconnectScheduled;
    private bool _directRewardAwaitingAck;
    private bool _directRewardHomeRefreshPending;
    private int _boxHomeRefreshDelayMilliseconds = 1_000;
    private Action? _sessionReconnect;
    public AccountModel AccountModel { get; set; } = null!;
    public int SessionId { get; private set; }

    public void SetSessionId(int sessionId)
    {
        SessionId = sessionId;
    }

    public void EndClientTurnReceived(int tick, int checksum, IEnumerable<LogicCommand> commands,
        LogicGameListener? logicGameListener = null)
    {
        logicGameListener ??= _logicGameListener;
        var commandList = commands.ToList();
        AccountTraceLogger.Log(AccountModel.GetAccountId(), "END-TURN",
            $"count={commandList.Count} types={string.Join(',', commandList.Take(12).Select(command => command.GetCommandType()))}");

        foreach (var command in commandList)
        {
            var commandType = command.GetCommandType();
            var canExecute = command.CanExecute(this);
            if (command is UnknownLogicCommand)
            {
                if (commandType is 206 or 514)
                {
                    // These are emitted by the client around the play flow.
                    // Battles are intentionally disabled on this deployment,
                    // so do not present them as protocol errors.
                    AccountTraceLogger.Log(AccountModel.GetAccountId(), "BATTLE-DISABLED-COMMAND",
                        $"STATUS=INTENTIONAL_BATTLE_MAINTENANCE command={commandType} " +
                        "CAN_EXECUTE=FALSE; BATTLE_SYSTEM_DISABLED_BY_SERVER; " +
                        "PLAY_BUTTON_REQUEST_WAS_REJECTED_INTENTIONALLY; " +
                        "CLIENT_NOTICE=ŞUANDA BAKIM MOLASIDAYIZ.");
                }
                else
                {
                    AccountTraceLogger.Log(AccountModel.GetAccountId(), "UNKNOWN-CLIENT-COMMAND",
                        $"type={commandType} canExecute={canExecute}");
                }
                continue;
            }

            AccountTraceLogger.Log(AccountModel.GetAccountId(), "CLIENT-COMMAND",
                $"type={commandType} canExecute={canExecute}");
            if (!canExecute) continue;

            var result = command.Execute(this);
            AccountTraceLogger.Log(AccountModel.GetAccountId(), "CLIENT-COMMAND-RESULT",
                $"type={commandType} result={result}");
            if (result != 0) logicGameListener!.Send(new OutOfSyncMessage());
        }

        // Scalar Brawl Pass rewards (gold/gems) use a safe 203 animation. Once
        // the client has acknowledged that animation, the next empty turn is
        // a safe point to refresh OwnHomeData in-place. Unlike the box path,
        // do not reconnect: the Brawl Pass screen can then show its claimed
        // tick immediately.
        if (_directRewardHomeRefreshPending && commandList.Count == 0)
        {
            _directRewardHomeRefreshPending = false;
            try
            {
                logicGameListener?.Send(new OwnHomeDataMessage(AccountModel, false));
                AccountTraceLogger.Log(AccountModel.GetAccountId(),
                    "BRAWL-PASS-DIRECT-REFRESH",
                    "refresh=own_home_data_in_place sent=True reconnect=False");
            }
            catch (Exception exception)
            {
                AccountTraceLogger.Log(AccountModel.GetAccountId(),
                    "BRAWL-PASS-DIRECT-REFRESH-ERROR",
                    $"exception={exception.GetType().Name} " +
                    $"message={exception.Message.Replace('\r', ' ').Replace('\n', ' ')}");
            }
        }

        // A box reward acknowledgement is received while the native reward
        // view is still active. Sending OwnHomeData from that same turn can
        // crash the V41 client, while 24106 was ignored by this client build.
        // After a later idle turn, refresh through a controlled reconnect so
        // the next Login/OwnHomeData pair contains the durable claim state.
        if (_boxHomeRefreshPending && commandList.Count == 0 && !_boxHomeReconnectScheduled)
        {
            _boxHomeReconnectScheduled = true;
            var reconnect = _sessionReconnect;
            var refreshDelayMilliseconds = _boxHomeRefreshDelayMilliseconds;
            AccountTraceLogger.Log(AccountModel.GetAccountId(), "BOX-CLAIM-RECONNECT-SCHEDULED",
                $"refresh=pendingUntilFreshLogin delayMs={refreshDelayMilliseconds} reconnectAvailable={reconnect != null} " +
                "reason=avoid_unsafe_own_home_data_during_native_box_view");

            _ = Task.Run(async () =>
            {
                // The 203 delivery has already been acknowledged by this
                // point. A short orderly reconnect gives the client a fresh
                // 24101 snapshot quickly, while still avoiding a concurrent
                // OwnHomeData frame during the native box animation.
                await Task.Delay(refreshDelayMilliseconds);
                if (reconnect == null)
                {
                    AccountTraceLogger.Log(AccountModel.GetAccountId(), "BOX-CLAIM-RECONNECT-SKIP",
                        "reason=session_reconnect_callback_missing");
                    return;
                }

                try
                {
                    _boxHomeRefreshPending = false;
                    AccountTraceLogger.Log(AccountModel.GetAccountId(), "BOX-CLAIM-RECONNECT",
                        "action=disconnect_for_fresh_login_home_snapshot");
                    reconnect();
                }
                catch (Exception exception)
                {
                    AccountTraceLogger.Log(AccountModel.GetAccountId(), "BOX-CLAIM-RECONNECT-ERROR",
                        $"exception={exception.GetType().Name} " +
                        $"message={exception.Message.Replace('\r', ' ').Replace('\n', ' ')}");
                }
            });
        }
    }

    public LogicGameListener? GetGameListener()
    {
        return _logicGameListener;
    }

    public void SetGameListener(LogicGameListener? logicGameListener)
    {
        _logicGameListener = logicGameListener;
    }

    public void SetServerMessageSender(Action<PiranhaMessage>? sender)
    {
        _serverMessageSender = sender;
    }

    public void SetSessionReconnect(Action? reconnect)
    {
        _sessionReconnect = reconnect;
    }

    public bool SendServerMessage(PiranhaMessage message)
    {
        if (_serverMessageSender == null) return false;
        _serverMessageSender(message);
        return true;
    }

    public void RequestBoxHomeRefresh(int delayMilliseconds = 1_000)
    {
        _boxHomeRefreshPending = true;
        _boxHomeReconnectScheduled = false;
        _boxHomeRefreshDelayMilliseconds = Math.Clamp(delayMilliseconds, 500, 5_000);
    }

    public void MarkDirectRewardAwaitingAck()
    {
        _directRewardAwaitingAck = true;
        AccountTraceLogger.Log(AccountModel.GetAccountId(),
            "BRAWL-PASS-DIRECT-ACK-WAIT",
            "command=203 waiting=True refresh_after_ack=True");
    }

    public bool AcknowledgeDirectReward()
    {
        if (!_directRewardAwaitingAck) return false;
        _directRewardAwaitingAck = false;
        _directRewardHomeRefreshPending = true;
        AccountTraceLogger.Log(AccountModel.GetAccountId(),
            "BRAWL-PASS-DIRECT-REFRESH-SCHEDULED",
            "refresh=own_home_data_in_place wait_for_empty_turn=True reconnect=False");
        return true;
    }

    public void MarkBoxHomeRefreshAcknowledged()
    {
        _boxHomeRefreshPending = false;
    }

    /// <summary>
    /// Ends the current native home view before a direct Brawl Pass gem claim
    /// is refreshed. The client must acknowledge 24106 with 14105 before the
    /// server sends OwnHomeData; sending that frame directly during the
    /// reward transition causes a V41 native null-pointer crash.
    /// </summary>
    public bool RequestGemHomeLogicStop()
    {
        var accountId = AccountModel.GetAccountId();
        if (_logicGameListener == null)
        {
            AccountTraceLogger.Log(accountId, "BRAWL-PASS-GEM-REFRESH-SKIP",
                "reason=game_listener_missing");
            return false;
        }

        AccountTraceLogger.Log(accountId, "BRAWL-PASS-GEM-REFRESH-SCHEDULED",
            "action=send_stop_home_logic type=24106 wait_for_client_ack=14105 " +
            "own_home_data=deferred reconnect=false");
        _logicGameListener.Send(new StopHomeLogicMessage());
        return true;
    }

    public LogicClientHome GetHome()
    {
        return _logicClientHome;
    }

    public void SetHome(LogicClientHome logicClientHome)
    {
        _logicClientHome = logicClientHome;
    }

    public LogicClientAvatar GetPlayerAvatar()
    {
        return _logicClientAvatar;
    }

    public void SetHomeOwnerAvatar(LogicClientAvatar logicClientAvatar)
    {
        _logicClientAvatar = logicClientAvatar;
    }
}
