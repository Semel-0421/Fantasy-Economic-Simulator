# Plan 4 unsaved-state audit

Task 1 of `plans/2026-11-08-plan4-agents-finance.md`. Source: the frozen C# reference (`4bf3804`, `Assets/_Game`), read
system by system. Nothing in `Assets/_Game` or `engine/` was changed for this audit. Plan 3's audit
(`plan3-audit.md`) covers the nine systems already ported; this one covers the 30 remaining systems
(`agents.travel`, `agents.merchants`, 27 finance-side systems, `agents.guidance`, `agents.groups`, `agents.licenses`)
and the 34 player commands.

## Verdict

All 30 systems stay in Plan 4, and the plan's task grouping (3: travel and merchants; 4-6: finance A-C; 7: guidance,
groups, licenses) is valid with **two ordering constraints inside it** (see "Group order"). The unsaved state that
crosses systems is small and fully listed in the register below. Findings the implementers must know:

1. **`BondOffers` is a per-turn unsaved cache shared by up to six systems and by the commands.** It is built on the
   first `BondDesk` call of a turn, depleted by every later call, and cleared only at the end of
   `agents.credit`. Users in pipeline order: player bond/share/short/venture commands (Commands phase),
   `agents.ventures` (108), `agents.asset_markets` (109), `agents.investment` (121), `agents.credit` (128).
   A user ported in Rust while an earlier user is replayed from its record would rebuild the offers from the wrong
   cash and give different amounts. This forces the port order ventures, asset_markets, investment, credit (the
   plan's task order already gives it) and ventures before asset_markets inside Task 4.
2. **`AssetMarkets.BurstNation` is unsaved and written by `agents.asset_markets` (109), read by `agents.finance`
   (110)** on quarter-end turns only. Finance must not be ported before asset_markets (same Task 4; port
   asset_markets first).
3. **`NodeTolls` is unsaved but a pure function of saved `agents.policy` (`TollStep`) plus the map and config**:
   `Tariff[n] = Scale(TollStep[nation]).MulInt(baseTariff[n])` for every node under a nation, the map's base outside
   nations. `agents.merchants` (Task 3) reads it, `agents.policy` (Task 6) is its only writer. Task 3 must derive it
   from the saved `TollStep`, not wait for the Rust policy system.
4. **`agents.guidance` reads `ctx.Commands.AppliedThisTurn`** (not saved). The Rust `CommandQueue` already keeps
   `applied_this_turn`; the Rust `Command` trait has no guidance hook yet (C# `IGuidanceSource`, implemented by
   `BuyFacilityCommand`, `FoundCompanyCommand`, `BuyLicenseCommand`). Task 7 adds the hook with a default of none;
   Task 8 implements it. The step fixtures issue no commands, so guidance only decays there.
5. **No `ISimEvent` has a subscriber in `Assets/_Game`** (the only `Subscribe` call outside `EventBus` is in
   `CoreTests`). Dispatch changes no service and no state, so **no handler changes a service during dispatch**; the
   Review Focus item "a handler changes a service" is empty. Events are listed per system only for completeness.
6. **Row order is state.** `NativeTable.RemoveRowSwapBack` (swap the last row into the hole) is how rows are removed
   from `agents.cargo`, `agents.bills`, `agents.bonds`, `agents.orders`, `agents.contracts`, `agents.shares`,
   `agents.shorts`, `agents.npc_loans`, `agents.loans`, `agents.auctions`, `agents.licenses`, `agents.guild_memberships`.
   Loops that remove while iterating run backwards. The Rust tables must swap-back identically.
7. **The merchant job keeps a counter that steers behaviour.** `TripStats[8]` (coins from sales) is compared before and
   after `Act` for `MustAct`; the job's `TripStats` array is copied into `MerchantTripStats` and zeroed after the job.
   `MerchantTripStats` and `CreditStats` themselves are write-only diagnostics (no system reads them; tests and the
   view do), so Rust may skip them, but the in-job counter must exist.
8. **Dead branches in the shipped data**, which the fixtures and golden runs never reach: `lod.mode = 1` (every node
   Detailed, so `MerchantFlowJob` and `PoolIdleGroups` do nothing and `agents.merchant_generation` sees all nodes
   Detailed), `merchants.rumors = 0` (`Board`, `Listen` unreachable), `finance.event_chance_permille = 0` (random
   financial event never drawn), `traders.bank_loan_share_permille = 0`. Port them anyway and cover them with unit
   tests against hand-computed values, as Plan 3 did for aggregate production. `NoBills` sets
   `credit.bill_cap_weeks = 0` (no bills).
9. **`DefenseSystem` has no `turn > 0` guard on its new-year branch**: at turn 0 it sets `YearBudget` and `Security`.
   Every other yearly system guards `Turn > 0` (income_tax opens its books at turn 0 instead, see below).

## Pipeline order and cadence

Order is `(phase, order, id)` with ordinal id comparison, from `build/systems.json`. Plan 3's nine systems are marked
(P3). `FinanceOrder` (Agents/FinanceOrder.cs) names the Finance bands. `agents.banks` and `agents.roads` share
order 100 and are separated by id.

| Pos | System | Phase | Order | Plan 4 task |
|---|---|---|---|---|
| 1 | `core.commands` (P3 shell) | Commands | 0 | 8 (commands) |
| 2 | `agents.travel` | Commands | 100 | 3 |
| 3 | `agents.lod` (P3) | Lod | 0 | |
| 4-5 | `economy.world_events`, `economy.modifier_fold` (P3) | Environment | 0, 100 | |
| 6 | `agents.production` (P3) | Production | 0 | |
| 7 | `agents.merchants` | Agents | 0 | 3 |
| 8-10 | `economy.pricing`, `agents.household_spending`, `agents.home_surplus` (P3) | Markets | 100-102 | |
| 11 | `economy.currency` (P3) | Finance | 0 | |
| 12 | `agents.banks` | Finance | 100 | 4 |
| 13 | `agents.roads` | Finance | 100 | 4 |
| 14 | `agents.state_spending` | Finance | 105 | 4 |
| 15 | `agents.labor` | Finance | 106 | 4 |
| 16 | `agents.defense` | Finance | 107 | 4 |
| 17 | `agents.ventures` | Finance | 108 | 4 |
| 18 | `agents.asset_markets` | Finance | 109 | 4 |
| 19 | `agents.finance` | Finance | 110 | 4 |
| 20 | `agents.shorts` | Finance | 111 | 4 |
| 21 | `agents.income_tax` | Finance | 115 | 5 |
| 22 | `agents.nobles` | Finance | 116 | 5 |
| 23 | `agents.migration` | Finance | 117 | 5 |
| 24 | `agents.state_projects` | Finance | 118 | 5 |
| 25 | `agents.subsidies` | Finance | 119 | 5 |
| 26 | `agents.households` (P3) | Finance | 120 | |
| 27 | `agents.investment` | Finance | 121 | 5 |
| 28 | `agents.merchant_vehicles` | Finance | 122 | 5 |
| 29 | `agents.orders` | Finance | 123 | 6 |
| 30 | `agents.demand_forecast` | Finance | 124 | 6 |
| 31 | `agents.merchant_membership` | Finance | 125 | 6 |
| 32 | `agents.contracts` | Finance | 126 | 6 |
| 33 | `agents.strategic_subsidy` | Finance | 127 | 6 |
| 34 | `agents.credit` | Finance | 128 | 6 |
| 35 | `agents.trader_loans` | Finance | 129 | 6 |
| 36 | `agents.policy` | Finance | 130 | 6 |
| 37 | `agents.merchant_generation` | Finance | 140 | 6 |
| 38 | `agents.guidance` | Guidance | 0 | 7 |
| 39 | `agents.groups` | Cleanup | 0 | 7 |
| 40 | `agents.licenses` | Cleanup | 0 | 7 |

Clock: `Year = Turn / 52`, `IsNewYear = Turn % 52 == 0`, `IsQuarterEnd = Turn % 13 == 12`. "Quarter start" below is
`Turn > 0 && Turn % 13 == 0` (the C# `OrderSystem.QuarterWeeks` test), which is a different turn from quarter end. The
step fixtures sample turns 0, 1, 3, 51, 52, 53, 103, 104:

| Cadence | Sampled turns that exercise it |
|---|---|
| every turn | all 8 |
| NY: new year with `Turn > 0` | 52, 104 |
| QE: quarter end (`Turn % 13 == 12`) | 51, 103 |
| Q0: quarter start (`Turn > 0`, `Turn % 13 == 0`) | 52, 104 |
| defense purchase (`Turn % 4 == 0`) | 0, 52, 104 |
| defense new-year branch (no turn guard) | 0, 52, 104 |

| System | Cadence |
|---|---|
| travel | every turn (acts only on the arrival turn; no fixture trip, since the player never travels) |
| merchants | every turn |
| banks, roads, shorts (when `ShortTable` is non-empty), ventures, asset_markets (rent and weekly credit; quarter close on QE), labor (wages every turn; employment on QE), state_spending, trader_loans, credit, orders (queue every turn; households order on Q0), contracts (deliver every turn; sign on NY), groups, licenses, guidance, merchant_membership (dealings every turn, `ListOpenGroups` and joining on NY), nobles (spend every turn; protection and aid on NY), state_projects (finish and start every turn; plan on NY), income_tax (`EnsureRows` every turn, `OpenBooks` when not yet opened; collect on NY) | as stated |
| defense | budget on NY and turn 0; purchase every 4th turn |
| finance | `Publish` on NY; record, event update and loan rate on QE |
| demand_forecast | Q0 only, and only with `production.forecast_enabled` |
| migration, subsidies, investment, merchant_vehicles, strategic_subsidy, merchant_generation, policy | NY only |

## Per-system table

Section keys are the C# `IPersistentComponent` keys. "Via X" means the write goes through a helper in the named class.
Read and write sets come from the code of the system and the helpers it calls (`MoneyLedger`, `BondDesk`,
`ShareBook`, `NpcLending`, `Nobles.CollectTax`). "Services" are the unsaved or derived inputs.

| System | Saved read | Saved written | Services / unsaved read (writer) | Unsaved written | Events (subscribers: none) | Random | Cadence |
|---|---|---|---|---|---|---|---|
| `agents.travel` | `agents.player` | `agents.player` (Node, TravelDestination, ArrivalTurn) | none | none | `PlayerArrived` | none | arrival turn |
| `agents.merchants` | `agents.merchants`, `agents.cargo`, `agents.bills`, `agents.bill_room`, `agents.banks`, `agents.policy` (Licensed), `agents.purses`, `agents.development` (Imports), `agents.groups`, `economy.markets`, `economy.household_cash`, `economy.households`, `economy.currency`, `agents.npc_loans` | `agents.merchants` (Node, Destination, ArrivalTurn, Cash, NegativeCashWeeks, LoadCost, Memory, HedgeFloor, Trait, Vehicle), `agents.cargo`, `agents.bills` (rows added), `agents.bill_room`, `agents.banks` (Reserve, InternationalReserve), `agents.purses` (Float), `agents.development` (Imports), `economy.markets` (Stock, Supply, Demand, InputBid), `economy.household_cash` (Cash, HomeGrown, via MoneyLedger), `agents.groups` (Cash), `agents.npc_loans` (WriteOff), `agents.income_tax` (RestartMerchant), `agents.merchant_genetics` (RecordBankruptcy) | `LodState` (agents.lod), `NodeTolls` (derived, see register), `HouseholdNeeds.Units` (derived per turn), `TravelCosts` (derived), insurer-guild cache | `MerchantTripStats`, `CreditStats.BillsIssued/Discounted` (write-only) | `MerchantBankrupt` | `agents.encounters` (entity a, salt 1); `agents.merchant_bankrupt` (entity a, replacement trait); `agents.licenses.merchants`, `.international`, `.intercontinental` (turn 0, entity a, license holding) | every turn |
| `agents.banks` | `agents.banks`, `agents.loans`, `agents.margin`, `agents.auctions`, `agents.npc_loans`, `agents.player`, `agents.ship`, `agents.facilities`, `agents.merchants`, `agents.cargo`, `agents.groups`, `agents.shares`, `agents.land`, `agents.ventures`, `agents.purses`, `agents.income_tax`, `economy.markets`, `economy.currency` | `agents.banks`, `agents.loans`, `agents.margin`, `agents.auctions`, `agents.npc_loans`, `agents.player` (Coins), `agents.ship`, `agents.facilities` (Cash, Group, Mode, Owner, Repurposable, WarehouseClaimUntil, NegativeCashWeeks), `agents.merchants` (Cash, LoadCost), `agents.cargo`, `agents.purses`, `economy.markets` (Supply via `Sell`) | `PlayerBalanceSheet`, `GroupDirectory` (derived) | none | `LoanRepaid`, `MarginCalled`, `CollateralSeized`, `NpcLoanDefaulted`, `AuctionClosed` | none | every turn |
| `agents.roads` | `world.roads`, `agents.banks`, `agents.player` | `world.roads`, `agents.banks` (Reserve, PlayerDeposit), `agents.player` (Coins), `economy.household_cash` (via `PayHouseholds`) | none | none | `RoadBuilt`, `RoadLost` | none | every turn |
| `agents.state_spending` | `agents.banks`, `economy.currency`, `economy.households`, `economy.household_cash` | `agents.banks` (Reserve), `economy.household_cash` (via `MoneyLedger.PayNationWages`, `PayNationTownWages`) | none | none | none | none | every turn |
| `agents.labor` | `agents.labor`, `agents.merchants`, `economy.households`, `economy.currency` | `agents.merchants` (Cash), `agents.labor` (Employment, LongRunActivity, QuarterSpending), `economy.household_cash` (via ledger) | none | `MerchantTripStats.SpentWages` | none | none | wages every turn; employment QE |
| `agents.defense` | `agents.banks`, `agents.development`, `agents.purses`, `economy.markets` | `agents.development` (YearBudget, DefenseSpent, Security), `agents.banks` (Reserve), `agents.purses` (Float), `economy.markets` (Stock, Supply, Demand) | none | scratch `_spend/_paid/_value/_carry` (reset each run) | none | none | budget NY and turn 0; purchase `Turn % 4 == 0` |
| `agents.ventures` | `agents.ventures`, `agents.banks`, `economy.currency`, `agents.purses`, `economy.markets`, `economy.household_cash`, `economy.households` | `agents.ventures`, `agents.banks` (Reserve, PlayerDeposit), `economy.markets` (via `Buy`, `Sell`), `economy.household_cash` (via `BondDesk`) | `BondOffers` (first asker or shared, see register) | `BondOffers` | none | `agents.ventures.wreck` (entity v) | every turn |
| `agents.asset_markets` | `agents.asset_markets`, `agents.banks`, `agents.finance`, `agents.shares`, `agents.land`, `agents.ventures`, `agents.player`, `agents.groups`, `agents.facilities`, `economy.household_cash`, `agents.purses` | `agents.asset_markets`, `agents.banks` (Reserve, PlayerDeposit), `economy.household_cash` (Cash via purses and `BondDesk`), `agents.player` (Coins at nodes) | `BondOffers` | `AssetMarkets.BurstNation`, `BondOffers` | `MarketHeated`, `MarketBurst` | none (no dice, DEC-131) | every turn (rent, credit, reprice); QE closes the quarter |
| `agents.finance` | `agents.finance`, `agents.banks`, `agents.loans`, `agents.npc_loans`, `economy.currency`, `agents.asset_markets`, plus everything `MoneyTotals.Recompute` reads (player, facilities, merchants, groups, ventures, purses, household_cash, banks) | `agents.finance` (Records, Event, EventStart, LoanRate, Published*), `agents.banks` (Reserve, bank run), `economy.currency` (MoneyTotal on NY), `economy.household_cash` (bank run via `PayNationHouseholds`) | `AssetMarkets.BurstNation` (asset_markets), `MoneyTotals` (recomputed at its slot) | `MoneyTotals` | `FinancialEventStarted`, `FinancialEventEnded`, `StatisticsPublished` | `agents.finance.events` (entity nation; dead at `finance.event_chance_permille = 0`) | `Publish` NY; the rest QE |
| `agents.shorts` | `agents.shorts`, `agents.short_licences`, `agents.banks`, `agents.player`, `agents.facilities`, `agents.groups`, `agents.loans` | `agents.short_licences` (CalledTurn), `agents.banks` (Reserve, PlayerDeposit), `agents.shorts` (fees), `agents.player` | none | none | `ShortCalled`, `ShortsCovered` | none | every turn; with no shorts open it only resets `CalledTurn` to -1 |
| `agents.income_tax` | `agents.income_tax`, `economy.household_cash`, `economy.households`, `agents.facilities`, `agents.merchants`, `agents.npc_loans` | `agents.income_tax` (rows, Opened, Last*), `economy.household_cash` (Cash), `agents.facilities` (Cash, Level, LossYears), `agents.merchants` (Cash), `agents.nobles` (via `Nobles.CollectTax`), `agents.banks` (via `ToState`) | none | `MerchantTripStats.SpentTax` | `IncomeTaxCollected`, `FacilityClosed` | none | `EnsureRows` every turn; first call opens the books and returns; collect NY |
| `agents.nobles` | `agents.nobles`, `agents.purses`, `economy.households` | `agents.nobles` (Cash, ContractHolds, ProtectionOwed), `agents.banks` (via `PayNationWages`), `economy.household_cash` | none | none | `NobleContractBroken` | none | spend every turn; protection and aid NY |
| `agents.migration` | `agents.migration`, `agents.labor`, `agents.nobles`, `agents.facilities`, `economy.households`, `economy.household_cash`, `agents.purses` | `agents.migration` (EnactedYear, FreeShare), `economy.household_cash` (Cash, SavedYear), `agents.nobles` (Cash), `economy.households` (Population) | none | none | `EmancipationEnacted`, `PeopleMigrated` | none | NY (and `migration.max_share > 0`) |
| `agents.state_projects` | `agents.development` (Projects), `agents.facilities`, `agents.groups`, `economy.markets`, `economy.households`, `economy.household_cash`, `agents.land`, `world.roads`, `agents.purses`, `agents.banks` | `agents.development` (Projects, LandLevel, HarborLevel, RouteGrade, Imports), `agents.banks` (Reserve), `agents.facilities` (rows appended, Group), `agents.groups` (private enterprise row), `world.roads`, `agents.land` | `TravelCosts` (derived), `NodePorts` and `NodeSpecialties` (static) | none | `StateProjectFinished` | none | finish and start every turn; plan and `Imports` reset NY |
| `agents.subsidies` | `agents.development`, `agents.banks`, `agents.facilities`, `agents.groups`, `economy.households`, `agents.land`, `economy.markets`, `agents.purses` | `agents.development` (Subsidies), `agents.banks` (Reserve), `agents.facilities` (rows appended, Group), `agents.groups` | `DevelopmentState.SubsidyRate` (written here, see register) | `DevelopmentState.SubsidyRate` | `SubsidyEnacted` | none | NY |
| `agents.investment` | `agents.facilities`, `agents.groups`, `agents.land`, `agents.demand_forecast`, `agents.development`, `agents.purses`, `agents.bonds`, `economy.markets`, `economy.household_cash` | `agents.groups` (Cash, private enterprise rows), `agents.facilities` (Level, appended rows, Group), `agents.bonds` (via `BondDesk.Raise`), `economy.household_cash` (via `BondDesk`) | `BondOffers`, `CompanyRooms` (refreshed each use) | `BondOffers`, `CreditStats` | `FacilityExpanded`, `FacilityFounded` | none | NY (and `investment.payback_years > 0`) |
| `agents.merchant_vehicles` | `agents.merchants`, `agents.orders`, `economy.markets` | `agents.merchants` (Vehicle), `agents.orders` (rows added) | none | scratch `_ordering` | none (`MerchantVehicleBought` is published by `agents.orders`) | `agents.merchant_vehicle_wear` (entity a) | NY |
| `agents.orders` | `agents.orders`, `agents.merchants`, `economy.markets`, `economy.households`, `economy.household_cash`, `economy.currency`, `agents.purses` | `agents.orders`, `economy.markets` (Stock, Supply, Demand), `economy.household_cash` (Cash, SpentYear), `agents.merchants` (Cash, Vehicle) | none | `MerchantTripStats.SpentVehicles` | `MerchantVehicleBought` | none | queue every turn; households order on Q0 |
| `agents.demand_forecast` | `agents.demand_forecast`, `agents.orders`, `agents.facilities`, `economy.markets`, `economy.households`, `economy.currency`, `agents.purses` | `agents.demand_forecast` (Forecast, Plan, Produced, StockAtStart) | none | scratch lists | none | none | Q0 only, with `production.forecast_enabled` |
| `agents.merchant_membership` | `agents.merchants`, `agents.groups`, `agents.income_tax` | `agents.merchants` (Association, Cash), `agents.groups` (Cash) | `GroupDirectory` (derived), `_openAt` (rebuilt NY) | `MerchantTripStats.SpentFeesToAssociations` | none | `agents.merchant_join` (entity a, NY, only unassociated NPC groups) | every turn; join and `ListOpenGroups` NY |
| `agents.contracts` | `agents.contracts`, `agents.demand_forecast`, `agents.facilities`, `agents.merchants`, `agents.groups`, `economy.markets`, `economy.households`, `economy.currency`, `agents.purses` | `agents.contracts` (rows, Prepaid), `economy.markets` (Stock, Supply), `agents.merchants` (Cash), `agents.groups` (Cash) | `_refused` (cleared each NY before use) | none | `SupplyContractSigned`, `SupplyContractBroken` | none | deliver every turn; sign NY; none at all when `contract.per_nation <= 0` |
| `agents.strategic_subsidy` | `agents.banks`, `agents.facilities`, `agents.groups`, `agents.purses` | `agents.groups` (Cash), `agents.banks` (Reserve) | `_levels` scratch | none | none | none | NY (and `state.strategic_subsidy_permille > 0`) |
| `agents.credit` | `agents.bills`, `agents.bill_room`, `agents.bonds`, `agents.banks`, `agents.finance`, `agents.facilities`, `agents.groups`, `agents.merchants`, `agents.purses`, `economy.household_cash`, `economy.markets`, `economy.currency`, `agents.income_tax`, `agents.shares` | `agents.bills` (Face, Holder, rows added and removed), `agents.bill_room` (Room, BankRoom), `agents.bonds` (rows, Face, Interest, BehindWeeks), `agents.banks` (Reserve, PlayerDeposit), `agents.purses` (Float), `agents.merchants` (Cash), `agents.facilities` (Cash), `agents.groups` (Cash), `economy.household_cash` (Cash, SavedYear), `agents.shares` (via `ShareBook.List`) | `BondOffers` (shared, cleared here), `CompanyRooms` | `BondOffers.Turn = -1` at end, `CreditStats` | `BondDefaulted` | none | every turn |
| `agents.trader_loans` | `agents.trader_loans`, `agents.banks`, `agents.purses`, `economy.household_cash` | `agents.trader_loans` (OwedBank, OwedHouseholds), `agents.purses` (Float), `economy.household_cash` (Cash, SavedYear), `agents.banks` (Reserve) | none | none | none | none | every turn |
| `agents.policy` | `agents.policy`, `agents.groups`, `agents.facilities`, `agents.merchants`, `agents.banks`, `economy.currency`, `agents.finance` | `agents.policy` (TollStep, LicensePriceStep, MintStep, Licensed, LastInfluence, LastWorth, Lobbying cleared), `economy.currency` (MintRateMultiplier), `agents.banks` (Reserve) | `NodeTolls` (written here) | `NodeTolls` (`ScaleNation` when a step changes) | `PolicyReviewed`, `MintingPolicyChanged` | none | NY |
| `agents.merchant_generation` | `agents.merchants`, `agents.merchant_genetics` | `agents.merchants` (Trait, Memory), `agents.merchant_genetics` (Weight, YearBankruptcies) | `LodState` (agents.lod) | none | none | `agents.merchant_generation` (entity a, NY) | NY (and `merchants.traits`) |
| `agents.guidance` | `agents.guidance` | `agents.guidance` (Weights decay, FocusNode) | `CommandQueue.AppliedThisTurn` | none | none | none | every turn |
| `agents.groups` | `agents.guild_memberships` | `agents.guild_memberships` (rows removed when `Turn + 1 >= ExpiresTurn`) | none | none | `GuildMembershipExpired` | none | every turn |
| `agents.licenses` | `agents.licenses` | `agents.licenses` (rows removed when `Turn + 1 >= ExpiresTurn`) | none | none | `LicenseExpired` | none | every turn |

### Per-system notes

- **`agents.merchants`** (`Merchants.cs`, `ArbitrageMerchantBehavior.cs`, `MerchantJob.cs`, `MerchantJobTrips.cs`,
  `MerchantFlowJob.cs`, `MerchantTraits.cs`, `AgentTables.cs`; about 2,300 lines in C#, so the Rust port needs
  several files). `Execute` = `RunDetailed` then `RunAggregate`. `RunDetailed`: `PrepareRoutes` (per-route per-unit
  charge, danger, expected loss from `TravelCosts`, which depend on `world.roads` and `agents.development`
  RouteGrade/HarborLevel), `PrepareCoins` (coin rates from `economy.currency`, international reserves copied into the
  job and written back by `SettleCoins`), `HouseholdNeeds.Refresh`, `DetailedMerchantJob.Execute`, `SettleDetailed`
  (bill room settle against `bank.Reserve`, hedge net into reserves, tolls per node to the state through
  `MoneyLedger.ToState`), then `GoBankrupt` for the collected agents in collection order, then `RecordTripStats`.
- `DetailedMerchantJob` element order: `ResetScratch` (cargo row lists per agent, bid bounds, board), then agents
  `a = 0..N-1` where `IsStepped(a)` (Automated, and detailed at its node or home, or travelling), each running
  `Arrive` if due, `Act` if idle, then `PayUpkeep`; finally `RemoveEmptyRows` backwards with swap-back on the three
  cargo columns. Bankrupt agents are only collected in the job; `GoBankrupt` runs after it.
- `MerchantFlowJob` (aggregate nodes): nodes `0..N-1` skipped when Detailed; with `lod.mode = 1` it never does work.
  Same for `PoolIdleGroups`. It is plain loops over nodes, then each node's cells, then its outgoing routes.
- Merchant hidden fields: `_insurersForGroups` caches `MoneyLedger.InsuranceGuild` per node keyed on the group count
  only. Groups are only appended and the guild of a city never changes, so it equals a per-turn recompute.
  `_firstRow`, `_nextRow`, `_rows`, `_board`, `_stocked*`, `_bid*`, `_cellBits` are job scratch reset in `ResetScratch`.
  `_levels`, trait tables, vehicle tables, `_nodeCategorySpecialty`, `_routeToward`, `_nodePort`, `_nodeLicenseRow` are
  static from config and map.
- **`agents.banks`** (`BankSystem.cs`, 820 lines; `NpcLending.cs`, `BankTables.cs`): distribute newly minted coins,
  international balance, deposit interest, loan service (backwards), margin check, NPC loan service (backwards),
  lending to NPCs, auction close (backwards). Seizing collateral sells goods through `MarketFlows.Sell` and publishes
  one `CollateralSeized` per asset kind. `agents.production` also reaches `NpcLending.SeizeFacility`.
- **`agents.asset_markets`** (`AssetMarkets.cs`): `PayRent`, then (unless `market.credit_share` and `market.momentum`
  are both zero) `Fundamentals`, and per market `Credit` then `Reprice`; QE: `CloseQuarter` (groups trade, clears
  `BurstNation`, advances `HistoryIndex`, idle money, heat and burst). Scratch `_value`, `_idle`, `_lender` are rewritten
  by `Fundamentals`.
- **`agents.credit`** (`Credit.cs`, 786 lines): `PayBills` (bills backwards), `SellBillsOfGroupsInTheRed`,
  `ServiceBonds` (bonds backwards, default restarts the scan), `KeepFacilitiesInCash`, `SellStateBonds`, `RefreshRoom`
  (`BillRoom`), then `BondOffers.Turn = -1`. `OnLoaded` only resets that turn.
- **`agents.policy`** (`PolicySystem.cs`, 571 lines, `PolicyState.cs`): NY review in nation order then continent
  order; `Review` keeps per-run dictionaries (rebuilt each run). `Initialize` and `OnLoaded` call `ApplyTolls`.
- **`agents.demand_forecast`** (`MadeToOrder.cs`): `ChainPasses = 6` passes of `MeasureFedCells` and `PlanMakers`; all
  lists are scratch.
- **`agents.income_tax`**: the first call opens the books (`OpenBooks`) and returns, so turn 0 writes
  `agents.income_tax`; later calls only `EnsureRows` until NY.
- **`agents.guidance`**: every turn multiplies each weight by `1 - decay` (config `_keep`), then adds each applied
  command's signal.
- **`agents.groups`** is the cleanup of guild memberships. Its `OnLoaded` (`GroupSetup.AssignOwners`) can write
  `agents.groups` and `agents.facilities` after a load (older saves only); the step records never went through a load.

## Player commands

`Core/Commands.cs`: `CommandSystem` drains the queue, `Validate` then `Apply` per command, publishes `CommandRejected` or
`CommandApplied`; `CommandQueue.AppliedThisTurn` is cleared at the next drain. Thirty-four `ICommand` classes exist, all
in `Assets/_Game/Agents` (there are none elsewhere). Messages are the `CommandResult.Fail` strings; the list below
names the fixed ones, `{...}` ones are interpolated. "BO" marks commands that call `BondDesk` (they can be the first
asker of `BondOffers` in the Commands phase).

| Command (file) | Fields | Sections touched | Events | Notes |
|---|---|---|---|---|
| `BankTransferCommand` (BankCommands) | Amount | `agents.banks`, `agents.player`, `agents.finance` (reads bank run) | none | 7 messages incl. bank-run caps |
| `ExchangeCommand` | FromContinent, ToContinent, Amount | `agents.banks` (international reserve), `agents.player`, `economy.currency` | `CoinsExchanged` | 7 messages |
| `TakeLoanCommand` | Amount | `agents.loans`, `agents.banks`, `agents.player` | `LoanTaken` | 5 messages; credit-crunch refusal reads `agents.finance` |
| `RepayLoanCommand` | Amount | `agents.loans`, `agents.banks`, `agents.player` | `LoanRepaid` | 3 messages |
| `BidCommand` | Facility, Amount | `agents.auctions` | none | 4 messages |
| `BuyBondCommand` (BondCommands) | Issuer, Amount | `agents.bonds`, `agents.player`, `economy.household_cash`, `agents.groups`, `agents.banks` | `BondTraded` | BO; 3 messages |
| `SellBondCommand` | Issuer, Amount | same | `BondTraded` | BO; 2 messages |
| `MoveFacilityCashCommand` (FacilityCommands) | Facility, Amount | `agents.facilities`, `agents.income_tax`, `agents.player` | none | 3 messages |
| `SetFacilityControlCommand` | Facility, Mode | `agents.facilities` | none | 3 messages |
| `BuyFacilityCommand` (guidance) | Facility, Price, Axis | `agents.facilities`, `agents.groups`, `agents.banks`, `agents.npc_loans`, `agents.player`, `economy.household_cash` | none | 9 messages |
| `FoundCompanyCommand` (guidance) | Headquarters, Focus, Cost | `agents.groups`, `agents.player` | none | 6 messages |
| `ExpandFacilityCommand` (FacilityInvestment) | Facility | `agents.facilities`, `agents.player`, `economy.household_cash` | `FacilityExpanded` | 2 messages |
| `RepurposeFacilityCommand` (FacilityRepurpose) | Facility, DefKey | `agents.facilities`, `agents.player`, `economy.household_cash`; bumps unsaved `FacilityTable.UseChanges` | `FacilityRepurposed` | 3 messages |
| `JoinGuildCommand` (Guilds) | Guild | `agents.guild_memberships`, `agents.groups`, `agents.player` | `GuildJoined` | 1 message plus `GuildHalls.Check` |
| `MoveGuildHallCargoCommand` | Guild, Good, Quantity, ToHall | `agents.cargo` | none | 4 messages |
| `BuyPlotCommand` (Land) | none | `agents.land`, `agents.player`, `economy.household_cash` | none | 3 messages |
| `SellPlotCommand` | none | `agents.land`, `agents.player`, `economy.household_cash` | none | 3 messages |
| `BuyLicenseCommand` (Licenses, guidance) | Good | `agents.licenses`, `agents.player`, `agents.banks` (price to the nation's reserve via `MoneyLedger.ToState`) | `LicenseBought` | 6 messages |
| `BuyTradeLicenseCommand` | Kind | `agents.licenses`, `agents.player` | none | 5 messages |
| `BuyShipCommand` (PlayerShip) | none | `agents.ship`, `agents.player`, `economy.household_cash` | none | 2 messages |
| `SellShipCommand` | none | `agents.ship`, `agents.player`, `economy.household_cash` | none | 1 message |
| `LobbyCommand` (PolicySystem) | Nation, Policy, Direction, Amount, BaseGood | `agents.policy` (Lobbying), `agents.player`, `agents.groups`, `agents.banks` | `Lobbied` | 8 messages |
| `BuildRoadCommand` (Roads) | Destination | `world.roads`, `agents.player`, `economy.household_cash` | none | 6 messages |
| `BuyShareCommand` (Shares) | Facility, Count | `agents.shares`, `agents.groups`, `agents.player`, `economy.household_cash` | none | BO via `ShareBook`; 3 messages |
| `SellShareCommand` | Facility, Count | same | none | BO; 3 messages |
| `BuyShortLicenceCommand` (Shorts) | Kind | `agents.short_licences`, `agents.banks`, `agents.player` | none | 5 messages |
| `SellShortCommand` | Venture, Asset, Count | `agents.shorts`, `agents.banks` | none | BO; 3 messages |
| `CoverShortCommand` | Venture, Asset, Count | `agents.shorts`, `agents.banks` | none | BO; 4 messages |
| `BuyGoodsCommand` (TradeCommands) | Good, Quantity, PayFromDeposit | `agents.cargo`, `economy.markets`, `agents.player`, `agents.banks`, `agents.purses` | `GoodsTraded` | 1 message; reads `agents.finance` |
| `SellGoodsCommand` | Good, Quantity | `agents.cargo`, `economy.markets`, `agents.player`, `agents.purses` | `GoodsTraded` | 3 messages |
| `MoveCargoCommand` | Facility, Good, Quantity, ToWarehouse | `agents.cargo` | `CargoMoved` | 3 messages |
| `TravelCommand` (TravelCommands) | Destination, Guards, Insurance | `agents.player`, `agents.cargo`, `agents.purses`, `agents.groups`, `economy.household_cash` | `TripStarted`, `TripEncounter` | 6 messages; draws `agents.encounters` entity 0, salt 0 |
| `BuyVentureShareCommand` (Ventures) | Venture, Count | `agents.ventures`, `agents.player` | none | BO (through `Ventures.cs:120`); 3 messages |
| `SellVentureShareCommand` | Venture, Count | `agents.ventures`, `agents.player` | none | 2 messages |

Commands that read unsaved services: all quote commands (`TravelCommand`, `BuildRoadCommand`) build a fresh
`TravelCosts`; `BuyLicense` and trade licences read the merchant-independent `LicenseRules` streams only for NPCs.
Commands that mutate nothing unsaved: all but `RepurposeFacilityCommand` (`UseChanges`, read only by the production line
cache, which Rust rebuilds each turn) and the BO group (`BondOffers`).

## Random streams

All draws are `DetRandom.For(seed, turn, stream, entity, salt)` (equal to `ctx.RandomFor`); the "turn 0" rows pass a
literal 0 so the draw is independent of the turn.

| Stream | Where | Turn | Entity, salt |
|---|---|---|---|
| `agents.encounters` | `Encounters.Roll`: player trips (entity 0, salt 0); merchant trips in `DetailedMerchantJob` (entity a, salt 1) | current | as given |
| `agents.merchant_bankrupt` | `GoBankrupt` (new trait draw, `MerchantGenetics.Draw`) | current | a |
| `agents.licenses.merchants`, `.international`, `.intercontinental` | `LicenseRules` and `DetailedMerchantJob.HoldsLicenses` / `HoldsTradeLicense` | 0 | merchant a |
| `agents.merchant_capital`, `agents.merchant_trait`, `agents.spawn`, `agents.player.start` | world build only | 0 | build-time |
| `agents.merchant_join` | `MerchantMembershipSystem` (NY) | current | a |
| `agents.merchant_generation` | `MerchantGenerationSystem` (NY; one rng per agent, first draw gates, the rest feed `Draw`) | current | a |
| `agents.merchant_vehicle_wear` | `MerchantVehicleSystem` (NY) | current | a |
| `agents.ventures.wreck` | `VentureSystem` (sailing) | current | v |
| `agents.finance.events` | `FinanceSystem.UpdateEvent` (QE; dead with chance 0) | current | nation |

Systems with no draws: travel, banks, roads, state_spending, labor, defense, asset_markets, shorts, income_tax, nobles,
migration, state_projects, subsidies, investment, orders, demand_forecast, contracts, strategic_subsidy, credit,
trader_loans, policy, guidance, groups, licenses.

## Burst jobs

| Job | Run by | Element order | Notes |
|---|---|---|---|
| `DetailedMerchantJob` (`IJob`) | merchants | agents ascending | managed path exists (`ForceManaged`), used by `MerchantTests` and `SimulationTests` parity checks; fixtures were exported with the default Burst path |
| `MerchantFlowJob` (`IJob`) | merchants | nodes, cells, routes ascending | idle with lod mode 1 |
| `DefensePurchaseJob` (`IJob`) | defense | pass 1 nodes (stock value per nation), pass 2 nodes then cells; per-nation `Carry` | runs only on `Turn % 4 == 0`; has no managed twin, `Schedule().Complete()` always |

Production, upkeep, pricing and consumption jobs were audited in Plan 3.

## Unsaved-input register

"Derived" means a pure function of saved sections, config and map at the start of the turn (or at the writer's slot).

| Unsaved state | Written by | Read by (Plan 4) | Rebuilt on C# load | Rust obligation |
|---|---|---|---|---|
| `LodState.Detail` | `agents.lod` | merchants, merchant_generation | `LodSystem.OnLoaded` | Already ported; computed at its slot before both readers |
| `NodeTolls.Tariff`, `SpecialtySurcharge` | `PolicySystem` (build, `OnLoaded`, review when `TollStep` changes) | merchants (Tariff, SurchargeJob inputs), `TravelCosts` (tolls in quotes) | `PolicySystem.OnLoaded` -> `ApplyTolls` | Derive from saved `TollStep` for every nation; refresh at the start of Task 3's merchants and at policy's slot; check in the replay |
| `BondOffers` (Turn, Houses, Groups, Nations) | first `BondDesk` call of a turn; depleted by every later call | commands, ventures, asset_markets, investment, credit | `CreditSystem.OnLoaded` sets `Turn = -1` | Keep as Rust unsaved state with the same lazy first-ask rule (built from `HouseholdCash.Cash`, `Desired`, `GroupTable.Cash`, `IsPlayer` at that moment) and `Turn = -1` at the end of credit; port users in pipeline order |
| `AssetMarkets.BurstNation` | `AssetMarketSystem.CloseQuarter` (clear, then set on burst) | `FinanceSystem.UpdateEvent` | not rebuilt (cleared before use) | Rust field on `AssetMarkets`, not in the section bytes; written only at QE by asset_markets |
| `MoneyTotals` | `FinanceSystem` (`Recompute` on QE) and the view | `FinanceSystem` record | `FinanceSystem.OnLoaded` invalidates | Recompute inside finance at its slot from the state at that point |
| `DevelopmentState.SubsidyRate` | `SubsidySystem` (NY, after policy changes) and `OnLoaded` | production | `SubsidySystem.OnLoaded` | Plan 3 derived it; Task 5's subsidies must call the same fill after changing `Subsidies` |
| `DevelopmentState.SubsidyLeft` | production | production | not rebuilt | Scratch, unchanged |
| `FacilityTable.UseChanges` | `ChangeUse` (command) | production line cache | not saved | Unneeded (Rust rebuilds lines each turn) |
| `CommandQueue.AppliedThisTurn` | `CommandSystem` | `agents.guidance` | `NotifyLoaded` clears the queue | Already kept in Rust; add guidance hook to `Command` |
| `TravelCosts` quotes | derived per call from `world.roads`, `agents.development` (RouteGrade, HarborLevel), `NodeTolls`, policy, markets | merchants, state_projects, travel and road commands | not stored | Pure function; no state |
| `HouseholdNeeds.Units` | merchants (`Refresh` each turn) | merchants | not stored | Pure function of saved households, markets, currency |
| `GroupDirectory` | derived from `GroupTable` | banks, investment, merchant_membership, state_projects, subsidies, policy, production | rebuilt when `Count` or `LoadCount` changes | Pure lookup; last row wins for private enterprise, state enterprise, guild; `PrivateAt` appends a row on demand (and `FamilyAt` then pays it from the local households) |
| `PlayerBalanceSheet` | derived | banks, commands | not stored | Pure function |
| `CompanyRooms` | refreshed before each use | investment, credit | not stored | Pure function of facilities and bonds, refreshed identically |
| `MerchantTripStats`, `CreditStats` | merchants, labor, orders, income_tax, membership, credit, investment | tests, view only | not stored | Not read by any system; keep only the in-job `TripStats[8]` counter |
| `HouseholdCash.LuxuryTaxYear` | household_spending | tests only | not rebuilt | As Plan 3 |
| `NodeTable`, `RouteTable`, `NodeBanks`, `NodePorts`, `NodeSpecialties`, `RouteHazards`, `PurseState.NodeFloat/NationNodes`, `MerchantTraitTable`, `MarketStore` static columns | build | many | built with the world | Static |

## Hidden state (private fields that survive a turn)

Only the items below hold anything across turns; every other private field is scratch cleared or fully rewritten at the
start of the system's run, or static from config and map.

| System | Field | Verdict |
|---|---|---|
| `agents.merchants` | `_insurersForGroups`, `_nodeInsurer` | Real cache keyed on group count; equal to a per-turn recompute because groups are only appended |
| `agents.merchants` | `_firstRow` (resized when the agent count changes), `_nextRow`, `_rows`, `_board` | Scratch, rebuilt in `ResetScratch` |
| `agents.contracts` | `_refused` | Cleared each NY before `Sign`, never read elsewhere |
| `agents.credit` | `_stateBehind`, `_floatPot`, `_owed`, `_rooms` | Cleared or refreshed at the start of the step that uses them |
| `agents.investment` | `_facilitiesAt`, `_groupsAt`, `_groupsListed` | Rebuilt at the start of each NY run (`_groupsListed = 0`) |
| `agents.merchant_membership` | `_openAt` | Rebuilt on NY before use |
| `agents.subsidies` | `_count` | Rebuilt on NY |
| `agents.defense` | `_spend`, `_paid`, `_value`, `_carry` | `_spend` is recomputed every turn; the others are reset by the job |
| `agents.asset_markets` | `_value`, `_idle`, `_lender` | Rewritten by `Fundamentals` each run |
| `agents.demand_forecast` | `_makers`, `_lines`, `_fedCells`, flow arrays | Rebuilt at each Q0 run |
| `agents.policy` | `Review` dictionaries | New per NY run |

C# bugs: none found in the code read for this audit; observations that are benign are noted above (the `PrivateAt`
rebuild marker, the insurer cache key). Nothing was added to `port-notes.md`.

## C# load path

`GameSimulation.Load` -> `TurnPipeline.NotifyLoaded`: clears the command queue and event bus, then calls `OnLoaded` in
pipeline order. Plan 4 listeners:

| Listener | Rebuilds | Effect on a continuing run (Task 9 mid-run load) |
|---|---|---|
| `CreditSystem` | `BondOffers.Turn = -1` | A turn boundary already has `Turn = -1` (credit clears it last), so a loaded run continues identically |
| `FinanceSystem` | `MoneyTotals.Invalidate()` | Recomputed at the next QE anyway |
| `PolicySystem` | `ApplyTolls()` | Equals the incremental tolls (set from base, never multiplied) |
| `GroupSystem` | `GroupSetup.AssignOwners`, then `PayLocalHouseholds` for new rows | Finds none on a current save |
| (Plan 3) lod, modifier_fold, production, subsidies | as `plan3-audit.md` | |

The `AssetMarkets.BurstNation` flags are lost on load but cleared before the next read, so they cannot matter. Saves at
turns 52 and 520 (Task 9) are turn boundaries.

## Group order for Tasks 3-8

The step replay replays every unported system by applying its recorded section changes, so ordering only matters where
a Rust system reads unsaved state written by an unported earlier one. Constraints found:

1. `NodeTolls` (merchants, Task 3) must be derived from saved `TollStep`; do not depend on the Rust policy (Task 6).
2. `BondOffers`: ventures before asset_markets (inside Task 4), then investment (Task 5), then credit (Task 6). Each
   task keeps the invariant that every `BondDesk` user earlier in the pipeline than a ported one is also ported. Until
   Task 8, the Commands phase has no first-asker; Task 8's commands call the same Rust `BondDesk` before any system
   (their turn begins with `Turn != offers.Turn`, so they build the offers first).
3. `asset_markets` before `finance` (Task 4): `BurstNation`.
4. `income_tax` (115) and `households` (P3, 120) have no unsaved coupling; `economy.currency` (P3) precedes banks.
5. `agents.guidance` needs the `Command` guidance hook; `agents.groups` and `agents.licenses` are independent and trivial,
   so they may be done earlier if a task has room.

Recommended order, which equals the plan's tasks with the two inside-task orderings:

| Task | Systems, in the order to port and register |
|---|---|
| 3 | `agents.travel`, then `agents.merchants` (derive `NodeTolls`; split by concern: behaviour, job, trips, flow, traits) |
| 4 | `agents.banks`, `agents.roads`, `agents.state_spending`, `agents.labor`, `agents.defense`, `agents.ventures`, `agents.asset_markets`, `agents.finance`, `agents.shorts` |
| 5 | `agents.income_tax`, `agents.nobles`, `agents.migration`, `agents.state_projects`, `agents.subsidies`, `agents.investment`, `agents.merchant_vehicles` |
| 6 | `agents.orders`, `agents.demand_forecast`, `agents.merchant_membership`, `agents.contracts`, `agents.strategic_subsidy`, `agents.credit`, `agents.trader_loans`, `agents.policy`, `agents.merchant_generation` |
| 7 | `agents.guidance` (with the `Command` hook), `agents.groups`, `agents.licenses` |
| 8 | the 34 commands and the views |

Add each ported id to `PORTED_SYSTEMS` in `engine/fes-parity/src/steps.rs` in the same order. Tasks that touch the
tables listed in Verdict note 6 must check that the Rust removal is swap-back.

## Service checks in the step replay

Existing checks (Plan 3): `LodState.detail`, `MarketFlows.multipliers`, `DevelopmentState.subsidy_rate`. Additions:

| Service | Rebuild compared against | Writer | Sources | When fresh |
|---|---|---|---|---|
| `NodeTolls.tariff`, `specialty_surcharge` | derive from `agents.policy` `TollStep`, map base tolls and config | `agents.policy` (and the load) | `agents.policy` | after policy ran in Rust, and from turn 0 until a record changes `agents.policy` |
| `DevelopmentState.subsidy_rate` | `fill_subsidy_rates` | `agents.subsidies` (new check at its slot), production reader | `agents.development` | as Plan 3, now also refreshed by Rust subsidies on NY |
| `BondOffers.turn` | must be -1 after the turn once credit is ported (and after ventures..investment alone, may stay equal to the current turn) | `agents.credit` | none | after credit ran in Rust |

Not checked, with the reason:

- `BondOffers` contents: depleted by calls, not derivable from saved state; covered by the section compares of
  `economy.household_cash`, `agents.bonds`, `agents.groups`, `agents.facilities`, `economy.markets` and by unit tests
  that fix the first-ask timing (Task 6 and Task 8).
- `AssetMarkets.BurstNation`: not derivable; the sampled QE turns 51 and 103 only show it if a burst happened. Cover it
  with a unit test (burst, then finance QE gives `CreditCrunch`) in Task 4.
- `MoneyTotals`: valid only at finance's slot; a bank run changes inputs after the compute, so a post-record compare
  would differ. Covered by `agents.finance` records (`NationTotal`, `ContinentTotal`) at turns 51 and 103.
- Per-turn derived helpers (`TravelCosts`, `HouseholdNeeds`, `GroupDirectory`, `PlayerBalanceSheet`, `CompanyRooms`,
  insurer cache): recomputed inside the system that needs them, nothing crosses records.
- `MerchantTripStats`, `CreditStats`, `HouseholdCash.LuxuryTaxYear`: not state.
- `CommandQueue.AppliedThisTurn`: empty in the fixtures; Task 8 tests exercise guidance.

## C# tests that cover each system

By reference to the system's types under `Assets/_Game/Tests/EditMode` (most simulation tests drive several systems
through `GameSimulation`, so a file may cover more than listed). Task owners port the tests marked; the golden runs
cover the rest only on the turns they reach.

| System or area | Test files |
|---|---|
| travel, encounters, costs | `TravelCostTests`, `HazardTests`, `InfrastructureTests`, `TradeLicenseTests`, `SimulationTests` |
| merchants | `MerchantTests` (managed and Burst parity), `MerchantSaleTests`, `MerchantInsuranceTests`, `MerchantCapitalTests`, `InternationalTradeTests`, `TraderMarginTests` |
| banks, loans, auctions, margin | `BankTests`, `MarginTests`, `MoneyTests`, `FacilityClosureTests`, `PlayerRuleTests` |
| roads | `InfrastructureTests` |
| state_spending, labor | `MoneyTests`, `TraderLoanTests` |
| defense, state_projects, subsidies | `StateProjectTests`, `SubsidySiteTests`, `StateImportsTests` |
| ventures | `VentureTests`, `ShortTests` |
| asset_markets, land rent | `AssetMarketTests`, `RentTests`, `LandTests` |
| finance | `FinanceTests`, `AssetMarketTests`, `LoadTransitionTests` |
| shorts | `ShortTests` |
| income_tax | `MoneyTests`, `BankTests`, `FacilityClosureTests`, `ShareTests` |
| nobles | `NobleTests` |
| migration | `MigrationTests` |
| investment, orders, demand_forecast | `InvestmentTests`, `ForecastTests`, `RecipeMarginTests` |
| merchant_vehicles | `InvestmentTests`, `MerchantCapitalTests` |
| merchant_membership | `MoneyTests`, `FinanceTests`, `InvestmentTests` |
| contracts, strategic_subsidy | `ContractRuleTests`, `InvestmentTests` |
| credit, bills, bonds | `CreditTests`, `BillMarketTests`, `ClassesCoverAndBondsTests`, `MerchantSaleTests` |
| trader_loans | `TraderLoanTests`, `TraderMarginTests` |
| policy, licenses | `PolicyTests`, `LicenseTests`, `TravelCostTests` |
| merchant_generation | `MerchantTests` |
| guidance | `SimulationTests` |
| groups | `GroupTests`, `LoadTransitionTests` |
| shares | `ShareTests`, `ShortTests`, `MarginTests` |
| golden hashes | `GoldenRunTests` |

Tests that issue commands (Task 8): `PlayerRuleTests` (BuyFacility, FoundCompany, SetFacilityControl, Bid, BuyGoods),
`SimulationTests` (BuyFacility, FoundCompany, BuyGoods), `MoneyTests` (MoveFacilityCash, SellGoods),
`ClassesCoverAndBondsTests` (BuyBond, SellBond), `ShareTests` (BuyShare, SellShare), `ShortTests` (BuyShortLicence,
SellShort, CoverShort, BankTransfer), `VentureTests` (venture shares), `LandTests` (plots), `MarginTests` (TakeLoan,
RepayLoan), `FacilityRepurposeTests`, `TradeLicenseTests`, `TraderMarginTests`, `FinanceTests`. Commands with no C#
test: `ExchangeCommand`, `ExpandFacilityCommand`, `JoinGuildCommand`, `MoveGuildHallCargoCommand`, `BuyLicenseCommand`,
`BuyShipCommand`, `SellShipCommand`, `LobbyCommand`, `BuildRoadCommand`, `MoveCargoCommand`, `TravelCommand` by name;
Task 8 writes Rust tests for them from the validation messages above (some are covered indirectly through
`HazardTests`, `InfrastructureTests`, `PolicyTests`, `LicenseTests`, `TravelCostTests`).
