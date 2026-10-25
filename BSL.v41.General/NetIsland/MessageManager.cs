using System.Net.Sockets;
using BSL.v41.General.Cloud;
using BSL.v41.General.GHelp;
using BSL.v41.General.NetIsland.LaserBattle;
using BSL.v41.General.Network;
using BSL.v41.General.Networking;
using BSL.v41.Logic.Database;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Database.Alliance;
using BSL.v41.Logic.Dynamic;
using BSL.v41.Logic.Environment.LaserCommand.Laser;
using BSL.v41.Logic.Environment.LaserListener;
using BSL.v41.Logic.Environment.LaserMessage;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_1;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_11;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_27;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_4;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Alliance;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Mode;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Player;
using BSL.v41.Logic.Environment.LaserNotification;
using BSL.v41.Logic.Environment.LaserNotification.Laser;
using BSL.v41.StaticService.Laser;
using BSL.v41.Supercell.Titan.CommonUtils;
using BSL.v41.Supercell.Titan.CommonUtils.Utils;
using BSL.v41.Titan.Mathematical.Data;
using BSL.v41.Titan.Graphic;
using BSL.v41.Titan.Diagnostics;
using BSL.v41.Titan.Utilities;
using BSL.v41.Tools.LaserCsv;
using BSL.v41.Tools.LaserCsv.Manufacturer.Laser;
using BSL.v41.Tools.LaserData;
using BSL.v41.Tools.LaserFactory;

namespace BSL.v41.General.NetIsland;

public class MessageManager(
    Socket? listener,
    Socket? userListener,
    Connection? connection,
    ServerConnection? serverConnection)
{
    private readonly LogicHomeMode _logicHomeMode = new();
    public readonly Dictionary<int, PiranhaMessage> ToProxyMessageDictionary = new();
    private int _joinedTime;
    private LanguageFactory _languageFactory = LanguageFactory.English;
    private long _lastKeepAliveReceivedTime = LogicTimeUtil.GetTimestamp();
    private Socket? _listener = listener;
    private int _ping = 100;
    private int _playerGeoEventSlot;
    private int _playerStatus;
    public int ToProxyMessageSeed = 300;

    public void SendMessage(PiranhaMessage? piranhaMessage)
    {
        if (piranhaMessage!.IsServerToClientMessage()) serverConnection!.GetMessaging()!.Send(piranhaMessage);
    }

    public bool IsActive()
    {
        if (_playerGeoEventSlot < 0 || LogicTimeUtil.GetTimestamp() - _lastKeepAliveReceivedTime < 6 * 5)
            return LogicTimeUtil.GetTimestamp() - _lastKeepAliveReceivedTime < 6 * 5;

        IdentifierListener.RemoveLogicGameListenerByAccountId(serverConnection!.GetAccountModel().GetAccountId());

        Saver.OwnMatchmakingManager.MatchmakingManagers[_playerGeoEventSlot]
            .RemovePlayerFromMassive(serverConnection!.GetAccountModel().GetAccountId(), true);
        {
            _playerGeoEventSlot = -1;
        }

        serverConnection!.GetAccountModel()
            .SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.PlayerStatus, 0);

        return LogicTimeUtil.GetTimestamp() - _lastKeepAliveReceivedTime < 6 * 5;
    }

    public int GetPing()
    {
        return _ping;
    }

    public int ConstructInstance(PiranhaMessage piranhaMessage)
    {
        ToProxyMessageSeed++;
        ToProxyMessageDictionary.TryAdd(ToProxyMessageSeed, piranhaMessage);

        return ToProxyMessageSeed;
    }

    public int ReceiveMessage(PiranhaMessage? piranhaMessage)
    {
        if (!piranhaMessage!.IsClientToServerMessage()) return 0;

        var traceAccount = serverConnection!.GetAccountModelOrNull();
        if (traceAccount != null)
            AccountTraceLogger.Log(traceAccount.GetAccountId(), "MESSAGE",
                $"received type={piranhaMessage.GetMessageType()} name={piranhaMessage.GetMessageTypeName()}");

        if (serverConnection!.GetAccountModel() == null!)
            return piranhaMessage.GetMessageType() switch
            {
                10101 => LoginMessageReceived((LoginMessage)piranhaMessage),
                10108 => KeepAliveMessageReceived((KeepAliveMessage)piranhaMessage),
                _ => 0
            };

        if (IsActive())
            switch (piranhaMessage.GetMessageType())
            {
                case 30000:
                    return AttributionMessageReceived((AttributionMessage)piranhaMessage);
            }

        KeepAliveMessageReceived(null!);

        return piranhaMessage.GetMessageType() switch
        {
            10101 => LoginMessageReceived((LoginMessage)piranhaMessage),
            10108 => KeepAliveMessageReceived((KeepAliveMessage)piranhaMessage),
            14103 => MatchmakeRequestMessageReceived((MatchmakeRequestMessage)piranhaMessage),
            10177 => ClientInfoMessageReceived((ClientInfoMessage)piranhaMessage),
            10111 => AccountIdentifiersMessageReceived((AccountIdentifiersMessage)piranhaMessage),
            10107 => ClientCapabilitiesMessageReceived((ClientCapabilitiesMessage)piranhaMessage),
            10110 => AnalyticEventMessageReceived((AnalyticEventMessage)piranhaMessage),
            14106 => CancelMatchmakingMessageReceived((CancelMatchmakingMessage)piranhaMessage),
            14166 => ChronosEventSeenMessageReceived((ChronosEventSeenMessage)piranhaMessage),
            14102 => EndClientTurnMessageReceived((EndClientTurnMessage)piranhaMessage),
            14105 => HomeLogicStoppedMessageReceived((HomeLogicStoppedMessage)piranhaMessage),
            14113 => GetPlayerProfileMessageReceived((GetPlayerProfileMessage)piranhaMessage),
            14114 => GetBattleLogMessageReceived((GetBattleLogMessage)piranhaMessage),
            14403 => GetLeaderboardMessageReceived((GetLeaderboardMessage)piranhaMessage),
            14350 => TeamCreateMessageReceived((TeamCreateMessage)piranhaMessage),
            14353 => TeamLeaveMessageReceived((TeamLeaveMessage)piranhaMessage),
            14361 => TeamMemberStatusMessageReceived((TeamMemberStatusMessage)piranhaMessage),
            14109 => GoHomeFromOfflinePractiseMessageReceived((GoHomeFromOfflinePractiseMessage)piranhaMessage),
            14110 => AskForBattleEndMessageReceived((AskForBattleEndMessage)piranhaMessage),
            14277 => GetSeasonRewardsMessageReceived((GetSeasonRewardsMessage)piranhaMessage),
            14101 => GoHomeMessageReceived((GoHomeMessage)piranhaMessage),
            14366 => PlayerStatusMessageReceived((PlayerStatusMessage)piranhaMessage),
            10555 => ClientInputMessageReceived((ClientInputMessage)piranhaMessage),
            10212 => ChangeAvatarNameMessageReceived((ChangeAvatarNameMessage)piranhaMessage),
            14301 => CreateAllianceMessageReceived((CreateAllianceMessage)piranhaMessage),
            14302 => AskForAllianceDataMessageReceived((AskForAllianceDataMessage)piranhaMessage),
            14316 => ChangeAllianceSettingsMessageReceived((ChangeAllianceSettingsMessage)piranhaMessage),
            14308 => LeaveAllianceMessageReceived((LeaveAllianceMessage)piranhaMessage),
            14315 => ChatToAllianceStreamMessageReceived((ChatToAllianceStreamMessage)piranhaMessage),
            14359 => TeamChatMessageReceived((TeamChatMessage)piranhaMessage),
            30000 => AttributionMessageReceived((AttributionMessage)piranhaMessage),
            14118 => SinglePlayerMatchRequestMessageReceived((SinglePlayerMatchRequestMessage)piranhaMessage),
            _ => 0
        };
    }

    private int GetPlayerProfileMessageReceived(GetPlayerProfileMessage piranhaMessage)
    {
        var requester = serverConnection!.GetAccountModel();
        var requestedAccountId = piranhaMessage.GetAccountId();
        var account = Databases.AccountDatabase.LoadAccount(requestedAccountId);

        // Some clients carry a non-zero high LogicLong word for a player
        // profile. AccountDatabase keys are the low-word local account IDs.
        // Preserve the exact request first, then accept only an unambiguous
        // low-word match; never fall back to the requester or another account.
        if (account is null && requestedAccountId > 0)
        {
            var lowAccountId = (long)(uint)requestedAccountId;
            if (lowAccountId != requestedAccountId)
            {
                var lowWordAccount = Databases.AccountDatabase.LoadAccount(lowAccountId);
                if (lowWordAccount is not null)
                {
                    account = lowWordAccount;
                    AccountTraceLogger.Log(requester.GetAccountId(), "PROFILE-RESOLVE",
                        $"requestedAccountId={requestedAccountId} resolvedAccountId={lowAccountId} " +
                        "reason=logic_long_low_word_match");
                }
            }
        }

        AccountTraceLogger.Log(requester.GetAccountId(), "PROFILE-REQUEST",
            $"requesterAccountId={requester.GetAccountId()} requestedAccountId={requestedAccountId} " +
            $"targetFound={account is not null}");

        if (account is null)
        {
            AccountTraceLogger.Log(requester.GetAccountId(), "PROFILE-REJECT",
                $"requesterAccountId={requester.GetAccountId()} requestedAccountId={requestedAccountId} " +
                "reason=target_account_not_found responseReason=404");
            SendMessage(new ServerErrorMessage(404));
            return 0;
        }

        AccountTraceLogger.Log(requester.GetAccountId(), "PROFILE-RESPONSE",
            $"requesterAccountId={requester.GetAccountId()} profileAccountId={account.GetAccountId()}");
        SendMessage(new PlayerProfileMessage(account));
        return 1;
    }

    private int GetLeaderboardMessageReceived(GetLeaderboardMessage piranhaMessage)
    {
        var account = serverConnection!.GetAccountModel();
        var accountId = account.GetAccountId();
        var leaderboardType = piranhaMessage.GetLeaderboardType();
        var brawlerGlobalId = piranhaMessage.GetBrawlerGlobalId();
        var isRegional = piranhaMessage.IsRegional();

        // Only the three V41 ranking types are meaningful. Unknown values are
        // safely normalized to the player leaderboard instead of producing an
        // invalid response packet.
        if (leaderboardType is < 0 or > 2)
            leaderboardType = 1;

        var accounts = Databases.AccountDatabase.SnapshotAccounts();
        var alliances = Databases.AllianceDatabase.SnapshotAlliances();
        AccountTraceLogger.Log(accountId, "LEADERBOARD-REQUEST",
            $"type=14403 leaderboardType={leaderboardType} brawlerGlobalId={brawlerGlobalId} " +
            $"isRegional={isRegional} decodeValid={piranhaMessage.IsDecodeValid()} " +
            $"accountCount={accounts.Count} allianceCount={alliances.Count}");

        SendMessage(new LeaderboardMessage(
            leaderboardType,
            brawlerGlobalId,
            isRegional,
            accounts,
            alliances,
            requesterAccountId: accountId));

        AccountTraceLogger.Log(accountId, "LEADERBOARD-RESPONSE",
            $"type=24403 leaderboardType={leaderboardType} source=account_database " +
            $"accountCount={accounts.Count} allianceCount={alliances.Count}");
        return 1;
    }

    private int GetBattleLogMessageReceived(GetBattleLogMessage piranhaMessage)
    {
        var accountId = serverConnection!.GetAccountModel().GetAccountId();
        AccountTraceLogger.Log(accountId, "BATTLE-DISABLED-REPLAY",
            "STATUS=INTENTIONAL_BATTLE_MAINTENANCE; BATTLE_LOG_AND_REPLAY_SERVICE_DISABLED; " +
            "REQUEST_HANDLED_WITHOUT_REPLAY_DATA; THIS_IS_NOT_A_PROTOCOL_ERROR.");
        return 1;
    }

    private int TeamCreateMessageReceived(TeamCreateMessage piranhaMessage)
    {
        var account = serverConnection!.GetAccountModel();
        var teamId = account.GetAccountId() * 1000 + 1;
        AccountTraceLogger.Log(account.GetAccountId(), "TEAM-CREATE",
            $"teamId={teamId} ownerAccountId={account.GetAccountId()} result=created");
        SendMessage(new TeamMessage(account, teamId));
        return 1;
    }

    private int TeamLeaveMessageReceived(TeamLeaveMessage piranhaMessage)
    {
        AccountTraceLogger.Log(serverConnection!.GetAccountModel().GetAccountId(), "TEAM-LEAVE",
            "result=left");
        SendMessage(new TeamLeftMessage());
        return 1;
    }

    private int ChatToAllianceStreamMessageReceived(ChatToAllianceStreamMessage piranhaMessage)
    {
        var account = serverConnection!.GetAccountModel();
        var allianceId = Convert.ToInt64(account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.AllianceId));
        if (allianceId <= 0 || string.IsNullOrWhiteSpace(piranhaMessage.GetMessage())) return 0;

        var alliance = Databases.AllianceDatabase.LoadAlliance(allianceId);
        var members = alliance?.GetFieldValueByAllianceStructureParameterFromAllianceModel(
            AllianceStructure.AllianceMemberEntryList) as Dictionary<long, AllianceMemberEntry>;
        if (members == null || !members.ContainsKey(account.GetAccountId()))
        {
            AccountTraceLogger.Log(account.GetAccountId(), "CHAT-REJECT",
                $"channel=alliance channelId={allianceId} reason=sender_not_member");
            return 0;
        }

        AccountTraceLogger.Log(account.GetAccountId(), "CHAT-RECEIVED",
            $"channel=alliance channelId={allianceId} messageChars={piranhaMessage.GetMessage().Length} " +
            "broadcast=not_available_in_current_v41_message_set");
        _ = PersistChatMessageAsync(account.GetAccountId(), "alliance", allianceId,
            piranhaMessage.GetMessage());
        return 1;
    }

    private int TeamChatMessageReceived(TeamChatMessage piranhaMessage)
    {
        var account = serverConnection!.GetAccountModel();
        if (string.IsNullOrWhiteSpace(piranhaMessage.GetMessage())) return 0;

        // The current emulator exposes one team per account through TeamCreate.
        // Use that same stable channel identifier until full team membership is
        // implemented; the sender is always taken from the authenticated session.
        var teamId = account.GetAccountId() * 1000 + 1;
        AccountTraceLogger.Log(account.GetAccountId(), "CHAT-RECEIVED",
            $"channel=team channelId={teamId} messageChars={piranhaMessage.GetMessage().Length}");
        _ = PersistChatMessageAsync(account.GetAccountId(), "team", teamId,
            piranhaMessage.GetMessage());
        return 1;
    }

    private int TeamMemberStatusMessageReceived(TeamMemberStatusMessage piranhaMessage)
    {
        AccountTraceLogger.Log(serverConnection!.GetAccountModel().GetAccountId(), "TEAM-STATUS",
            "status update received; no persistent room membership state is implemented");
        return 1;
    }

    private async Task PersistChatMessageAsync(long accountId, string channelType, long channelId,
        string message)
    {
        var persisted = await MySqlPersistence.InsertChatMessageAsync(accountId, channelType, channelId, message);
        AccountTraceLogger.Log(accountId, "CHAT-PERSIST",
            $"channel={channelType} channelId={channelId} messageChars={message.Length} persisted={persisted}");
    }

    private int SinglePlayerMatchRequestMessageReceived(SinglePlayerMatchRequestMessage piranhaMessage)
    {
        RejectBattleRequest();
        return 1;
    }

    private int GetSeasonRewardsMessageReceived(GetSeasonRewardsMessage piranhaMessage)
    {
        var account = serverConnection!.GetAccountModel();
        AccountTraceLogger.Log(account.GetAccountId(), "BRAWL-PASS-MAINTENANCE",
            "BRAWL PASS GİRİŞİ KASITLI OLARAK DEVRE DIŞI; ÖDÜL KOMUTU ÇALIŞTIRILMADI; " +
            "ANA MENÜYE DÖNÜŞ GÖNDERİLİYOR; İSTEMCİ MESAJI=ŞUANDA BAKIM MOLADAYIZ.");
        SendMessage(new OwnHomeDataMessage(account, false));
        SendBrawlPassMaintenanceNotice();

        return 1;
    }

    private void SendBrawlPassMaintenanceNotice()
    {
        var accountId = serverConnection!.GetAccountModel().GetAccountId();
        AccountTraceLogger.Log(accountId, "BRAWL-PASS-MAINTENANCE-NOTICE",
            "BRAWL PASS BAKIM MESAJI GÖNDERİLDİ; BU BİR PROTOKOL HATASI DEĞİL; " +
            "BU DAVRANIŞ KASITLI OLARAK UYGULANDI; MESAJ=ŞUANDA BAKIM MOLADAYIZ.");
        var notice = new AvailableServerCommandMessage();
        notice.SetServerCommand(new LogicAddNotificationCommand(
            new FloaterTextNotification("Şuanda bakım moladayız.")));
        SendMessage(notice);
    }

    private int AttributionMessageReceived(AttributionMessage piranhaMessage)
    {
        if (!piranhaMessage.StringReferenceOpenList[9].Equals("utm_source=google-play&utm_medium=organic")) return 0;
        if (!piranhaMessage.StringReferenceOpenList[3]
                .Equals($"{Fingerprint.GetMajorVersion()}.{Fingerprint.GetMinorVersion()}")) return 0;
        if (!piranhaMessage.StringReferenceOpenList[0].Contains("Mozilla")) return 0;
        if (!piranhaMessage.StringReferenceOpenList[0].Contains("Linux")) return 0;
        if (!piranhaMessage.StringReferenceOpenList[0].Contains("Android")) return 0;

        serverConnection!.GetAccountModel().SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.UserIsAuthed, true); 

        return 1;
    }

    private int LeaveAllianceMessageReceived(LeaveAllianceMessage piranhaMessage)
    {
        AllianceResponseMessage? allianceResponseMessage;

        var v1 = (long)serverConnection!.GetAccountModel()
            .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId);

        if (v1 < 1)
        {
            allianceResponseMessage = new AllianceResponseMessage();
            {
                allianceResponseMessage.IsResponse(94);
            }

            SendMessage(allianceResponseMessage);
            return 0;
        }

        var v2 = Databases.AllianceDatabase.LoadAlliance(v1);
        {
            var d1 =
                (Dictionary<long, AllianceMemberEntry>)v2.GetFieldValueByAllianceStructureParameterFromAllianceModel(
                    AllianceStructure.AllianceMemberEntryList);
            {
                d1.Remove(serverConnection!.GetAccountModel().GetAccountId());
            }

            v2.SetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure.AllianceMemberEntryList,
                d1);
        }

        serverConnection.GetAccountModel()
            .SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId, (long)0);

        allianceResponseMessage = new AllianceResponseMessage();
        {
            allianceResponseMessage.IsResponse(80);
        }

        SendMessage(allianceResponseMessage);
        SendMessage(new MyAllianceMessage());

        var allianceModel = Databases.AllianceDatabase.LoadAlliance(97);
        var allianceMemberEntry = new AllianceMemberEntry
        {
            AccountId = serverConnection.GetAccountModel().GetAccountId(),
            AllianceRoleHelperTable = AllianceRoleHelperTable.Member
        };

        serverConnection!.GetAccountModel()
            .SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId,
                allianceModel.GetAllianceId());
        serverConnection.GetAccountModel()
            .SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceMemberEntry,
                allianceMemberEntry);

        var c = (Dictionary<long, AllianceMemberEntry>)
            allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(
                AllianceStructure.AllianceMemberEntryList);
        c.Add(serverConnection.GetAccountModel().GetAccountId(), allianceMemberEntry);

        allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
            AllianceStructure.AllianceMemberEntryList, c);

        ToSendAutomizeHelper.SendAllianceStatus(
            Databases.AllianceDatabase.LoadAlliance(
                Convert.ToInt64(serverConnection!.GetAccountModel()
                    .GetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.AllianceId))), serverConnection!.GetAccountModel().GetAccountId());
        return 1;
    }

    private int ChangeAllianceSettingsMessageReceived(ChangeAllianceSettingsMessage piranhaMessage)
    {
        AllianceResponseMessage? allianceResponseMessage;

        if (Databases.AllianceDatabase.LoadAlliance((long)serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId)) ==
            null!)
        {
            allianceResponseMessage = new AllianceResponseMessage();
            {
                allianceResponseMessage.IsResponse(93);
            }

            SendMessage(allianceResponseMessage);
            return 0;
        }

        if (!LogicDataTables.GetAllianceRoleByName(((AllianceMemberEntry)serverConnection!.GetAccountModel()
                    .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceMemberEntry))
                .AllianceRoleHelperTable.GetCsvName()).GetCanChangeAllianceSettings())
        {
            allianceResponseMessage = new AllianceResponseMessage();
            {
                allianceResponseMessage.IsResponse(95);
            }

            SendMessage(allianceResponseMessage);
            return 0;
        }

        if (!Helper.GetIsAdequateString(piranhaMessage.GetAllianceDescription()))
        {
            allianceResponseMessage = new AllianceResponseMessage();
            {
                allianceResponseMessage.IsResponse(23);
            }

            SendMessage(allianceResponseMessage);
            return 1;
        }

        var allianceModel = Databases.AllianceDatabase.LoadAlliance((long)serverConnection!.GetAccountModel()
            .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId));

        allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
            AllianceStructure.AllianceDescription, piranhaMessage.GetAllianceDescription());
        allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
            AllianceStructure.AllianceBadgeData, piranhaMessage.GetAllianceBadgeData());
        allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
            AllianceStructure.AllianceRegion, piranhaMessage.GetAllianceRegion());
        allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
            AllianceStructure.AllianceType, piranhaMessage.GetAllianceType());
        allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
            AllianceStructure.AllianceRequiredScore, piranhaMessage.GetRequiredScore());
        allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
            AllianceStructure.AllianceFamilyType, piranhaMessage.GetIsFamilyType());

        allianceResponseMessage = new AllianceResponseMessage();
        {
            allianceResponseMessage.IsResponse(10);
        }

        SendMessage(allianceResponseMessage);

        ToSendAutomizeHelper.SendMyAlliance(this,
            Databases.AllianceDatabase.LoadAlliance((long)serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId)),
            serverConnection!.GetAccountModel(), true);

        ToSendAutomizeHelper.SendAllianceStatus(
            Databases.AllianceDatabase.LoadAlliance(
                Convert.ToInt64(serverConnection!.GetAccountModel()
                    .GetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.AllianceId))), serverConnection!.GetAccountModel().GetAccountId());

        return 1;
    }

    private int AskForAllianceDataMessageReceived(AskForAllianceDataMessage piranhaMessage)
    {
        if (Databases.AllianceDatabase.LoadAlliance(piranhaMessage.GetAllianceId()) == null!)
            return 0;

        ToSendAutomizeHelper.SendAllianceStatus(
            Databases.AllianceDatabase.LoadAlliance(
                Convert.ToInt64(serverConnection!.GetAccountModel()
                    .GetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.AllianceId))), serverConnection!.GetAccountModel().GetAccountId());

        ToSendAutomizeHelper.SendAllianceData(this,
            Databases.AllianceDatabase.LoadAlliance(piranhaMessage.GetAllianceId()),
            (long)serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId),
            myAccountId: serverConnection.GetAccountModel().GetAccountId());

        ToSendAutomizeHelper.SendAllianceStatus(
            Databases.AllianceDatabase.LoadAlliance(
                Convert.ToInt64(serverConnection!.GetAccountModel()
                    .GetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.AllianceId))), serverConnection!.GetAccountModel().GetAccountId());

        return 1;
    }

    private int CreateAllianceMessageReceived(CreateAllianceMessage piranhaMessage)
    {
        AllianceResponseMessage? allianceResponseMessage;

        if (Convert.ToInt32(serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId)) > 0)
        {
            allianceResponseMessage = new AllianceResponseMessage();
            {
                allianceResponseMessage.IsResponse(21);
            }

            SendMessage(allianceResponseMessage);
            return 0;
        }

        if (Convert.ToInt32(serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(
                    AccountStructure.LastCreateAllianceTime)) > 1 * 60 * 60 &&
            LogicTimeUtil.GetTimestamp() - Convert.ToInt32(serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(
                    AccountStructure.LastCreateAllianceTime)) < 1 * 60 * 60)
        {
            allianceResponseMessage = new AllianceResponseMessage();
            {
                allianceResponseMessage.IsResponse(21);
            }

            SendMessage(allianceResponseMessage);
            return 0;
        }

        if (Helper.GetIsAdequateString(piranhaMessage.AllianceName) &&
            Helper.GetIsCorrectName(piranhaMessage.AllianceName))
        {
            if (Helper.GetIsAdequateString(piranhaMessage.AllianceDescription))
            {
                var allianceMemberEntry = new AllianceMemberEntry
                {
                    AccountId = serverConnection.GetAccountModel().GetAccountId(),
                    AllianceRoleHelperTable = AllianceRoleHelperTable.Leader
                };

                var allianceMemberList = new Dictionary<long, AllianceMemberEntry>();
                {
                    allianceMemberList.Add(serverConnection.GetAccountModel().GetAccountId(), allianceMemberEntry);
                }

                var allianceModel = Databases.AllianceDatabase.CreateAlliance(new AllianceModel());
                {
                    allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
                        AllianceStructure.AllianceCreatedDate,
                        $"{DateTime.Now:yyyy/MM/dd-(dddd) HH:mm:ss}");
                    allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
                        AllianceStructure.AllianceName, piranhaMessage.AllianceName);
                    allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
                        AllianceStructure.AllianceDescription, piranhaMessage.AllianceDescription);
                    allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
                        AllianceStructure.AllianceBadgeData, piranhaMessage.GetAllianceBadgeData());
                    allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
                        AllianceStructure.AllianceRegion, piranhaMessage.AllianceRegion);
                    allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
                        AllianceStructure.AllianceType, piranhaMessage.GetAllianceType());
                    allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
                        AllianceStructure.AllianceRequiredScore, piranhaMessage.GetRequiredScore());
                    allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
                        AllianceStructure.AllianceFamilyType, piranhaMessage.IsFamilyType);
                    allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
                        AllianceStructure.AllianceMemberEntryList, allianceMemberList);
                    allianceModel.SetFieldValueByAllianceStructureParameterFromAllianceModel(
                        AllianceStructure.AllianceLanguage, 1000000);
                }

                serverConnection!.GetAccountModel()
                    .SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId,
                        allianceModel.GetAllianceId());
                serverConnection.GetAccountModel()
                    .SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceMemberEntry,
                        allianceMemberEntry);
                serverConnection.GetAccountModel()
                    .SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.LastCreateAllianceTime,
                        LogicTimeUtil.GetTimestamp());

                var allianceHeaderEntry = new AllianceHeaderEntry();
                {
                    allianceHeaderEntry.SetAllianceId(allianceModel.GetAllianceId());
                    allianceHeaderEntry.SetAllianceName(allianceModel
                        .GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure.AllianceName)
                        .ToString()!);
                    allianceHeaderEntry.SetScore(allianceMemberList.Sum(member =>
                        Convert.ToInt32(Databases.AccountDatabase.LoadAccount(member.Value.AccountId)
                            .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.Trophies))));
                    allianceHeaderEntry.SetAllianceType(Convert.ToInt32(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceType)));
                    allianceHeaderEntry.SetAllianceBadgeData(Convert.ToInt32(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceBadgeData)));
                    allianceHeaderEntry.SetRequiredScore(Convert.ToInt32(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceRequiredScore)));
                    allianceHeaderEntry.SetPreferredLanguage(Convert.ToInt32(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceLanguage)));
                    allianceHeaderEntry.SetAllianceRegionData(Convert.ToInt32(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceRegion)));
                    allianceHeaderEntry.SetNumberOfMembers(allianceMemberList.Count);
                    allianceHeaderEntry.SetIsFamilyType(
                        (bool)allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceFamilyType));
                }

                var allianceFullEntry = new AllianceFullEntry();
                {
                    allianceFullEntry.SetAllianceDescription(allianceModel
                        .GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceDescription).ToString()!);
                    allianceFullEntry.SetAllianceHeaderEntry(allianceHeaderEntry);
                    allianceFullEntry.SetAllianceMembers(allianceMemberList);
                }

                var allianceDataMessage = new AllianceDataMessage();
                {
                    allianceDataMessage.IsMyAlliance = true;
                    allianceDataMessage.SetAllianceFullEntry(allianceFullEntry);
                }

                var myAllianceMessage = new MyAllianceMessage();
                {
                    myAllianceMessage.OnlineMembers = allianceMemberList.Count(keyValuePair =>
                        IdentifierListener.GetV2LogicGameListenerByAccountId(keyValuePair.Key));
                    myAllianceMessage.AllianceRole = allianceMemberEntry.AllianceRoleHelperTable;
                    myAllianceMessage.AllianceHeaderEntry = allianceHeaderEntry;
                }

                allianceResponseMessage = new AllianceResponseMessage();
                {
                    allianceResponseMessage.IsResponse(20);
                }

                SendMessage(allianceResponseMessage);
                SendMessage(myAllianceMessage);
                SendMessage(allianceDataMessage);
                return 1;
            }

            allianceResponseMessage = new AllianceResponseMessage();
            {
                allianceResponseMessage.IsResponse(23);
            }

            SendMessage(allianceResponseMessage);
            return 1;
        }

        allianceResponseMessage = new AllianceResponseMessage();
        {
            allianceResponseMessage.IsResponse(piranhaMessage.AllianceName.Length < 3 ? 24 : 22);
        }

        SendMessage(allianceResponseMessage);
        return 1;
    }

    private int ChangeAvatarNameMessageReceived(ChangeAvatarNameMessage piranhaMessage)
    {
        if (piranhaMessage.GetNameSetByUser()) return 0;
        
        if (Helper.GetIsAdequateString(piranhaMessage.GetName()) &&
            Helper.GetIsCorrectName(piranhaMessage.GetName()))
        {
            serverConnection!.GetAccountModel().SetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.NameSetByUser,
                !piranhaMessage.GetNameSetByUser());

            serverConnection!.GetAccountModel().SetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.AvatarName,
                piranhaMessage.GetName());

            var availableServerCommandMessage = new AvailableServerCommandMessage();
            {
                availableServerCommandMessage.SetServerCommand(new LogicChangeAvatarNameCommand(
                    piranhaMessage.GetName(), 0));
            }

            SendMessage(availableServerCommandMessage);
        }
        else
        {
            var avatarNameChangeFailed = new AvatarNameChangeFailedMessage();
            {
                avatarNameChangeFailed.SetReason(piranhaMessage.GetName().Length < 3 ? 2 :
                    piranhaMessage.GetName().Length >= 15 ? 1 : 0);
            }

            SendMessage(avatarNameChangeFailed);
        }

        return 1;
    }

    private int EndClientTurnMessageReceived(EndClientTurnMessage piranhaMessage)
    {
        var accountId = serverConnection!.GetAccountModel().GetAccountId();
        var commands = piranhaMessage.GetCommands().ToList();
        AccountTraceLogger.Log(accountId, "END-TURN-RECEIVED",
            $"tick={piranhaMessage.GetTick()} checksum={piranhaMessage.GetChecksum()} " +
            $"commandCount={commands.Count} commandTypes={string.Join(',', commands.Select(command => command.GetCommandType()))} " +
            $"decodedBytes={piranhaMessage.GetDecodedByteOffset()} payloadBytes={piranhaMessage.GetPayloadLength()} " +
            $"payloadHex={piranhaMessage.GetPayloadHex()}");
        _logicHomeMode.EndClientTurnReceived(piranhaMessage.GetTick(), piranhaMessage.GetChecksum(),
            commands);

        return 1;
    }

    private int ChronosEventSeenMessageReceived(ChronosEventSeenMessage piranhaMessage)
    {
        // ignored.
        return 0;
    }

    private int GoHomeFromOfflinePractiseMessageReceived(GoHomeFromOfflinePractiseMessage piranhaMessage)
    {
        // The local proxy transport is not part of the live V41 deployment
        // (localhost:1002 is intentionally absent). Send the home refresh on
        // the authenticated game socket so the client can leave offline
        // practice instead of waiting forever for a queued proxy message.
        ApplyTutorialBypass(serverConnection!.GetAccountModel());
        SendMessage(new OwnHomeDataMessage(serverConnection.GetAccountModel(), false));
        return 1;
    }

    private int GoHomeMessageReceived(GoHomeMessage piranhaMessage)
    {
        SendMessage(new OwnHomeDataMessage(serverConnection!.GetAccountModel(), false));
        return 1;
    }

    private int HomeLogicStoppedMessageReceived(HomeLogicStoppedMessage piranhaMessage)
    {
        var accountId = serverConnection!.GetAccountModel().GetAccountId();
        _logicHomeMode.MarkBoxHomeRefreshAcknowledged();
        AccountTraceLogger.Log(accountId, "HOME-LOGIC-STOPPED",
            "CLIENT_ACK=14105; SENDING_FRESH_HOME_DATA_AFTER_SAFE_STATE_TRANSITION; " +
            "BRAWL_PASS_CLAIM_STATE_REFRESH=SERVER_SNAPSHOT.");
        SendMessage(new OwnHomeDataMessage(serverConnection.GetAccountModel(), false));
        return 1;
    }

    private int AskForBattleEndMessageReceived(AskForBattleEndMessage piranhaMessage)
    {
        // This is the end of the client-side tutorial/practice battle. No
        // server battle is running, so return the authenticated account home.
        ApplyTutorialBypass(serverConnection!.GetAccountModel());
        SendMessage(new OwnHomeDataMessage(serverConnection.GetAccountModel(), false));
        return 1;
    }

    private int PlayerStatusMessageReceived(PlayerStatusMessage piranhaMessage)
    {
        _playerStatus = piranhaMessage.GetPlayerStatus();
        
        return 1;
    }

    private int ClientInputMessageReceived(ClientInputMessage piranhaMessage)
    {
        serverConnection!.Disconnect();
        return 0;
    }

    private int CancelMatchmakingMessageReceived(CancelMatchmakingMessage piranhaMessage)
    {
        SendMessage(new MatchMakingCancelledMessage());

        if (_playerGeoEventSlot < 0) return 0;

        Saver.OwnMatchmakingManager.MatchmakingManagers[_playerGeoEventSlot]
            .RemovePlayerFromMassive(serverConnection!.GetAccountModel().GetAccountId());
        {
            _playerGeoEventSlot = -1;
        }

        return 1;
    }

    private int AnalyticEventMessageReceived(AnalyticEventMessage piranhaMessage)
    {
        if (true)
        {
            var c2 = new AvailableServerCommandMessage();
            {
                c2.SetServerCommand(new LogicAddNotificationCommand(new FloaterTextNotification(
                    piranhaMessage.GetAnalyticEvent().GetEvent() + $" ({LogicTimeUtil.GetTimestamp()}): " +
                    piranhaMessage.GetAnalyticEvent().GetEventInfo())));
            }
        }

        return 1;
    }

    private int ClientCapabilitiesMessageReceived(ClientCapabilitiesMessage piranhaMessage)
    {
        _ping = piranhaMessage.GetPing();
        return 1;
    }

    private int AccountIdentifiersMessageReceived(AccountIdentifiersMessage piranhaMessage)
    {
        return 0;
    }

    private int ClientInfoMessageReceived(ClientInfoMessage piranhaMessage)
    {
        var accountId = serverConnection!.GetAccountModel().GetAccountId();
        if (!ServerFeatureFlags.BattlesEnabled)
        {
            AccountTraceLogger.Log(accountId, "BATTLE-DISABLED",
                "Client UDP info request ignored while battles are disabled.");
            return 1;
        }

        if (!InteractiveModule.UdpSessionIdsMassive.TryGetValue(accountId, out var sessionId)) return 0;
        if (!InteractiveModule.LogicBattleModeServersMassive!.TryGetValue(sessionId, out var battleServer)) return 0;

        ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Udp,
            $"Client UDP info requested: account={accountId}; session={sessionId}; " +
            $"port={battleServer.GetInitialRandomSeed()}; domain={Saver.UdpInfoDomain}.");
        SendMessage(new UdpConnectionInfoMessage(battleServer.GetInitialRandomSeed(),
            Saver.UdpInfoDomain,
            sessionId));

        return 1;
    }

    private int MatchmakeRequestMessageReceived(MatchmakeRequestMessage piranhaMessage)
    {
        var accountId = serverConnection!.GetAccountModel().GetAccountId();
        AccountTraceLogger.Log(accountId, "BATTLE-MAINTENANCE",
            "OYNA DÜĞMESİNE BASILDI; SAVAŞ SİSTEMİ KASITLI OLARAK DEVRE DIŞI; " +
            "EŞLEŞTİRME BAŞLATILMADI; İSTEMCİYE GÖSTERİLEN MESAJ=ŞUANDA BAKIM MOLASIDAYIZ.");
        RejectBattleRequest();
        return 1;
    }

    private void RejectBattleRequest()
    {
        CancelActiveMatchmaking();
        SendMessage(new MatchMakingCancelledMessage());
        SendBattleDisabledNotice();
    }

    private void SendBattleDisabledNotice()
    {
        var accountId = serverConnection!.GetAccountModel().GetAccountId();
        AccountTraceLogger.Log(accountId, "BATTLE-NOTICE-SENT",
            "BAKIM MESAJI GÖNDERİLDİ; MESAJ=ŞUANDA BAKIM MOLASIDAYIZ.; " +
            "BU BİR PROTOKOL HATASI DEĞİL, KASITLI SUNUCU DAVRANIŞIDIR.");
        var notice = new AvailableServerCommandMessage();
        notice.SetServerCommand(new LogicAddNotificationCommand(
            new FloaterTextNotification("Şuanda bakım moladayız.")));
        SendMessage(notice);
    }

    private void CancelActiveMatchmaking()
    {
        if (_playerGeoEventSlot < 0) return;

        if (Saver.OwnMatchmakingManager.MatchmakingManagers.TryGetValue(_playerGeoEventSlot,
                out var matchmakingManager))
            matchmakingManager.RemovePlayerFromMassive(serverConnection!.GetAccountModel().GetAccountId());

        _playerGeoEventSlot = -1;
        serverConnection!.GetAccountModel().SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.PlayerStatus, 0);
    }

    private int KeepAliveMessageReceived(KeepAliveMessage piranhaMessage)
    {
        if (piranhaMessage != null!) _lastKeepAliveReceivedTime = LogicTimeUtil.GetTimestamp();
        else _lastKeepAliveReceivedTime = LogicTimeUtil.GetTimestamp() - 5;

        if (piranhaMessage == null!) return 1;

        SendMessage(new KeepAliveServerMessage());

        serverConnection!.GetAccountModel().SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.LastOnlineTime,
            LogicTimeUtil.GetTimestamp());

        serverConnection!.GetAccountModel().SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.PlayTimeSeconds,
            Convert.ToInt32(serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.PlayTimeSeconds)) +
            6 * 4);

        return 1;
    }

    private int LoginMessageReceived(LoginMessage piranhaMessage)
    {
        if (ServerUpdatePolicy.IsOutdated(piranhaMessage))
        {
            var updateMessage = new LoginFailedMessage();
            {
                updateMessage.SetErrorCode(Convert.ToInt32(ErrorCodeHelperTable.CustomMessage));
                updateMessage.SetContentUrl(string.Empty);
                updateMessage.SetRedirectDomain(string.Empty);
                updateMessage.SetUpdateUrl(ServerUpdatePolicy.GetUpdateUrl());
                updateMessage.SetRemoveResourceFingerprintData(string.Empty);
                updateMessage.SetSecondsUntilMaintenanceEnd(-1);
                updateMessage.SetShowContactSupportForBan(false);
                updateMessage.SetReason(ServerUpdatePolicy.GetUpdateReason());
            }

            ConsoleLogger.WriteTextWithPrefix(ConsoleLogger.Prefixes.Tcp,
                $"Client update required. Received version: {piranhaMessage.GetClientMajorVersion()}." +
                $"{piranhaMessage.GetClientBuild()}.{piranhaMessage.GetClientMinor()}; " +
                $"required: {ServerUpdatePolicy.GetRequiredVersionString()}.");
            SendMessage(updateMessage);
            return 0;
        }

        var accountModel = new AccountModel();
        long? switchedTargetAccountId = null;
        var authenticatedAccountId = 0L;
        var switchFound = false;
        var switchDecision = "not_evaluated";
        var plIns = GlobalId.GetInstanceId(piranhaMessage.GetPreferredLanguage());

        int accountLvl;
        {
            accountLvl = piranhaMessage.GetAccountId() == 0
                ? 1 // need to create
                : 2; // account created (local usage)

            if (accountLvl == 2)
                try
                {
                    var existingAccount = Databases.AccountDatabase.LoadAccount(piranhaMessage.GetAccountId());
                    if (existingAccount == null!)
                    {
                        accountLvl = 3; // clear app data
                    }
                    else if (BannedAccountStore.IsBanned(piranhaMessage.GetAccountId()))
                    {
                        var bannedMessage = new LoginFailedMessage();
                        {
                            bannedMessage.SetErrorCode(Convert.ToInt32(ErrorCodeHelperTable.CustomMessage));
                            bannedMessage.SetContentUrl(string.Empty);
                            bannedMessage.SetRedirectDomain(string.Empty);
                            bannedMessage.SetUpdateUrl(string.Empty);
                            bannedMessage.SetRemoveResourceFingerprintData(string.Empty);
                            bannedMessage.SetSecondsUntilMaintenanceEnd(-1);
                            bannedMessage.SetShowContactSupportForBan(true);
                            bannedMessage.SetReason(BannedAccountStore.GetReason(piranhaMessage.GetAccountId()));
                        }

                        SendMessage(bannedMessage);
                        switchDecision = "banned";
                        return 0;
                    }
                    else if (piranhaMessage.GetAccountId() == 3 &&
                             !LocalIdentityStore.IsBoundToAccount(3))
                    {
                        switchDecision = "target_redis_binding_unavailable";
                        accountLvl = 3; // fail closed if the #3 Redis binding is unavailable
                    }
                    else if (piranhaMessage.GetAccountId() == 3 &&
                             TestCodeStore.TryConsume(3,
                                 piranhaMessage.GetPassToken(),
                                 piranhaMessage.GetDevice(),
                                 GetRemoteAddress()))
                    {
                        authenticatedAccountId = 3;
                        switchDecision = "test_code_accepted";
                        accountLvl = 4;
                    }
                    else
                    {
                        var passTokenMatches = piranhaMessage.GetPassToken() ==
                                                existingAccount
                                                    .GetFieldValueByAccountStructureParameterFromAccountModel(
                                                        AccountStructure.PassToken).ToString();
                        if (!passTokenMatches)
                        {
                            switchDecision = "pass_token_mismatch";
                            accountLvl = 3;
                        }
                        else
                        {
                            authenticatedAccountId = piranhaMessage.GetAccountId();
                            if (AccountSwitchStore.TryConsume(
                                    piranhaMessage.GetAccountId(),
                                    piranhaMessage.GetDevice(),
                                    GetRemoteAddress(),
                                    out var targetAccountId,
                                    out switchFound,
                                    out switchDecision))
                            {
                                switchedTargetAccountId = targetAccountId;
                                accountLvl = 5;
                            }
                            else
                                accountLvl = 4;
                        }
                    }
                }
                catch (Exception exception)
                {
                    switchDecision = $"login_exception_{exception.GetType().Name}";
                    accountLvl = 3; // clear app data
                }
        }

        switch (accountLvl)
        {
            case 1:
            {
                var notificationFactory = new NotificationFactory();
                {
                    notificationFactory.AccountModel = accountModel;
                }

                accountModel = Databases.AccountDatabase.CreateAccount(accountModel);
                {
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.HomeId,
                        Helper.GenerateRandomIntForBetween(1, 10));
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.PassToken,
                        Helper.GenerateToken(accountModel.GetAccountId()));
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AvatarName,
                        "Brawler");
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.TrophyRoadProgress,
                        1);
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.PlayerThumbnailGlobalId,
                        GlobalId.CreateGlobalId(CsvHelperTable.PlayerThumbnails.GetId(), 0));
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.NameColorGlobalId,
                        GlobalId.CreateGlobalId(CsvHelperTable.NameColors.GetId(), 0));
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.SessionCount,
                        Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                            AccountStructure
                                .SessionCount)) + 1);
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.LogicOfferBundleList,
                        CreateStarterOffers());
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.AccountCreatedDate,
                        $"{DateTime.Now:yyyy/MM/dd-(dddd) HH:mm:ss}");
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.StartPlayTimeSeconds,
                        LogicTimeUtil.GetTimestamp());
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.PlayTimeSeconds, 0);
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.HomeBrawlerGlobalId, GlobalId.CreateGlobalId(
                            CsvHelperTable.Characters.GetId(), 0));
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.SupportedCreator,
                        "BSL-V41");
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.NotificationFactoryIntraSignedId,
                        accountModel.GetAccountId() * 10);
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.IntValueEntryX, 
                        new Dictionary<int, IntValueEntry>
                        {
                            { 1, new IntValueEntry(1, 100) }, // starting gold
                            { 15, new IntValueEntry(15, 1) },
                            { 16, new IntValueEntry(16, 0) } // starting gems
                        });
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.IntValueEntryY,
                        new Dictionary<int, IntValueEntry>
                        {
                            {
                                1, new IntValueEntry(1,
                                    GlobalId.CreateGlobalId(CsvHelperTable.Themes.GetId(), 38)) 
                            }
                        });
                }

                Databases.NotificationIntraSignedDatabase.AddAppendix(Convert.ToInt64(
                    accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.NotificationFactoryIntraSignedId)), notificationFactory);
                break;
            }
            case 3:
            {
                try
                {
                    if (piranhaMessage.GetAccountId() < 0)
                    {
                        userListener!.Close();
                    }
                    else
                    {
                        var loginFailedMessage = new LoginFailedMessage();
                        {
                            loginFailedMessage.SetErrorCode(Convert.ToInt32(ErrorCodeHelperTable.CustomMessage));
                            loginFailedMessage.SetContentUrl("");
                            loginFailedMessage.SetRedirectDomain("");
                            loginFailedMessage.SetUpdateUrl("");
                            loginFailedMessage.SetRemoveResourceFingerprintData("");
                            loginFailedMessage.SetSecondsUntilMaintenanceEnd(-1);
                            loginFailedMessage.SetShowContactSupportForBan(false);
                            loginFailedMessage.SetReason(
                                _languageFactory.TranslateTo("Проведите очистку данных приложения, пожалуйста!"));
                        }

                        SendMessage(loginFailedMessage);
                    }
                }
                catch
                {
                    // ignored.
                }

                break;
            }
            case 4:
            {
                accountModel = Databases.AccountDatabase.LoadAccount(piranhaMessage.GetAccountId());
                EnsureStarterOffers(accountModel);
                if (MySqlPersistence.TryRestoreOfferPurchases(accountModel))
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.LogicOfferBundleList,
                        accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                            AccountStructure.LogicOfferBundleList));
                {
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.PlayTimeSeconds,
                        Convert.ToInt32(
                            accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure
                                .PlayTimeSeconds)) + 10);
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.SessionCount,
                        Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                            AccountStructure
                                .SessionCount)) + 1);
                }
                break;
            }
            case 5:
            {
                accountModel = Databases.AccountDatabase.LoadAccount(switchedTargetAccountId!.Value);
                EnsureStarterOffers(accountModel);
                if (MySqlPersistence.TryRestoreOfferPurchases(accountModel))
                    accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.LogicOfferBundleList,
                        accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                            AccountStructure.LogicOfferBundleList));
                accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                    AccountStructure.SessionCount,
                    Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.SessionCount)) + 1);
                break;
            }
        }

        serverConnection!.SetLoginDevice(piranhaMessage.GetDevice());
        ApplyTutorialBypass(accountModel);
        serverConnection!.SetAccountModel(accountModel);
        AccountTraceLogger.Log(accountModel.GetAccountId(), "LOGIN",
            $"login accepted requestedAccount={piranhaMessage.GetAccountId()} sessionCount=" +
            accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.SessionCount));
        if (AdminAccess.IsAdmin(accountModel.GetAccountId()))
            accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.SupportedCreator, "BSL-ADMIN");
        InteractiveModule.RegisterPlayerSession(accountModel.GetAccountId(), serverConnection);
        var allianceModel = Databases.AllianceDatabase.LoadAlliance(
            (long)accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.AllianceId));

        var loginOkMessage = new LoginOkMessage();
        {
            loginOkMessage.SetAccountId(Convert.ToInt64(accountModel
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AccountId)));
            loginOkMessage.SetHomeId(Convert.ToInt64(accountModel
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.HomeId)));
            loginOkMessage.SetPassToken(accountModel
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.PassToken).ToString()!);
            loginOkMessage.SetFacebookId("EE0");
            loginOkMessage.SetGamecenterId("Z");
            loginOkMessage.SetServerMajorVersion(Fingerprint.GetMinorVersion());
            loginOkMessage.SetServerBuild(Fingerprint.GetBuildVersion());
            loginOkMessage.SetContentVersion(Fingerprint.GetMajorVersion());
            loginOkMessage.SetServerEnvironment(GlobalStaticCloud.PragmaAndServerEnvironment!);
            loginOkMessage.SetSessionCount(Convert.ToInt32(
                accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.SessionCount)));
            loginOkMessage.SetPlayTimeSeconds(Convert.ToInt32(
                accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure
                    .PlayTimeSeconds)));
            loginOkMessage.SetDaysSinceStartedPlaying(
                Convert.ToInt32(
                    accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure
                        .PlayTimeSeconds)) / 60 / 60 / 24);
            loginOkMessage.SetServerTime($"{DateTime.Now:yyyy/MM/dd-(dddd) HH:mm:ss}");
            loginOkMessage.SetAccountCreatedDate(accountModel
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AccountCreatedDate)
                .ToString()!);
            loginOkMessage.SetStartupCooldownSeconds(60);
            loginOkMessage.SetLoginCountry(_languageFactory.GetTextCodeByLanguage().ToUpper());
            loginOkMessage.SetKunlunId("0f");
            loginOkMessage.SetTier(0);
            loginOkMessage.SetSecondsUntilAccountDeletion(0);
        }

        var myAllianceMessage = new MyAllianceMessage();
        {
            myAllianceMessage.OnlineMembers = allianceModel == null!
                ? 0
                : ToOpenDictionaryHelper.ToAllianceMemberEntryDictionary(
                    allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                        .AllianceMemberEntryList)).Count(keyValuePair =>
                    IdentifierListener.GetV2LogicGameListenerByAccountId(keyValuePair.Key));

            myAllianceMessage.AllianceRole = allianceModel == null!
                ? 0
                : ((AllianceMemberEntry)accountModel
                    .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceMemberEntry))
                .AllianceRoleHelperTable;

            if (allianceModel == null!)
            {
                myAllianceMessage.AllianceHeaderEntry = null!;
            }
            else
            {
                var allianceHeaderEntry = new AllianceHeaderEntry();
                {
                    allianceHeaderEntry.SetAllianceId(allianceModel.GetAllianceId());
                    allianceHeaderEntry.SetAllianceName(allianceModel
                        .GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure.AllianceName)
                        .ToString()!);
                    allianceHeaderEntry.SetScore(ToOpenDictionaryHelper.ToAllianceMemberEntryDictionary(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceMemberEntryList)).Sum(member =>
                        Convert.ToInt32(Databases.AccountDatabase.LoadAccount(member.Value!.AccountId)
                            .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.Trophies))));
                    allianceHeaderEntry.SetAllianceType(Convert.ToInt32(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceType)));
                    allianceHeaderEntry.SetAllianceBadgeData(Convert.ToInt32(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceBadgeData)));
                    allianceHeaderEntry.SetRequiredScore(Convert.ToInt32(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceRequiredScore)));
                    allianceHeaderEntry.SetPreferredLanguage(Convert.ToInt32(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceLanguage)));
                    allianceHeaderEntry.SetAllianceRegionData(Convert.ToInt32(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceRegion)));
                    allianceHeaderEntry.SetNumberOfMembers(ToOpenDictionaryHelper.ToAllianceMemberEntryDictionary(
                        allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceMemberEntryList)).Count);
                    allianceHeaderEntry.SetIsFamilyType(
                        (bool)allianceModel.GetFieldValueByAllianceStructureParameterFromAllianceModel(AllianceStructure
                            .AllianceFamilyType));
                }

                myAllianceMessage.AllianceHeaderEntry = allianceHeaderEntry;
            }
        }

        SendMessage(loginOkMessage);
        var initialAccount = serverConnection!.GetAccountModel();
        // Keep the initial home payload compatible with the V41 client. The
        // current client build hangs at 100% when the experimental offer
        // bundle is included in this packet; box delivery is handled by the
        // server command path instead.
        var sentLoginOkAccountId = loginOkMessage.GetAccountIdForDiagnostics();
        var loadedHomeDataAccountId = initialAccount.GetAccountId();
        AccountTraceLogger.Log(loadedHomeDataAccountId, "LOGIN-AUDIT",
            $"requestedAccountId={piranhaMessage.GetAccountId()} authenticatedAccountId={authenticatedAccountId} " +
            $"resolvedAccountId={accountModel.GetAccountId()} sentLoginOkAccountId={sentLoginOkAccountId} " +
            $"loadedHomeDataAccountId={loadedHomeDataAccountId} sessionId={serverConnection.GetSessionId()} " +
            $"switchFound={switchFound} switchDecision={switchDecision}");
        // The serialized offer bundle is not V41-compatible yet. Keep the
        // proven login payload shape so it cannot re-enter the tutorial flow.
        SendMessage(new OwnHomeDataMessage(initialAccount, false));
        DeliverPendingAdminRewards(initialAccount);
        SendMessage(myAllianceMessage);

        // to set;

        _joinedTime = LogicTimeUtil.GetTimestamp();

        _logicHomeMode.AccountModel = serverConnection!.GetAccountModel();
        _logicHomeMode.SetSessionId(serverConnection.GetSessionId());
        _logicHomeMode.SetServerMessageSender(SendMessage);
        _logicHomeMode.SetSessionReconnect(DisconnectForFreshLogin);

        _logicHomeMode.SetGameListener(new LogicGameListener(connection!.GetUserListener()!, _logicHomeMode));
        _logicHomeMode.SetHome(null!);
        _logicHomeMode.SetHomeOwnerAvatar(null!);

        IdentifierListener.RemoveLogicGameListenerByAccountId(serverConnection!.GetAccountModel().GetAccountId());
        IdentifierListener.SetLogicGameListenerByAccountId(serverConnection!.GetAccountModel().GetAccountId(),
            _logicHomeMode.GetGameListener()!);

        _languageFactory = plIns switch
        {
            17 => LanguageFactory.Russian,
            0 => LanguageFactory.English,
            9 => LanguageFactory.German,
            13 => LanguageFactory.Chinese,
            _ => LanguageFactory.English
        };

        serverConnection!.GetAccountModel().SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.LanguageLaserFactory, _languageFactory);

        if ((long)serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId) <= 0) return 1;

        ToSendAutomizeHelper.SendMyAlliance(this,
            Databases.AllianceDatabase.LoadAlliance((long)serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId)),
            serverConnection!.GetAccountModel(), true);

        ToSendAutomizeHelper.SendAllianceData(this,
            Databases.AllianceDatabase.LoadAlliance((long)serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId)),
            (long)serverConnection!.GetAccountModel()
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.AllianceId), true,
            serverConnection.GetAccountModel().GetAccountId());

        ToSendAutomizeHelper.SendAllianceStatus(
            Databases.AllianceDatabase.LoadAlliance(
                Convert.ToInt64(serverConnection!.GetAccountModel()
                    .GetFieldValueByAccountStructureParameterFromAccountModel(
                        AccountStructure.AllianceId))), serverConnection!.GetAccountModel().GetAccountId());
        return 1;
    }

    private string? GetRemoteAddress()
    {
        return serverConnection!.GetRemoteAddress();
    }

    private void DisconnectForFreshLogin()
    {
        var account = serverConnection!.GetAccountModelOrNull();
        if (account != null)
            AccountTraceLogger.Log(account.GetAccountId(), "BOX-CLAIM-RECONNECT-NOTICE",
                "sending=DisconnectedMessage reason=2 delayMs=250 " +
                "reason=fresh_login_home_snapshot; no_own_home_data_sent_on_active_box_view");

        // Give the V41 client an orderly session-end frame before closing the
        // socket. An abrupt close leaves the native reward view in an error
        // dialog; the explicit frame lets its normal retry path reconnect.
        var disconnectedMessage = new DisconnectedMessage();
        disconnectedMessage.SetReason(2);
        SendMessage(disconnectedMessage);
        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            try
            {
                serverConnection.Disconnect();
            }
            catch (Exception exception)
            {
                if (account != null)
                    AccountTraceLogger.Log(account.GetAccountId(), "BOX-CLAIM-RECONNECT-ERROR",
                        $"phase=socket_close exception={exception.GetType().Name}");
            }
        });
    }

    private void DeliverPendingAdminRewards(AccountModel account)
    {
        foreach (var deliveryType in AdminRewardStore.Take(account.GetAccountId()))
        {
            AccountTraceLogger.Log(account.GetAccountId(), "ADMIN-DELIVERY",
                $"delivering queued deliveryType={deliveryType}");
            var reward = new AvailableServerCommandMessage();
            reward.SetServerCommand(new LogicGiveDeliveryItemsCommand(account, deliveryType));
            SendMessage(reward);
        }
    }

    private static void EnsureStarterOffers(AccountModel accountModel)
    {
        var offers = accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.LogicOfferBundleList) as List<LogicOfferBundle>;
        if (offers is { Count: > 0 } && offers.All(offer => offer != null && offer.IsValid())) return;

        accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.LogicOfferBundleList, CreateStarterOffers());
    }

    private static void ApplyTutorialBypass(AccountModel accountModel)
    {
        // The client decides whether to show the first-play flow from this
        // persisted progress. These are the two locally administered test
        // accounts; no ordinary player is affected.
        if (accountModel.GetAccountId() is not (3 or 4)) return;

        const int completedTutorialTrophies = 500;

        var trophies = Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.Trophies));
        if (trophies < completedTutorialTrophies)
            accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.Trophies, completedTutorialTrophies);

        var maxTrophies = Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.MaxTrophies));
        if (maxTrophies < completedTutorialTrophies)
            accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.MaxTrophies, completedTutorialTrophies);

        var progress = Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.TrophyRoadProgress));
        if (progress < completedTutorialTrophies)
            accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.TrophyRoadProgress, completedTutorialTrophies);

        if (Convert.ToInt32(accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.PlayerAge)) != 999)
            accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.PlayerAge, 999);

        if (!(bool)accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.UserIsAuthed))
            accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.UserIsAuthed, true);

        var values = accountModel.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry>;
        if (values?.Remove(15) == true)
            accountModel.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.IntValueEntryX, values);

        AccountTraceLogger.Log(accountModel.GetAccountId(), "TUTORIAL-SKIP",
            $"trophies={Math.Max(trophies, completedTutorialTrophies)} progress=" +
            $"{Math.Max(progress, completedTutorialTrophies)} age=999");
    }

    private static List<LogicOfferBundle> CreateStarterOffers()
    {
        var endTime = LogicTimeUtil.GetTimestamp() + 24 * 60 * 60;
        return
        [
            CreateFreeOffer(ShopItemHelperTable.FreeBox, "Free Box", endTime),
            CreateFreeOffer(ShopItemHelperTable.BrawlBox, "Brawl Box", endTime),
            CreateFreeOffer(ShopItemHelperTable.BigBox, "Big Box", endTime),
            CreateFreeOffer(ShopItemHelperTable.MegaBox, "Mega Box", endTime)
        ];
    }

    private static LogicOfferBundle CreateFreeOffer(ShopItemHelperTable item, string title, int endTime)
    {
        return new LogicOfferBundle(
            [new LogicGemOffer(item, 1)],
            title,
            "shop_theme",
            ShopPriceTypeHelperTable.ByGems,
            0,
            0,
            endTime,
            true,
            false);
    }
}
