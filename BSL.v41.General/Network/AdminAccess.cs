namespace BSL.v41.General.Network;

/// <summary>
/// Server-side roles for the local test environment. Account #1 is the
/// reserved administrator/test accounts requested for this installation.
/// </summary>
public static class AdminAccess
{
    public static bool IsAdmin(long accountId)
    {
        return accountId is 1 or 2 or 3;
    }
}
