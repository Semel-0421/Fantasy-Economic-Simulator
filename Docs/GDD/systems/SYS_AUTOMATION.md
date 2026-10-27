---
doc: SYS-AUTOMATION
title: Delegation: facility managers and hired merchant groups
status: draft
updated: 2026-10-27
related: [OVERVIEW, SYS-PRODUCTION, SYS-MERCHANTS, SYS-INTEREST-GROUPS, DEC-064]
---

# Delegation

## Purpose
As the business spans more places, the player hands lower tiers to managers and hired merchant groups,
which is the pitch's "lower tiers automate".

## Rules
- [Confirmed] The player can put a manager on a facility and hire merchant groups; regional agencies for
  distant continents come later (DEC-064).
- [Confirmed] Each delegated asset keeps its own cash, as facilities do today; there is no company
  treasury (DEC-064).
- [Confirmed] Hired merchant groups trade by free arbitrage and by standing orders; free arbitrage comes
  first (DEC-064).
- [Confirmed] Managers and hired merchant groups are paid a share of the asset's upkeep as wages ([Draft]
  +20%) (DEC-064).
- [Draft] Claude's proposals:
  - A facility with a manager (Automated mode) sells its output at the market and buys inputs from its
    own cash, like an NPC facility; without one (Manual) its output goes to the warehouse as today
    (DEC-005).
  - Hiring a group happens at a node of city level or above; the hiring price is the group's starting
    cash for that level, which becomes the group's own cash. Its wage is its upkeep plus 20%.
  - The player moves coins into or out of a hired group's cash at the group's home node, like a
    facility's (`MoveFacilityCashCommand`).
  - Standing orders, after free arbitrage: "carry facility X's warehouse goods to the best market" and
    "buy good G at A, sell at B".
  - Wages go to households (DEC-061).

## Unity implementation notes
Not built (P15). The facility mode flag exists but production ignores it; `AgentTable` already allows
player-owned groups, and the merchant behavior runs only Automated groups.
