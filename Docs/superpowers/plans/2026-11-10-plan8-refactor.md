# Plan 8: Pre-release Refactoring — Implementation Plan

> **For agents:** Roles and rules: `AGENTS.md`, `Docs/superpowers/agent-rules.md`. Refactoring standards: `Docs/GDD/05_Refactoring_Standards.md` (limits and procedure, DEC-108).

**Goal:** Before the judged release, bring the web code under the same published limits the Rust engine already enforces, remove dead code and duplication in both, and leave behaviour unchanged.

**Starting point (2026-11-10, `1026997`):** Rust: clippy denies cognitive complexity > 15, long functions and nesting > 4; file length ≤ 1,000 checked by script; 8 `allow(clippy::…)` exemptions. Web: 164 source files, no complexity, length, duplication or dead-code limits; largest hand-written files `main.ts` 366, `map/map.ts` 312, `ui/finance.ts` 274.

## Global Constraints
- **A refactoring changes structure only.** No rule, number, text, layout or bug fix. A bug found on the way is written in the report, not fixed here.
- Behaviour proof after every task: parity exactly as `.github/workflows/engine.yml` (no new hashes recorded), scenario goldens (1,359 / 3,657 / 2,263), all web tests incl. Edge UI tests, and after engine tasks the Edge bench (worst < 1 s, average < 0.7 s; within noise of before).
- Small batches, one commit per task, commit by path. No push without Copilot's gate and the user's yes.
- An exemption needs a one-line reason next to it and stays rare.

---

### Task 1: Web limits enforced by lint (Codex, gpt-6.1-sol, high)
- Add to `web/eslint.config.*` as errors, with the limits of `05_Refactoring_Standards.md`: cognitive complexity 15 (`eslint-plugin-sonarjs`), cyclomatic `complexity` 25, `max-lines-per-function` 80 (skip blank lines and comments), `max-depth` 3, `max-params` 7, `max-lines` 1,000. Generated or data files (`web/src/i18n/*` tables, `web/src/sim/pkg`) are excluded by path with a reason.
- List every violation in the report first, then fix by splitting functions and modules (expected: `main.ts`, `map/map.ts`, `ui/finance.ts`, others the lint finds). Test files may use a higher function-length limit if `describe` blocks trip it; say so in the config.
- Done: `npm run lint` clean with the rules on; web tests and build pass. **Commit.**

### Task 2: Web dead code and duplication (Codex, gpt-6.1-sol, medium)
- Dead code: run `knip` (dev dependency or `npx`), remove unused files, exports and dependencies; keep the test-only dev handle and say why.
- Duplication: run `jscpd` over `web/src` at Sonar's threshold (100 tokens over 10 lines); remove real duplicates by extraction; language tables are excluded.
- Add both as npm scripts (`npm run deadcode`, `npm run dup`) and to the web job in CI if they run under a minute. **Commit.**

### Task 3: Engine exemptions, dead code, duplication (Codex, gpt-6.1-sol, high)
- Review the 8 `allow(clippy::…)`: remove each by restructuring (for example a parameter struct for `too_many_arguments`) or keep it with a one-line reason.
- Unused dependencies (`cargo machete` or `cargo udeps`), unused `pub` items across crates, leftover test-only helpers in `src`.
- Duplication: `jscpd` over `engine/*/src` at the same threshold; extract only where it does not touch a hot path's shape; report the rest.
- Done: full engine gate + parity + bench. **Commit.**

### Task 4: Standards document and CI (Codex, gpt-6.1-sol, low)
- Update `Docs/GDD/05_Refactoring_Standards.md`: add the Rust and web columns (which tool enforces which limit, the commands), keep the C# column as the frozen reference.
- Make sure CI runs every limit (engine job: clippy + file length; web job: lint, dead code, duplication). **Commit.**

### Task 5: Review and gate (Claude review, Copilot gate)
- Claude reads the four reports against this plan: no behaviour change, exemptions justified, bugs found listed for the user.
- Copilot full pre-push gate; ask the user before pushing.

## Review Focus
- A "refactoring" that changes a result: any parity difference, golden profit change, or UI test expectation edit is a failure of the task, not something to update.
- Splitting a hot-path function so the bench slows beyond noise.
- Lint rules switched off or thresholds raised to make the lint pass.
- Dead-code removal that deletes something only tests or the worker use dynamically.
