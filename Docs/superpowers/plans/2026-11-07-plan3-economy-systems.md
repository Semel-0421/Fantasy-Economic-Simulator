# Plan 3: Economy Systems — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The environment, production, pricing, household and currency systems run in Rust and change
exactly the bytes C# changes, system by system, on every sampled turn of the step fixtures.

**Architecture:** A step-replay harness loads the save recorded before a sampled turn and walks C#'s
pipeline order. A ported system runs in Rust and its changed sections are compared with the record; an
unported system is replayed by applying its recorded section changes. After each phase's dispatch the
world is resynchronised to the record, so one wrong system cannot hide behind another. State that is not
saved (services such as `LodState`, `MarketFlows`) cannot be replayed, so an audit first lists which
ported system reads which unsaved state and who writes it; writers must be ported in this plan or the
reader is moved to Plan 4.

**Tech Stack:** Rust 1.99.1 workspace (`fes-core`, `fes-data`, `fes-save`, `fes-sim`, `fes-parity`).

**Spec:** `docs/superpowers/specs/2026-11-04-rust-web-port-design.md` (sections 6, 7.2, 7.3). Plans 1-2.

## Global Constraints

Plans 1-2 constraints hold (integers and `Fx` only, `float_arithmetic` denied; half-away rounding; UTF-16
ordering; .NET layout; DEC-108 limits; fmt and `clippy --all-targets --all-features -D warnings`; rules,
numbers and order of operations as in C# `4bf3804`, bugs into `docs/superpowers/port-notes.md`; English).
In addition:

- Systems implement `fes_sim::System` with C#'s `id`, `phase`, `order` (from `build/systems.json`).
- Event handlers are `FnMut(&E, &mut SimContext) -> Result<(), SimError>`; commands `validate` / `apply`.
- C# Burst jobs are ported as plain sequential Rust giving the same results (results are keyed by
  (seed, turn, stream, entity), not thread order). No threads in this plan.
- Random draws only through `ctx.random_for(stream, entity, salt)` with C#'s stream names.
- Systems in scope (C# id → file): `agents.lod` (Agents/Lod.cs), `economy.world_events`
  (Economy/MarketModifiers.cs), `economy.modifier_fold` (Economy/MarketFlows.cs), `agents.production`
  (Agents/ProductionSystem.cs), `economy.pricing` (Economy/MarketSystems.cs, Economy/Households.cs),
  `agents.household_spending` and `agents.home_surplus` (Agents/Purses.cs), `economy.currency`
  (Economy/Currency.cs), `agents.households` (Agents/HouseholdGrowth.cs).
- Sampled turns: 0, 1, 3, 51, 52, 53, 103, 104 of `Default_Seed1_ThirtyYears` (new-year and
  quarterly cadences fall on 51-53 and 103-104).

## Review Focus

- A system that is due only on some turns (tier cadence, quarter, new year) — every system is checked on
  all 8 sampled turns, including the ones where it does nothing (no changed sections expected).
- A system that reads unsaved state left by an unported system — Task 1's audit; the harness fails loudly
  if a ported system's service input was never produced this turn.
- An `Fx` division or `MulInt` near overflow in pricing — compared byte for byte; any mismatch names the
  section and first differing offset.
- A handler a ported system subscribes that fires during dispatch — the dispatch step compares the
  sections ported handlers touch before resynchronising.
- Load mid-history: a save from turn 52 loads and the next ported system matches — the harness always
  starts from a recorded save, so this is exercised every test.

---

### Task 1: Unsaved-state audit

**Files:**
- Create: `Docs/superpowers/plan3-audit.md`

- [ ] **Step 1: For each system in scope** read its C# `Initialize`/`Execute`/handlers and list: saved
  sections read and written; services (unsaved) read and written; events published and subscribed; random
  streams; cadence (when it is due).
- [ ] **Step 2: For each unsaved input**, name its writer. If the writer is outside this plan's scope and
  runs earlier in the same turn (e.g. `agents.travel`, `agents.merchants`), state whether the value can be
  rebuilt from saved state on load (C# `ILoadListener.OnLoaded` / `Initialize`) — then the harness
  rebuilds it — or not, in which case move the reader to Plan 4 and say so.
- [ ] **Step 3: Write the in-scope list** that Tasks 3-7 follow (may differ from Global Constraints after
  the audit) and **commit**.

---

### Task 2: Step-replay harness

**Files:**
- Create: `engine/fes-sim/src/replay.rs` (feature `testing`), `engine/fes-sim/tests/replay.rs`
- Modify: `engine/fes-parity/src/{lib.rs, main.rs}` (`--steps` mode)

**Interfaces:**
- Consumes: `Simulation::load`, `TurnPipeline`, `SimContext::dispatch_events`, `fes_save`.
- Produces: `StepFile::read(path) -> Result<StepFile, SimError>` (format in `engine/fixtures/README.md`:
  `FESSTEP1`, turn, save, records of label / world hash / changed sections).
- Produces: `fn replay_turn(step: &StepFile, db: &GameDatabase, map: &MapDef, ported: &[&str]) -> Result<ReplayReport, SimError>`:
  load the save; for each record in order — `system:<id>` ported: run the Rust system, diff every section
  against the recorded state after the step, report `Different { label, key, offset }` or `Equal`;
  unported: apply the recorded changed sections; `dispatch:<phase>`: run Rust dispatch, diff sections, then
  overwrite the world with the record; `clock`: advance and compare the world hash.
- Produces: `ReplayReport { steps: Vec<StepResult> }`, `ReplayReport::first_difference()`.
- Produces: `fes-parity --fixtures <dir> --steps [--system <id>]` printing per turn per ported system
  equal / different (section, offset); exit 1 on any difference.

- [ ] **Step 1: Failing tests:** `step_files_parse` (all 8, record counts and hashes);
  `replay_with_nothing_ported_reaches_every_recorded_hash` (proves apply/resync logic);
  `a_wrong_fake_system_is_reported_with_offset`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS, gates. **Commit.**

---

### Task 3: Environment — `agents.lod`, `economy.world_events`, `economy.modifier_fold`

**Files:**
- Create: `engine/fes-sim/src/agents/lod_system.rs`, `engine/fes-sim/src/economy/{world_events.rs, modifier_fold.rs}`
- Create: `engine/fes-sim/tests/systems_environment.rs`

**Interfaces:**
- Consumes: Task 2; `LodState`, `MarketModifierTable`, `MarketFlows`, events from `GameDatabase`.
- Produces: `LodSystem`, `WorldEventSystem`, `ModifierFoldSystem` registered by their modules at C#'s
  positions; `DefaultLodPolicy` as the `ILodPolicy` service.

- [ ] **Step 1: Failing test** `environment_systems_match_on_every_sampled_turn` (replay with these 3 ported;
  every step equal on all 8 turns), plus unit tests ported from the C# tests that cover them
  (`Tests/EditMode`, grep the class names).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS. **Commit.**

---

### Task 4: `agents.production`

**Files:**
- Create: `engine/fes-sim/src/agents/production/{mod.rs, ...}` (split by responsibility under 1,000 lines)
- Create: `engine/fes-sim/tests/systems_production.rs`

- [ ] **Step 1: Failing test** `production_matches_on_every_sampled_turn` (ported: Task 3 systems +
  production), plus C# unit tests for production.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS. **Commit.**

---

### Task 5: `economy.pricing`

**Files:**
- Create: `engine/fes-sim/src/economy/pricing/{mod.rs, consumption.rs, reprice.rs}`
- Create: `engine/fes-sim/tests/systems_pricing.rs`

**Interfaces:**
- Produces: `PricingSystem` (household consumption per tier schedule, then repricing of due levels, as
  `Economy/MarketSystems.cs` + `Economy/Households.cs`); the Burst job path ported as a plain loop in the
  same element order.
- Produces (spec 7.2 view): `fn market_view(sim: &Simulation, node: NodeId) -> MarketView` — a plain copy
  (`Vec<GoodQuote { good: GoodId, price: Fx, stock: i64 }>`), `Clone + Send`.

- [ ] **Step 1: Failing tests** `pricing_matches_on_every_sampled_turn`; `market_view_copies_prices`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS. **Commit.**

---

### Task 6: `agents.household_spending`, `agents.home_surplus`

**Files:**
- Create: `engine/fes-sim/src/agents/purses_systems.rs`; `engine/fes-sim/tests/systems_purses.rs`

- [ ] **Step 1: Failing test** `purse_systems_match_on_every_sampled_turn`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS. **Commit.**

---

### Task 7: `economy.currency`, `agents.households`

**Files:**
- Create: `engine/fes-sim/src/economy/currency_system.rs`, `engine/fes-sim/src/agents/household_growth.rs`;
  `engine/fes-sim/tests/systems_finance_econ.rs`

- [ ] **Step 1: Failing test** `currency_and_household_growth_match_on_every_sampled_turn`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement** (the `ICurrencyModel` seam as a trait object service).
  **Step 4: Run** — PASS. **Commit.**

---

### Task 8: Plan-wide check and timing

**Files:**
- Modify: `engine/fes-parity/src/lib.rs` (`ported_systems()` lists every Plan 3 system),
  `.github/workflows/engine.yml` (run `--steps`), `Docs/superpowers/port-notes.md`

- [ ] **Step 1:** `cargo run -p fes-parity -- --fixtures fixtures --steps` — every ported system equal on
  all 8 turns; turn-0 build still 172 equal.
- [ ] **Step 2: Time** each ported system on the sample world (release, median of 20 replays of turn 52)
  and record in `port-notes.md` next to the C# per-system figures from `00_Architecture.md` where given.
- [ ] **Step 3: Commit.** Ask the user before pushing.
