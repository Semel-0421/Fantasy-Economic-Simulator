# Plan 6: Scenarios and Presentation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The three teaching scenarios of spec 5 are playable start to finish in the browser — scripted,
random-free, about 30 minutes each, ending on a financial-statement results screen with net profit as the
score — with the growth presentation at every completed trade and the NPCs visibly moving while a turn
computes. Free mode stays the plain C#-equal simulation.

**Architecture:** A `Scenario` layer in `fes-sim` holds the scenario-only rules (spec 5.7) behind one switch
that is off in free mode, so all golden runs and step fixtures stay equal. A scenario is a data file
(`data/scenarios/<id>.ron` or CSV, builder's choice, documented) naming seed, start, objectives and a list of
scripted events (trigger → effect). Effects use existing rules where they exist (contracts, raids, prices,
company results) and the few new teaching rules otherwise. The web app adds a scenario HUD (objective,
deadline, event dialogs), the results statement, the growth presentation and a map animation of merchant
moves.

**Tech Stack:** as Plan 5 (Rust 1.99.1, wasm-bindgen, Vite + TS, PixiJS 8, Playwright with Edge locally).

**Spec:** `docs/superpowers/specs/2026-11-04-rust-web-port-design.md` (section 5, 7.2). Plans 1-5.

## Global Constraints

- Plans 1-5 constraints hold. With the scenario switch off, `fes-parity --runs` (release), `--steps` and the
  full-scale hash stay equal — checked after every engine change.
- Scenario-only rules (5.7) never run in free mode: 1.5× first sale, scripted raids and events, contract
  requests, random events and encounter rolls switched off.
- Scenarios run on the full-scale shipped world (`engine/fixtures/fullscale/` data shipped as the game's
  data) with a fixed seed each; the turn budget applies (worst < 1 s in the browser bench, re-run after
  engine changes).
- Turns advance only on **Next week**. Each trade's result shows the moment it is made: preview = applied
  result, computed on current state plus the commands already queued that week in order (spec 5.2a); a test
  asserts equality for every command family.
- Scenarios never save (spec 9.2); results screen offers **Next scenario** and **Main menu** only; a scenario
  never continues into free mode.
- Player-facing text in English; scenario text lives in the data files, not in code.
- About 30 minutes each: target 18-24 weeks per scenario at 1-1.5 min of play per week; an automated
  "golden path" playthrough per scenario reaches the objective in the browser test.

## Review Focus

- Free mode or a golden run touched by a scenario rule — the switch-off parity checks.
- A player who ignores the objective (never accepts the contract, never buys shares) still reaches the
  results screen at the deadline with an honest statement (maybe a loss).
- A scripted event whose trigger never fires (the player went elsewhere) — every trigger has a deadline
  fallback; tests cover both paths.
- Preview differs from applied result when two orders in one week touch the same stock — equality test.
- Growth presentation spamming on many small trades — batched within a short window; skippable; respects
  reduced-motion.

---

### Task 1: Scenario switch, random-off, and the results statement

**Files:** `engine/fes-sim/src/scenario/{mod.rs, rules.rs, statement.rs}`, `engine/fes-sim/tests/scenario_core.rs`.

**Interfaces:**
- Produces: `ScenarioRules` service (`active: bool`, flags for each teaching rule); `SimulationBuilder::scenario(def)`;
  random world events and route-encounter rolls skipped when `active` (one guard each, at the C# roll sites);
  `StatementView { revenue, costs (by kind), net_profit, start_coins, end_coins, weeks }` from the existing
  ledgers.
- [ ] Tests: free-mode builds still match `turn0.json` and `--runs`; with `active` and no script, 30 turns
  show no random event and no encounter; statement sums tie to coin change. **Commit.**

### Task 2: Scripted events and objectives

**Files:** `engine/fes-sim/src/scenario/{script.rs, objectives.rs, defs.rs}`, `data/scenarios/README.md`, tests.

**Interfaces:**
- Produces: `ScenarioDef { id, title, seed, start_node, start_coins, weeks, objectives, events }` loaded from
  the data file; `Trigger::{Week(n), PlayerAt(node), Holds(good, qty), ContractDone(id), Never→fallback week}`;
  `Effect::{Dialog(text_key), OfferContract(..), Raid(route, ..), PriceShock(node, good, pct), CompanyNews(..),
  Dividend(..), BondIssue(..), LicenceOffer(..)}` mapped onto existing systems; `ObjectiveView`; events
  `ScenarioDialog`, `ObjectiveDone`, `ScenarioEnded` in `SimEventRecord`.
- [ ] Tests per trigger and effect, fallback paths, deterministic replay of a script. **Commit.**

### Task 3: Preview equals applied

**Files:** `engine/fes-sim/src/simulation.rs` (`preview` on state + queued commands), `tests/preview_equals_applied.rs`.
- [ ] For every command family, preview after N queued orders equals the applied result in the next
  Commands phase (coins, cargo, stock). **Commit.**

### Task 4: Scenario 1 "First Venture" (trading basics)

**Files:** `data/scenarios/s1_first_venture.*`, rules in `scenario/rules.rs`, `tests/scenario_s1.rs`.
- Content (spec 5.3): start in a village with modest coins; protected opening — the first goods bought sell
  for 1.5× the price paid anywhere, ending at the first sale; a supply-contract request (deliver N of a good
  to a town by week D; completing it leaves the player with 3× starting coins); escort and insurance offered
  before the contract trip; one scripted raid on the contract route showing protected vs unprotected
  outcomes; 20 weeks.
- [ ] Golden-path test reaches 3× coins; ignore-everything path ends with an honest statement; raid with and
  without protection. **Commit.**

### Task 5: Scenario 2 (shares and bonds)

**Files:** `data/scenarios/s2_*.*`, `tests/scenario_s2.rs`.
- Proposed content (builder may refine; spec 5.4 leaves it open): start in a city with an exchange and coins,
  no cargo. Week 2 a trading company lists shares; week 6 a harvest boom lifts a grain company — its
  dividend arrives (growth presentation); week 10 a nation issues bonds at a high coupon; week 14 news of a
  company's losses (a chance to sell, or to short with the exchange licence); week 18 bond coupon paid;
  ends week 22. Objective: net profit above a target reachable without shorting; shorting is the stretch.
- [ ] Golden-path and passive-path tests; dividend and coupon events fire the presentation. **Commit.**

### Task 6: Scenario 3 (licences and international trade)

**Files:** `data/scenarios/s3_*.*`, `tests/scenario_s3.rs`.
- Proposed content (spec 5.5): start at a port with a fortune and a ship; a foreign continent's shortage
  raises demand for a continental special good; the player must buy the special-goods licence and the
  international trade licence, insure the voyage, sell abroad (growth presentation), and return before the
  deadline (24 weeks). Objective: net profit after licence costs.
- [ ] Golden-path and no-licence paths (sale refused with the rule's message). **Commit.**

### Task 7: Web — scenario HUD, dialogs, results screen

**Files:** `web/src/ui/{scenario.ts, dialog.ts, results.ts}`, menu wiring, tests.
- [ ] Menu enables scenarios 1-3; objective panel with deadline; event dialogs (pause the input until
  dismissed); results statement (revenue, costs by kind, net profit as the score, start/end coins) with
  **Next scenario** / **Main menu**; scenarios never write saves. Playwright golden path per scenario.
  **Commit.**

### Task 8: Growth presentation and NPC movement

**Files:** `web/src/ui/growth.ts`, `web/src/map/caravans.ts`, `web/src/audio/sfx.ts` (placeholder coin
sound; final audio in Plan 7), `engine/fes-sim/src/views.rs` (merchant positions view if missing), tests.
- [ ] On each completed trade (and dividends, coupons, special-good sales): brief pause of input, coins panel
  enlarges, number counts up, coin sound; batched, skippable, reduced-motion respected. While a turn
  computes, caravans animate from last week's positions toward their destinations, then settle on the new
  view. **Commit.**

### Task 9: Playtest pass and budget

- [ ] Run each scenario's golden path in the browser on the full-scale world; record weeks, wall-clock
  minutes at a human pace estimate, net profit, and worst turn time (bench gate still met). Tune scenario
  numbers (not economy rules) until each lands at about 30 minutes and is winnable with the taught tool.
  Record in `Docs/superpowers/scenario-notes.md`. **Commit.** Ask the user before pushing.
