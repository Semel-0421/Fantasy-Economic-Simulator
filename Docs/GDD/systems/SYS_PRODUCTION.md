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
- [Confirmed] Facilities pay a weekly upkeep (wages), which can push cash below zero (DEC-051).
- [Confirmed] A facility whose cash stays below zero for 5 consecutive weeks goes bankrupt (DEC-051).
- [Confirmed] When the player loses a facility, they can still empty its warehouse for a period; what is
  left afterwards is gone (DEC-052).
- [Draft] Upkeep per level-1 facility: primary 50, secondary 100, tertiary 50, times the facility level
  (`facility.<tier>.upkeep`). The upkeep leaves the economy until money totals exist (P9).
- [Draft] Claim period on a lost warehouse: 8 weeks (`cargo.lost_warehouse_weeks`); during it the player
  may only take goods out. Buying the facility back ends the claim, the goods staying the player's.
- [Draft] Starting cash per tier: primary 500, secondary 2,000, tertiary 1,000 (`facility.<tier>.start_cash`).
- [Draft] Bought inputs are not scaled by event modifiers, like the player's purchases; output still is
  (Claude's proposal).
- [Draft] Tag list, tag-to-specialty table and effect sizes start as placeholder data and config values,
  tuned in balancing (not a design question).

## Edge cases
- Several facilities competing for the same stock: they are served in facility order (deterministic).
- A facility that can afford only part of its inputs: counts as short (all or nothing).

## Unity implementation notes
Cash, inputs, income, bankruptcy and player output (P4), specialties (P6) and NPC loans (P9b, see
SYS-BANKING) are implemented.
- `ProductionJob` (single-threaded Burst) runs two passes per facility in facility order: check every
  input's stock and the total cost, then buy (stock out, through supply into demand, like
  `MarketFlows.Buy`), pay, and produce. An input with no market anywhere means the facility never produces.
- `FacilityTable` has `Cash`, `NegativeCashWeeks` and `WarehouseClaimUntil` (section version 2;
  `FacilityTableV1Migration` writes an unset cash that `ProductionSystem.OnLoaded` replaces with the tier's
  starting cash). After the production job and the warehouse overflow sale, `FacilityUpkeepJob` (Burst)
  charges upkeep and lists bankrupt facilities. Bankruptcy resets the row (NPC owner, automated, starting
  cash), sets the warehouse claim for a player facility and publishes `FacilityBankrupt`; an expired claim
  clears the warehouse and publishes `LostWarehouseCleared`.
- The job emits player-owned output into a buffer; the system then stores it in
  `CargoHolders.Warehouse(facility)` (capacity `cargo.warehouse.max_weight` / `max_size`, [Draft] 2,000 /
  1,500) and sells the overflow into the facility's cash.
- `MoveCargoCommand` moves goods between the cart and the warehouse (out of a lost warehouse during its
  claim), `MoveFacilityCashCommand` pays coins in or takes them out; both need the player at the facility's
  node. A bought facility keeps its cash.
- Until P5 carries ore to cities, smithies only pay upkeep, so they cycle through bankruptcy and new
  operators about every 25 weeks.
- Specialties: `map_nodes.csv` has a `tags` column and `specialties.csv` maps tags to categories or base
  goods (file and column names Claude-chosen, [Draft]). `NodeSpecialties` (world component, not saved)
  holds each node's specialty base goods; a node's specialties come from its own tags only.
- Always carried: `MarketLayoutBuilder` adds a node's specialties in every grade its level carries for any
  good (a village: Low and Mid).
- Lower price: `MarketStore.CellSpecialty` marks the cells; their anchor (and starting price) is
  multiplied by `specialty.price_permille`.
- Production: an output line of a node's specialty is multiplied by `specialty.output_permille`; inputs
  stay the same. Of that output, `specialty.grade_up_permille` comes out one grade higher: a detailed
  facility rolls once per line and week (all or none), an aggregate one splits by the expected share. No
  grade-up from Best, or when no market up the hierarchy trades the higher grade.
- Effect sizes ([Draft]): output x1.25, 20% one grade higher, local anchor x0.8. Facility spawning does
  not favour specialties.

