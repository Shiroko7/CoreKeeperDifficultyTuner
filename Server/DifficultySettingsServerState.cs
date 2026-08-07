/// <summary>
/// Authoritative difficulty state, server-side. Applying here also pushes the values into
/// DifficultyRuntime, which is what DifficultyDamageSystem actually reads on both worlds.
/// </summary>
public static class DifficultySettingsServerState
{
    public static DifficultyValues Current { get; private set; } = DifficultyValues.Normal;

    /// <summary>
    /// Stable per-world identifier (from ServerGuidCD), set once by
    /// DifficultyPersistenceLoadSystem as soon as it's available. Scopes saved settings to
    /// this specific world so a brand-new world always starts at vanilla rather than
    /// inheriting whatever was last used elsewhere.
    /// </summary>
    public static string WorldKey { get; private set; } = "";

    public static void SetWorldKey(string worldKey)
    {
        WorldKey = worldKey;
    }

    public static void Apply(DifficultyValues values)
    {
        Current = values;
        DifficultyRuntime.Set(values);
    }
}
