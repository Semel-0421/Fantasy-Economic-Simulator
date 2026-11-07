# Plan 4: Agents and Finance Systems — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every remaining C# system and the player's commands run in Rust, so the kept golden runs
(`Default_Seed1_ThirtyYears`, `Default_Seed2`, `Default_Seed3`, `NoBills`) match C# turn by turn to the
end and give the `GoldenRunTests` hashes; the full-scale world's turn time is measured.

**Architecture:** As Plan 3. The 30 remaining systems are ported in pipeline groups, each proved on the 8
step-fixture turns with the step-replay harness (saved sections plus the service checks). Then a
full-run harness plays whole runs from turn 0 and compares every turn's world hash and section hashes
with `turns.json`, and the final save's SHA-256 with `GoldenRunTests`. Player commands are ported with
the C# unit tests that issue them, since the golden runs issue none.

**Tech Stack:** Rust 1.99.1 workspace (`fes-core`, `fes-data`, `fes-save`, `fes-sim`, `fes-parity`);
`sha2` (already a dependency); Unity EditMode test for the full-scale data export.

**Spec:** `docs/superpowers/specs/2026-11-04-rust-web-port-design.md` (sections 6, 7.2, 7.3). Plans 1-3,
`Docs/superpowers/plan3-audit.md`.

## Global Constraints

Plans 1-3 constraints hold (integers and `Fx` only; half-away rounding; UTF-16 ordering; .NET layout;
DEC-108 limits; fmt and `clippy --all-targets --all-features -D warnings`; rules, numbers and order as C#
`4bf3804`, bugs into `Docs/superpowers/port-notes.md`; English; systems keep C# id, phase, order; draws
only through `ctx.random_for` with C# stream names; Burst jobs as plain loops in C# element order; no
threads; split borrows (`WorldState::get2_mut`, tail-only copies) instead of whole-column clones). In
addition:

- Never stage `Packages/` or `ProjectSettings/` (Unity update files); stage by path, never `git add -A`.
- Systems in scope (C# id → file, under `Assets/_Game/Agents/` unless noted): travel (TravelCommands.cs),
  merchants (AgentTables.cs, ArbitrageMerchantBehavior.cs, MerchantJob.cs, MerchantFlowJob.cs), banks
  (BankSystem.cs), roads (Roads.cs), state_spending (Purses.cs), labor (LaborMarket.cs), defense
  (DefenseSystem.cs), ventures (Ventures.cs), asset_markets (AssetMarkets.cs), finance (FinanceState.cs),
  shorts (Shorts.cs), income_tax (IncomeTax.cs), nobles (Nobles.cs), migration (Migration.cs),
  state_projects (StateProjectSystem.cs), subsidies (SubsidySystem.cs), investment
  (FacilityInvestment.cs), merchant_vehicles (MerchantVehicles.cs), orders and demand_forecast
  (MadeToOrder.cs), merchant_membership (MerchantMembership.cs), contracts and strategic_subsidy
  (Contracts.cs), credit (Credit.cs), trader_loans (TraderLoans.cs), policy (PolicyState.cs),
  merchant_generation (MerchantTraits.cs), guidance (Guidance.cs), groups (Groups.cs), licenses
  (Licenses.cs). All `agents.*`.
- Player commands: TradeCommands.cs, TravelCommands.cs and every other `ICommand` under `Assets/_Game`.
- Turn budget gate (spec 7.3) is measured in Plan 5 in the browser; this plan records native release
  times on the full-scale world as the first indication.

## Review Focus

- A system whose handler changes a service during dispatch — the harness's service checks run after
  dispatch; Task 1 lists every such handler and its service.
- A rare branch no sampled turn reaches (bankruptcy, default, war, auction, short squeeze) — the full-run
  harness compares every turn of 30 years, and the C# unit tests for each system are ported.
- Order of merchant decisions inside a turn (C# job results keyed by entity; application order) — any
  difference shows as a section mismatch on the same turn in the full run.
- A command validated on stale state (player buys after a price change in the same turn) — ported C#
  command tests.
- Save in the middle of a run, load, continue: the hash continues — Task 9 test at turns 52 and 520.

---

### Task 1: Audit of the remaining systems

**Files:** Create `Docs/superpowers/plan4-audit.md`.

- [ ] **Step 1:** For each in-scope system and command: saved sections read/written, services read/
  written, events published/subscribed (and what each handler changes), random streams, cadence, C#
  hidden fields across turns, Burst jobs and their element order, C# unit tests that cover it.
- [ ] **Step 2:** Group order for Tasks 3-8 (writers before readers of unsaved state) and the services the
  replay must check; **commit**.

### Task 2: Full-run harness

**Files:** `engine/fes-sim/src/full_run.rs` (feature `testing`), `engine/fes-parity/src/runs.rs`,
`engine/fes-sim/tests/full_run.rs`.

**Interfaces:**
- Produces: `fn play_run(db, map, run: &RunFixture, until: Option<i32>) -> Result<RunReport, SimError>`
  building the run (config overrides as `apply_overrides`), stepping turn by turn, comparing each turn's
  world hash and per-section FNV with `turns.json`; `RunReport::first_divergence() -> Option<(turn, key)>`;
  `fn final_sha256_32(sim: &Simulation) -> String` (SHA-256 of `save()`, first 32 hex, as `GoldenRunTests`).
- Produces: `fes-parity --fixtures <dir> --runs [--run <name>] [--until <turn>]` printing the first
  divergent turn and section per run.

- [ ] **Step 1: Failing tests** `turn0_matches_without_stepping`, `first_divergence_names_turn_and_section`
  (with a deliberately wrong fake system). **Step 2-4:** implement, pass, **commit**.

### Task 3: `agents.travel` and `agents.merchants`

**Files:** `engine/fes-sim/src/agents/{travel.rs, merchants/{mod.rs, behavior.rs, job.rs, flow_job.rs, ...}}`,
`engine/fes-sim/tests/systems_merchants.rs`.

- [ ] Replay equal on all 8 turns with these ported; port C# MerchantTests / travel tests; record native
  timing of merchants on turn 52. **Commit.**

### Task 4: Finance group A — banks, roads, state_spending, labor, defense, ventures, asset_markets, finance, shorts

**Files:** one module per system under `engine/fes-sim/src/agents/`; `tests/systems_finance_a.rs`.

- [ ] Replay equal on all 8 turns; C# unit tests ported. **Commit** (one per system is fine).

### Task 5: Finance group B — income_tax, nobles, migration, state_projects, subsidies, investment, merchant_vehicles

- [ ] As Task 4, `tests/systems_finance_b.rs`. **Commit.**

### Task 6: Finance group C — orders, demand_forecast, merchant_membership, contracts, strategic_subsidy, credit, trader_loans, policy, merchant_generation

- [ ] As Task 4, `tests/systems_finance_c.rs`. **Commit.**

### Task 7: guidance, groups, licenses

- [ ] As Task 4, `tests/systems_late.rs`. After this, `fes-parity --steps` reports every record of every
  turn computed in Rust (nothing applied from the record). **Commit.**

(Task 1 may reorder Tasks 3-7; follow the audit.)

### Task 8: Player commands and views

**Files:** `engine/fes-sim/src/commands/` (one file per command family), `engine/fes-sim/src/views.rs`,
`engine/fes-sim/tests/player_commands.rs`.

**Interfaces:**
- Produces: one Rust command per C# `ICommand` with the same validation messages, and
  `enum PlayerCommand` (plain data, `Clone + Send`) with `fn into_command(self) -> Box<dyn Command>` for
  Plan 5's worker messages.
- Produces (spec 7.2): plain-copy views for purse, cargo, contracts, shares, licences and the statement
  data the results screen needs (`Clone + Send`), each from the system that owns the data.

- [ ] Port every C# test that issues commands (`TradeCommands`, travel, contracts, shares, loans...);
  pass. **Commit.**

### Task 9: Golden runs to the end

- [ ] **Step 1:** `cargo run --release -p fes-parity -- --fixtures fixtures --runs` — all 4 runs match every
  turn; `final_sha256_32` equals the `GoldenRunTests` hash for each.
- [ ] **Step 2:** Test `save_load_mid_run_continues` (save at 52 and 520, load, play 10 turns, hashes
  equal `turns.json`).
- [ ] **Step 3:** CI runs `--runs` in release for the 3 short runs and `--until 520` for the 30-year run
  (keep CI under 10 minutes; the full 30 years runs locally). **Commit.**

### Task 10: Full-scale timing

**Files:** `Assets/_Game/Tests/EditMode/ParityFixtureExporter.cs` (new explicit test
`WritesFullScaleData`), `engine/fixtures/fullscale/` (data only, LFS if > 5 MB),
`engine/fes-parity/tests/timing.rs`, `Docs/superpowers/port-notes.md`.

- [ ] **Step 1:** Export `TestWorlds.FullScale()` CSVs from C# (public members only).
- [ ] **Step 2:** Rust plays turns 0-53 of the full-scale world in release; record average, worst and the
  new-year turn, plus per-system medians, next to the C# web baseline (average 1.03 s, worst 1.64 s).
- [ ] **Step 3:** If the native worst turn is over 0.5 s, list the top three systems and a concrete
  speed-up for each in `port-notes.md` (no rule changes). **Commit.** Ask the user before pushing.
