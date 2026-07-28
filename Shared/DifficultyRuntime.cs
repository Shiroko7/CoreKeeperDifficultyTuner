/// <summary>
/// The single set of difficulty values that DifficultyDamageSystem reads, on BOTH the server
/// world and every client world.
///
/// Why shared rather than server-only: the game's own UpdateHealthFromBufferSystem is declared
/// ServerSimulation | ClientSimulation, meaning clients locally predict damage for predicted
/// ghosts. If only the server scaled damage, every hit would mispredict on the client and the
/// health bar would visibly rubber-band as the server correction arrived. Running the same
/// scaling on both sides off the same values keeps prediction in agreement.
///
/// Written by the server (DifficultySettingsServerState.Apply) and by each client on receiving
/// DifficultySyncRpc. On a listen-server host both worlds live in one process and therefore
/// share this static - both write identical values, so that's consistent rather than a race.
/// A remote client that hasn't received its first sync yet simply runs vanilla (all 1.0) for
/// those few frames, which is also exactly what the server does for it until then.
/// </summary>
public static class DifficultyRuntime
{
    public static DifficultyValues Current { get; private set; } = DifficultyValues.Normal;

    public static void Set(DifficultyValues values)
    {
        Current = values;
    }
}
