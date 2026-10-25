using BSL.v41.General.Cloud;

namespace BSL.v41.General.Network;

/// <summary>
/// One explicit, admin-authorized next-login switch to the reserved #3 test account.
/// It never changes a database record and expires after one minute.
/// </summary>
public static class AccountSwitchStore
{
    private const long TargetAccountId = 3;
    private const int LifetimeSeconds = 60;
    private static readonly object Gate = new();
    private static readonly Dictionary<long, Entry> Pending = new();

    public static bool TryArm(long sourceAccountId, string? code, out string message)
    {
        if (sourceAccountId <= 0 || sourceAccountId == TargetAccountId)
        {
            message = "Geçerli bir kaynak hesabı seçin; hedef hesap sabit olarak #3'tür.";
            return false;
        }

        if (!InteractiveModule.ServerConnections!.TryGetValue(sourceAccountId, out var connection))
        {
            message = "Kaynak hesabın aktif ve doğrulanmış oturumu bulunamadı.";
            return false;
        }

        var device = connection.GetLoginDevice();
        var remoteAddress = connection.GetRemoteAddress();
        if (string.IsNullOrWhiteSpace(device) || string.IsNullOrWhiteSpace(remoteAddress))
        {
            message = "Kaynak oturumunun cihaz/bağlantı bilgisi hazır değil.";
            return false;
        }

        if (!TestCodeStore.TryConsumeForAdmin(code))
        {
            message = "Test kodu geçersiz veya süresi dolmuş.";
            return false;
        }

        lock (Gate)
        {
            Pending[sourceAccountId] = new Entry(
                TargetAccountId,
                device,
                remoteAddress,
                DateTimeOffset.UtcNow.AddSeconds(LifetimeSeconds));
        }

        message = "Bir sonraki doğrulanmış login için #3 geçişi hazırlandı; 60 saniye içinde uygulamayı yeniden açın.";
        return true;
    }

    public static bool TryConsume(long sourceAccountId, string? device, string? remoteAddress,
        out long targetAccountId)
    {
        return TryConsume(sourceAccountId, device, remoteAddress, out targetAccountId,
            out _, out _);
    }

    public static bool TryConsume(long sourceAccountId, string? device, string? remoteAddress,
        out long targetAccountId, out bool switchFound, out string decision)
    {
        targetAccountId = 0;
        switchFound = false;
        decision = "not_found";
        if (string.IsNullOrWhiteSpace(device) || string.IsNullOrWhiteSpace(remoteAddress))
        {
            decision = "missing_device_binding";
            return false;
        }

        lock (Gate)
        {
            if (!Pending.TryGetValue(sourceAccountId, out var entry)) return false;
            switchFound = true;
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                Pending.Remove(sourceAccountId);
                decision = "expired";
                return false;
            }

            // The login handler has already validated the source account's
            // passToken before reaching this method. Bind the one-time switch
            // to that authenticated account and its device, but do not make a
            // mobile-network/IP change invalidate the request.
            if (!string.Equals(entry.Device, device, StringComparison.Ordinal))
            {
                decision = "device_binding_mismatch";
                return false;
            }

            if (!LocalIdentityStore.IsBoundToAccount(TargetAccountId))
            {
                decision = "target_redis_binding_unavailable";
                return false;
            }

            Pending.Remove(sourceAccountId);
            targetAccountId = entry.TargetAccountId;
            decision = "accepted";
            return true;
        }
    }

    private sealed record Entry(long TargetAccountId, string Device, string RemoteAddress,
        DateTimeOffset ExpiresAt);
}
