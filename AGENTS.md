# Agent roles

Three AI agents work on this project, each in one role. Orca (`orca` CLI) starts and connects them.

| Role | Agent | Does | Does not |
|---|---|---|---|
| Planner | Claude Code | Writes and reviews plans and specs (`Docs/superpowers/plans`, `specs`); checks each finished plan against its spec; decides what runs next | Implement plan tasks |
| Builder | Codex | Executes plan tasks: code, tests, gates, one commit per task on the working branch | Change a plan's scope; push |
| Gatekeeper | GitHub Copilot CLI | Checks the branch before every commit batch and before every push (checklist below); reports pass or a list of blockers | Write features; push |

## Flow

1. Claude writes the plan (English, plus a Korean `.ko.md` copy for the user) and briefs Codex with one task or a task batch.
2. Codex implements, runs the gates, commits locally, and writes a report to `.superpowers/sdd/<plan>/task-N-report.md`.
3. Claude reads the report and checks it against the plan. Gaps go back to Codex.
4. Before a push, Copilot runs the pre-push checklist and writes `.superpowers/sdd/<plan>/gate-report.md`.
5. Only on Copilot's pass and the user's yes does anyone push. The user's approval is required for every push.

## Pre-push checklist (Copilot)

- `git status`: nothing under `Packages/` or `ProjectSettings/` staged or committed (Unity update files); no secrets, keys, or `.env` files.
- Each commit message ends with the agent's co-author line; files were staged by path.
- Rust (in `engine/`): `cargo fmt --check`, `cargo clippy --all-targets --all-features -- -D warnings`, `cargo test --all-features`.
- Parity (free mode must equal the C# reference), as in `.github/workflows/engine.yml`: `cargo run -p fes-parity -- --fixtures fixtures`, the same with `--steps`, and `cargo run --release -p fes-parity -- --fixtures fixtures --runs` all equal.
- Web (in `web/`): `npm run lint`, `npm run typecheck`, `npm test`, `npm run build`.
- Large files (> 5 MB) or build output (`engine/target`, `web/dist`, `node_modules`) not committed.

## Rules for every agent

- Talk to the user in Korean; code, commits and docs in English. Plans for the user's review get a `.ko.md` copy.
- No in-game AI or network calls. Never handle API keys or account secrets; the user creates them.
- Never stage `Packages/` or `ProjectSettings/`; never `git add -A`.
- Ask the user before every push and anything that needs their accounts.
- Work on branch `rust-port` unless told otherwise. The C# Unity project is frozen as the reference.
