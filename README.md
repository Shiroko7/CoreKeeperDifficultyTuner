# DifficultyTuner

A Core Keeper mod that adds difficulty sliders — scale incoming and outgoing
damage, per world, in multiplayer, without writing a single byte to your save.

This folder mirrors what becomes `Assets/DifficultyTuner/` inside the
[Core Keeper Mod SDK](https://github.com/Pugstorm/CoreKeeperModSDK) Unity
project. It is not buildable standalone — drop it in there and package from the
Editor.

## The design, and why it is the way it is

Core Keeper runs on Unity DOTS/ECS. The naive way to build this mod is to find
enemy health and multiply it. That version existed, and it was wrong in four
separate ways. What replaced it is a **single stateless pass** over the damage
queue.

### One hook instead of a dozen

Every damage and heal in the game — melee, ranged, magic, explosions, boss
specials — funnels through one global `HealthChangeBuffer` each tick.
`DifficultyDamageSystem` rewrites entries in that buffer in place, scheduled
`[UpdateBefore(typeof(UpdateHealthFromBufferSystem))]` so it lands before the
game consumes them.

That is the whole implementation. The alternative was patching the dozen-plus
attack-behaviour components that each carry their own flat damage field, and
missing whichever one ships next patch.

### Scaling damage, not stats

Both sliders resolve into one multiplier on a **transient** damage event:

```
finalMultiplier = sourceDamageMult / victimHealthMult
```

So a "2× health" enemy takes half damage rather than having its
`HealthCD.maxHealth` rewritten. Nothing persistent is ever mutated. That single
decision buys four properties:

- **Uninstalling is instant and total.** Nothing was written to the world save,
  so there is nothing to unwind. No corrupted characters, no 4000-HP slimes left
  behind.
- **Values cannot compound.** No per-entity state means nothing to re-derive —
  not across restarts, and not across the entity destroy/recreate cycle that
  `DestroyEntityWhenNoNearbyPlayerCD` drives constantly as players move around.
- **No rounding drift.** There is no integer round-trip, so repeated slider
  adjustments cannot accumulate error into saved stats.
- **No double-application.** The game's own `UpdateHealthByMaxHealthSystem`
  independently rescales health whenever a player's max health changes. A mod
  that also rescales health fights it. This one never touches that system's
  inputs.

Healing is deliberately left unscaled. A health multiplier reduces incoming
damage but does not proportionally weaken your potions, which keeps regen
feeling normal and avoids any interaction with `HealthRegenerationCD`.

### Multiplayer: the server decides

Difficulty is server state, not a client preference, so the flow is the
standard authoritative one:

- `RequestDifficultyChangeRpc` — client asks.
- `DifficultyRequestReceiveSystem` — server validates and applies.
- `DifficultySyncRpc` + `DifficultyNewConnectionSyncSystem` — server pushes
  current values to everyone, including clients that join later.
- `DifficultySyncReceiveSystem` — client applies what it is told.

The damage system itself runs under
`ServerSimulation | ClientSimulation` so prediction stays consistent and you do
not get rubber-banding health bars.

### Persistence, and a sandbox that bites

Settings are keyed **per world** (via `ServerGuidCD`), so a new world always
starts at Normal instead of inheriting whatever you set last time.

Storage goes through `PugMod.API.Config`, and that is not a stylistic choice.
Mods load into a sandboxed assembly (RoslynCSharp / Trivial.CodeSecurity) that
**rejects raw `System.IO` outright**. The first version of the persistence layer
used `System.IO.File` and the entire mod failed to load with *"failed code
security verification / Illegal Namespace References."* `API.Config` is the
sanctioned substitute — internally it routes through `API.ConfigFilesystem`,
which is what is actually whitelisted.

This is the kind of thing the modding wiki does not tell you.

## Using it

`Manager.enableConsole` is set in `EarlyInit` so chat commands are reachable
after mods load. In-game:

```
/difficultytuner.toggle
```

brings up the overlay (`UI/DifficultyOverlay.cs`); commands live in
`UI/DifficultyCommands.cs`.

## Layout

```
DifficultyTunerMod.cs   IMod entry point
Shared/                 DifficultyDamageSystem — the entire mechanic
Server/                 authoritative state, persistence, request handling
Client/                 receives synced values
Network/                the two RPCs
UI/                     overlay + chat commands
```

## Status

Compiles against the SDK. Built alongside
[CombatMeter](https://github.com/Shiroko7/CoreKeeperCombatMeter), which is where
the ECS reverse-engineering notes live if you want the longer version of how
this game's internals were worked out.
