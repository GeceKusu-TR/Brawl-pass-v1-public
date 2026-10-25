using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;

namespace BSL.v41.Logic.Database.Account;

/// <summary>
/// Redis shadow store for complete account snapshots.
///
/// The JSON database remains the durable, human-readable source on disk. Redis
/// is a fast, atomic shadow copy and recovery source for a damaged/missing
/// account file. Redis errors are deliberately fail-safe: the game continues
/// using the disk database and no credentials are written to logs.
/// </summary>
public static class AccountRedisStore
{
    private const string AccountsKey = "bsl:account-state:v1";
    private const string ResourcesKey = "bsl:account-resources:v1";
    private static readonly object Gate = new();

    public static bool Save(AccountModel account)
    {
        try
        {
            var accountId = account.GetAccountId();
            if (accountId <= 0) return false;

            var json = account.AccountModelToJson();
            lock (Gate)
            {
                Execute("HSET", AccountsKey, accountId.ToString(), json);
                SaveResourceFields(account);
            }

            return true;
        }
        catch (Exception exception)
        {
            // Do not log JSON, tokens, resources, or Redis credentials.
            Console.WriteLine($"[ACCOUNT-REDIS] snapshot unavailable: {exception.GetType().Name}");
            return false;
        }
    }

    private static void SaveResourceFields(AccountModel account)
    {
        var accountId = account.GetAccountId().ToString();
        var entries = account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();

        Execute("HSET", ResourcesKey,
            $"{accountId}:gold", GetResource(entries, 1).ToString(),
            $"{accountId}:gems", GetResource(entries, 16).ToString(),
            $"{accountId}:brawl_pass_tokens", GetResource(entries, 28).ToString(),
            $"{accountId}:power_points", GetPowerPoints(entries).ToString(),
            $"{accountId}:trophies", Convert.ToInt32(account.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.Trophies)).ToString());
    }

    private static int GetResource(Dictionary<int, IntValueEntry> entries, int key)
    {
        return entries.TryGetValue(key, out var entry) ? entry.Value : 0;
    }

    private static int GetPowerPoints(Dictionary<int, IntValueEntry> entries)
    {
        return Math.Max(GetResource(entries, 12), GetResource(entries, 29));
    }

    public static void Mirror(IReadOnlyDictionary<long, AccountModel> accounts)
    {
        var saved = 0;
        foreach (var account in accounts.Values)
            if (Save(account)) saved++;

        Console.WriteLine($"[ACCOUNT-REDIS] mirrored {saved}/{accounts.Count} account snapshots.");
    }

    public static bool TryLoadAll(out Dictionary<long, AccountModel> accounts)
    {
        accounts = new Dictionary<long, AccountModel>();
        try
        {
            RedisReply reply;
            lock (Gate) reply = Execute("HGETALL", AccountsKey);
            if (reply.Kind != RedisReplyKind.Array || reply.Items == null) return false;

            for (var i = 0; i + 1 < reply.Items.Count; i += 2)
            {
                var idText = reply.Items[i].Value;
                var json = reply.Items[i + 1].Value;
                if (!long.TryParse(idText, out var accountId) || string.IsNullOrWhiteSpace(json)) continue;

                var account = JsonConvert.DeserializeObject<AccountModel>(json);
                if (account != null) accounts[accountId] = account;
            }

            return accounts.Count > 0;
        }
        catch (Exception exception)
        {
            accounts.Clear();
            Console.WriteLine($"[ACCOUNT-REDIS] recovery unavailable: {exception.GetType().Name}");
            return false;
        }
    }

    private static RedisReply Execute(params string[] arguments)
    {
        var (host, port) = ReadEndpoint();
        using var client = new TcpClient();
        client.Connect(host, port);
        using var stream = client.GetStream();
        stream.ReadTimeout = 2000;
        stream.WriteTimeout = 2000;

        var payload = BuildCommand(arguments);
        stream.Write(payload, 0, payload.Length);
        return ReadReply(stream);
    }

    private static byte[] BuildCommand(IReadOnlyList<string> arguments)
    {
        using var buffer = new MemoryStream();
        WriteAscii(buffer, $"*{arguments.Count}\r\n");
        foreach (var argument in arguments)
        {
            var bytes = Encoding.UTF8.GetBytes(argument);
            WriteAscii(buffer, $"${bytes.Length}\r\n");
            buffer.Write(bytes, 0, bytes.Length);
            WriteAscii(buffer, "\r\n");
        }

        return buffer.ToArray();
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static RedisReply ReadReply(Stream stream)
    {
        var type = stream.ReadByte();
        if (type < 0) throw new IOException("Redis bağlantısı kapandı.");

        return (char)type switch
        {
            '+' => new(RedisReplyKind.Simple, ReadLine(stream), null),
            '-' => throw new InvalidOperationException(ReadLine(stream)),
            ':' => new(RedisReplyKind.Integer, ReadLine(stream), null),
            '$' => ReadBulkReply(stream),
            '*' => ReadArrayReply(stream),
            _ => throw new IOException("Redis yanıtı anlaşılamadı.")
        };
    }

    private static RedisReply ReadBulkReply(Stream stream)
    {
        var lengthText = ReadLine(stream);
        if (!int.TryParse(lengthText, out var length))
            throw new IOException("Redis bulk yanıtı geçersiz.");
        if (length < 0) return new(RedisReplyKind.Bulk, null, null);

        var bytes = new byte[length];
        ReadExactly(stream, bytes);
        ReadExactly(stream, new byte[2]);
        return new(RedisReplyKind.Bulk, Encoding.UTF8.GetString(bytes), null);
    }

    private static RedisReply ReadArrayReply(Stream stream)
    {
        var countText = ReadLine(stream);
        if (!int.TryParse(countText, out var count) || count < 0)
            return new(RedisReplyKind.Array, null, null);

        var items = new List<RedisReply>(count);
        for (var i = 0; i < count; i++) items.Add(ReadReply(stream));
        return new(RedisReplyKind.Array, null, items);
    }

    private static string ReadLine(Stream stream)
    {
        using var buffer = new MemoryStream();
        var previous = -1;
        while (true)
        {
            var current = stream.ReadByte();
            if (current < 0) throw new IOException("Redis bağlantısı kapandı.");
            if (previous == '\r' && current == '\n')
            {
                var bytes = buffer.ToArray();
                return Encoding.UTF8.GetString(bytes, 0, Math.Max(0, bytes.Length - 1));
            }

            buffer.WriteByte((byte)current);
            previous = current;
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0) throw new IOException("Redis bağlantısı kapandı.");
            offset += read;
        }
    }

    private static (string Host, int Port) ReadEndpoint()
    {
        var endpoint = System.Environment.GetEnvironmentVariable("BSL_REDIS_ENDPOINT") ?? "127.0.0.1:6379";
        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(endpoint[(separator + 1)..], out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("BSL_REDIS_ENDPOINT geçersiz.");

        return (endpoint[..separator], port);
    }

    private enum RedisReplyKind
    {
        Simple,
        Integer,
        Bulk,
        Array
    }

    private readonly record struct RedisReply(RedisReplyKind Kind, string? Value, List<RedisReply>? Items);
}
