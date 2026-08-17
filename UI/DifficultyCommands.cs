using PugMod;
using UnityEngine.Scripting;

public static class DifficultyCommands
{
    [Preserve]
    [CommandWithModSupport("difficultytuner.toggle", "Shows or hides the difficulty tuner window (admin only).")]
    public static string ToggleTuner()
    {
        return DifficultyOverlay.Toggle();
    }
}
