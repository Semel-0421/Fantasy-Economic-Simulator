---
doc: SYS-PRODUCTION
title: Production and node specialties
status: draft
updated: 2026-10-27
related: [SYS-INVENTORY, SYS-MERCHANTS, SYS-BANKING, DEC-004, DEC-005, DEC-016, DEC-018, DEC-028, DEC-029, DEC-035, DEC-037]
---

# Production and node specialties

## Purpose
Facilities (farms, mines, workshops, services) turn recipe inputs into outputs every week. Shortages of
raw materials propagate to finished goods, and each node's specialties give every market a local identity.

## Rules
- [Confirmed] A facility buys its recipe inputs from the stock of the nearest market (own node, then
  ancestors) that trades each input (DEC-004).
- [Confirmed] All or nothing: if any input is short, the facility produces nothing that week (DEC-004).
  Partial fills (DEC-020) therefore count as short.
- [Confirmed] Every facility, NPC or player-owned, holds cash and pays for its inputs (DEC-018).
- [Confirmed] Output of player-owned facilities always goes to the player's warehouse at that site (DEC-005).
- [Confirmed] Output that does not fit the warehouse is sold at the market (DEC-029).
- [Confirmed] NPC facility output keeps going to the nearest market that trades the good.
- [Confirmed] Nodes carry terrain tags (coast, mountain, forest...); specialties are assigned from them
  automatically (DEC-016).
- [Confirmed] A specialty gives four effects (DEC-016):
  - production bonus for facilities making it at that node
  - the node's market always carries it
  - a chance of higher-grade output
  - a lower local price
- [Confirmed] Starting cash per industry tier comes from config (DEC-028).
- [Confirmed] NPC output is sold at the market's current price, which is the facility's income (DEC-028).
- [Confirmed] A facility whose cash runs out goes bankrupt and gets a new operator (DEC-028).
- [Confirmed] This applies to player-owned facilities too: the player loses the facility (DEC-035).
- [Confirmed] Facilities can borrow from banks, pledging facilities as collateral (DEC-037).
- [Confirmed] Unpaid debt leads to seizure of collateral facilities after a grace period (DEC-033,
  DEC-037).
- [Confirmed] A seized facility is auctioned (the player may bid); with no winning bid it goes to a new NPC
  operator (DEC-039).
- [Draft] Tag list, tag-to-specialty table and effect sizes start as placeholder data and config values,
  tuned in balancing (not a design question).

## Edge cases
- Several facilities competing for the same stock: they are served in facility order (deterministic).
- A facility that can afford only part of its inputs: counts as short (all or nothing).

## Unity implementation notes
- `ProductionSystem` is a single-threaded Burst job (`ProductionJob`); facility order is already fixed, so
  first-come input allocation stays deterministic.
- All-or-nothing needs two passes per facility inside the job: check stock and cash for every input, then
  consume, pay and add outputs.
- `FacilityTable` gains a cash column; its save section version bumps with a migration. Bankruptcy resets
  the row (new operator, starting cash) so facility ids stay stable.
- Player-owned output needs the job to emit into a player-storage buffer instead of market supply.
- Specialties: `map_nodes.csv` gains a `tags` column and a new `specialties.csv` maps tags to categories
  or goods (file and column names Claude-chosen, [Draft]). The four effects plug into facility spawning,
  `MarketLayoutBuilder` (always carried), the production job (bonus and grade roll) and the pricing anchor
  (lower local price).
- Effect sizes (bonus %, grade chance, price discount) start as config values [Draft].

