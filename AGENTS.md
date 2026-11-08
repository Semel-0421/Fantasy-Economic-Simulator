# Agent roles

Three AI agents work on this project, each in one role. Orca (`orca` CLI) starts and connects them.
**The full shared rules are in `Docs/superpowers/agent-rules.md` (Korean: `agent-rules.ko.md`). Every agent reads and follows them.**

| Role | Agent | Does | Does not |
|---|---|---|---|
| Planner | Claude Code | Writes and reviews plans and specs; checks finished work against them; settles design, determinism and parity questions; hands out tasks with model and effort | Implement plan tasks unless the user asks |
| Builder | Codex | Executes plan tasks: code, tests, gates, one commit per task, a report per task | Change a plan's scope; push |
| Gatekeeper | GitHub Copilot CLI | Fast check before each commit batch; full gate before each push; reports pass or numbered blockers | Edit code; push |

## Flow

1. Claude writes the plan (with a Korean `.ko.md` copy for the user) and briefs Codex, choosing model and effort for the task.
2. Codex implements, runs the gates, commits locally, and writes `.superpowers/sdd/<plan>/task-N-report.md`.
3. Claude checks the report against the plan; gaps go back to Codex.
4. Before a push, Copilot runs the full gate and writes `.superpowers/sdd/<plan>/gate-report.md`.
5. A push happens only after Copilot's pass **and** the user's yes.

## Key rules (see the full file)

- **Everything delivered to the user is in Korean.** Code, commits and agent-to-agent files are in English.
- Codex uses gpt-6.1-sol where possible and gpt-6-astra only for deep reasoning; model and reasoning effort may be changed per task without asking the user.
- Only the user approves pushes, account work and scope expansion.
- Never stage `Packages/`, `ProjectSettings/`, `.serena/`, build output, files over 5 MB, secrets; never `git add -A`.
- Free mode must equal the C# reference (parity as in `.github/workflows/engine.yml`).
- No in-game AI or network calls; no agent handles API keys or account secrets.
