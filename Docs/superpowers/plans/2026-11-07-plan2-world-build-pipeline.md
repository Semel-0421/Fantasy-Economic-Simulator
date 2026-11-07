# Plan 2: World Build and Turn Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `fes-sim` crate that builds the turn-0 world from `GameDatabase` + `MapDef` + seed with every
one of the 43 saved sections byte-equal to C#, loads and saves it, and runs a turn pipeline (phases,
order, events, commands) with no systems yet — the frame Plans 3 and 4 fill system by system.

**Architecture:** One component type per C# `IPersistentComponent`, each with `write` and `read` in .NET
`BinaryWriter` layout, kept in a `WorldState` ordered by key (UTF-16 ordinal). Modules (`world`, `economy`,
`agents`) register components and systems in C#'s order. Each component is proved twice: reading the
section from `start.save` and writing it back gives the same bytes (layout), and building it gives the same
bytes (build logic). `fes-parity` gets the real `BuildWorld` and reports every section ported-equal.

**Tech Stack:** Rust 1.99.1 workspace from Plan 1 (`fes-core`, `fes-data`, `fes-save`, `fes-parity`), new
crate `fes-sim`; Unity 6000.6.6f1 EditMode test (C#) for the extra fixtures.

**Spec:** `docs/superpowers/specs/2026-11-04-rust-web-port-design.md` (sections 6, 7.2). Roadmap:
`docs/superpowers/plans/2026-11-04-rust-port-roadmap.md`. Plan 1: `2026-11-04-plan1-engine-foundation.md`.

## Global Constraints

Plan 1's constraints hold unchanged (integers and `Fx` only, `clippy::float_arithmetic` denied in every sim
crate including `fes-sim`; half-away rounding; UTF-16 string hashing; .NET `BinaryWriter` layout; keys in
ordinal order; DEC-108 limits: cognitive complexity 15, function 80 lines, parameters 7, file 1,000 lines,
nesting 3; `rustfmt`, `clippy --all-targets --all-features -D warnings`; rules and order of operations
ported as they are, C# bugs into `docs/superpowers/port-notes.md`; English). In addition:

- C# reference `4bf3804`; nothing under `Assets/_Game` changes except new files in `Assets/_Game/Tests/EditMode/`.
- The parity contract is `fes_parity::BuildWorld = fn(&GameDatabase, &MapDef, u64) -> Result<Vec<RawSection>, String>` (Plan 1 ruling; it replaced the plan's `BuildSection`).
- Saves: current `FormatVersion` and current section versions only; no migrations (spec 9).
- Turn-0 sections (43, from `start.save`): `agents.asset_markets, auctions, banks, bill_room, bills, bonds,
  cargo, contracts, demand_forecast, development, facilities, finance, groups, guidance, guild_memberships,
  income_tax, labor, land, licenses, loans, margin, merchant_genetics, merchants, migration, nobles,
  npc_loans, orders, player, policy, purses, shares, ship, short_licences, shorts, trader_loans, ventures`;
  `core.clock`; `economy.currency, household_cash, households, markets, modifiers`; `world.roads`.
- Seeding draws use `DetRandom::for_key(seed, 0, det_hash::of(stream), entity, salt)` with C#'s stream
  names (`agents.player.start`, `agents.spawn`, `agents.merchant_trait`, `economy.currency`, ...).
- No timing (`Stopwatch`) code in the pipeline; Plan 5 measures from outside.

## Review Focus

- A section read from `start.save` and written back differs by one byte (field order, a `bool` as one byte,
  a list count as `i32`) — every component task has the `round_trips_start_save` test over all 4 runs.
- Two keys whose ordinal order differs between UTF-8 and UTF-16 — `WorldState` reuses `fes_save::world_hash`
  ordering; Task 1 test with `"\u{FF61}"` and `"\u{1D504}"` keys.
- A map with an invalid parent or a bad route is rejected with C#'s message before anything is built —
  Task 3 ports `WorldBuilder_RejectsInvalidParent` and `WorldBuilder_RejectsBadRoute_WithoutAllocating`.
- An event handler that publishes or subscribes during dispatch sees the same delivery order as C#'s index
  loop — Task 2 test.
- A run with a config override (NoBills) builds a different world from the same map — Task 7 checks all 4
  runs, not only the default ones.

---

### Task 0: Turn-0 and build fixtures from C#

**Files:**
- Modify: `Assets/_Game/Tests/EditMode/ParityFixtureExporter.cs`
- Modify: `engine/fixtures/README.md`
- Output: `engine/fixtures/runs/<name>/turn0.json`, `engine/fixtures/build/*.json` (committed)

**Interfaces:**
- Produces: `runs/<name>/turn0.json` = `{ "world_hash": "<u64>", "sections": { key: "<fnv u64>" } }` for
  all 4 runs, computed on the built world before any turn.
- Produces: `build/nodes.json` (Default_Seed1): the node table after `WorldBuilder` in built order
  (key, level, parent, continent, core, children CSR) and the route table (from, to, travel turns, cost,
  fee, outgoing CSR), via `ParityJson`; `build/systems.json`: every system in pipeline order
  `[{ "id", "phase", "order" }]` and every module in `ModuleOrder` order.

- [ ] **Step 1: Add the outputs** to `WritesParityFixtures` from public members only (`sim.World`,
  `sim.Systems`, services). If a needed table is not reachable through public members, write what is and
  report the rest; do not change simulation code.
- [ ] **Step 2: Run** `unity recompile --timeout 200 --no-banner --format json`, then the explicit test
  (`--filter WritesParityFixtures --include_explicit true`). Expected: pass, existing fixture files
  byte-identical (`git diff --stat engine/fixtures` shows only new files).
- [ ] **Step 3: Replace Plan 1's hardcoded turn-0 constant** in `engine/fes-save/tests/container.rs` with
  `turn0.json` for every run (`world_hash_matches_turn_zero` loops the 4 runs).
- [ ] **Step 4: Run** `GoldenRunTests` (pass) and `cargo test --all-features` (pass). **Commit.**

---

### Task 1: `fes-sim` crate, components and `WorldState`

**Files:**
- Create: `engine/fes-sim/{Cargo.toml, src/lib.rs, src/world.rs, src/clock.rs, src/schedule.rs}`
- Create: `engine/fes-sim/tests/world_state.rs`
- Modify: `engine/Cargo.toml` (member), `engine/fes-parity/Cargo.toml` (depends on `fes-sim`)

**Interfaces:**
- Consumes: `fes_core::io::{DotNetWriter, DotNetReader}`, `fes_save::{RawSection, world_hash, SaveHeader, write_save, read_save}`.
- Produces: `pub trait Component: Any { fn key(&self) -> &'static str; fn version(&self) -> i32; fn write(&self, w: &mut DotNetWriter<Vec<u8>>); fn read(&mut self, r: &mut DotNetReader, version: i32) -> Result<(), IoError>; }`.
- Produces: `WorldState` with `add<C: Component>(C)` (error on a duplicate key), `get<C>() -> &C`, `get_mut<C>()`, `sections() -> Vec<RawSection>` (ordinal key order), `load_sections(&[RawSection]) -> Result<(), SimError>` (unknown or missing key is an error), `hash() -> u64` (= `world_hash(&sections())`).
- Produces: `SimClock { seed: u64, turn: i32 }` (`core.clock`, version and layout from `Core/SimClock.cs`), `advance()`; `TierSchedule::new(&[TierCadence])` with `is_due(level, turn)` and `elapsed_turns(level, turn)` from `Core/TierSchedule.cs`.
- Produces: `SimError` (enum, `Display`) used by every later task.
- Produces (test helper, feature `testing`): `fn assert_round_trip(key: &str)` reading that section from each run's `start.save`, `read` into a fresh component, `write`, and asserting equal bytes with the first differing offset.

- [ ] **Step 1: Failing tests:** `clock_round_trips_start_save`; `world_hash_orders_keys_by_utf16` (keys
  `"\u{FF61}"` and `"\u{1D504}"` through fake components); `duplicate_key_is_rejected`;
  `missing_section_on_load_is_an_error`; `tier_schedule_matches_csharp` (period/offset cases from
  `CoreTests.cs`).
- [ ] **Step 2: Run** `cargo test -p fes-sim`. Expected: FAIL (crate missing).
- [ ] **Step 3: Implement.** Components live in a `Vec<Box<dyn Component>>` plus a key index; `get` downcasts
  via `Any`.
- [ ] **Step 4: Run** tests, fmt, clippy, file-length. Expected: PASS. **Commit.**

---

### Task 2: Pipeline, events and commands

**Files:**
- Create: `engine/fes-sim/src/{pipeline.rs, events.rs, commands.rs, context.rs, module.rs}`
- Create: `engine/fes-sim/tests/pipeline.rs`

**Interfaces:**
- Consumes: Task 1.
- Produces: `enum SimPhase { Commands = 0, Lod, Environment, Production, Agents, Markets, Finance, Guidance, Cleanup }`; `mod sim_order { EARLY = -100, DEFAULT = 0, LATE = 100 }`.
- Produces: `pub trait System { fn id(&self) -> &'static str; fn phase(&self) -> SimPhase; fn order(&self) -> i32 { 0 } fn initialize(&mut self, ctx: &mut SimContext) -> Result<(), SimError> { Ok(()) } fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError>; }`.
- Produces: `pub trait Module { fn id(&self) -> &'static str; fn requires(&self) -> &[&'static str]; fn register(&self, b: &mut SimBuilder) -> Result<(), SimError>; }`; `module_order(&[Box<dyn Module>])` = C# `ModuleOrder.Sort` (requires first, else insertion order; cycle or unknown id is an error).
- Produces: `TurnPipeline::new(Vec<Box<dyn System>>)` sorted by (phase, order, id ordinal); `advance(&mut SimContext)`: `events.begin_turn()`, each phase's systems then `events.dispatch()`, then `clock.advance()` — exactly `Core/Pipeline.cs`.
- Produces: `EventBus` with `subscribe::<E>(handler)`, `publish(E)`, `dispatch()` (index loop: handlers may publish or subscribe during dispatch), `turn_log()`; events are `pub trait SimEvent: Any + Debug`.
- Produces: `trait Command { fn apply(&self, ctx: &mut SimContext) -> CommandResult; }`, `CommandResult::{Ok, Error(String)}`, `CommandQueue { enqueue, applied_this_turn, len }`, `CommandSystem` (phase `Commands`) publishing `CommandApplied` / `CommandRejected` as `Core/Commands.cs`.
- Produces: `SimContext { world, services, events, commands, schedule, turn(), seed(), is_due(level), random_for(stream: &str, entity: i32, salt: u32) -> DetRandom }`; `Services` holds `Arc<GameDatabase>`, `Arc<MapDef>` and typed extras.

- [ ] **Step 1: Failing tests:** `systems_run_in_phase_order_id_order` (fake systems logging ids);
  `events_dispatch_after_each_phase`; `handler_publishing_during_dispatch_is_delivered_same_turn` (C# index
  loop semantics); `clock_advances_after_cleanup`; `rejected_command_publishes_rejection`;
  `module_order_puts_requires_first_and_rejects_cycles`; `random_for_equals_det_random_for_key`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** tests, fmt, clippy — PASS. **Commit.**

---

### Task 3: World module — nodes, routes and `world.roads`

**Files:**
- Create: `engine/fes-sim/src/world/{mod.rs, nodes.rs, routes.rs, extras.rs, roads.rs}` (port of
  `World/WorldBuilder.cs`, `World/Infrastructure.cs` and the node/route extras: specialties, tolls, banks,
  hazards, ports)
- Create: `engine/fes-sim/tests/world_module.rs`

**Interfaces:**
- Consumes: Tasks 1-2; `MapDef`, `GameDatabase`, `SimulationConfig`.
- Produces: `NodeTable` (built order, `parent`, `continent`, `level`, `core`, children CSR, `absorb`),
  `RouteTable` (outgoing CSR), `NodeSpecialties`, `NodeTolls`, `NodeBanks`, `RouteHazards`, `NodePorts` —
  services, not saved; `RoadState` (`world.roads`) as a `Component`; `WorldModule` (`Module`, id as C#).
- Produces: `fn build_nodes(map: &MapDef, config: &SimulationConfig) -> Result<NodeTable, SimError>`, `fn build_routes(map: &MapDef, nodes: &NodeTable, config: &SimulationConfig) -> Result<RouteTable, SimError>`.

- [ ] **Step 1: Failing tests:** `nodes_match_csharp` and `routes_match_csharp` (against
  `build/nodes.json`); `roads_round_trips_start_save`; `roads_build_matches_start_save` (all 4 runs);
  `orders_by_level_and_allows_village_under_region`, `rejects_invalid_parent`,
  `rejects_bad_route_without_allocating` (ported from `DataAndWorldTests.cs`, same messages).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement** in `WorldBuilder` order (nodes, optional absorb,
  routes, specialties, tolls, banks, hazards, ports, roads). **Step 4: Run** — PASS. **Commit.**

---

### Task 4: Economy module at turn 0

**Files:**
- Create: `engine/fes-sim/src/economy/{mod.rs, markets.rs, layout.rs, modifiers.rs, currency.rs, households.rs, household_cash.rs}`
- Create: `engine/fes-sim/tests/economy_module.rs`

**Interfaces:**
- Consumes: Tasks 1-3 (`NodeTable`, `NodeSpecialties`).
- Produces: components `MarketStore` (`economy.markets`, built by `MarketLayoutBuilder::build(nodes, db, specialties)`), `MarketModifierTable` (`economy.modifiers`), `CurrencyState` (`economy.currency`), `HouseholdState` (`economy.households`), `HouseholdCash` (`economy.household_cash`); service `MarketFlows` (not saved); `EconomyModule` registering them in `Economy/MarketSystems.cs` order. Systems are not ported here (Plan 3); the module registers none yet.
- Field names follow C# in snake_case; arrays stay columnar (`Vec<T>` per column) as the C# `NativeArray`s.

- [ ] **Step 1: Failing tests,** for each of the 5 keys: `<key>_round_trips_start_save` and
  `<key>_build_matches_start_save` (4 runs).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement** write/read first (round trip), then the build logic.
  `Households.cs` skips 3 legacy sections on old versions: port only the current version's path and note it.
- [ ] **Step 4: Run** — PASS. **Commit.**

---

### Task 5: Agent finance and securities tables

**Files:**
- Create: `engine/fes-sim/src/agents/{mod.rs, banks.rs, loans.rs, credit.rs, securities.rs, purses.rs, labor.rs, land.rs, finance.rs}` (split further if a file passes 1,000 lines)
- Create: `engine/fes-sim/tests/agents_finance.rs`

**Interfaces:**
- Consumes: Tasks 1-4.
- Produces components with their C# keys: `agents.banks, margin, loans, auctions, npc_loans, trader_loans,
  finance, income_tax, purses, labor, land, bills, bill_room, bonds, shares, shorts, short_licences,
  asset_markets, ventures`; services created alongside them in `AddFinanceAndStateTables` that are not saved
  (`MoneyTotals`, `CreditStats`, `BondOffers`, ...) as plain structs.
- Produces: `fn add_finance_and_state_tables(b: &mut SimBuilder) -> Result<(), SimError>` in C# order.

- [ ] **Step 1: Failing tests:** `<key>_round_trips_start_save` and `<key>_build_matches_start_save` for
  each of the 19 keys (a macro over the key list is fine).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement** (round trip first, then build). **Step 4: Run** — PASS.
  **Commit** (one commit per file group is fine).

---

### Task 6: Agent trade, society and policy tables

**Files:**
- Create: `engine/fes-sim/src/agents/{player.rs, cargo.rs, licenses.rs, ship.rs, orders.rs, contracts.rs, development.rs, nobles.rs, migration.rs, policy.rs, guidance.rs}`
- Create: `engine/fes-sim/tests/agents_society.rs`

**Interfaces:**
- Consumes: Tasks 1-5.
- Produces components: `agents.player` (start node from `ResolveStartNode`, stream `agents.player.start`),
  `cargo, licenses, ship, orders, demand_forecast, contracts, development, nobles, migration, policy,
  guidance`; non-saved `LodState` allocated as C# does.

- [ ] **Step 1: Failing tests:** round trip and build for each of the 12 keys.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS. **Commit.**

---

### Task 7: Seeding, `AgentsModule` and the full build

**Files:**
- Create: `engine/fes-sim/src/agents/{merchants.rs, facilities.rs, groups.rs, genetics.rs, seeding.rs, module.rs}`
- Create: `engine/fes-sim/src/{builder.rs, simulation.rs}`
- Modify: `engine/fes-parity/src/{lib.rs, main.rs}` (real builder, every turn-0 key ported)
- Create: `engine/fes-sim/tests/build.rs`

**Interfaces:**
- Consumes: Tasks 1-6.
- Produces components `agents.merchants, facilities, groups, guild_memberships, merchant_genetics`; facility
  seeding and NPC spawn (stream `agents.spawn`, salt `(tier << 24) | k`; traits on stream
  `agents.merchant_trait` only when `MerchantTraitsEnabled`); `AgentsModule` registering every agents
  component in `Agents/AgentsModule.cs` order (systems come in Plan 4).
- Produces: `SimulationBuilder::new(db, map, seed)`, `.from_data(&dyn DataSource, seed)`, `.build() -> Result<Simulation, SimError>` following `GameSimulation.Build()` (clock, services, `CommandSystem`, modules in `module_order`, pipeline, `initialize`).
- Produces (spec 7.2 shell for Plan 5): `Simulation { pub fn step(&mut self) -> Result<TurnReport, SimError>; pub fn queue(&mut self, cmd: Box<dyn Command>); pub fn save(&self) -> Vec<u8>; pub fn load(db, map, bytes) -> Result<Simulation, SimError>; pub fn world_hash(&self) -> u64; pub fn turn(&self) -> i32 }`; `TurnReport { turn: i32, events: Vec<Box<dyn SimEvent>> }` (the turn log). Views come with the systems that own the data.
- Produces: `fes_sim::build_world: BuildWorld` and `TURN0_KEYS: &[&str]` (the 43 keys); `fes_parity::ported_sections()` returns them.

- [ ] **Step 1: Failing tests:** round trip and build for the 5 keys; `build_matches_turn0_world_hash` (all
  4 runs, against `turn0.json`, and the full `start.save` bytes after the header equal `save()`);
  `load_then_save_is_identity`; `step_with_no_systems_advances_the_clock`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** tests, then
  `cargo run -p fes-parity -- --fixtures fixtures`. Expected: every run `43 ported-equal, 0 different`, exit 0.
- [ ] **Step 5: Commit.** Ask the user before pushing; when allowed, push and confirm CI.

---

### Task 8: Build speed note

**Files:**
- Create: `engine/fes-sim/benches/build.rs` (plain `std::time::Instant` loop in a `#[test] #[ignore]`, no
  extra crate)
- Modify: `docs/superpowers/port-notes.md`

- [ ] **Step 1: Measure** `SimulationBuilder::build` on the sample world (release, 20 runs, median); write the
  number into `port-notes.md`. The full-scale world comes with Plan 4's turn-budget work. No target here; Plan 4 owns the turn budget.
- [ ] **Step 2: Commit.**
