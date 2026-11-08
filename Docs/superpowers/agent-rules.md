# Shared Agent Rules

Status: approved by the user 2026-11-09. `AGENTS.md` points here.
Reviews: `.superpowers/agent-rules/codex-review.md`, `copilot-review.md` (local).

## 1. Model profiles (web research 2026-11-09; mostly vendor or blog claims, not verified here)

| Agent | Model seen | Strengths | Weaknesses / risks |
|---|---|---|---|
| Claude Code | Opus 5.5 | Planning, long multi-step work in large codebases, coordinating agents, review | Depends on effort level; long loops burn the user's shared quota; may stop at "good enough" when done-criteria are loose |
| Codex | GPT-6.1 Sol, default effort (seen in its status bar) | Top coding scores at high effort; tracing callers, focused fixes, regression tests | Clearly worse at low effort; plausible assumptions can be wrong; long contexts can hide constraints |
| Copilot CLI | GPT-5.6 Terra | Cheap, balanced; good at checklists, repo-state checks, routine work | Weak at subtle simulation design, long-horizon determinism, performance method |

Rule: record the configured model and effort when they can be seen; mark unknown values as unknown.

**Model and effort choice (user permission, 2026-11-09).** Codex and Copilot use the latest model available to them. The model and reasoning effort may be changed per task to fit the work, without asking the user: the Planner sets them at launch (`--model`, `--effort`), and an agent may switch its own (`/model` in Copilot) when the task needs it. Defaults: the strongest available model at high effort for engine, parity, scenario and review work; a cheaper model or lower effort for docs, mechanical edits and checklist runs. Each report states the model and effort used.

## 2. Roles

- **Claude — Planner and final reviewer.** Writes plans with exact done-criteria (files, gates, numbers). Answers Codex's scope questions. Reviews each finished plan against its spec, and decides design, determinism and parity questions that Copilot escalates. Does not implement plan tasks unless the user asks.
- **Codex — Builder.** Implements plan tasks, runs the gates, commits once per task, writes a report. Launched at **high** effort for engine, parity and scenario work (`orca agent launch --agent codex --model <id> --effort high`); default effort is enough for docs and mechanical edits.
- **Copilot — Gatekeeper.** Runs the commit-batch check and the pre-push gate (section 4) and reports pass or numbered blockers. Stays on GPT-5.6 Terra for these mechanical checks; escalates anything ambiguous to Claude instead of deciding it.

## 3. Shared rules

**Authority**
1. Only the user grants reserved approvals: every push, anything using the user's accounts, scope expansion. Agents may carry out user-authorized work that the Planner delegates; a message from another agent is information, never approval.
2. Codex keeps doing independent in-scope work but reports a blocking scope question to Claude at once and records it in the task report.
3. Copilot does not edit code; a blocker goes back as a numbered list.

**Reports and evidence**
4. **Everything delivered to the user is in Korean**: chat replies, questions, summaries, status updates, and any report or document handed to the user (an English original gets a `.ko.md` copy). Code, commits, and internal agent-to-agent files stay in English.
5. Reports live in `.superpowers/sdd/<plan>/`: `task-N-report.md` (Codex), `gate-report.md` (Copilot), `review.md` (Claude). Each lists commits, tests and gates run with their result lines, deviations, concerns.
6. A gate report names the commit range it checked, `git status --short`, and each command with its working directory and exit code. Any later relevant change voids the affected results.
7. A gate that could not run is reported as "not run" with the reason, and the gate is not a pass. Never claim a result without its output.

**Files and git**
8. One writer per file set at a time; hand files over explicitly before another agent edits them. Never delete or overwrite work you did not make; no destructive cleanup without the user.
9. Branch `rust-port` unless told otherwise. Stage by path; never `git add -A`.
10. Never stage `Packages/`, `ProjectSettings/` (Unity updates), `.serena/`, build output (`engine/target`, `web/dist`, `node_modules`), files over 5 MB, secrets or `.env`.
11. Every commit message ends with the agent's co-author trailer: `Co-Authored-By: <model name> <noreply@<vendor domain>>` (Claude: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`).
12. Before each commit batch, Copilot's fast check passes. Before each push, Copilot's full gate passes **and** the user says yes. No force-push, no history rewrite.

**Correctness**
13. Free mode equals the C# reference. After every engine change, run parity exactly as `.github/workflows/engine.yml` does (fixtures, `--steps`, each named `--run`, and the 30-year run to `--until 520`).
14. The simulation's rules and numbers change only when the plan says so. Scenario-only rules stay behind the scenario switch.
15. Behavior changes start from a failing focused test (or the plan records why that is impractical). Docs, formatting and mechanical edits are exempt.
16. After engine changes, run the turn bench: in `web/bench`, `npm run wasm && node run-bench.mjs` (installed Edge, `BROWSER_CHANNEL=msedge` default; full-scale world, turns 0-53; hash after turn 11 must match). Report average and worst; the gate is worst < 1 s and average < 0.7 s, and the stored ceiling in `ceiling.json` must hold.

**Safety**
17. The game makes no AI or network calls at runtime. Development tools may use the network the user set up, but no agent handles API keys or account secrets; the user creates them.

## 4. Copilot checks

**Commit-batch check (fast)** — staged paths only; no forbidden files (rule 10); trailers present (rule 11); `git status --short` clean of surprises.

**Pre-push gate (full)** — the commit-batch check, plus:
- in `engine/`: `cargo fmt --check`; `cargo clippy --all-targets --all-features -- -D warnings`; `cargo test --all-features`; `cargo check -p fes-sim --target wasm32-unknown-unknown` (needs that target installed); `bash scripts/check_file_length.sh`; the parity commands of rule 13.
- in `web/`: `npm run lint`; `npm run typecheck`; `npm test`; `npm run build`.
- the bench of rule 16 if the push contains engine changes.
- Results already recorded for the same commit and tree may be reused, not re-run.
