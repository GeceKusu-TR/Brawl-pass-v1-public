using System.Collections.Concurrent;
using MySqlConnector;
using BSL.v41.Logic.Database.Account;
using BSL.v41.Logic.Environment.LaserMessage.Sepo.Home.Laser.Laser;

namespace BSL.v41.Logic.Database;

/// <summary>
/// Durable MySQL persistence for data that must survive Redis restarts.
/// The JSON account database remains the game-state source of truth; this
/// service maintains durable leaderboard rows and chat history alongside it.
///
/// The connection string is supplied only through BSL_MYSQL_CONNECTION_STRING.
/// Database failures are fail-safe: the game keeps serving from its existing
/// JSON/Redis stores and queued leaderboard writes are retried in memory.
/// </summary>
public static class MySqlPersistence
{
    private const int DefaultBrawlPassSeasonId = 9;
    private static readonly TimeSpan BrawlPassSeasonLength = TimeSpan.FromDays(30);
    private const int MaxChatLength = 2000;
    private static readonly ConcurrentDictionary<long, LeaderboardSnapshot> PendingLeaderboards = new();
    private static readonly ConcurrentDictionary<long, ResourceSnapshot> PendingResources = new();
    private static readonly ConcurrentDictionary<long, OfferPurchaseSnapshot> PendingOfferPurchases = new();
    private static readonly SemaphoreSlim PendingSignal = new(0);
    private static readonly SemaphoreSlim ResourceSignal = new(0);
    private static readonly SemaphoreSlim OfferPurchaseSignal = new(0);
    private static readonly object InitGate = new();
    private static string? _connectionString;
    private static bool _enabled;
    private static bool _initialized;

    public static bool IsEnabled => _enabled;

    public static void Initialize()
    {
        lock (InitGate)
        {
            if (_initialized) return;
            _initialized = true;

            _connectionString = System.Environment.GetEnvironmentVariable("BSL_MYSQL_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                Console.WriteLine("[MYSQL] disabled: BSL_MYSQL_CONNECTION_STRING is not set.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = SchemaSql;
                command.ExecuteNonQuery();
                _enabled = true;
                _ = Task.Run(LeaderboardWorkerAsync);
                _ = Task.Run(ResourceWorkerAsync);
                _ = Task.Run(OfferPurchaseWorkerAsync);
                Console.WriteLine("[MYSQL] connected; chat, leaderboard, and resource schema is ready.");
            }
            catch (Exception exception)
            {
                _enabled = false;
                Console.WriteLine($"[MYSQL] disabled after connection failure: {exception.GetType().Name}.");
            }
        }
    }

    public static void EnqueueLeaderboard(AccountModel account)
    {
        if (!_enabled || account == null!) return;

        var accountId = account.GetAccountId();
        if (accountId <= 0) return;

        var playerName = account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.AvatarName)?.ToString() ?? string.Empty;
        var trophies = ReadInt(account, AccountStructure.Trophies);
        var maxTrophies = ReadInt(account, AccountStructure.MaxTrophies);

        PendingLeaderboards[accountId] = new LeaderboardSnapshot(
            accountId,
            playerName.Length > 255 ? playerName[..255] : playerName,
            trophies,
            maxTrophies);

        try
        {
            PendingSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // The worker is already scheduled; the dictionary contains the
            // latest snapshot, so no update is lost.
        }
    }

    public static void EnqueueResources(AccountModel account)
    {
        if (!_enabled || account == null!) return;

        var accountId = account.GetAccountId();
        if (accountId <= 0) return;

        var entries = account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.IntValueEntryX) as Dictionary<int, IntValueEntry> ?? new();
        PendingResources[accountId] = new ResourceSnapshot(
            accountId,
            GetResource(entries, 1),
            GetResource(entries, 16),
            GetResource(entries, 28),
            Math.Max(GetResource(entries, 12), GetResource(entries, 29)));
        try
        {
            ResourceSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // The dictionary already contains the latest value.
        }
    }

    public static bool TryRestoreResources(AccountModel account)
    {
        if (!_enabled || account == null! || account.GetAccountId() <= 0) return false;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT gold, gems, brawl_pass_tokens, power_points
                FROM account_resources
                WHERE account_id = @account_id;
                """;
            command.Parameters.AddWithValue("@account_id", account.GetAccountId());
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return false;

            account.ApplyPersistedResourceValues(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] resource restore failed: {exception.GetType().Name}.");
            return false;
        }
    }

    public static void EnqueueOfferPurchases(AccountModel account)
    {
        if (!_enabled || account == null! || account.GetAccountId() <= 0) return;

        var offers = account.GetFieldValueByAccountStructureParameterFromAccountModel(
            AccountStructure.LogicOfferBundleList) as List<LogicOfferBundle>;
        if (offers == null) return;

        PendingOfferPurchases[account.GetAccountId()] = new OfferPurchaseSnapshot(
            account.GetAccountId(), offers.Select(offer => offer?.GetPurchased() == true).ToArray());
        try
        {
            OfferPurchaseSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // The dictionary already contains the latest value.
        }
    }

    public static bool TryRestoreOfferPurchases(AccountModel account)
    {
        if (!_enabled || account == null! || account.GetAccountId() <= 0) return false;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT offer_index
                FROM account_offer_purchases
                WHERE account_id = @account_id AND purchased = 1;
                """;
            command.Parameters.AddWithValue("@account_id", account.GetAccountId());
            using var reader = command.ExecuteReader();
            var purchased = new HashSet<int>();
            while (reader.Read()) purchased.Add(reader.GetInt32(0));
            if (purchased.Count == 0) return false;

            var offers = account.GetFieldValueByAccountStructureParameterFromAccountModel(
                AccountStructure.LogicOfferBundleList) as List<LogicOfferBundle>;
            if (offers == null) return false;
            for (var index = 0; index < offers.Count; index++)
                offers[index].SetPurchased(purchased.Contains(index));
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] offer purchase restore failed: {exception.GetType().Name}.");
            return false;
        }
    }

    public static HashSet<int> GetBrawlPassClaimedLevels(long accountId, int seasonId, int track)
    {
        var claimed = new HashSet<int>();
        if (!_enabled || accountId <= 0) return claimed;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT level
                FROM brawl_pass_claims
                WHERE account_id = @account_id AND season_id = @season_id AND track = @track;
                """;
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@season_id", seasonId);
            command.Parameters.AddWithValue("@track", track);
            using var reader = command.ExecuteReader();
            while (reader.Read()) claimed.Add(reader.GetInt32(0));
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] brawl pass claim read failed: {exception.GetType().Name}.");
        }

        return claimed;
    }

    public static bool TryClaimBrawlPassLevel(long accountId, int seasonId, int track, int level)
    {
        if (!_enabled || accountId <= 0 || seasonId < 0 || track is < 0 or > 1 || level < 0) return false;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT IGNORE INTO brawl_pass_claims
                    (account_id, season_id, track, level, claimed_at)
                VALUES
                    (@account_id, @season_id, @track, @level, UTC_TIMESTAMP(6));
                """;
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@season_id", seasonId);
            command.Parameters.AddWithValue("@track", track);
            command.Parameters.AddWithValue("@level", level);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] brawl pass claim write failed: {exception.GetType().Name}.");
            return false;
        }
    }

    public static bool HasBrawlPassClaim(long accountId, int seasonId, int track, int level)
    {
        if (!_enabled || accountId <= 0 || seasonId < 0 || track is < 0 or > 1 || level < 0) return false;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1
                FROM brawl_pass_claims
                WHERE account_id = @account_id AND season_id = @season_id
                  AND track = @track AND level = @level
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@season_id", seasonId);
            command.Parameters.AddWithValue("@track", track);
            command.Parameters.AddWithValue("@level", level);
            return command.ExecuteScalar() is not null;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] brawl pass claim existence read failed: {exception.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Stores the brawler selected for a wildcard-power Brawl Pass reward.
    /// The season/level key makes retries idempotent, so a repeated client
    /// selection cannot add the same power points twice.
    /// </summary>
    public static bool RecordBrawlPassBrawlerPower(long accountId, int seasonId,
        int level, int brawlerGlobalId, int powerPoints)
    {
        if (!_enabled || accountId <= 0 || seasonId < 0 || level < 0 ||
            brawlerGlobalId <= 0 || powerPoints <= 0) return false;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO brawl_pass_brawler_power
                    (account_id, season_id, level, brawler_global_id, power_points, updated_at)
                VALUES
                    (@account_id, @season_id, @level, @brawler_global_id, @power_points, UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    brawler_global_id = VALUES(brawler_global_id),
                    power_points = VALUES(power_points),
                    updated_at = UTC_TIMESTAMP(6);
                """;
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@season_id", seasonId);
            command.Parameters.AddWithValue("@level", level);
            command.Parameters.AddWithValue("@brawler_global_id", brawlerGlobalId);
            command.Parameters.AddWithValue("@power_points", powerPoints);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] brawl pass brawler power write failed: {exception.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Records a box delivery/claim per account. The claim key is supplied by
    /// the caller so retries update the same row instead of creating another
    /// claim. This is an audit/state record; it never grants resources.
    /// </summary>
    public static bool RecordBoxClaim(long accountId, string claimKey, int deliveryType,
        string source, int? boxIndex = null, string status = "claimed")
    {
        if (!_enabled || accountId <= 0 || string.IsNullOrWhiteSpace(claimKey) ||
            deliveryType is < 10 or > 12 || string.IsNullOrWhiteSpace(source)) return false;

        claimKey = claimKey.Trim();
        source = source.Trim();
        status = string.IsNullOrWhiteSpace(status) ? "claimed" : status.Trim();
        if (claimKey.Length > 160 || source.Length > 64 || status.Length > 32) return false;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO box_claims
                    (account_id, claim_key, delivery_type, box_index, source, status,
                     first_seen_at, last_seen_at, attempts)
                VALUES
                    (@account_id, @claim_key, @delivery_type, @box_index, @source, @status,
                     UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 1)
                ON DUPLICATE KEY UPDATE
                    delivery_type = VALUES(delivery_type),
                    box_index = VALUES(box_index),
                    source = VALUES(source),
                    status = VALUES(status),
                    last_seen_at = UTC_TIMESTAMP(6),
                    attempts = attempts + 1;
                """;
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@claim_key", claimKey);
            command.Parameters.AddWithValue("@delivery_type", deliveryType);
            command.Parameters.AddWithValue("@box_index", boxIndex.HasValue ? boxIndex.Value : DBNull.Value);
            command.Parameters.AddWithValue("@source", source);
            command.Parameters.AddWithValue("@status", status);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] box claim write failed: {exception.GetType().Name}.");
            return false;
        }
    }

    public static bool SetBrawlPassPremium(long accountId, int seasonId)
    {
        if (!_enabled || accountId <= 0 || seasonId < 0) return false;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO brawl_pass_entitlements
                    (account_id, season_id, premium, updated_at)
                VALUES
                    (@account_id, @season_id, 1, UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    premium = 1,
                    updated_at = UTC_TIMESTAMP(6);
                """;
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@season_id", seasonId);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] brawl pass entitlement write failed: {exception.GetType().Name}.");
            return false;
        }
    }

    public static bool HasBrawlPassPremium(long accountId, int seasonId)
    {
        if (!_enabled || accountId <= 0 || seasonId < 0) return false;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT premium
                FROM brawl_pass_entitlements
                WHERE account_id = @account_id AND season_id = @season_id;
                """;
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@season_id", seasonId);
            return Convert.ToBoolean(command.ExecuteScalar() ?? false);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] brawl pass entitlement read failed: {exception.GetType().Name}.");
            return false;
        }
    }

    public static BrawlPassSeasonState GetBrawlPassSeasonState()
    {
        var now = DateTime.UtcNow;
        var fallback = new BrawlPassSeasonState(DefaultBrawlPassSeasonId, now, now.Add(BrawlPassSeasonLength));
        if (!_enabled) return fallback;

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = """
                SELECT season_id, started_at, ends_at
                FROM brawl_pass_season_state
                WHERE state_id = 1
                FOR UPDATE;
                """;
            using var reader = select.ExecuteReader();
            if (reader.Read())
            {
                var seasonId = reader.GetInt32(0);
                var startedAt = reader.GetDateTime(1);
                var endsAt = reader.GetDateTime(2);
                reader.Close();

                if (endsAt <= now)
                {
                    seasonId++;
                    startedAt = now;
                    endsAt = now.Add(BrawlPassSeasonLength);
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = """
                        UPDATE brawl_pass_season_state
                        SET season_id = @season_id, started_at = @started_at, ends_at = @ends_at
                        WHERE state_id = 1;
                        """;
                    update.Parameters.AddWithValue("@season_id", seasonId);
                    update.Parameters.AddWithValue("@started_at", startedAt);
                    update.Parameters.AddWithValue("@ends_at", endsAt);
                    update.ExecuteNonQuery();
                }

                transaction.Commit();
                return new BrawlPassSeasonState(seasonId, startedAt, endsAt);
            }

            reader.Close();
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO brawl_pass_season_state
                    (state_id, season_id, started_at, ends_at)
                VALUES (1, @season_id, @started_at, @ends_at);
                """;
            insert.Parameters.AddWithValue("@season_id", fallback.SeasonId);
            insert.Parameters.AddWithValue("@started_at", fallback.StartedAt);
            insert.Parameters.AddWithValue("@ends_at", fallback.EndsAt);
            insert.ExecuteNonQuery();
            transaction.Commit();
            return fallback;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] brawl pass season read failed: {exception.GetType().Name}.");
            return fallback;
        }
    }

    /// <summary>
    /// Persists an in-game chat message once a protocol handler has validated
    /// its sender and channel. Text is parameterized and length-limited.
    /// </summary>
    public static async Task<bool> InsertChatMessageAsync(long accountId, string channelType,
        long? channelId, string messageText, CancellationToken cancellationToken = default)
    {
        if (!_enabled || accountId <= 0 || string.IsNullOrWhiteSpace(channelType) ||
            string.IsNullOrWhiteSpace(messageText)) return false;

        channelType = channelType.Trim();
        messageText = messageText.Trim();
        if (channelType.Length > 32 || messageText.Length > MaxChatLength) return false;

        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO player_chat_messages
                    (account_id, channel_type, channel_id, message_text, created_at)
                VALUES
                    (@account_id, @channel_type, @channel_id, @message_text, UTC_TIMESTAMP(6));
                """;
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@channel_type", channelType);
            command.Parameters.AddWithValue("@channel_id", channelId.HasValue ? channelId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@message_text", messageText);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[MYSQL] chat write failed: {exception.GetType().Name}.");
            return false;
        }
    }

    private static async Task LeaderboardWorkerAsync()
    {
        while (true)
        {
            List<LeaderboardSnapshot>? batch = null;
            try
            {
                await PendingSignal.WaitAsync();
                batch = new List<LeaderboardSnapshot>();
                foreach (var pair in PendingLeaderboards.ToArray())
                    if (PendingLeaderboards.TryRemove(pair.Key, out var snapshot)) batch.Add(snapshot);

                if (batch.Count == 0) continue;
                await WriteLeaderboardBatchAsync(batch);
            }
            catch (Exception exception)
            {
                if (batch is { Count: > 0 })
                {
                    foreach (var snapshot in batch)
                        PendingLeaderboards[snapshot.AccountId] = snapshot;
                    try
                    {
                        PendingSignal.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        // The worker is already scheduled for a retry.
                    }
                }

                Console.WriteLine($"[MYSQL] leaderboard write failed: {exception.GetType().Name}.");
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static async Task ResourceWorkerAsync()
    {
        while (true)
        {
            List<ResourceSnapshot>? batch = null;
            try
            {
                await ResourceSignal.WaitAsync();
                batch = new List<ResourceSnapshot>();
                foreach (var pair in PendingResources.ToArray())
                    if (PendingResources.TryRemove(pair.Key, out var snapshot)) batch.Add(snapshot);

                if (batch.Count == 0) continue;
                await WriteResourceBatchAsync(batch);
            }
            catch (Exception exception)
            {
                if (batch is { Count: > 0 })
                {
                    foreach (var snapshot in batch)
                        PendingResources[snapshot.AccountId] = snapshot;
                    try
                    {
                        ResourceSignal.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        // The worker is already scheduled for a retry.
                    }
                }

                Console.WriteLine($"[MYSQL] resource write failed: {exception.GetType().Name}.");
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static async Task OfferPurchaseWorkerAsync()
    {
        while (true)
        {
            List<OfferPurchaseSnapshot>? batch = null;
            try
            {
                await OfferPurchaseSignal.WaitAsync();
                batch = new List<OfferPurchaseSnapshot>();
                foreach (var pair in PendingOfferPurchases.ToArray())
                    if (PendingOfferPurchases.TryRemove(pair.Key, out var snapshot)) batch.Add(snapshot);

                if (batch.Count == 0) continue;
                await WriteOfferPurchaseBatchAsync(batch);
            }
            catch (Exception exception)
            {
                if (batch is { Count: > 0 })
                {
                    foreach (var snapshot in batch)
                        PendingOfferPurchases[snapshot.AccountId] = snapshot;
                    try
                    {
                        OfferPurchaseSignal.Release();
                    }
                    catch (SemaphoreFullException)
                    {
                        // The worker is already scheduled for a retry.
                    }
                }

                Console.WriteLine($"[MYSQL] offer purchase write failed: {exception.GetType().Name}.");
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static async Task WriteLeaderboardBatchAsync(IReadOnlyCollection<LeaderboardSnapshot> batch)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO leaderboard_entries
                (leaderboard_type, account_id, player_name, trophies, max_trophies, updated_at)
            VALUES
                ('trophies', @account_id, @player_name, @trophies, @max_trophies, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                player_name = VALUES(player_name),
                trophies = VALUES(trophies),
                max_trophies = VALUES(max_trophies),
                updated_at = UTC_TIMESTAMP(6);
            """;
        var accountId = command.Parameters.Add("@account_id", MySqlDbType.Int64);
        var playerName = command.Parameters.Add("@player_name", MySqlDbType.VarChar, 255);
        var trophies = command.Parameters.Add("@trophies", MySqlDbType.Int32);
        var maxTrophies = command.Parameters.Add("@max_trophies", MySqlDbType.Int32);

        foreach (var entry in batch)
        {
            accountId.Value = entry.AccountId;
            playerName.Value = entry.PlayerName;
            trophies.Value = entry.Trophies;
            maxTrophies.Value = entry.MaxTrophies;
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task WriteResourceBatchAsync(IReadOnlyCollection<ResourceSnapshot> batch)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO account_resources
                (account_id, gold, gems, brawl_pass_tokens, power_points, updated_at)
            VALUES
                (@account_id, @gold, @gems, @brawl_pass_tokens, @power_points, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                gold = VALUES(gold),
                gems = VALUES(gems),
                brawl_pass_tokens = VALUES(brawl_pass_tokens),
                power_points = VALUES(power_points),
                updated_at = UTC_TIMESTAMP(6);
            """;
        var accountId = command.Parameters.Add("@account_id", MySqlDbType.Int64);
        var gold = command.Parameters.Add("@gold", MySqlDbType.Int32);
        var gems = command.Parameters.Add("@gems", MySqlDbType.Int32);
        var brawlPassTokens = command.Parameters.Add("@brawl_pass_tokens", MySqlDbType.Int32);
        var powerPoints = command.Parameters.Add("@power_points", MySqlDbType.Int32);

        foreach (var entry in batch)
        {
            accountId.Value = entry.AccountId;
            gold.Value = entry.Gold;
            gems.Value = entry.Gems;
            brawlPassTokens.Value = entry.BrawlPassTokens;
            powerPoints.Value = entry.PowerPoints;
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task WriteOfferPurchaseBatchAsync(IReadOnlyCollection<OfferPurchaseSnapshot> batch)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM account_offer_purchases WHERE account_id = @account_id;";
        var deleteAccountId = delete.Parameters.Add("@account_id", MySqlDbType.Int64);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO account_offer_purchases (account_id, offer_index, purchased, updated_at)
            VALUES (@account_id, @offer_index, @purchased, UTC_TIMESTAMP(6));
            """;
        var insertAccountId = insert.Parameters.Add("@account_id", MySqlDbType.Int64);
        var offerIndex = insert.Parameters.Add("@offer_index", MySqlDbType.Int32);
        var purchased = insert.Parameters.Add("@purchased", MySqlDbType.Byte);

        foreach (var entry in batch)
        {
            deleteAccountId.Value = entry.AccountId;
            await delete.ExecuteNonQueryAsync();
            insertAccountId.Value = entry.AccountId;
            for (var index = 0; index < entry.Purchased.Length; index++)
            {
                offerIndex.Value = index;
                purchased.Value = entry.Purchased[index];
                await insert.ExecuteNonQueryAsync();
            }
        }

        await transaction.CommitAsync();
    }

    private static int GetResource(Dictionary<int, IntValueEntry> entries, int key)
    {
        return entries.TryGetValue(key, out var entry) ? entry.Value : 0;
    }

    private static int ReadInt(AccountModel account, AccountStructure structure)
    {
        var value = account.GetFieldValueByAccountStructureParameterFromAccountModel(structure);
        return value == null ? 0 : Convert.ToInt32(value);
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS player_chat_messages (
            id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
            account_id BIGINT NOT NULL,
            channel_type VARCHAR(32) NOT NULL,
            channel_id BIGINT NULL,
            message_text VARCHAR(2000) NOT NULL,
            created_at DATETIME(6) NOT NULL,
            PRIMARY KEY (id),
            KEY idx_chat_channel_time (channel_type, channel_id, created_at),
            KEY idx_chat_account_time (account_id, created_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS leaderboard_entries (
            leaderboard_type VARCHAR(32) NOT NULL,
            account_id BIGINT NOT NULL,
            player_name VARCHAR(255) NOT NULL,
            trophies INT NOT NULL DEFAULT 0,
            max_trophies INT NOT NULL DEFAULT 0,
            updated_at DATETIME(6) NOT NULL,
            PRIMARY KEY (leaderboard_type, account_id),
            KEY idx_leaderboard_trophies (leaderboard_type, trophies, max_trophies, account_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS account_resources (
            account_id BIGINT NOT NULL,
            gold INT NOT NULL DEFAULT 0,
            gems INT NOT NULL DEFAULT 0,
            brawl_pass_tokens INT NOT NULL DEFAULT 0,
            power_points INT NOT NULL DEFAULT 0,
            updated_at DATETIME(6) NOT NULL,
            PRIMARY KEY (account_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        ALTER TABLE account_resources
            ADD COLUMN IF NOT EXISTS power_points INT NOT NULL DEFAULT 0 AFTER brawl_pass_tokens;

        CREATE TABLE IF NOT EXISTS account_offer_purchases (
            account_id BIGINT NOT NULL,
            offer_index INT NOT NULL,
            purchased TINYINT(1) NOT NULL DEFAULT 0,
            updated_at DATETIME(6) NOT NULL,
            PRIMARY KEY (account_id, offer_index)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS brawl_pass_claims (
            account_id BIGINT NOT NULL,
            season_id INT NOT NULL,
            track TINYINT NOT NULL,
            level INT NOT NULL,
            claimed_at DATETIME(6) NOT NULL,
            PRIMARY KEY (account_id, season_id, track, level)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS brawl_pass_season_state (
            state_id TINYINT NOT NULL,
            season_id INT NOT NULL,
            started_at DATETIME(6) NOT NULL,
            ends_at DATETIME(6) NOT NULL,
            PRIMARY KEY (state_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS brawl_pass_entitlements (
            account_id BIGINT NOT NULL,
            season_id INT NOT NULL,
            premium TINYINT(1) NOT NULL DEFAULT 0,
            updated_at DATETIME(6) NOT NULL,
            PRIMARY KEY (account_id, season_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS brawl_pass_brawler_power (
            account_id BIGINT NOT NULL,
            season_id INT NOT NULL,
            level INT NOT NULL,
            brawler_global_id INT NOT NULL,
            power_points INT NOT NULL,
            updated_at DATETIME(6) NOT NULL,
            PRIMARY KEY (account_id, season_id, level)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

        CREATE TABLE IF NOT EXISTS box_claims (
            account_id BIGINT NOT NULL,
            claim_key VARCHAR(160) NOT NULL,
            delivery_type TINYINT NOT NULL,
            box_index INT NULL,
            source VARCHAR(64) NOT NULL,
            status VARCHAR(32) NOT NULL,
            first_seen_at DATETIME(6) NOT NULL,
            last_seen_at DATETIME(6) NOT NULL,
            attempts INT NOT NULL DEFAULT 1,
            PRIMARY KEY (account_id, claim_key),
            KEY idx_box_claims_account_time (account_id, last_seen_at),
            KEY idx_box_claims_status (status, last_seen_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        """;

    private readonly record struct LeaderboardSnapshot(long AccountId, string PlayerName, int Trophies,
        int MaxTrophies);
    private readonly record struct ResourceSnapshot(long AccountId, int Gold, int Gems, int BrawlPassTokens,
        int PowerPoints);
    private readonly record struct OfferPurchaseSnapshot(long AccountId, bool[] Purchased);
}

public readonly record struct BrawlPassSeasonState(int SeasonId, DateTime StartedAt, DateTime EndsAt);
