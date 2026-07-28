using System;

/// <summary>
/// One full set of difficulty multipliers. Blittable so it can be embedded directly in both
/// RPC structs (client request and server broadcast) as well as used as plain runtime state.
/// [Serializable] so JsonUtility can also read/write it directly for disk persistence
/// (DifficultySettingsPersistence).
///
/// Every value here is applied purely as damage math at the moment damage happens - see
/// DifficultyDamageSystem. Nothing in this struct ever mutates saved entity data.
/// "Health" multipliers mean effective durability (a 2x health mob takes half damage), not a
/// literal change to HealthCD.maxHealth.
/// </summary>
[Serializable]
public struct DifficultyValues : IEquatable<DifficultyValues>
{
    public float MobHealthMult;
    public float MobDamageMult;
    public float BossHealthMult;
    public float BossDamageMult;
    public float PlayerHealthMult;
    public float PlayerDamageMult;
    public float MultiplayerScalingFactor;

    /// <summary>
    /// Tilts how strongly the mob/boss multipliers apply across the game's tier curve, as an
    /// exponent on (entityLevel / referenceLevel). 0 = flat (a multiplier means the same thing
    /// to a tier-2 slime boss and a tier-20 endgame boss). Positive = weight the change toward
    /// late game. Negative = weight it toward early game.
    ///
    /// This exists because the game's own curves are wildly asymmetric: boss HP is
    /// 300*level^2.45 while enemy damage is only 13*level^1.15. Across the real progression
    /// (Glurch at level ~2.4 to Core Commander at level ~20) boss HP grows ~190x while boss
    /// damage grows ~12x. A single flat multiplier is therefore uniform in relative terms but
    /// lands very differently in felt difficulty at the two ends of the game.
    /// </summary>
    public float TierCurve;

    public static readonly DifficultyValues Normal = new DifficultyValues
    {
        MobHealthMult = 1f,
        MobDamageMult = 1f,
        BossHealthMult = 1f,
        BossDamageMult = 1f,
        PlayerHealthMult = 1f,
        PlayerDamageMult = 1f,
        MultiplayerScalingFactor = 1f,
        TierCurve = 0f,
    };

    /// <summary>
    /// True when every multiplier is 1.0 - i.e. the mod is a complete no-op and damage passes
    /// through untouched.
    /// </summary>
    public bool IsVanilla => Equals(Normal);

    public bool Equals(DifficultyValues other)
    {
        const float eps = 0.005f;
        return Close(MobHealthMult, other.MobHealthMult, eps)
            && Close(MobDamageMult, other.MobDamageMult, eps)
            && Close(BossHealthMult, other.BossHealthMult, eps)
            && Close(BossDamageMult, other.BossDamageMult, eps)
            && Close(PlayerHealthMult, other.PlayerHealthMult, eps)
            && Close(PlayerDamageMult, other.PlayerDamageMult, eps)
            && Close(MultiplayerScalingFactor, other.MultiplayerScalingFactor, eps)
            && Close(TierCurve, other.TierCurve, eps);
    }

    private static bool Close(float a, float b, float eps)
    {
        return Math.Abs(a - b) < eps;
    }
}
