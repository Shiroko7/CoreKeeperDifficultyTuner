using Unity.NetCode;

/// <summary>
/// Broadcast from the server to every client (including the host's own client world)
/// whenever the active difficulty values change, so every player's UI reflects the current
/// settings live - not just the admin who made the change.
/// </summary>
public struct DifficultySyncRpc : IRpcCommand
{
    public DifficultyValues Values;
}
