---
doc: SPEC-RUST-PORT
title: Rust rebuild for the web, Chroma Awards entry
status: draft
updated: 2026-11-04
related: [REF-CHROMA-S2, DEC-110, DEC-108, ARCHITECTURE]
---

# Rust rebuild for the web, Chroma Awards entry

Marked **[User]** where the user decided, **[Claude]** where Claude made the call (the user may still
override it). Unmarked text describes the project as it is.

## 1. Intent

The game is a turn-based fantasy economic simulation built in Unity (C#, Burst). It is to be rebuilt with
the simulation in Rust, for two reasons **[User]**:

1. **Speed.** The target is one second a turn on a web build with every node and agent computed (DEC-110).
   The last web measurement missed it: Chrome, turns 0 to 53, average 1.03 s, worst 1.64 s, 19 of 54 turns
   over 1 s. Unity's web player runs Burst jobs on one thread.
2. **Cleaner code**, under the published limits the project already enforces (DEC-108,
   `Docs/GDD/05_Refactoring_Standards.md`).

The first release is an entry to the **Chroma Awards Season 2**, Game Division, **Puzzle & Strategy**
category **[User]**. The rules that bind it are in `Docs/GDD/references/CHROMA_AWARDS_S2.md`, summarised in
section 3. The entry must be the Rust version **[User]**.

Releases must also be easy to update on the web **[User]**, and the game ships later on Windows (Steam) and
Android (Play Store) **[User]**.

## 2. Goals, non-goals, anti-goals

Goals
- A web build on itch.io, playable in a desktop browser with no download, submitted by **2026-12-24**
  (two weeks before the 2027-01-07 deadline, so the entry is visible while the gallery is busy) **[User]**.
- Three scenarios of about 30 minutes each, a free mode, and the presentation in section 5 **[User]**.
- The Rust simulation reproduces the C# simulation's results bit for bit (section 6).
- One `git push` builds, tests and deploys a development build; a release is one tagged command **[User,
  structure confirmed]**.
- The code base is ready for Windows and Android without rewriting the UI **[Claude]**.

Non-goals for the contest
- Windows and Android builds, store pages, Steamworks. They come after 2026-12-24 from the same code
  **[User accepted Claude's proposal]**.
- Calling any AI service from the running game **[User]**: AI is used only to make assets (section 8).
- The personal website. It is built later; builds only need to be ready to drop into it.
- New economy rules beyond what the scenarios need.

Anti-goals (the work fails even if it runs)
- Reaching the turn budget by computing less: no LOD, aggregate shortcuts, fewer hops or fewer bids
  (DEC-110). Speed must come from execution, not from changed results.
- A judged build that differs from what was tested, or a judged link that changes after submission.
- Long tutorials. The category asks for rules understandable without them.
- Any API key, server call or user data collection in the game.

## 3. Contest constraints (from REF-CHROMA-S2)

Equal-weight judging, 1 to 10 each, and what in this design answers it:

| Criterion (25% each) | Answered by |
|---|---|
| Theme & Division Purpose (gameplay; Puzzle & Strategy: clever systems, clear rules, no long tutorials) | Scenarios that teach one system each through play (section 5) |
| Creativity & AI Innovation (original world, meaningful non-gimmicky AI) | The economy itself; AI-made art and audio with a recorded process (section 8) |
| Craft & Technical Polish (no glitches, clean visuals) | Pixel art UI, the growth presentation, parity tests and release gate |
| Sound & Emotional Impact | Music and sound cues tied to trades, contracts, raids and the results screen |

Hard requirements the build must meet:
- Public, embeddable web link; no download; plays as depicted.
- All in-game text in English (the user talks to Claude in Korean; the game itself ships English).
- At least one AI tool, disclosed. Sponsor tool awards need a tool tag and proof of significant use.
- A downloadable gameplay video and a thumbnail.
- Only original or licensed code, art, music. No third-party IP.
- **The submission is frozen once preliminary judging starts**, which is as soon as it is submitted.
- The game may run inside the gallery's iframe, where cross-origin isolation (and so wasm threads) is
  generally unavailable. **The turn budget must be met on one thread** **[Claude]**; threads are a bonus
  where available.

## 4. Approaches considered

How the game is built:

| Approach | Trade-off | Outcome |
|---|---|---|
| **A. Rust simulation + web UI (TypeScript, Canvas for the map), wrapped for desktop and mobile** | Fastest way to build data-heavy, polished screens (price tables, statements, charts); small wasm; the UI is not Rust, but all heavy computation is | **Chosen [User]** |
| B. Bevy: everything in Rust | One language and code base; weak tools for table-heavy UI, large web build, slow compiles, API churn | Rejected: UI time and polish |
| C. Macroquad + egui | Small and fast; egui looks like a developer tool, hurting polish | Rejected: polish |

How A ships off the web (decided now so the code does not paint itself into a corner):

| Option | Notes | Outcome |
|---|---|---|
| Tauri 2 | Native Rust core (faster than wasm, threads), small installers, can link Steamworks; Android support less mature than desktop | **Windows/Steam [Claude, user accepted]**; first try for Android |
| Capacitor | Mature Android wrapper; core runs as wasm | **Android fallback** if Tauri mobile blocks |
| PWA | Free with the web build: installable, offline, updates instantly | **Added from the start [Claude, user accepted]** |
| Electron | Large installers, two wrappers to maintain | Rejected |
| Dioxus | UI in Rust, contradicts A | Rejected |

Consequence: the simulation crate exposes one interface (section 7.2) used three ways: as wasm in a web
worker, natively inside Tauri, and natively in tests.

## 5. The game as judged

### 5.1 Main menu (top to bottom) [User]
1. Scenario 1, "First Venture" (trading basics)
2. Scenario 2, indirect investment (shares and bonds)
3. Scenario 3, licences and international trade (special goods)
4. Free mode, placed last so players follow the scenarios first

Scenario titles other than the order are placeholders.

### 5.2 Scenario rules shared by all three [User]
- About 30 minutes each.
- **Fixed seed, no randomness**: only scripted events happen, so every player sees the same world. This
  means more than a fixed seed (which `DetRandom` already makes reproducible): the economy's own random
  events and encounter rolls are **switched off** in scenarios, so the scripted ones are the only ones.
  The switch is a scenario-only rule (5.7).
- **[Claude]** Scenarios run on the full-scale shipped world (`config.csv` as shipped, 4,460 merchant
  groups) with a fixed seed per scenario, so judges see the real economy and the turn budget applies.
- Each ends with a **results screen in the form of a financial statement** (revenue, costs, net profit),
  with **net profit shown as the score**.
- From the results screen: go on to the next scenario, or return to the main menu. **A scenario does not
  continue into free mode.**
- **[Claude, accepted]** Scenarios 2 and 3 use the same growth presentation as scenario 1 at their payoff
  moments (a dividend arrives, a special good sells abroad).

### 5.2a How a turn passes [User]
Turns advance **only when the player presses "Next week"**, as in the Civilization series: the player
finishes every action for the week (buy, sell, travel orders, contracts, investments), then presses the
button and the NPCs move. Nothing advances on a clock, in scenarios or in free mode.

**[Claude]** Each trade's result is shown the moment the player makes it (the growth presentation in 5.6
plays then, not at the end of the turn). The command still applies in the turn's Commands phase, as in C#;
the preview is computed on the current state plus the commands already queued that week, in the same
order, so it equals what the turn applies. A test asserts preview and applied result are equal. While the
turn computes after "Next week", the UI shows the NPCs moving (the computation runs in the worker).

### 5.3 Scenario 1, trading basics [User]
1. A protected opening of about ten minutes: **the first goods the player buys sell for 1.5 times anywhere**.
   **[Claude, accepted]** 1.5 times the price paid; the protection ends with the first sale rather than a
   clock, since judges play at different speeds.
2. A **supply contract request**: deliver goods by a deadline. Completing it leaves the player with **three
   times their starting coins**. Supply contracts exist between NPCs (`Assets/_Game/Agents/Contracts.cs`);
   this extends them to a request the player accepts.
3. **Escort mercenaries or insurance** protect the trip; the player learns to weigh risk against cost.
   Both exist (`Docs/GDD/systems/SYS_ROUTE_HAZARDS.md`: guards cut the chance by 70%, insurance refunds 80%
   of cargo loss). **[Claude, accepted]** One scripted raid on the contract route shows the difference
   between going protected and not.

### 5.4 Scenario 2, indirect investment [User]
Profit from companies through shares and bonds. The rules exist: shares, the exchange and short selling;
national and company bonds (`SYS_BANKING`). The scripted events and targets are left to the builder
(section 11).

### 5.5 Scenario 3, licences and international trade [User]
Special goods need a licence to sell; trade between nations and continents needs its own licence (DEC-116,
`SYS_LICENSES`). Events and targets are left to the builder.

### 5.6 Growth presentation [User]
When a trade completes, the game **pauses briefly, the coins panel enlarges, and the number counts up live**,
so growth is visible. **[Claude, accepted]** A coin sound plays with it.

### 5.7 Scenario-only rules [Claude, accepted]
The 1.5 times sale, scripted raids, contract requests and other teaching rules exist **only in scenario
mode**. With them off, the simulation must produce the same golden hashes as the C# reference, so the
economy's own behaviour is never blurred for teaching.

## 6. Correctness: parity with the C# simulation

**The reference [User]** is the C# simulation at the commit where the work now under way in another
session (web build testing on `meta-analysis-fixes`) is finished. That commit is **`4bf3804`** (recorded 2026-11-04, DEC-136); the C# simulation code is frozen from it. Its rules, numbers and order of
operations are ported as they are; bugs found on the way are reported, not fixed in the port (as in
DEC-108).

- Same numbers: integers and `Fx` (Q47.16 in an `i64`), symmetric rounding, no floats in simulation code.
- Same randomness: `DetRandom.For(seed, turn, stream, entity, salt)` and `DetHash.Of` ported exactly.
- **Same saved state.** `GoldenRunTests` hashes the bytes `SaveSerializer` writes. The Rust port writes the
  same format byte for byte, so the golden hashes can be compared directly, and C# saves load in Rust.
- Same turn pipeline: phases, `(Order, Id)` ordering, event dispatch after each phase.
- **Parity scope [Claude]**: only the branches the shipped config uses are ported (`lod.mode,1`; rumors
  off; the shipped merchant settings). The golden runs that exist only to cover other branches
  (`Aggregate`, `Mixed`, `Rumors`, `PlainMerchants` in `GoldenRunTests`) are not ported; LOD aggregation is
  an anti-goal anyway. The gate is every remaining golden run, for its full length.
- If a system's parity is not reached by its phase end, the scenario that depends on it waits; a build is
  never released with a known mismatch **[Claude]**.
- A parity harness runs both implementations on the golden seeds and configurations and, on a mismatch,
  compares the state per turn and per component to find the first divergent system.
- Port order follows the module dependency order (`ModuleOrder`), so each system can be checked against
  C# as soon as it is ported, before the next is started.

C# rule changes during the port: once the reference commit is recorded, the C# code is frozen
**[Claude]**; new rules (the scenario rules) and later balance work are done only in Rust.

## 7. Architecture

### 7.1 Repository layout [Claude]
Same repository, beside the Unity project (Unity ignores folders outside `Assets`):

```
engine/            Cargo workspace
  fes-core/        simulation: no I/O, no threads required, no platform code
  fes-data/        CSV definitions, config, data hash
  fes-save/        save format (byte compatible with SaveSerializer)
  fes-wasm/        wasm-bindgen bindings for the web worker
  fes-parity/      parity runner against C# golden outputs
web/               TypeScript UI (Vite), Canvas map (PixiJS), PWA manifest and service worker
.github/workflows/ CI and deploy
```

`Docs/GDD/` stays the design record; this spec and the plan live under `docs/superpowers/`.

### 7.2 The simulation interface
The UI never touches simulation state directly, as today (`GameSimulation.View` / `Actions`):
- `step()` runs one turn; commands queue between turns.
- Views return plain copies (current market, purse, statement, contracts, shares), serialisable to the UI.
- Events the presentation reacts to (trade completed, contract done, raid, dividend) come back with each turn.

On the web the simulation runs in a **Web Worker**, so the UI keeps animating while a turn is computed.

### 7.3 Performance
- Budget: one second a turn, web, one thread, full computation (DEC-110 rules apply unchanged).
- Baseline: Unity web build in Chrome on the development desktop, full-scale world, shipped config
  (4,460 merchant groups), turns 0 to 53, after commit `c4470e2`: average 1.03 s, worst 1.64 s (new year),
  19 of 54 turns over 1 s; merchants 0.44-0.72 s, pricing 0.39-0.49 s, quarterly demand forecast about
  0.47 s. An earlier build met it (0.49 s average, 0.84 s worst), so `00_Architecture.md` is out of date.
  Recorded in `00_Architecture.md` (DEC-136).
- **Gate [Claude]**: the same world and turns, Chrome on the development desktop, inside an itch.io embed:
  worst turn under 1 s, and **average under 0.7 s** as a margin for judges' weaker machines. Profiling
  starts in phase 1 on ported pricing and merchants, not at the end.
- Multithreading (rayon over wasm threads) is used only where cross-origin isolation is available and must
  produce identical results; the single-thread path is the one the budget is measured on.
- The Rust build uses release optimisation, LTO and `wasm-opt`; profile before restructuring.

### 7.4 Code standards
The C# limits (complexity, length, nesting, parameters, file size, duplication, dead code) carry over as
their Rust equivalents, enforced in CI with `clippy` (cognitive complexity, too many lines, too many
arguments at the same values) and `rustfmt`. TypeScript gets ESLint with the same complexity limits.

## 8. Assets and AI use [User: AI only as a production tool]

| Use | Tool [Claude, accepted] | Why |
|---|---|---|
| Music and sound effects | ElevenLabs (Music, SFX) | Organiser's tool; eligible for the ElevenLabs Awards |
| Pixel art: map, towns, merchants, icons | SpriteCook | Sponsor tool award; matches the 2D pixel art style |
| English scenario text | Claude | English text is required |

Meshy (3D) does not fit a 2D pixel game and is not used.

An **asset log** records each asset, the tool, the prompt or process, and screenshots, for the tool-award
proof and the disclosure the rules require.

## 9. Delivery

### 9.1 Pipeline [User confirmed structure]
- **Every push**: Rust tests, parity run, clippy, wasm build, web build, deploy to a development page.
- **A release**: tagged version; the same artefact is pushed to itch.io with `butler`, with a version
  number. The release is blocked unless parity and the turn budget pass.
- **Judged build**: the itch.io page that is submitted receives only verified releases and is **not
  updated after submission**. Development continues on a separate page.
- **Personal website (later)**: the same artefact deployed under versioned paths (`/play/v1.2/`), so a bad
  release rolls back by pointing at the previous path.

### 9.2 Saves
Saves are the byte-compatible format of section 6, stored in the browser (IndexedDB) on the web. Free mode
saves; scenarios start fresh each time **[Claude]**. Only the current C# `FormatVersion` must load; the
`ISaveMigration` chain for older versions is not ported, since no player holds C# saves **[Claude]**.

## 10. Schedule (about seven weeks to 2026-12-24) and cut order

Rough phases [Claude], to be refined in the plan:
1. Core types, data loading, save format, parity harness; world, markets and pricing ported.
2. Remaining systems ported in dependency order until every golden run matches.
3. Web worker, UI shell, map, trading screens; scenario 1 end to end.
4. Scenarios 2 and 3, results screen, audio and art pass.
5. Performance on the web, polish, video and thumbnail, submission.

If time runs short, cut in this order **[User accepted Claude's proposal]**:
1. Windows and Android (already after the contest).
2. Free mode's depth (judges score the scenarios).
3. The three scenarios and the growth presentation are kept to the end.

## 11. Left to the builder
- The scripted events, targets and numbers of scenarios 2 and 3, within section 5.
- Exact UI layout and art direction within 2D pixel art.
- Crate internals, Rust data layout and how parallelism is split, as long as results match C#.
- Choice of UI framework within TypeScript (plain, or a small library), and of the development host.
