using BSL.v41.Logic.Environment.LaserMessage.Laser.Node_1;
using BSL.v41.Tools.LaserData;

namespace BSL.v41.General.Network;

/// <summary>
/// Uses the fingerprint version already sent in LoginOk as the server's
/// required client version. When the fingerprint is bumped, older APKs get a
/// normal LoginFailed update response instead of remaining on a loading screen.
/// </summary>
public static class ServerUpdatePolicy
{
    public static (int Major, int Build, int Minor) GetRequiredVersion()
    {
        var configured = Environment.GetEnvironmentVariable("BSL_REQUIRED_CLIENT_VERSION");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var parts = configured.Split('.', StringSplitOptions.TrimEntries);
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out var major) &&
                int.TryParse(parts[1], out var build) &&
                int.TryParse(parts[2], out var minor))
                return (major, build, minor);
        }

        return (Fingerprint.GetMajorVersion(), Fingerprint.GetBuildVersion(), Fingerprint.GetMinorVersion());
    }

    public static bool IsOutdated(LoginMessage loginMessage)
    {
        var client = (loginMessage.GetClientMajorVersion(), loginMessage.GetClientBuild(),
            loginMessage.GetClientMinor());
        var required = GetRequiredVersion();

        return client != required;
    }

    public static string GetRequiredVersionString()
    {
        var version = GetRequiredVersion();
        return $"{version.Major}.{version.Build}.{version.Minor}";
    }

    public static string GetUpdateUrl()
    {
        return Environment.GetEnvironmentVariable("BSL_UPDATE_URL") ?? string.Empty;
    }

    public static string GetUpdateReason()
    {
        return Environment.GetEnvironmentVariable("BSL_UPDATE_MESSAGE")
               ?? "Sunucu güncellendi. Lütfen APK'yı güncelleyin.";
    }
}
