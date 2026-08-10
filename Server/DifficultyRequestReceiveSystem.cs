using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// Receives RequestDifficultyChangeRpc from clients. Admin privileges are re-checked here on
/// the server via the connection entity's ConnectionAdminLevelCD - the same
/// GetAdminLevelOnServer() extension the game's own NetworkCommandServerSystem/PlayerCommand
/// ServerSystem use to gate admin-only actions. A client claiming to be admin in the request
/// itself would prove nothing; this is the actual trust boundary.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(RunSimulationSystemGroup))]
public partial class DifficultyRequestReceiveSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var adminLookup = GetComponentLookup<ConnectionAdminLevelCD>(true);
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        bool applied = false;

        Entities.ForEach((Entity entity, in RequestDifficultyChangeRpc rpc, in ReceiveRpcCommandRequest rpcSource) =>
        {
            int adminLevel = adminLookup.GetAdminLevelOnServer(rpcSource.SourceConnection);
            if (adminLevel > 0)
            {
                DifficultySettingsServerState.Apply(rpc.Values);
                if (!string.IsNullOrEmpty(DifficultySettingsServerState.WorldKey))
                {
                    DifficultySettingsPersistence.Save(DifficultySettingsServerState.WorldKey, rpc.Values);
                }

                applied = true;
            }
            else
            {
                Debug.LogWarning("[DifficultyTuner] Ignored a difficulty change request from a non-admin connection.");
            }

            ecb.DestroyEntity(entity);
        }).WithoutBurst().Run();

        ecb.Playback(EntityManager);
        ecb.Dispose();

        if (applied)
        {
            Entity syncEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(syncEntity, new DifficultySyncRpc { Values = DifficultySettingsServerState.Current });
            EntityManager.AddComponentData(syncEntity, new SendRpcCommandRequest { TargetConnection = Entity.Null });
        }
    }
}
