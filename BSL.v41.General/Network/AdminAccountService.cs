using BSL.v41.General.Cloud;
using BSL.v41.Logic.Database;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserListener;
using BSL.v41.Logic.Environment.LaserCommand.Laser;
using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_9;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;
using BSL.v41.Logic.Environment.LaserNotification.Laser;
using BSL.v41.Titan.Graphic;
using BSL.v41.Titan.Diagnostics;

namespace BSL.v41.General.Network;

public static class AdminAccountService
{
    private const int GoldKey = 1;
    private const int GemsKey = 16;
    private const int BrawlPassTokensKey = 28;
    private const int PowerPointsKey = 12;

    public static bool Change(long accountId, string resource, int amount, bool set, out string message)
    {
        var account = Databases.AccountDatabase.LoadAccount(accountId);
        if (account == null!)
        {
            message = $"#{accountId} bulunamadı.";
            return false;
        }

        if (amount < 0)
        {
            message = "Miktar negatif olamaz.";
            return false;
        }

        if (resource.Equals("trophies", StringComparison.OrdinalIgnoreCase))
        {
            var current = Convert.ToInt32(account.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.Trophies));
            var next = set ? amount : AddClamped(current, amount);
            next = Math.Max(next, 0);
            account.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.Trophies, next);

            var maxTrophies = Convert.ToInt32(account.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.MaxTrophies));
            account.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.MaxTrophies,
                set ? next : Math.Max(maxTrophies, next));
            // The V41 home screen gates social features from trophy-road
            // progress as well as the visible trophy value. Keep those two
            // persisted values consistent when the panel changes trophies.
            var trophyRoadProgress = Convert.ToInt32(account
                .GetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.TrophyRoadProgress));
            account.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.TrophyRoadProgress,
                set ? next : Math.Max(trophyRoadProgress, next));
            message = $"#{accountId} kupa değeri {next} oldu.";
        }
        else
        {
            var key = resource.ToLowerInvariant() switch
            {
                "gold" or "altın" => GoldKey,
                "gems" or "elmas" => GemsKey,
                "brawlpass" or "brawl-pass" or "jeton" or "brawl pass jetonu" => BrawlPassTokensKey,
                "powerpoints" or "power-points" or "güç puanı" or "güçpuanı" or "pp" => PowerPointsKey,
                _ => 0
            };

            if (key == 0)
            {
                message = "Kaynak seçimi geçersiz.";
                return false;
            }

            var entries = account.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
            var current = entries.TryGetValue(key, out var entry) ? entry.Value : 0;
            var next = set ? amount : AddClamped(current, amount);
            next = Math.Max(next, 0);
            entries[key] = new IntValueEntry(key, next);
            account.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.IntValueEntryX,
                entries);
            message = $"#{accountId} {DisplayName(resource)} değeri {next} oldu.";
        }

        RefreshOnlineHome(account);
        return true;
    }

    public static bool Remove(long accountId, string resource, int amount, out string message)
    {
        var account = Databases.AccountDatabase.LoadAccount(accountId);
        if (account == null!)
        {
            message = $"#{accountId} bulunamadı.";
            return false;
        }

        if (amount < 0)
        {
            message = "Miktar negatif olamaz.";
            return false;
        }

        if (resource.Equals("trophies", StringComparison.OrdinalIgnoreCase))
        {
            var current = Convert.ToInt32(account.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.Trophies));
            var next = Math.Max(current - amount, 0);
            account.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.Trophies, next);
            message = $"#{accountId} kupa değeri {next} oldu.";
        }
        else
        {
            var key = resource.ToLowerInvariant() switch
            {
                "gold" or "altın" => GoldKey,
                "gems" or "elmas" => GemsKey,
                "brawlpass" or "brawl-pass" or "jeton" or "brawl pass jetonu" => BrawlPassTokensKey,
                "powerpoints" or "power-points" or "güç puanı" or "güçpuanı" or "pp" => PowerPointsKey,
                _ => 0
            };

            if (key == 0)
            {
                message = "Kaynak seçimi geçersiz.";
                return false;
            }

            var entries = account.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
            var current = entries.TryGetValue(key, out var entry) ? entry.Value : 0;
            var next = Math.Max(current - amount, 0);
            entries[key] = new IntValueEntry(key, next);
            account.SetFieldValueByAccountStructureParameterFromAccountModel(AccountStructure.IntValueEntryX,
                entries);
            message = $"#{accountId} {DisplayName(resource)} değeri {next} oldu.";
        }

        RefreshOnlineHome(account);
        return true;
    }

    public static bool AddInboxReward(long accountId, string reward, out string message)
    {
        var account = Databases.AccountDatabase.LoadAccount(accountId);
        if (account == null!)
        {
            message = $"#{accountId} bulunamadı.";
            return false;
        }

        if (TryGetDeliveryType(reward, out var deliveryType))
        {
            var online = IdentifierListener.GetV2LogicGameListenerByAccountId(accountId);
            AccountTraceLogger.Log(accountId, "ADMIN-BOX",
                $"requested reward={reward} deliveryType={deliveryType} online={online}");
            if (online)
            {
                var command = new AvailableServerCommandMessage();
                command.SetServerCommand(new LogicGiveDeliveryItemsCommand(account, deliveryType));
                var listener = IdentifierListener.GetLogicGameListenerByAccountId(accountId);
                listener.Send(command);
                message = $"{reward} ödülü #{accountId} hesabına gönderildi.";
            }
            else
            {
                AdminRewardStore.Enqueue(accountId, deliveryType);
                AddInboxText(account, reward);
                message = $"{reward} ödülü #{accountId} gelen kutusu kuyruğuna eklendi.";
            }
        }
        else
        {
            AddInboxText(account, reward);
            message = $"{reward} bildirimi #{accountId} gelen kutusuna eklendi.";
        }

        return true;
    }

    public static bool TryGetDeliveryType(string reward, out int deliveryType)
    {
        deliveryType = reward.ToLowerInvariant() switch
        {
            "mega kutu" or "mega box" => 12,
            "büyük kutu" or "big box" => 11,
            "savaş kutusu" or "brawl kutusu" or "brawl box" => 10,
            _ => 0
        };
        return deliveryType != 0;
    }

    private static void AddInboxText(AccountModel account, string reward)
    {
        var notificationId = Convert.ToInt64(account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.NotificationFactoryIntraSignedId));
        var notifications = Databases.NotificationIntraSignedDatabase.GetAppendix(notificationId);
        notifications.AccountModel = account;
        notifications.Create(new FreeTextNotification($"Yönetici ödülü: {reward}",
            BSL.v41.Supercell.Titan.CommonUtils.Helper.GetCurrentTimeInSecondsSinceEpoch()));
    }

    private static void RefreshOnlineHome(AccountModel account)
    {
        var accountId = account.GetAccountId();
        if (IdentifierListener.GetV2LogicGameListenerByAccountId(accountId))
            IdentifierListener.GetLogicGameListenerByAccountId(accountId)
                .Send(new OwnHomeDataMessage(account, false));
    }

    private static string DisplayName(string resource)
    {
        return resource.ToLowerInvariant() switch
        {
            "gold" or "altın" => "Altın",
            "gems" or "elmas" => "Elmas",
            "brawlpass" or "brawl-pass" or "jeton" or "brawl pass jetonu" => "Brawl Pass jetonu",
            "powerpoints" or "power-points" or "güç puanı" or "güçpuanı" or "pp" => "Güç puanı",
            _ => resource
        };
    }

    private static int AddClamped(int current, int amount)
    {
        return (int)Math.Clamp((long)current + amount, 0L, int.MaxValue);
    }
}
