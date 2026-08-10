using Unity.NetCode;

/// <summary>
/// Sent from an admin's client to the server, asking it to apply a new set of difficulty
/// multipliers. The server re-validates admin privileges itself (see
/// DifficultyRequestReceiveSystem) - a client merely claiming to be admin here proves nothing.
/// </summary>
public struct RequestDifficultyChangeRpc : IRpcCommand
{
    public DifficultyValues Values;
}
