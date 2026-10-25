namespace BSL.v41.General.Network;

public static class ServerFeatureFlags
{
    // Battle/UDP is intentionally disabled while the V41 client flow is being stabilized.
    // Keep this hard-disabled until the battle payload and native client path are verified.
    public static bool BattlesEnabled => false;

    // Starter offers remain disabled until their V41 payload is verified by a real client.
    // Set BSL_ENABLE_STARTER_OFFERS=1 only after the client has been tested.
    public static bool StarterOffersEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("BSL_ENABLE_STARTER_OFFERS"), "1",
            StringComparison.OrdinalIgnoreCase);
}
