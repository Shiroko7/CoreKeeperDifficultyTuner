using Unity.Entities;

/// <summary>
/// Establishes this server's per-world key (from ServerGuidCD, which isn't guaranteed to exist
/// on the very first tick, hence the retry) and loads that world's previously saved difficulty
/// values exactly once per session.
///
/// Loading is safe to do at any point: the values only affect how damage is scaled from that
/// moment on, and nothing in this mod carries per-entity state that could be double-applied by
/// loading a frame early or late.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(RunSimulationSystemGroup), OrderFirst = true)]
public partial class DifficultyPersistenceLoadSystem : SystemBase
{
    private bool _loaded;

    protected override void OnUpdate()
    {
        if (_loaded || !SystemAPI.HasSingleton<ServerGuidCD>())
        {
            return;
        }

        _loaded = true;
        string worldKey = SystemAPI.GetSingleton<ServerGuidCD>().Value.ToString();
        DifficultySettingsServerState.SetWorldKey(worldKey);
        if (DifficultySettingsPersistence.TryLoad(worldKey, out DifficultyValues values))
        {
            DifficultySettingsServerState.Apply(values);
        }
    }
}
