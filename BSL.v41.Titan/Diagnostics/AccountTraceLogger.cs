namespace BSL.v41.Titan.Diagnostics;

/// <summary>
/// Narrow diagnostic trace for accounts under investigation. It never records
/// tokens, passwords, email addresses, or raw packet bodies.
/// </summary>
public static class AccountTraceLogger
{
    private static readonly HashSet<long> TracedAccountIds = [3, 4];
    private static readonly object Sync = new();
    private static bool _fullCaptureInitialized;

    private static string TracePath(long accountId) => Path.Combine(
        Directory.GetCurrentDirectory(), "SaveBase", $"account_{accountId}_trace.log");

    private static string? FullCapturePath
    {
        get
        {
            var configured = System.Environment.GetEnvironmentVariable("BSL_ACCOUNT4_FULL_TRACE_PATH");
            if (string.IsNullOrWhiteSpace(configured)) return null;
            return Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(Directory.GetCurrentDirectory(), configured);
        }
    }

    public static void Log(long accountId, string phase, string details)
    {
        if (!TracedAccountIds.Contains(accountId)) return;

        try
        {
            var line = $"{DateTimeOffset.Now:O} [{phase}] {details}{Environment.NewLine}";
            lock (Sync)
            {
                // Keep the per-account files for post-mortem analysis, and
                // mirror the same sanitized line to stdout so PM2 captures it
                // in bsl-v41-out.log. Callers only pass non-secret details.
                try
                {
                    Console.Out.Write($"[ACCOUNT-TRACE] account={accountId} {line}");
                }
                catch
                {
                    // A logging sink must never affect the game server.
                }

                var path = TracePath(accountId);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line);

                if (accountId == 4)
                {
                    var fullCapturePath = FullCapturePath;
                    if (!string.IsNullOrWhiteSpace(fullCapturePath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullCapturePath)!);
                        if (!_fullCaptureInitialized)
                        {
                            File.WriteAllText(fullCapturePath,
                                $"# account=4 full capture started={DateTimeOffset.Now:O}{Environment.NewLine}");
                            _fullCaptureInitialized = true;
                        }

                        File.AppendAllText(fullCapturePath, line);
                    }
                }
            }
        }
        catch
        {
            // Diagnostics must never be allowed to break the server.
        }
    }
}
