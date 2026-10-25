using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace BSL.v41.General.Network;

/// <summary>
/// Durable web credentials. This store never writes a game pass token or a
/// plaintext password. Game data remains in the existing account JSON store.
/// </summary>
public static class PlayerCredentialStore
{
    private const int MemoryKb = 65536;
    private const int Iterations = 3;
    private const int Parallelism = 2;
    private const int HashBytes = 32;
    private const int SaltBytes = 16;
    private const int MaxFailures = 5;
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WebSessionLifetime = TimeSpan.FromHours(12);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, WebSession> Sessions = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool Bind(long accountId, string username, string password, out string message)
    {
        username = NormalizeUsername(username);
        if (!IsValidUsername(username) || !IsValidPassword(password))
        {
            message = "Kullanıcı adı 3-64 karakter, şifre en az 12 karakter olmalı.";
            return false;
        }

        var account = BSL.v41.Logic.Database.Databases.AccountDatabase.LoadAccount(accountId);
        if (account == null!) { message = $"#{accountId} bulunamadı."; return false; }

        lock (Gate)
        {
            var records = Load();
            if (records.Any(x => x.AccountId != accountId && x.Username == username))
            { message = "Bu kullanıcı adı başka bir hesaba bağlı."; return false; }

            var existing = records.FirstOrDefault(x => x.AccountId == accountId);
            if (existing is not null && existing.Username != username)
            { message = "Bu hesap zaten bir kullanıcı adına bağlı; değiştirmek için şifre değiştirme işlemini kullanın."; return false; }

            var now = DateTimeOffset.UtcNow;
            var record = existing ?? new CredentialRecord { AccountId = accountId, CreatedAt = now };
            record.Username = username;
            record.PasswordHash = HashPassword(password);
            record.UpdatedAt = now;
            record.FailedAttempts = 0;
            record.LockedUntil = null;
            if (existing is null) records.Add(record);
            Save(records);
            message = $"#{accountId} hesabına kullanıcı adı bağlandı. Oyun verileri değiştirilmedi.";
            return true;
        }
    }

    public static bool ChangePassword(string username, string currentPassword, string newPassword, out string message)
    {
        username = NormalizeUsername(username);
        lock (Gate)
        {
            var records = Load();
            var record = records.FirstOrDefault(x => x.Username == username);
            if (record is null || !VerifyAndRecordFailure(record, currentPassword, records))
            { message = "Kullanıcı adı veya mevcut şifre hatalı."; Save(records); return false; }
            if (!IsValidPassword(newPassword)) { message = "Yeni şifre en az 12 karakter olmalı."; return false; }
            record.PasswordHash = HashPassword(newPassword);
            record.UpdatedAt = DateTimeOffset.UtcNow;
            Save(records);
            RevokeSessions(record.AccountId);
            message = "Şifre değiştirildi; aktif web oturumları kapatıldı.";
            return true;
        }
    }

    public static bool Authenticate(string username, string password, out string sessionToken, out long accountId, out string message)
    {
        sessionToken = string.Empty; accountId = 0;
        username = NormalizeUsername(username);
        lock (Gate)
        {
            var records = Load();
            var record = records.FirstOrDefault(x => x.Username == username);
            if (record is null || !VerifyAndRecordFailure(record, password, records))
            { Save(records); message = "Kullanıcı adı veya şifre hatalı."; return false; }
            record.FailedAttempts = 0; record.LockedUntil = null; record.LastLoginAt = DateTimeOffset.UtcNow;
            Save(records);
            sessionToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            Sessions[sessionToken] = new WebSession(record.AccountId, DateTimeOffset.UtcNow.Add(WebSessionLifetime));
            accountId = record.AccountId; message = "Giriş başarılı."; return true;
        }
    }

    public static bool ValidateSession(string token, out long accountId)
    {
        lock (Gate)
        {
            accountId = 0;
            if (!Sessions.TryGetValue(token, out var session)) return false;
            if (session.ExpiresAt <= DateTimeOffset.UtcNow) { Sessions.Remove(token); return false; }
            accountId = session.AccountId; return true;
        }
    }

    public static int RevokeSessions(long accountId)
    {
        lock (Gate)
        {
            var keys = Sessions.Where(x => x.Value.AccountId == accountId).Select(x => x.Key).ToArray();
            foreach (var key in keys) Sessions.Remove(key);
            return keys.Length;
        }
    }

    public static bool RevokeSession(string token)
    {
        lock (Gate) return Sessions.Remove(token);
    }

    public static bool ChangePasswordForAccount(long accountId, string currentPassword, string newPassword, out string message)
    {
        lock (Gate)
        {
            var records = Load();
            var record = records.FirstOrDefault(x => x.AccountId == accountId);
            if (record is null || !VerifyAndRecordFailure(record, currentPassword, records))
            { Save(records); message = "Mevcut parola hatalı."; return false; }
            if (!IsValidPassword(newPassword)) { message = "Yeni şifre en az 12 karakter olmalı."; return false; }
            record.PasswordHash = HashPassword(newPassword);
            record.UpdatedAt = DateTimeOffset.UtcNow;
            Save(records);
            var keys = Sessions.Where(x => x.Value.AccountId == accountId).Select(x => x.Key).ToArray();
            foreach (var key in keys) Sessions.Remove(key);
            message = "Şifre değiştirildi; diğer web oturumları kapatıldı.";
            return true;
        }
    }

    public static bool HasCredential(long accountId)
    {
        lock (Gate) return Load().Any(x => x.AccountId == accountId);
    }

    public static IReadOnlyList<(long AccountId, string Username, DateTimeOffset? LastLoginAt)> Snapshot()
    {
        lock (Gate) return Load().Select(x => (x.AccountId, x.Username, x.LastLoginAt)).ToArray();
    }

    private static bool VerifyAndRecordFailure(CredentialRecord record, string password, List<CredentialRecord> records)
    {
        var now = DateTimeOffset.UtcNow;
        if (record.LockedUntil is { } locked && locked > now) return false;
        if (VerifyPassword(record.PasswordHash, password)) return true;
        record.FailedAttempts++;
        if (record.FailedAttempts >= MaxFailures) { record.LockedUntil = now.Add(Lockout); record.FailedAttempts = 0; }
        return false;
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, MemoryKb, Iterations, Parallelism);
        return $"argon2id$v=19$m={MemoryKb},t={Iterations},p={Parallelism}${B64(salt)}${B64(hash)}";
    }

    private static bool VerifyPassword(string encoded, string password)
    {
        try
        {
            var parts = encoded.Split('$');
            if (parts.Length != 5 || parts[0] != "argon2id") return false;
            var parameters = parts[2].Split(',').ToDictionary(x => x[..x.IndexOf('=')], x => int.Parse(x[(x.IndexOf('=') + 1)..]));
            var salt = FromB64(parts[3]);
            var expected = FromB64(parts[4]);
            var actual = Derive(password, salt, parameters["m"], parameters["t"], parameters["p"]);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }

    private static byte[] Derive(string password, byte[] salt, int memory, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        { Salt = salt, MemorySize = memory, Iterations = iterations, DegreeOfParallelism = parallelism };
        return argon.GetBytes(HashBytes);
    }

    private static string Path => System.IO.Path.Combine(Environment.CurrentDirectory, "SaveBase", "player_credentials.json");
    private static List<CredentialRecord> Load()
    {
        try { return File.Exists(Path) ? JsonSerializer.Deserialize<List<CredentialRecord>>(File.ReadAllText(Path)) ?? [] : []; }
        catch { return []; }
    }

    private static void Save(List<CredentialRecord> records)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(records, JsonOptions), Encoding.UTF8);
        File.Move(temp, Path, true);
    }

    private static string NormalizeUsername(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
    private static bool IsValidUsername(string value) => value.Length is >= 3 and <= 64 && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');
    private static bool IsValidPassword(string? value) => !string.IsNullOrEmpty(value) && value.Length >= 12 && value.Length <= 256;
    private static string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromB64(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));

    private sealed class CredentialRecord
    {
        public long AccountId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? LastLoginAt { get; set; }
        public int FailedAttempts { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
    private sealed record WebSession(long AccountId, DateTimeOffset ExpiresAt);
}
