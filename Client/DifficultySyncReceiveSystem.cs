using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

/// <summary>
/// Applies the server's broadcast difficulty values on every client (including the host's own
/// client world), so each client's local damage prediction uses the same numbers the server
/// does and the UI shows what's actually active.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(RunSimulationSystemGroup))]
public partial class DifficultySyncReceiveSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        Entities.WithAll<ReceiveRpcCommandRequest>().ForEach((Entity entity, in DifficultySyncRpc rpc) =>
        {
            DifficultyRuntime.Set(rpc.Values);
            ecb.DestroyEntity(entity);
        }).WithoutBurst().Run();

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}
