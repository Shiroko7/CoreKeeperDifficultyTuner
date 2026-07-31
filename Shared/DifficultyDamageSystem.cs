using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// The entire difficulty implementation, in one stateless pass.
///
/// Rewrites HealthChangeBuffer entries in place before the game's own
/// UpdateHealthFromBufferSystem consumes them. That buffer is the single global queue every
/// damage/heal in the game funnels through each tick, so one hook here covers melee, ranged,
/// magic, explosions and boss specials without touching the dozen+ separate attack-behavior
/// components that each carry their own flat damage field.
///
/// DESIGN: nothing here ever writes to persistent entity data. Both "damage" and "health"
/// sliders resolve into a single multiplier applied to a transient damage event:
///
///     finalMultiplier = sourceDamageMult / victimHealthMult
///
/// A "health" multiplier therefore means effective durability - a 2x-health mob takes half
/// damage rather than having its HealthCD.maxHealth rewritten. This is deliberate and is what
/// makes the mod safe:
///   * Nothing is written to the world save, so uninstalling is instant and total.
///   * There is no per-entity state to re-derive, so values cannot compound - not across
///     restarts, and not across the entity destroy/recreate cycle that
///     DestroyEntityWhenNoNearbyPlayerCD drives as players move around the world.
///   * There is no integer round-trip, so repeated adjustments cannot accumulate rounding
///     drift into saved stats.
/// An earlier version of this mod did mutate HealthCD/SpawnerCD and hit all three of those
/// problems, plus double-application against the game's own UpdateHealthByMaxHealthSystem
/// (which independently rescales health whenever a player's maxHealth changes). Scaling damage
/// instead of stats avoids that system entirely.
///
/// Healing is intentionally left unscaled: a health multiplier reduces incoming damage but
/// does not make heals proportionally weaker, which keeps potions/regen feeling normal and
/// avoids any interaction with HealthRegenerationCD.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(UpdateHealthSystemGroup))]
[UpdateBefore(typeof(UpdateHealthFromBufferSystem))]
public partial class DifficultyDamageSystem : SystemBase
{
    protected override void OnUpdate()
    {
        DifficultyValues settings = DifficultyRuntime.Current;
        if (settings.IsVanilla)
        {
            return;
        }

        if (!SystemAPI.HasSingleton<WorldInfoCD>() || !SystemAPI.HasSingleton<HealthChangeBuffer>())
        {
            return;
        }

        WorldInfoCD worldInfo = SystemAPI.GetSingleton<WorldInfoCD>();

        // The world's own Casual/Normal/Hard mode bakes a flat x2 (Hard) / x0.5 (Casual) into
        // enemy and boss attack damage at conversion time - verified across
        // MeleeAttackStateConverter, RangeAttackStateConverter, ExplodeStateConverter and
        // CoreBossConverter, all gated identically on the attacker having EnemyAuthoring.
        // Dividing it back out makes the mob/boss damage sliders absolute: 100% always means
        // vanilla-Normal damage, whichever mode this world was created in.
        float worldModeDamageMult = 1f;
        if (worldInfo.IsWorldModeEnabled(WorldMode.Hard))
        {
            worldModeDamageMult = 2f;
        }
        else if (worldInfo.IsWorldModeEnabled(WorldMode.Casual))
        {
            worldModeDamageMult = 0.5f;
        }

        // WorldInfoCD.numberPlayers is a [GhostField], so server and client agree on it.
        // Counting PlayerGhost entities locally instead would risk the client seeing a
        // different number than the server and mispredicting.
        int playerCount = math.max(1, worldInfo.numberPlayers);
        float playerCountFactor = playerCount > 1 ? math.pow(playerCount, 0.6f) : 1f;
        float mpBlend = 1f + (playerCountFactor - 1f) * settings.MultiplayerScalingFactor;

        var changes = SystemAPI.GetSingletonBuffer<HealthChangeBuffer>(false);
        if (changes.Length == 0)
        {
            return;
        }

        var ownerLookup = GetComponentLookup<OwnerReferenceCD>(true);
        var playerGhostLookup = GetComponentLookup<PlayerGhost>(true);
        var enemyLookup = GetComponentLookup<EnemyCD>(true);
        var bossLookup = GetComponentLookup<BossCD>(true);
        var levelLookup = GetComponentLookup<LevelCD>(true);
        float tierCurve = settings.TierCurve;

        for (int i = 0; i < changes.Length; i++)
        {
            HealthChange change = changes[i].healthChange;
            if (change.amount >= 0)
            {
                continue; // only scale damage, never healing
            }

            // How durable is the thing being hit? Mobs and bosses additionally get the tier
            // tilt applied from their own level; players have no tier so they never do.
            float victimHealthMult;
            if (playerGhostLookup.HasComponent(change.entity))
            {
                victimHealthMult = settings.PlayerHealthMult;
            }
            else if (bossLookup.HasComponent(change.entity))
            {
                victimHealthMult = settings.BossHealthMult * mpBlend * TierFactor(change.entity, levelLookup, tierCurve);
            }
            else if (enemyLookup.HasComponent(change.entity))
            {
                victimHealthMult = settings.MobHealthMult * mpBlend * TierFactor(change.entity, levelLookup, tierCurve);
            }
            else
            {
                // Tiles, plants, structures, critters - not part of this mod's scope.
                continue;
            }

            // How hard does the attacker hit? Attribution walks OwnerReferenceCD up to 10 hops
            // so a minion/turret/projectile counts as its owner, matching the game's own
            // kill-attribution logic in UpdateHealthFromBufferSystem.
            ResolveSourceCategory(change.causedByEntity, ownerLookup, playerGhostLookup, bossLookup, enemyLookup,
                out bool sourceIsPlayer, out bool sourceIsBoss, out bool sourceIsEnemy, out Entity resolvedSource);

            float sourceDamageMult = 1f;
            if (sourceIsPlayer)
            {
                sourceDamageMult = settings.PlayerDamageMult;
            }
            else if (sourceIsBoss)
            {
                sourceDamageMult = settings.BossDamageMult / worldModeDamageMult * TierFactor(resolvedSource, levelLookup, tierCurve);
            }
            else if (sourceIsEnemy)
            {
                sourceDamageMult = settings.MobDamageMult / worldModeDamageMult * TierFactor(resolvedSource, levelLookup, tierCurve);
            }

            float multiplier = sourceDamageMult / math.max(0.0001f, victimHealthMult);
            if (math.abs(multiplier - 1f) < 0.0001f)
            {
                continue;
            }

            // Mutate ONLY amount and write the same struct back. HealthChange carries a dozen
            // other fields (optionalPositionToDropLootWhenDamaged, skipLootDropOnDestroy,
            // pullLootToPlayer, bypassDamageReduction, damagedByExplosion, applyToNonPredicted,
            // ...) - constructing a fresh struct here would silently blank all of them and
            // break loot drops and prediction flags.
            // Damage is negative in this buffer. Keep any real hit at >= 1 damage so a low
            // multiplier can never make something unkillable by rounding a hit down to zero.
            change.amount = -math.max(1, (int)math.round(-change.amount * multiplier));
            changes[i] = new HealthChangeBuffer { healthChange = change };
        }
    }

    /// <summary>
    /// Reference level the flat multipliers are anchored to. Level 10 is the Sea tier
    /// (Morpha/Omoroth) - roughly mid-game - so a positive TierCurve makes changes bite harder
    /// after that point and softer before it, rather than only ever scaling one direction.
    /// </summary>
    private const float TierReferenceLevel = 10f;

    /// <summary>
    /// (level / referenceLevel) ^ curve. Returns exactly 1 when the curve is flat or the
    /// entity has no LevelCD (not everything that can be damaged carries an area level), so an
    /// unknown tier degrades to the plain flat multiplier rather than to something arbitrary.
    /// </summary>
    private static float TierFactor(Entity entity, ComponentLookup<LevelCD> levelLookup, float curve)
    {
        if (math.abs(curve) < 0.0001f || entity == Entity.Null || !levelLookup.HasComponent(entity))
        {
            return 1f;
        }

        float level = math.max(1f, levelLookup[entity].level);
        return math.pow(level / TierReferenceLevel, curve);
    }

    private static void ResolveSourceCategory(
        Entity causedByEntity,
        ComponentLookup<OwnerReferenceCD> ownerLookup,
        ComponentLookup<PlayerGhost> playerGhostLookup,
        ComponentLookup<BossCD> bossLookup,
        ComponentLookup<EnemyCD> enemyLookup,
        out bool isPlayer,
        out bool isBoss,
        out bool isEnemy,
        out Entity resolved)
    {
        isPlayer = false;
        isBoss = false;
        isEnemy = false;
        resolved = Entity.Null;
        if (causedByEntity == Entity.Null)
        {
            return;
        }

        Entity current = causedByEntity;
        for (int i = 0; i <= 10; i++)
        {
            resolved = current;

            if (playerGhostLookup.HasComponent(current))
            {
                isPlayer = true;
                return;
            }

            if (bossLookup.HasComponent(current))
            {
                isBoss = true;
                return;
            }

            if (enemyLookup.HasComponent(current))
            {
                isEnemy = true;
                return;
            }

            if (!ownerLookup.HasComponent(current))
            {
                return;
            }

            current = ownerLookup[current].owner;
        }
    }
}
