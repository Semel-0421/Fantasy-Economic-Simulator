---
doc: INDEX
title: Fantasy Economic Simulator design index
status: draft
updated: 2026-10-27
---

# Fantasy Economic Simulator design docs

> Read order: this file, then only the documents for the task at hand.
> Status markers: [Confirmed] decided by the user · [Draft] provisional · [TBD] needed but open (has a Q).
> IDs: SYS-* system · DEC-* decision · Q-* open question.

## At a glance
| Item | Value |
|---|---|
| Pitch | A peddler in one kingdom grows into a five-continent trading power; lower tiers automate as the business scales |
| Genre | Turn-based economic simulation |
| Visual style | 2D pixel art |
| Platforms | Android, Windows |
| Unity version | 6000.6.6f1 (URP 2D) |
| Turn | 1 week (52 turns per year) |
| Start | A random village, no bank access (DEC-040) |
| World | 5 continents, 25 nations, 50 regions, 250 cities, 1,250 villages |
| Goods | 20 categories x 50 goods x 4 grades = 4,000 items |
| Stage | Core simulation skeleton done; economy rules being designed |

## Document map
| Document | Contents |
|---|---|
| [00_Architecture.md](00_Architecture.md) | Assemblies, world model, markets, determinism, pipeline, LOD, currency, save/load (as built) |
| [01_Extension_Guide.md](01_Extension_Guide.md) | How to add systems, state, commands, views and Burst jobs |
| [02_Data_Formats.md](02_Data_Formats.md) | CSV files and config keys (as built) |
| [03_Decisions.md](03_Decisions.md) | Decision log (DEC) and open questions (Q) |
| [04_Roadmap.md](04_Roadmap.md) | Implementation phases for the economy rules |
| [systems/SYS_MERCHANTS.md](systems/SYS_MERCHANTS.md) | NPC merchant arbitrage |
| [systems/SYS_PRODUCTION.md](systems/SYS_PRODUCTION.md) | Production inputs, player output, node specialties |
| [systems/SYS_INVENTORY.md](systems/SYS_INVENTORY.md) | Item weight and size, cart, warehouses, player trading |
| [systems/SYS_TRAVEL_COSTS.md](systems/SYS_TRAVEL_COSTS.md) | Travel fees and protective tolls |
| [systems/SYS_BANKING.md](systems/SYS_BANKING.md) | Banks, currency exchange, loans and seizure |
| [systems/SYS_INFRASTRUCTURE.md](systems/SYS_INFRASTRUCTURE.md) | Ports and paved roads |
| [systems/SYS_INTEREST_GROUPS.md](systems/SYS_INTEREST_GROUPS.md) | Companies, guilds, state and private enterprises |
| [systems/SYS_LICENSES.md](systems/SYS_LICENSES.md) | Licenses for special goods |
| [systems/SYS_ROUTE_HAZARDS.md](systems/SYS_ROUTE_HAZARDS.md) | Hard terrain and random encounters |
| [systems/SYS_MARKETS.md](systems/SYS_MARKETS.md) | Demand, necessity/luxury elasticity |
| [systems/SYS_PLAYER_START.md](systems/SYS_PLAYER_START.md) | Random village start |

Documents 00 to 02 describe the built code; `systems/` describes agreed rules not yet built.

## Open questions
Detail: [03_Decisions.md](03_Decisions.md)

| ID | Question | Blocks |
|---|---|---|
| Q-033 | How the player relates to interest groups | P12 |
| Q-034 | How interest groups influence policy (Claude added) | P12 |
| Q-030 | Basic price index basket (deferred by the user) | Later |

## Recent changes
See [CHANGELOG.md](CHANGELOG.md). Latest: 2026-10-27, collateral and seizure resolved, random village
start and state-owned banks added (DEC-038 to DEC-041).
