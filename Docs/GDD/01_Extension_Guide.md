# 01 — Extension Guide

How to add features without editing existing systems.

## Add a system

1. Implement `ISimSystem` (`FES.Core`):
   - `Id`: stable, dotted (`"economy.taxes"`), declared as `const string SystemId`; derive the RNG stream
     with `static readonly uint Stream = DetHash.Of(SystemId)`.
   - `Phase` + `Order`: where it runs; use `SimOrder.Early/Default/Late`. Ties break by `Id` (ordinal).
   - `Initialize(ctx)`: resolve components (`ctx.World.Get<T>()`) and services (`ctx.Services.Get<T>()`) once.
   - `Execute(ctx)`: read/write components; publish `ISimEvent`s for notifications.
   - Own native memory? Also implement `IDisposable`; the pipeline disposes it.
   - Keep derived (non-saved) state? Implement `ILoadListener` to rebuild it after a load.
2. Register it from a module: `builder.AddSystem(new TaxSystem());`
3. Add the module to the builder: `SimulationBuilder.FromData(src, seed).AddModule(new TaxModule())`.

Rules: systems never call each other; share data through components, notify through events.
Event handlers must never change simulation state (events are for UI, logs and achievements).
To react to player actions, read `ctx.Commands.AppliedThisTurn`.
Use `ctx.IsDue(level)` to respect tier cadence, and `ctx.RandomFor(stream, entity)` for randomness.

## Add state

- Implement `IWorldComponent` (rebuilt each run) or `IPersistentComponent` (saved + hashed). Add
  `IDisposable` if it owns native memory (`array.DisposeIfCreated()` per field).
- Per-entity tables: derive from `NativeTable` and declare columns with `Column<T>()`; save, load and
  disposal then cover every column automatically.
- Persistent: choose a permanent `Key` (`"economy.taxes"`), start at `Version = 1`, write fields
  explicitly (never raw struct memory with padding). `NativeIO` writes/reads `NativeArray`/`NativeList`.
- Changing the layout later: bump `Version` and register an `ISaveMigration` for the old version on
  `GameSimulation.Migrations`.

## Replace a rule

Services registered with `SetDefault` can be overridden before modules run:

```csharp
SimulationBuilder.FromData(source, seed)
    .ConfigureServices(s => s.Set<ILodPolicy>(new MyLodPolicy()))
    .Build();
```

Replaceable today: `ILodPolicy`, `IMerchantBehavior`, `ICurrencyModel`.

## Add a player action

Implement `ICommand` (`Validate` must not mutate and must reject negative amounts; `Apply` mutates;
commands keep no simulation-derived state). To steer expansion direction, also implement
`IGuidanceSource.GetGuidance`. Enqueue with `GameSimulation.Enqueue(cmd)`; it runs at the start
of the next turn and publishes `CommandApplied` / `CommandRejected`.

## Add market flows

Producers, consumers and merchants add supply/demand through `MarketFlows.AddSupply/AddDemand`
(never `MarketStore` directly), so event modifiers apply. `TryFindNearestMarket` finds the node's own
market or the nearest ancestor that trades the good.

## Add market effects

Add a `MarketModifier` to `MarketModifierTable` (node subtree, category or all, target
Supply/Demand/Value, multiplier, expiry turn). Pricing folds all modifiers; it never needs to know
the cause. Random events need only a row in `events.csv`.

## Burst jobs

Keep jobs in the owning module, pass `NativeArray`s and value parameters, use integer/`Fx` math only.
Writes outside the job index need `[NativeDisableParallelForRestriction]` and must touch disjoint
ranges per index (e.g. a market's own cell range). Add a parity test comparing the job against a
managed run (see `PricingSystem.ForceManaged`).

## Checklist before merging a system

- [ ] EditMode tests pass (`FES.Tests.EditMode`), including determinism and save/load.
- [ ] New persistent state round-trips through save/load.
- [ ] New tunable numbers live in `config.csv` / `SimulationConfig`, not in system code.
- [ ] No `UnityEngine`, `System.Random`, `DateTime.Now`, `float`/`double` in simulation folders.
- [ ] Scale test (`Category=Scale`, explicit) still within budget.
