---
doc: EXTENSION-GUIDE
title: How to extend the simulation
status: final
updated: 2026-11-01
---

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
2. Register it from a module: `builder.AddSystem(new TaxSystem());`. The module lists the modules whose
   components it reads in `Requires` (e.g. `{ "world", "economy" }`); they register first.
3. Add the module to the builder: `SimulationBuilder.FromData(src, seed).AddModule(new TaxModule())`.
   The order of `AddModule` calls does not matter beyond tie-breaking.

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
- Changing the layout later: bump `Version` and register an `ISaveMigration` for the old version from
  the owning module with `builder.AddMigration(...)` (see `PlayerStateV1Migration`).
- A new data file: add its name to `DataFiles.All`, or Android builds will not load it.

## Replace a rule

Services registered with `SetDefault` can be overridden before modules run:

```csharp
SimulationBuilder.FromData(source, seed)
    .ConfigureServices(s => s.Set<ILodPolicy>(new MyLodPolicy()))
    .Build();
```

Replaceable today: `ILodPolicy`, `IMerchantBehavior` (`IdleMerchantBehavior` turns trade off), `ICurrencyModel`.

Any single system can also be replaced or dropped by id, without editing its module:

```csharp
SimulationBuilder.FromData(source, seed)
    .ReplaceSystem(new MyPricingSystem())   // its Id must be "economy.pricing"
    .RemoveSystem("agents.merchants")
    .Build();
```

The replacement keeps the id, so it keeps the RNG stream and pipeline slot. Naming an id that no
module registers is a build error.

## Read state from presentation

UI code uses `GameSimulation.View` (`SimulationView`) for queries and `GameSimulation.Actions`
(`PlayerActions`) for player actions. Views return copies (`MarketQuote`, `RouteInfo`, `FacilityInfo`)
and fill caller-owned lists to avoid allocations per frame. New queries go into `SimulationView`;
never hand components or `NativeArray`s to presentation.

## Add a player action

Implement `ICommand` (`Validate` must not mutate and must reject negative amounts; `Apply` mutates;
commands keep no simulation-derived state). To steer expansion direction, also implement
`IGuidanceSource.GetGuidance`. Enqueue with `GameSimulation.Enqueue(cmd)`; it runs at the start
of the next turn and publishes `CommandApplied` / `CommandRejected`. Add a matching method to
`PlayerActions` so presentation can issue it without referencing the command's assembly.

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
- [ ] `CodeStandardsTests` pass (05_Refactoring_Standards).
- [ ] `GoldenRunTests` pass. A change meant to alter the simulation records the new hashes in the same commit; a refactoring records none.
