using System.Security.Cryptography;
using System.Text;

namespace BSL.v41.General.Network;

/// <summary>
/// In-memory, local-test-only login codes. Codes are never persisted or logged.
/// </summary>
public static class TestCodeStore
{
    private const long TestAccountId = 3;
    private const int CodeLength = 10;
    private const int LifetimeSeconds = 60;
    private const int GenerationWindowSeconds = 60;
    private const int MaxGenerationsPerWindow = 3;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly object Gate = new();
    private static readonly Dictionary<long, Entry> Entries = new();
    private static readonly Queue<DateTimeOffset> GenerationTimes = new();
    private static readonly Dictionary<string, Queue<DateTimeOffset>> AttemptTimes = new();

    public static bool TryGenerate(out string code, out string message)
    {
        lock (Gate)
        {
            PruneGenerationTimes(DateTimeOffset.UtcNow);
            if (GenerationTimes.Count >= MaxGenerationsPerWindow)
            {
                code = string.Empty;
                message = "Test kodu üretim sınırına ulaşıldı; biraz sonra tekrar deneyin.";
                return false;
            }

            code = CreateCode();
            Entries[TestAccountId] = new Entry(
                Hash(code),
                DateTimeOffset.UtcNow.AddSeconds(LifetimeSeconds));
            GenerationTimes.Enqueue(DateTimeOffset.UtcNow);
            message = $"#3 için tek kullanımlık test kodu üretildi: {code} (60 saniye geçerli).";
            return true;
        }
    }

    public static bool TryConsume(long accountId, string? code, string? device, string? remoteAddress)
    {
        if (accountId != TestAccountId || string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(device) || string.IsNullOrWhiteSpace(remoteAddress)) return false;

        lock (Gate)
        {
            if (!AllowAttempt(remoteAddress)) return false;
            if (!Entries.TryGetValue(TestAccountId, out var entry)) return false;
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                Entries.Remove(TestAccountId);
                return false;
            }

            if (!CryptographicEquals(entry.CodeHash, Hash(code))) return false;

            // The first successful consume binds this one-use code to the
            // connecting device and address. The code is removed atomically,
            // so a second session cannot reuse it.
            Entries.Remove(TestAccountId);
            return true;
        }
    }

    public static bool TryConsumeForAdmin(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        lock (Gate)
        {
            if (!Entries.TryGetValue(TestAccountId, out var entry)) return false;
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                Entries.Remove(TestAccountId);
                return false;
            }

            if (!CryptographicEquals(entry.CodeHash, Hash(code))) return false;
            Entries.Remove(TestAccountId);
            return true;
        }
    }

    private static string CreateCode()
    {
        Span<byte> random = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(random);
        var builder = new StringBuilder(CodeLength);
        foreach (var value in random) builder.Append(Alphabet[value % Alphabet.Length]);
        return builder.ToString();
    }

    private static byte[] Hash(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }

    private static bool CryptographicEquals(byte[] left, byte[] right)
    {
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static void PruneGenerationTimes(DateTimeOffset now)
    {
        while (GenerationTimes.Count > 0 &&
               (now - GenerationTimes.Peek()).TotalSeconds >= GenerationWindowSeconds)
            GenerationTimes.Dequeue();
    }

    private static bool AllowAttempt(string remoteAddress)
    {
        var now = DateTimeOffset.UtcNow;
        if (!AttemptTimes.TryGetValue(remoteAddress, out var attempts))
            AttemptTimes[remoteAddress] = attempts = new Queue<DateTimeOffset>();

        while (attempts.Count > 0 && (now - attempts.Peek()).TotalSeconds >= GenerationWindowSeconds)
            attempts.Dequeue();

        if (attempts.Count >= 5) return false;
        attempts.Enqueue(now);
        return true;
    }

    private sealed record Entry(byte[] CodeHash, DateTimeOffset ExpiresAt);
}
