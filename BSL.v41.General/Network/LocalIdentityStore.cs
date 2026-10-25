using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace BSL.v41.General.Network;

/// <summary>
/// Stores a private-server identity alias in Redis. This is deliberately not
/// connected to Supercell ID or any external service.
/// </summary>
public static class LocalIdentityStore
{
    private const long SupportedAccountId = 3;
    private const string KeyPrefix = "bsl:private-id:v1";

    /// <summary>
    /// Login gate for the reserved local administrator account. Redis failure
    /// is treated as a denial so the identity binding can never become a
    /// bypass when the backing store is unavailable.
    /// </summary>
    public static bool IsBoundToAccount(long accountId)
    {
        if (accountId != SupportedAccountId)
            return false;

        try
        {
            var (host, port) = ReadEndpoint();
            using var client = new TcpClient();
            client.Connect(host, port);

            using var stream = client.GetStream();
            stream.ReadTimeout = 2000;
            stream.WriteTimeout = 2000;
            RedisCommand(stream, "HGET", $"{KeyPrefix}:account:{SupportedAccountId}", "account_id");
            var reply = ReadReply(stream);
            return reply.Kind == RedisReplyKind.Bulk && reply.Value == SupportedAccountId.ToString();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[LOCAL-ID] Redis validation failed: {exception.GetType().Name}");
            return false;
        }
    }

    public static bool TryBind(long accountId, string email, out string message)
    {
        if (accountId != SupportedAccountId)
        {
            message = "Yerel kimlik bağlama yalnızca #3 hesabı için kullanılabilir.";
            return false;
        }

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail == null)
        {
            message = "Geçerli bir e-posta adresi girin.";
            return false;
        }

        var emailHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)))
            .ToLowerInvariant();
        var emailKey = $"{KeyPrefix}:email:{emailHash}";
        var accountKey = $"{KeyPrefix}:account:{SupportedAccountId}";

        try
        {
            var (host, port) = ReadEndpoint();
            using var client = new TcpClient();
            client.Connect(host, port);

            using var stream = client.GetStream();
            stream.ReadTimeout = 2000;
            stream.WriteTimeout = 2000;

            RedisCommand(stream, "GET", emailKey);
            var existingAccount = ReadReply(stream);
            if (existingAccount.Kind == RedisReplyKind.Error)
                throw new InvalidOperationException(existingAccount.Value);

            if (existingAccount.Value is not null && existingAccount.Value != SupportedAccountId.ToString())
            {
                message = "Bu yerel kimlik başka bir hesaba bağlı.";
                return false;
            }

            if (existingAccount.Value is null)
            {
                RedisCommand(stream, "SET", emailKey, SupportedAccountId.ToString(), "NX");
                var setReply = ReadReply(stream);
                if (setReply.Kind == RedisReplyKind.Error)
                    throw new InvalidOperationException(setReply.Value);

                if (setReply.Value is null)
                {
                    message = "Yerel kimlik aynı anda başka bir işlem tarafından bağlandı; tekrar deneyin.";
                    return false;
                }
            }

            RedisCommand(stream,
                "HSET", accountKey,
                "account_id", SupportedAccountId.ToString(),
                "provider", "private-local",
                "email_sha256", emailHash,
                "bound_at", DateTimeOffset.UtcNow.ToString("O"));
            var hashReply = ReadReply(stream);
            if (hashReply.Kind == RedisReplyKind.Error)
                throw new InvalidOperationException(hashReply.Value);

            RedisCommand(stream, "SET", $"{KeyPrefix}:account:{SupportedAccountId}:email_key", emailKey);
            var reverseReply = ReadReply(stream);
            if (reverseReply.Kind == RedisReplyKind.Error)
                throw new InvalidOperationException(reverseReply.Value);

            message = "Yerel kimlik #3 hesabına bağlandı ve Redis kaydı oluşturuldu.";
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[LOCAL-ID] Redis bind failed: {exception.GetType().Name}");
            message = "Redis kaydı oluşturulamadı; sunucu tarafında değişiklik yapılmadı.";
            return false;
        }
    }

    private static string? NormalizeEmail(string? email)
    {
        var value = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 320)
            return null;

        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 ? value : null;
    }

    private static (string Host, int Port) ReadEndpoint()
    {
        var endpoint = Environment.GetEnvironmentVariable("BSL_REDIS_ENDPOINT") ?? "127.0.0.1:6379";
        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(endpoint[(separator + 1)..], out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("BSL_REDIS_ENDPOINT geçersiz.");

        return (endpoint[..separator], port);
    }

    private static void RedisCommand(Stream stream, params string[] arguments)
    {
        var builder = new StringBuilder().Append('*').Append(arguments.Length).Append("\r\n");
        foreach (var argument in arguments)
        {
            var bytes = Encoding.UTF8.GetBytes(argument);
            builder.Append('$').Append(bytes.Length).Append("\r\n");
            builder.Append(argument).Append("\r\n");
        }

        var payload = Encoding.UTF8.GetBytes(builder.ToString());
        stream.Write(payload, 0, payload.Length);
    }

    private static RedisReply ReadReply(Stream stream)
    {
        var type = stream.ReadByte();
        if (type < 0)
            throw new IOException("Redis bağlantısı kapandı.");

        return (char)type switch
        {
            '+' => new(RedisReplyKind.Simple, ReadLine(stream)),
            '-' => new(RedisReplyKind.Error, ReadLine(stream)),
            ':' => new(RedisReplyKind.Integer, ReadLine(stream)),
            '$' => ReadBulkReply(stream),
            _ => throw new IOException("Redis yanıtı anlaşılamadı.")
        };
    }

    private static RedisReply ReadBulkReply(Stream stream)
    {
        var lengthText = ReadLine(stream);
        if (!int.TryParse(lengthText, out var length))
            throw new IOException("Redis bulk yanıtı geçersiz.");
        if (length < 0)
            return new(RedisReplyKind.Bulk, null);

        var bytes = new byte[length];
        ReadExactly(stream, bytes);
        ReadExactly(stream, new byte[2]);
        return new(RedisReplyKind.Bulk, Encoding.UTF8.GetString(bytes));
    }

    private static string ReadLine(Stream stream)
    {
        using var buffer = new MemoryStream();
        var previous = -1;
        while (true)
        {
            var current = stream.ReadByte();
            if (current < 0)
                throw new IOException("Redis bağlantısı kapandı.");
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
            if (read == 0)
                throw new IOException("Redis bağlantısı kapandı.");
            offset += read;
        }
    }

    private enum RedisReplyKind
    {
        Simple,
        Error,
        Integer,
        Bulk
    }

    private readonly record struct RedisReply(RedisReplyKind Kind, string? Value);
}
