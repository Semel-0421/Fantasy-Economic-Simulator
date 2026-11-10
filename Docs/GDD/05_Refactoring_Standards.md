---
doc: REFACTORING
title: Refactoring standards
status: draft
updated: 2026-11-10
related: [DEC-108, 01_Extension_Guide]
---

# Refactoring standards

The limits below are the published defaults of the rules they name, not numbers picked for this project. They are enforced
in all three code bases: the C# reference by tests (`CodeStandardsTests`, `GoldenRunTests`, DEC-108), the Rust engine and the
web client by lint and scripts that CI runs (see "Enforcement by code base"), so code that breaks them fails the build.

## What counts as a refactoring
A change to the internal structure of the code that leaves its observable behaviour unchanged, made as a series of small
steps, with the system working after each one ([refactoring.com](https://refactoring.com/)). Here, observable behaviour is
what a run saves: a refactoring does not change a single byte of `GoldenRunTests`' saved states.

## Procedure
1. **Record the starting point.** All EditMode tests pass. Before touching a hot path, run
   `FullScale_ShippedRules_StayWithinTurnBudget` (explicit) and keep its turn time.
2. **Change the structure only, in small batches.** No rule, number or bug fix goes in with it, not even a bug found on the way:
   note it and fix it in its own change.
3. **After each batch**, recompile and run `GoldenRunTests`. A changed hash means the batch changed behaviour: find out why and
   undo it. Do not record a new hash.
4. **Before committing**, run all EditMode tests, and the scale test again if a hot path moved: the turn time stays within noise.
5. **Keep it apart.** A commit that records new golden hashes is a behaviour change with its own decision, not a refactoring.

## Limits
| What | Limit | Rule and source |
|---|---|---|
| Cognitive complexity of a method | 15 | Sonar S3776 ([default in sonar-dotnet](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.Core/Rules/CognitiveComplexityBase.cs)) |
| Cyclomatic complexity of a method | 25 | Microsoft [CA1502](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1502) |
| Lines of code in a method | 80 | Sonar S138 ([default](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.Core/Rules/MethodsShouldNotHaveTooManyLinesBase.cs)) |
| Nesting of control flow | 3 | Sonar S134 ([default](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.Core/Rules/FunctionNestingDepthBase.cs)) |
| Parameters of a method | 7 | Sonar S107 ([default](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.Core/Rules/TooManyParametersBase.cs)) |
| Lines in a file | 1,000 | Sonar S104 ([default](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.Core/Rules/FileLinesBase.cs)) |
| Duplicated block | none of 100 tokens over 10 lines | [Sonar's definition](https://docs.sonarsource.com/sonarqube-server/latest/user-guide/code-metrics/metrics-definition/) |
| Private method nothing calls | none | Sonar [S1144](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/rspec/cs/S1144.json) |

## Enforcement by code base
The C# code in `Assets/` is the frozen reference (its limits are tested as above); new work happens in `engine/` (Rust) and
`web/` (TypeScript). CI runs everything below: the `engine` workflow for Rust, the shared `web-build` action for the web client.

| Limit | C# (frozen reference) | Rust (`engine/`) | Web (`web/`) |
|---|---|---|---|
| Cognitive complexity 15 | `CodeStandardsTests` | clippy `cognitive_complexity` = deny, `clippy.toml` threshold 15 | `sonarjs/cognitive-complexity` 15 |
| Cyclomatic complexity 25 | `CodeStandardsTests` | not checked on its own (clippy has no such lint; cognitive complexity is the tighter limit) | `complexity` 25 |
| Lines in a function 80 | `CodeStandardsTests` | clippy `too_many_lines` = deny, threshold 80 | `max-lines-per-function` 80 (blank lines and comments skipped; test files 160 because `describe` blocks are long) |
| Nesting | 3 | clippy `excessive_nesting` = deny, threshold 4 (clippy counts nesting differently from Sonar, and 4 is its default) | `max-depth` 3 |
| Parameters 7 | `CodeStandardsTests` | clippy `too_many_arguments`, threshold 7, denied by `-D warnings` | `max-params` 7 |
| Lines in a file 1,000 | `CodeStandardsTests` | `bash engine/scripts/check_file_length.sh` | `max-lines` 1,000 (blank lines and comments skipped) |
| Duplicated block (100 tokens, 10 lines) | `CodeStandardsTests` | `npm run dup` in `web/` (jscpd; also reads the six engine crates) | `npm run dup` in `web/` (jscpd over `src` and `scripts`; language tables, generated bindings and assets excluded by path) |
| Unused code | unused private methods: `CodeStandardsTests` | rustc `dead_code` and `unused` warnings, denied by `-D warnings`; unused `pub` items across crates by hand-review (no tool in CI) | `npm run deadcode` (knip: unused files, exports, types and dependencies) |

Commands (all must exit 0):
- Rust, in `engine/`: `cargo fmt --check`; `cargo clippy --all-targets --all-features -- -D warnings`; `cargo test --all-features`; `cargo check -p fes-sim --target wasm32-unknown-unknown`; `bash scripts/check_file_length.sh`.
- Web, in `web/`: `npm run lint`; `npm run typecheck`; `npm run deadcode`; `npm run dup`; `npm test`; `npm run build`.
- Behaviour proof for any refactoring: the parity commands of `.github/workflows/engine.yml` (no new hashes), the scenario goldens, and, after an engine change, the turn bench (`web/bench`: `npm run wasm && node run-bench.mjs`).

Exemptions follow the same rule in every code base: an entry or comment next to the code with a one-line reason, and rarely.
Rust: a `#[allow(clippy::...)]` or `#[allow(dead_code)]` with its reason on the line above or beside it (now: `derivable_impls` on
`WorldLevel`, the fixed-point `suspicious_arithmetic_impl` on `Fx` multiplication and division, `dead_code` on the macro-made
`remove_row_swap_back` and on shared test-helper modules). Web: none; no `eslint-disable` comment may be added to pass a limit,
and `knip.json` lists only entry points that tools reach dynamically (worker, service-worker template, samples page) and
binaries called by path (`binaryen`, `wasm-bindgen`).

Cognitive complexity counts as sonar-dotnet's
[C# metric](https://raw.githubusercontent.com/SonarSource/sonar-dotnet/master/analyzers/src/SonarAnalyzer.CSharp/Metrics/CSharpCognitiveComplexityMetric.cs)
does. Microsoft's page on [cyclomatic complexity](https://learn.microsoft.com/en-us/visualstudio/code-quality/code-metrics-cyclomatic-complexity)
quotes NIST on 10 as a starting point; this project takes CA1502's 25 because cognitive complexity is the tighter limit in practice.

The limits apply to production code (C#: `Core`, `Data`, `World`, `Economy`, `Agents`, `Persistence`, `Simulation`, `Unity`; Rust: every crate's `src`; web: `src` and `scripts`), not to tests, except that the Rust lints also run over test targets (`--all-targets`) and the web lint also runs over tests with the 160-line function limit.

## Exemptions
A method may break a limit only with an entry and a reason in `CodeStandardsTests.Exempt`. Microsoft names a large `switch` as
the case to exempt, and Fowler warns that [a smell is only a sign to look deeper](https://martinfowler.com/bliki/CodeSmell.html),
so an exemption is allowed when splitting would make the code worse. An entry no longer needed fails the test, so the list can
only shrink. There are none now.

## Smells to look for by hand
A test cannot see these, so a review looks for them ([catalogue](https://refactoring.guru/refactoring/smells)): primitive obsession,
data clumps, divergent change, shotgun surgery, feature envy, inappropriate intimacy, message chains, speculative generality.

## What the checks do not do
- `SourceMetrics` reads source without a compiler, because Unity projects cannot run the Roslyn analyzers from a test. Its scores
  follow the rules' published counting, checked in `SourceMetricsTests`, but it leaves out the +1 Sonar adds for direct recursion,
  and it can differ from the real analyzers on unusual syntax.
- The check for unused methods sees names only, and only private methods: a name in use elsewhere hides an unused method.
- Microsoft's maintainability index (CA1505) and class coupling (CA1506) need a compiler, so they are not checked.
- The golden runs cover the sample world and some settings, seeds and spans, not every path. Player commands are covered by the other tests.

## Commit gate
Run `sh Tools/install-hooks.sh` once per checkout, and `sh Tools/verify.sh` before committing code or data. The hook refuses a commit whose code or data differs from what the tests last passed on.
