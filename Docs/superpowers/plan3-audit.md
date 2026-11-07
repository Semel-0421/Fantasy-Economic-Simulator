# Plan 3 unsaved-state audit

Task 1 of `plans/2026-11-07-plan3-economy-systems.md`. Source: the frozen C# reference (`4bf3804`, `Assets/_Game`),
read system by system. Nothing in `Assets/_Game` or `engine/` was changed for this audit.

## Verdict

All nine systems stay in Plan 3. No ported system reads unsaved state that an unported system writes earlier in
the same turn. Every unsaved input is either

- produced by a system that is itself in scope (`LodState.Detail` by `agents.lod`, `MarketFlows.Multipliers` by
  `economy.modifier_fold`), or
- a pure function of saved sections plus config and map, which C# also rebuilds on load (`SubsidyRate`), or
- static data built when the world is built (topology, `HouseholdState._demandScale`, `TierSchedule`).

`agents.merchants` and `agents.travel` write only saved sections (`economy.markets`, `agents.merchants`, ...) through
`MarketFlows.Buy` and `Sell`, which do not touch `Multipliers`. So the "reader of `MarketFlows` written by a merchant"
case from the brief does not occur. Nothing moves to Plan 4 on unsaved-state grounds.

Things the implementers must know, found by the audit and detailed below:

1. `LodState` is unsaved, and in the shipped data `lod.mode = 1` (every node Detailed). The aggregate paths
   (`DefaultLodPolicy` mode 0 and 2, production's expected-value output) are never exercised by the fixtures.
2. `HouseholdCash.LuxuryTaxYear` is an unsaved counter that only the tests read. It must not be ported as state.
3. `ProductionSystem` keeps a per-facility line cache that is not saved. It is a pure function of saved state in
   every reachable case (see "Hidden state"), so the Rust port may rebuild it every turn.
4. Production does `int * int` arithmetic in two places that C# evaluates in 32 bits before widening.
5. `GroupDirectory` lookups are last-row-wins for private enterprise, state enterprise and guild.
6. `core.absorb = 1` in the shipped data, so `NodeTable.Core`, `CoreStrength` and `IsSeat` matter for households,
   wages and pricing. They are not in the fixtures (`nodes.json` omits `CoreStrength`).

## Pipeline order and cadence

Order is `(phase, order, id)` with ordinal id comparison, from `build/systems.json`. In scope:

| Order | System | Phase | Order value |
|---|---|---|---|
| 1 | `agents.lod` | Lod | 0 |
| 2 | `economy.world_events` | Environment | 0 |
| 3 | `economy.modifier_fold` | Environment | 100 |
| 4 | `agents.production` | Production | 0 |
| 5 | `economy.pricing` | Markets | 100 |
| 6 | `agents.household_spending` | Markets | 101 |
| 7 | `agents.home_surplus` | Markets | 102 |
| 8 | `economy.currency` | Finance | 0 |
| 9 | `agents.households` | Finance | 120 |

Config in the shipped data: `tier.village` and `tier.city` period 1; `tier.region` period 2 offset 0; `tier.nation`
period 4 offset 1; `tier.continent` period 4 offset 0. A level is due when `(turn + offset) % period == 0`, where
`turn` is the clock turn before the turn runs. A level with period 1 is "inline" (consumes and reprices in the
same job); the others consume every turn through `ConsumptionJob` and reprice when due. `IsNewYear` is
`turn % 52 == 0`; `currency` mint and `households` also require `turn != 0`.

| Sampled turn | Region due | Nation due | Continent due | New year (mint, households) |
|---|---|---|---|---|
| 0 | yes | no | yes | no (turn 0 guard) |
| 1 | no | no | no | no |
| 3 | no | yes | no | no |
| 51 | no | yes | no | no |
| 52 | yes | no | yes | yes |
| 53 | no | no | no | no |
| 103 | no | yes | no | no |
| 104 | yes | no | yes | yes |

Every other behaviour of the nine systems runs every turn. `ElapsedTurns(level, turn) = min(period, turn + 1)`.

## Per-system table

Section keys are the C# `IPersistentComponent` keys. "Unsaved" means not in any `Write`.

| System | Saved read | Saved written | Unsaved read (writer) | Unsaved written | Events published (subscribers) | Random | Cadence |
|---|---|---|---|---|---|---|---|
| `agents.lod` | `agents.player` (Node), `agents.facilities` (Owner, Node), `agents.merchants` (Owner, Node) | none | none (config `lod.mode`, `lod.focus_level`; `NodeTable`, static) | `LodState.Detail` | none | none | every turn |
| `economy.world_events` | `economy.modifiers` | `economy.modifiers` | none (`GameDatabase.Events`, static) | none | `WorldEventStarted` (none) | `DetHash.Of("economy.world_events")`, entity = node, salt = event def index | every turn |
| `economy.modifier_fold` | `economy.modifiers` | none | none | `MarketFlows.Multipliers` | none | none | every turn |
| `agents.production` | see detail below | see detail below | `LodState` (agents.lod), `MarketFlows.Multipliers` (economy.modifier_fold), `DevelopmentState.SubsidyRate` (derived from saved subsidies), `GroupDirectory` (derived from `agents.groups`) | `DevelopmentState.SubsidyLeft` (scratch), the line cache and per-turn scratch | `FacilityBankrupt`, `LostWarehouseCleared`, `NpcLoanDefaulted` via `SeizeFacility` (none) | `"agents.production"` entity facility (detailed variance); `"agents.production.grade"` entity facility, salt line index | every turn |
| `economy.pricing` | `economy.markets`, `economy.household_cash`, `economy.households`, `economy.currency` | `economy.markets`, `economy.household_cash`, `economy.households` | `MarketFlows.Multipliers` (economy.modifier_fold) | none (scratch only) | none | none | settlement and consumption every turn; repricing by tier cadence |
| `agents.household_spending` | `economy.household_cash`, `agents.purses`, `agents.labor`, `economy.currency`, `agents.banks` | `economy.household_cash`, `agents.purses`, `agents.labor` (QuarterSpending), `agents.banks` (via `ToState`) | none | `HouseholdCash.LuxuryTaxYear` (write-only) | none | none | every turn |
| `agents.home_surplus` | `economy.household_cash`, `economy.markets`, `agents.purses` | `economy.household_cash`, `agents.purses`, `economy.markets` (Supply) | none (`MarketFlows.Sell` only writes the saved store) | none | none | none | every turn |
| `economy.currency` | `economy.currency` | `economy.currency` | none (`ICurrencyModel` service, stateless) | none | `CurrencyMinted` (none) | none at run time (`economy.currency.init` is build-time) | `UpdateRates` every turn; mint on new year, turn != 0 |
| `agents.households` | `economy.households`, `economy.household_cash` | `economy.households`, `economy.household_cash` (`StartYear`) | none | none | `HouseholdsUpdated` (none) | none | new year, turn != 0 |

No `ISimEvent` has a subscriber anywhere in `Assets/_Game` (the only `Subscribe` call is the `EventBus` definition),
so event dispatch changes no state. Rust handlers for these events are only needed if the plan wants the event
list; the byte comparison does not depend on them.

### `agents.lod` (`Agents/Lod.cs`)

- `Execute` and `OnLoaded` both call `ILodPolicy.Evaluate`. `DefaultLodPolicy` with `lod.mode != 0` sets every node
  to Detailed (1) or Aggregate (2) and returns. Mode 0: all Aggregate, then the subtree of
  `AncestorAt(player.Node, focus_level)` (or the player's node when that is -1) Detailed, then the node of every
  player-owned facility and merchant Detailed.
- The shipped data and the code default are both `lod.mode = 1`, so only the all-Detailed path is covered by the
  fixtures.
- Readers: `agents.production`, and in Plan 4 `agents.merchants`. Nothing reads it in the Commands phase, so
  computing it fresh at its slot is exact.

### `economy.world_events` (`Economy/MarketModifiers.cs`)

- `RemoveExpired(turn)` first (`ExpiresTurn <= turn`, order preserved), then for each event def in `GameDatabase.Events`
  order, for each node of the def's scope level in node order: `RandomFor(stream, n, def.Index).ChancePermille(...)`.
  A hit appends a modifier with `ExpiresTurn = turn + DurationTurns`.
- Modifier section layout: count, then `Node, Category, Target(byte), Multiplier raw, ExpiresTurn, SourceEvent`.

### `economy.modifier_fold` (`Economy/MarketFlows.cs`)

- `Rebuild` sets every multiplier to one, then for each active modifier in list order and each node of its subtree
  multiplies `Multipliers[(n * CategoryCount + c) * ModifierTargets.Count + target]` by the modifier with
  `Fx.RoundShift` of the product of raws. Category -1 means all categories.
- `Multipliers` is read only by `ProductionJob`, `PriceUpdateJob` and `ConsumptionJob` (grep over `Assets/_Game`,
  tests excluded), all after the fold in the same turn.

### `agents.production` (`Agents/ProductionSystem.cs`, `Agents/ProductionJobs.cs`)

Saved sections written: `economy.markets` (Stock, Supply, Demand, InputBid), `agents.facilities` (Cash,
NegativeCashWeeks, and on bankruptcy Owner, Mode, Group, Repurposable, LossYears, WarehouseClaimUntil),
`agents.purses` (Float), `agents.demand_forecast` (Produced), `agents.bill_room` (Room, BankRoom, BankSpent),
`agents.bills` (appended rows), `agents.banks` (Reserve through `SettleBank`, the subsidy settlement, `ToState`; also
PlayerDeposit through share dividends), `economy.household_cash` (Cash), `agents.income_tax` (`EnsureRows`,
FacilityDividends, `RestartFacility`), `agents.groups` (Cash, and a new private-enterprise row through `FamilyAt`),
`agents.cargo` (warehouse load and clear), and on a loan default `agents.npc_loans` (row removed) and
`agents.auctions` (row added).

Saved sections read: the same, plus `agents.demand_forecast` (Plan), `agents.development` (Subsidies, LandLevel),
`agents.shares`, `agents.merchants` (count and cash for `EnsureRows`), `economy.currency` (CoinSupply for
`StateExcess`), `agents.banks` (PlayerDeposit).

Per turn, in order: rebuild lines if stale, `ClearWeek`, `OpenSubsidies`, `ProductionJob` (facility order, shared stock),
`BillRoom.SettleBank` and subsidy settlement against `bank.Reserve`, `StorePlayerOutput`, `ShareBook.Snapshot`,
`FacilityUpkeepJob`, `ShareBook.PayHolders`, `PayWagesAway`, `PassOnDividends`, bankruptcies in facility order,
`ExpireWarehouseClaims`.

Port details found in the code:

- `long worth = clamped * Price[cell]` in `SellToMarket` and `clamped * GoodBasePrice[good]` in `Emit` multiply two
  `int` values in 32 bits (unchecked) and widen afterwards. Use `i32::wrapping_mul` before widening. Other products
  (`need * Price[cell]`, `(long)overflow * offer`) are `long`.
- `Stock[cell] -= (int)quantity` and `(int)Math.Min(...)` truncate a `long` to `int` (wrapping).
- Aggregate facilities (`NodeDetail != Detailed`) use the expected value: no variance draw, grade-up share
  `quantity * GradeUpChance`. Unreachable in the fixtures because every node is Detailed; port it, test it against
  hand-computed values, and say so in the report.
- Shipped config has `production.forecast_enabled = 1`, so `DemandForecast.Plan` is not all ones after the first
  quarterly forecast. It is a saved read, so the replay supplies it.
- The Burst job and the managed path must agree (`ForceManaged`); the fixtures were exported with the default
  (Burst) path. Port from the job's code.

### `economy.pricing` (`Economy/MarketSystems.cs`, `Economy/PriceKernel.cs`, `Economy/Households.cs`)

Per turn: `GrowAtHome`; for every node refresh `_anchorMul` (`CurrencyState.PriceIndex[continent]`),
`_necessityWeight`, `HouseholdCash.Settle`, `_luxuryWeight`, `_budget`, `_luxuryBudget`; then `ConsumptionJob` for the
non-inline levels; then, if any level is due, `PriceUpdateJob` over all markets. `Settle` writes `QuintileCash` and
`SpentSettled` every turn even when nothing reprices, so the comparison on a non-due turn is not empty.
`PriceUpdateJob.Execute` returns early for markets whose level is not due.

- `HouseholdState._demandScale` is unsaved but static: built in the constructor from `NodeTable`, map and config
  (`AbsorbClasses` when `NodeTable.Absorbed`). The Rust `HouseholdState::new` already does this; pricing needs
  `Core`, `CoreStrength` and `IsSeat` to match C#.
- `PricingSystem._levelElapsed` is only assigned for due levels and only read for due levels, so stale entries are
  never used.

### `agents.household_spending`, `agents.home_surplus` (`Agents/Purses.cs`)

- Spending moves `HouseholdCash.Spent[node]` to the node's float net of the luxury tax, tax to the state
  (`MoneyLedger.ToState`), adds the gross to `LaborState.QuarterSpending` and `SpentYear`, then zeroes `Spent`.
- Home surplus sells `HomeGrown` per cell at the trader offer as far as the float can pay, spoils the rest unless the
  good is preserved, `MarketFlows.Sell` adds to `Supply`, pays households at the market's node (`PayHouseholds`, which
  maps to the core town when `NodeTable.Absorbed`).
- `PurseState.NodeFloat`, `NationNodes` and the continent-of-float table are unsaved but static (built from
  `NodeTable` and `NodeBanks`).

### `economy.currency`, `agents.households`

- `currency`: on a new year (turn != 0) `MintYear` per continent in order, publishing `CurrencyMinted`; every turn
  `UpdateRates` recomputes `PriceIndex` and `ExchangeRate` from `MoneyTotal` over `BaselineMoneyTotal`. Pricing of the
  next turn reads the new index (one-turn lag by design).
- `households`: on a new year (turn != 0) per node: income per head toward the measured year income, population by
  the growth rule, `NecessityWanted` and `NecessityMet` reset; then `HouseholdCash.StartYear()`. It reads
  `YearIncome`, which includes `SavedYear` written by out-of-scope systems; all of that is saved state.

## Unsaved-input register

| Unsaved state | Written by | Read by (in scope) | Rebuilt on C# load | Rust obligation |
|---|---|---|---|---|
| `LodState.Detail` | `agents.lod` (every turn) | `agents.production` | `LodSystem.OnLoaded` | Computed by the Rust `lod` system each turn; no load hook needed because it runs before any reader |
| `MarketFlows.Multipliers` | `economy.modifier_fold` (every turn) | `agents.production`, `economy.pricing` | `ModifierFoldSystem.OnLoaded` | Computed by the Rust fold each turn. Nothing reads it before the fold within a turn |
| `DevelopmentState.SubsidyRate` | `SubsidySystem.Execute` (new year, after `RemoveAll` and additions, always refreshed) and `OnLoaded` | `agents.production` | `SubsidySystem.OnLoaded` -> `RefreshSubsidyRates` | Derive from saved `Subsidies` and `config.SubsidyRate`: `rate[nation * goods + good] = config rate`, else 0 |
| `DevelopmentState.SubsidyLeft` | `agents.production` (`OpenSubsidies`, every turn) | `agents.production` | not rebuilt, not needed | Scratch inside the system; floats beyond the nations are never set and stay 0 |
| `GroupDirectory` | derived from `agents.groups` | `agents.production` (`FamilyAt`) | rebuilt when `Count` or `LoadCount` changes | Pure lookup over `GroupTable`, last row wins for private enterprise, state enterprise and guild; a new private enterprise row is appended on demand |
| `FacilityTable.UseChanges` | `ChangeUse` | `agents.production` (cache key) | not saved | Not needed if lines are rebuilt each turn |
| `HouseholdCash.LuxuryTaxYear` | `agents.household_spending` | tests only | not rebuilt | Do not port as state. A plain counter on the Rust struct is optional and must stay out of the section bytes |
| `HouseholdState._demandScale` | constructor | `economy.pricing` | built with the world | Already in `fes-sim`; static |
| `PurseState.NodeFloat`, `NationNodes` | constructor | production, spending, surplus | built with the world | Static topology |
| `TierSchedule` | built from `config.TierCadences` | `economy.pricing` | built with the world | From config |
| `NodeTable.Core`, `CoreStrength`, `Absorbed` | `WorldBuilder` (`core.absorb`) | pricing, wages, households | built with the world | Static; `CoreStrength` is not in the fixtures, so it needs a unit test against C# `Absorb` |
| `ICurrencyModel` (`GoldStandardCurrencyModel`) | service | `economy.currency` | not stateful | Plain functions |

## Hidden state (private fields that survive a turn)

Only `ProductionSystem` has any that matters. Flagged each.

| System | Field | Verdict |
|---|---|---|
| `agents.production` | line cache `_lineStart`, `_lineCell`, `_lineGood`, `_lineModifier`, `_lineQuantity`, `_lineIsInput`, `_lineOutputMul`, `_lineUp*`, keyed on `_linesForCount` and `_linesForUses` | Real hidden state, not saved, reset by `OnLoaded`. The cache bakes in `DevelopmentState.LandLevel`, which is not in its key. It stays correct because `LandLevel[node]++` happens only in `StateProjectSystem.OpenFacility`, which also appends a facility (changes `Count`) and runs after production, so the next production rebuilds it. Facilities are never removed and `DefIndex` changes only through `ChangeUse`. So lines are a pure function of saved state; recompute them every turn, or cache keyed the same way |
| `agents.production` | `_floatReserve`, `_inputCost`, `_outputWage`, `_groupDividend`, `_playerOutput*`, `_bankrupt`, `_dividendsBefore`, `_nodeWagesAway` | Scratch, fully reassigned or cleared at the start of each `Execute` (`_nodeWagesAway` returns to zero within the turn) |
| `agents.production` | `_nodeWageAway`, `_defUpkeep` | Static, built in `Initialize` from config, map and `NodeBanks`; no state |
| `agents.production` | `_groups` (`GroupDirectory`) | Derived cache of `GroupTable` (see register) |
| `economy.pricing` | `_anchorMul`, `_necessityWeight`, `_luxuryWeight`, `_budget`, `_luxuryBudget` | Rewritten for every node every turn |
| `economy.pricing` | `_levelElapsed` | Only due levels are set and read |
| `economy.pricing` | `_homeGoods`, `_goodLuxuryShare`, `_levelWeight`, `_params`, `_demand` | Static from config and data |
| `agents.lod` | `DefaultLodPolicy._subtree` | Scratch, cleared per call |
| `economy.modifier_fold` | `MarketFlows._subtree` | Scratch, cleared per modifier |
| others | none | no fields beyond component and service references |

## C# load path (`GameSimulation.Load` -> `TurnPipeline.NotifyLoaded`)

`NotifyLoaded` clears the command queue and event bus, then calls `OnLoaded` on every system that implements
`ILoadListener`, in pipeline order. System `Initialize` runs once at build, not on load. The listeners are:

| Listener | What it rebuilds | In scope |
|---|---|---|
| `LodSystem` | `LodState.Detail` | yes, recomputed per turn |
| `ModifierFoldSystem` | `MarketFlows.Multipliers` | yes, recomputed per turn |
| `ProductionSystem` | invalidates the line cache; replaces the legacy "unset facility cash" sentinel with the tier's start cash (old saves only) | yes; the legacy fix-up is not needed for current saves |
| `SubsidySystem` | `DevelopmentState.SubsidyRate` | no (derived, see register) |
| `CreditSystem` | `BondOffers.Turn = -1` (unsaved week cache) | Plan 4 |
| `FinanceSystem` | `MoneyTotals.Invalidate()` (unsaved cache of money totals) | Plan 4 |
| `PolicySystem` | `ApplyTolls()` rewrites unsaved `NodeTolls` from saved `PolicyState` | Plan 4 (read by travel and merchants) |
| `GroupSystem` | `GroupSetup.AssignOwners` fills missing group owners; for a current save it should find none, but it can write `agents.groups` and `agents.facilities` on load | Plan 4; the step saves are recorded from a live run and never went through a load |

The step fixtures record a live continuing run (`StepTurn` on the simulation that has run all turns), so no unsaved
state in them came from a load. The Rust harness starts every turn from the recorded save, which is consistent with
the register above: each unsaved input in scope is regenerated at its writer's slot or derived from saved state.

## Final in-scope list for Tasks 3-7

Unchanged from Global Constraints. Nothing is moved to Plan 4.

| Task | Systems | Notes from the audit |
|---|---|---|
| 3 | `agents.lod`, `economy.world_events`, `economy.modifier_fold` | Lod: implement modes 0, 1, 2; only mode 1 is in the fixtures. World events: stream `economy.world_events`, entity node, salt def index; section `economy.modifiers`. Fold: derived `Multipliers` stored on the Rust side as unsaved state with a "produced this turn" marker so the harness fails loudly if pricing or production runs without it |
| 4 | `agents.production` | Largest system. Wrapping `i32` products, `(int)` truncations, last-row-wins group lookup, `SubsidyRate` derived from saved subsidies, lines rebuilt each turn. Depends on `MoneyLedger`, `ShareBook` (Snapshot, PayHolders), `NpcLending.SeizeFacility`, `IncomeTaxState`, `BillTable` and `BillRoom`, `CargoTable`. Check that file size stays under the 1,000-line limit; split by concern if not |
| 5 | `economy.pricing` | Settlement and consumption every turn, repricing by cadence (see the due-level table). `Core`, `CoreStrength` and `IsSeat` need to match |
| 6 | `agents.household_spending`, `agents.home_surplus` | `LuxuryTaxYear` is not state |
| 7 | `economy.currency`, `agents.households` | New year, turn != 0; sampled turns 52 and 104 exercise them, the other six must produce no change |

`agents.merchants` (writes `economy.markets` and `agents.merchants`), `agents.travel`, `agents.banks` and the other
Finance systems are replayed by applying their recorded section changes, as the plan says. Their unsaved caches
(`BondOffers`, `MoneyTotals`, `NodeTolls`) are not read by any system in scope.

## Service checks in the step replay

The step records hold saved sections only, so the replay cannot compare unsaved services with C#.
`fes_sim::replay_services::service_checks` instead compares each derivable service, after every Rust-run record
(ported system, dispatch, clock), with the same service rebuilt from the recorded state by the Rust load path; a
mismatch is reported as `Different` with key `service:<name>` and the first differing element as offset. A service is
checked only while it is fresh: after its writer ran in Rust (or after the load, when the load rebuilds it) and before
any record changes one of its source sections, because C# keeps it stale over such changes too.

| Service | Rebuild compared against | Writer | Sources |
|---|---|---|---|
| `LodState.detail` | `LodPolicyService.evaluate` (`LodSystem::on_loaded`) | `agents.lod` | `agents.player`, `agents.facilities`, `agents.merchants` |
| `MarketFlows.multipliers` | `fold_multipliers` (`ModifierFoldSystem::on_loaded`) | `economy.modifier_fold` | `economy.modifiers` |
| `DevelopmentState.subsidy_rate` | `fill_subsidy_rates` (C# `SubsidySystem.OnLoaded`) | `agents.production` | `agents.development` |

Unchecked (not derivable from saved state, or not a service a ported system reads across records):

- `DevelopmentState.subsidy_left`: scratch written and read inside one production run; its effect reaches
  `agents.banks` reserves, which the section compare covers.
- The production line cache: not kept in Rust (lines are rebuilt every turn), so there is nothing to check.
- `GroupDirectory`, `FacilityTable.UseChanges`, `HouseholdCash.LuxuryTaxYear`: not kept as Rust state.
- Static services (`HouseholdState._demandScale`, `PurseState.NodeFloat`, `NationNodes`, `TierSchedule`,
  `NodeTable.Core`/`CoreStrength`/`Absorbed`): built with the world, never written by a system; covered by their own
  unit tests.
- Plan 4 caches (`BondOffers`, `MoneyTotals`, `NodeTolls`): their systems are not ported.

With lod mode 1 in the fixtures, the `LodState.detail` check is trivially all-detailed; modes 0 and 2 are covered by
unit tests in `fes-sim/tests/systems_production.rs` (aggregate expected-value output, and pricing consuming it).
