---
doc: ARCHITECTURE
title: Core architecture
status: final
updated: 2026-11-01
---

# 00 — Core Architecture

Status: economy simulation implemented (2026-11-01). Code: `Assets/_Game/`. Tests: `Assets/_Game/Tests/EditMode`.

## Goals

- One turn = one week (52 turns/year). The player starts as a peddler in one kingdom and grows from
  village markets to a five-continent market economy, automating lower tiers along the way.
- Ships on Android and Windows: memory and per-turn cost are budgeted from day one.
- A stable skeleton that later fine-grained systems plug into without editing existing code.

## Assemblies

| Assembly | Folder | Depends on | Contents |
|---|---|---|---|
| `FES.Core` | `Core/` | Collections | ids, shared enums (incl. `GuidanceAxis`), `Fx` fixed-point, `DetRandom`, `SimClock`, `WorldState`, `ISimSystem`/`ISimModule`, `TurnPipeline`, `EventBus`, commands, `TierSchedule`, `NativeIO`, `NativeTable` |
| `FES.Data` | `Data/` | Core | CSV loader, definitions, `SimulationConfig`, `GameDatabase`, `CsvMapSource` |
| `FES.World` | `World/` | Core, Data | `NodeTable` (hierarchy), `RouteTable`, `WorldBuilder`, `WorldModule` |
| `FES.Economy` | `Economy/` | Core, Data, World | `MarketStore` (sparse), `MarketFlows`, Burst pricing/consumption jobs, modifiers + world events, currency |
| `FES.Agents` | `Agents/` | Core, Data, World, Economy | player, travel, merchants, facilities, Burst production, LOD, guidance, player commands, banks, finance, interest groups, policy, labor, tax, state economy |
| `FES.Persistence` | `Persistence/` | Core | save container, section versioning, migrations |
| `FES.Simulation` | `Simulation/` | all above | `SimulationBuilder` (composition root), `GameSimulation` facade, `SimulationView` (read-only queries), `PlayerActions` |
| `FES.Unity` | `Unity/` | Core, Data, Simulation | `SimulationRunner` MonoBehaviour, `StreamingAssetsData` loader — the only code allowed to use `UnityEngine` |

Modules declare the modules they need (`ISimModule.Requires`); `ModuleOrder.Sort` registers them in
dependency order, stable by the order added, and rejects missing modules and cycles.
Single systems can be swapped or dropped by id (`SimulationBuilder.ReplaceSystem` / `RemoveSystem`)
without touching the module that registers them.

Presentation reads state only through `GameSimulation.View` (copies and immutable definitions, never
components or native arrays) and changes it only through `GameSimulation.Actions`, which queue commands.
That is why `FES.Unity` needs no reference to World, Economy or Agents.

`FES.Data` and `FES.Persistence` set `noEngineReferences` (compiler-enforced). The other simulation
assemblies cannot, because `NativeArray` and the job system live in `UnityEngine.CoreModule`;
`ArchitectureTests` enforces the rule at source level for them (no `UnityEngine`, `System.Random`,
`DateTime.Now`, `float`/`double` in simulation folders).

## World model

- **Hierarchy**: one flat SoA `NodeTable`. Levels: Continent > Nation > Region > City > Village.
  A village may hang directly off a region. Nodes are sorted by level, so each level is a contiguous
  index range (`LevelStart`/`LevelCount`) that tier jobs iterate.
- **Markets**: every node has exactly one market; market index = node index.
- **Routes**: directed edges with travel turns and per-unit cost, CSR outgoing lists.
- **Travel**: `TravelCommand` starts a trip along a route; the player counts as at the origin until
  `agents.travel` moves them on turn (start + travel turns), publishing `PlayerArrived`.

## Data loading

`StreamingAssetsData.Load` (coroutine) reads the data folder directly where StreamingAssets is a plain
folder (Windows, Editor) and fetches `DataFiles.All` with `UnityWebRequest` where it is a URL (Android
APK). `SimulationRunner.Simulation` is null until loading finishes (`IsReady`).

Target scale (measured by `FullScale_MemoryAndTurnTime`):

| Item | Count |
|---|---|
| Continents / nations / regions / cities / villages | 5 / 25 / 50 / 250 / 1,250 (1,580 markets) |
| Goods | 20 categories x 50 goods x 4 grades = 4,000 independent items |
| Merchant groups | 9,250 (village 3, city 10, region 30, nation 50, continent 50 per node) |
| Producer groups | 8,500 (primary 4/village, secondary 4/city, tertiary 2/village) |

## Markets (sparse SoA)

Each level carries only the goods its `market_profiles.csv` rules allow. A market's cells are a
contiguous range sorted by good id. Per cell: `CellGood, Price, PrevPrice, Stock, Supply, Demand`
(6 x int = 24 B). With the test profile (village 400 items, city 1,500, region and above 4,000):

- 1,195,000 cells, **27.4 MB** market memory (a dense 4,000 x 1,580 layout would be ~150 MB).

`Supply`/`Demand` are accumulators since the market's last price update, so a tier that updates every
N turns consumes N turns of flow in one step, and its price step scales with N (`TierSchedule.ElapsedTurns`).

All flows go through `MarketFlows`, which applies the active supply/demand event modifiers at the moment
the flow happens. A modifier therefore affects exactly the turns it is active, even on markets that
reprice every 4 turns. Value modifiers are read only at repricing, so `events.csv` rejects Value events
shorter than the slowest repricing period they cover.

## Numbers and determinism

- All simulation state is integer or `Fx` (Q47.16 in a `long`). Results are bit-identical on ARM64 and
  x64, saves are portable, and the Burst job equals managed execution (`Pricing_BurstJob_MatchesManagedExecution`).
- Rounding is symmetric (half away from zero: `Fx.MulInt`, `Fx.RoundShift`). Floor rounding would bias
  every flow and price step downward.
- The data hash covers the definition files' text (line endings and BOM normalized; `.gitattributes`
  forces LF for `*.csv`), the map, and the effective config values — code defaults included, so
  changing a default in `SimulationConfig` invalidates old saves just like editing `config.csv`.
- Randomness: `DetRandom.For(seed, turn, stream, entity, salt)` — stateless, independent of thread count
  and iteration order. Stream ids come from `DetHash.Of("system.id")` (never `string.GetHashCode`).
- `WorldState.ComputeHash()` hashes every persistent component; tests compare hashes across runs and
  across save/load.

## Turn pipeline

Phases run in order; within a phase systems run by `(Order, Id)`; events dispatch after each phase.

| Phase | Systems (Order band) |
|---|---|
| Commands | `core.commands` — validate + apply queued player commands; records `Commands.AppliedThisTurn`; `agents.travel` (Late) — completes trips due this turn |
| Lod | `agents.lod` — `ILodPolicy` marks nodes Detailed / Aggregate |
| Environment | `economy.world_events` (Default) — expire/roll modifiers; `economy.modifier_fold` (Late) — refold `MarketFlows` |
| Production | `agents.production` (Default, Burst) |
| Agents | `agents.merchants` — `IMerchantBehavior` (default: `ArbitrageMerchantBehavior`) |
| Markets | `economy.pricing` (Late, Burst) — household consumption (per turn for slow levels, inline for every-turn levels), then reprices levels due per `TierSchedule`; `agents.household_spending` (Late + 1) |
| Finance | `economy.currency` (Default) — yearly minting, price index, exchange rates (pricing sees it next turn); then the Late band below |
| Guidance | `agents.guidance` — decay, then add signals of this turn's applied commands |
| Cleanup | `agents.groups`, `agents.licenses` — yearly dues and expiry |

The Finance phase's Late band, by offset from `SimOrder.Late` (the constants are in `FinanceOrder`):

| Offset | Systems |
|---|---|
| 0 | `agents.banks`, `agents.roads` |
| 5 | `agents.state_spending` |
| 6 | `agents.labor` |
| 7 | `agents.defense` |
| 10 | `agents.finance` |
| 15 | `agents.income_tax` |
| 18 | `agents.state_projects` |
| 19 | `agents.subsidies` |
| 20 | `agents.households` |
| 30 | `agents.policy` |
| 40 | `agents.merchant_generation` |

Order bands: `SimOrder.Early = -100`, `Default = 0`, `Late = 100`. Flows accumulate at Early/Default and
are consumed at Late.

Default cadence (`config.csv`): village 1, city 1, region 2, nation 4 (offset 1), continent 4.

Measured on the full-scale world (Editor, Windows desktop, safety checks on): **33.6 ms/turn** on average from
turn 60 to 182 (`[FES scale settled]`). Not yet profiled on Android.

Production is a single-threaded Burst job: facilities share market cells and cells clamp after every
add, so facility order must stay fixed. Each facility's target cells and modifier slots are resolved
once and cached until the facility table changes. Moving it from managed code to Burst left the
simulation hash unchanged (`Production_BurstJob_MatchesManagedExecution` guards it).

## Level of detail

`LodState` holds per-node `SimDetail`, recomputed each turn (and after a load) by the `ILodPolicy`
service, which is the single authority: systems only ask `IsDetailed(node)`. Default: the subtree of the
player's ancestor at `lod.focus_level` (Region) plus every node hosting a player-owned facility or
merchant is Detailed. Systems branch per entity: Detailed facilities roll weekly efficiency variance,
Aggregate ones use the expected value; merchants call `RunDetailed` per group (and for every group on a trip) and `RunAggregate` once for all
aggregate nodes.

## Pricing

`PriceKernel.Step` per cell: pressure = (demand − effective supply) / total, where effective supply
includes 25% of carried stock; step = pressure x adjust rate x elapsed turns, clamped to ±max step x
elapsed turns; plus a pull toward the anchor (base price x event value multiplier x continent price index)
of at least one coin, so cheap goods have no dead band. Unsold goods carry
over as stock. Production adds to the nearest market (own node or ancestor) that trades the good.

## Currency

`CurrencyState` per continent: gold/silver reserves, coin supply, baseline, price index, exchange
rate. `GoldStandardCurrencyModel` (replaceable `ICurrencyModel`): each new year mints 3% of the
remaining reserves → coin supply grows (inflation, which raises market anchors through the price
index); as reserves deplete, minting slows and that continent's coin appreciates. Exchange rate =
world-average price index / local price index.

## Expansion direction (guidance)

No hard gates. Commands implementing `IGuidanceSource` (e.g. `FoundCompanyCommand`,
`BuyFacilityCommand`) return a `GuidanceSignal`; `GuidanceSystem` reads `Commands.AppliedThisTurn`
(not the event bus) and adds weight to a `GuidanceAxis` (Trade, Agriculture, Industry, Finance, Guild)
and set a focus node. Weights decay 1%/turn; signal weights are in config. Future systems (offers, events, LOD focus) read
`GuidanceState` to bias content toward the player's chosen direction.

## Save / load

Header (magic, format version, data hash, seed, turn) + one length-prefixed section per
`IPersistentComponent` (key, version, bytes). Unknown sections are skipped; older sections run
through `MigrationRegistry`; a different data hash (definitions + map) is rejected. Modules register
migrations for the components they own (`SimBuilder.AddMigration`), e.g. `PlayerStateV1Migration`.
Loading is atomic: the file is parsed and migrated before anything is applied, and
`GameSimulation.Load` restores a backup if applying fails. After a load, queued commands and
undelivered events are dropped and `ILoadListener` systems rebuild derived state (LOD, modifier folds).
Static tables (`NodeTable`, `RouteTable`, market layout) are rebuilt from data, not saved.

## Known gaps

- The map is hand-written data; the seeded hex map comes with P18 (DEC-075).
- No roll-up of lower-tier flows into higher-tier markets beyond "nearest carrying market".
- Saves reject changed data instead of remapping ids by key.
- An older save without a section loads only into a newly built simulation, whose fresh state that section keeps; a running game refuses it (DEC-107).
- `SimulationView` has no view of merchants or world events yet.

Every node is detailed in play (DEC-100). Each node is also a class with one core node, `NodeTable.Core` (DEC-101).
Continents, nations and regions hold no market or people: their core towns take them on (DEC-104).
