using PugMod;

/// <summary>
/// Persists difficulty values via PugMod.API.Config, keyed per-world (see
/// DifficultySettingsServerState.WorldKey / ServerGuidCD) so different worlds don't bleed
/// into each other's settings and a brand-new world always starts at Normal.
///
/// Mods run in a sandboxed assembly (RoslynCSharp/Trivial.CodeSecurity) that rejects raw
/// System.IO calls outright - confirmed the hard way: a first version of this file used
/// System.IO.File directly and the whole mod failed to load with "failed code security
/// verification" / "Illegal Namespace References". API.Config is the sanctioned mod-facing
/// substitute (it goes through API.ConfigFilesystem internally, which is what's actually
/// whitelisted).
/// </summary>
public static class DifficultySettingsPersistence
{
    private const string Mod = "DifficultyTuner";
    private const string Key = "Values";

    public static void Save(string worldKey, DifficultyValues values)
    {
        API.Config.Set(Mod, worldKey, Key, values);
    }

    public static bool TryLoad(string worldKey, out DifficultyValues values)
    {
        return API.Config.TryGet(Mod, worldKey, Key, out values);
    }
}
