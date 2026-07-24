using PugMod;
using UnityEngine;

public class DifficultyTunerMod : IMod
{
    public const string ModId = "DifficultyTuner";

    internal static DifficultyTunerMod Instance { get; private set; }

    public void EarlyInit()
    {
        Instance = this;

        // Needed so the difficultytuner.toggle chat command is reachable after mods load
        // (mirrors the SDK's own ModCommandsExample/EnableConsole.cs).
        Manager.enableConsole = true;

        Debug.Log("[DifficultyTuner] Loaded.");
    }

    public void Init()
    {
        DifficultyOverlay.EnsureInstance();
    }

    public void Shutdown()
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    public void ModObjectLoaded(Object obj)
    {
    }

    public void Update()
    {
    }
}
