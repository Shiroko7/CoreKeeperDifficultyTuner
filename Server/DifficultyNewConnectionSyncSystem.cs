using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

/// <summary>
/// Sends the current difficulty values to a player's connection the moment their PlayerGhost
/// appears server-side. Without this, a player joining mid-session (after the host already
/// tuned difficulty, possibly in a previous run and reloaded from disk) would see the UI's
/// default Normal values until some unrelated change happened to trigger a broadcast.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(RunSimulationSystemGroup))]
public partial class DifficultyNewConnectionSyncSystem : SystemBase
{
    private struct DifficultySyncedCD : IComponentData
    {
    }

    protected override void OnUpdate()
    {
        var newPlayersQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerGhost>(), ComponentType.Exclude<DifficultySyncedCD>());
        if (newPlayersQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        var playerGhostLookup = GetComponentLookup<PlayerGhost>(true);
        var newPlayers = newPlayersQuery.ToEntityArray(Allocator.Temp);
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (Entity e in newPlayers)
        {
            Entity connection = playerGhostLookup[e].connection;
            Entity syncEntity = ecb.CreateEntity();
            ecb.AddComponent(syncEntity, new DifficultySyncRpc { Values = DifficultySettingsServerState.Current });
            ecb.AddComponent(syncEntity, new SendRpcCommandRequest { TargetConnection = connection });
            ecb.AddComponent<DifficultySyncedCD>(e);
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
        newPlayers.Dispose();
    }
}
