---
doc: SYS-INFRASTRUCTURE
title: Ports and paved roads
status: draft
updated: 2026-10-27
related: [SYS-ROUTE-HAZARDS, SYS-TRAVEL-COSTS, SYS-MERCHANTS, SYS-BANKING, SYS-INTEREST-GROUPS, DEC-034, DEC-053, DEC-054]
---

# Ports and paved roads

## Purpose
Infrastructure shapes where trade flows. Ports decide where the sea can be crossed and where ships wait;
paved roads make the busiest land routes cheaper and safer, and are worth investing in.

## Rules
### Ports
- [Confirmed] The map data marks which nodes are ports (DEC-053).
- [Confirmed] Ships are bought and sold only at ports (DEC-053).
- [Confirmed] Water routes (river, sea) connect ports only (DEC-053).
- [Confirmed] A ship stays docked at the port where the player lands; to sail it again the player returns
  to that port (DEC-053). Elsewhere, water routes need passage (DEC-034).

### Paved roads
- [Confirmed] Paving lowers a route's per-unit charge and its encounter chance (DEC-054).
- [Confirmed] Some roads are paved in the map data from the start; the player and interest groups can pay to
  build more (DEC-054).
- [Confirmed] A road whose upkeep is not paid slowly decays (DEC-054).
- [Confirmed] Road paving is one of the projects interest groups invest in (DEC-065).

## Unity implementation notes
Implemented in P11; the numbers below are [Draft] placeholders and the mechanics Claude's proposals.
- `map_nodes.csv` has a `port` flag and `map_routes.csv` a `paved` flag (Claude-chosen names). Sample
  ports: Aurelia region 2, Borealis regions 1 and 2; sample paved roads: Aurelia cities 1-1 and 1-2 to
  their region, Borealis city 1-2 to its region.
- `NodePorts` (static) checks that water routes run between ports. `PlayerShip` (section version 2,
  `PlayerShipV1Migration`) has `DockedAt`; a ship from before ports waits at the next port the player is
  at. `BuyShipCommand` and `SellShipCommand` work at ports; sailing moves the dock to the destination.
- `RoadState` (persistent, per route: condition, maintainer, finish turn of a road being built).
  `TravelCosts.PerUnitCharge` and `RouteDanger` apply paving, so the player, detailed merchants and the
  aggregate flow (recomputed each week) all see it. `RoadSystem` (Finance phase) finishes roads, charges
  upkeep and decays unpaid roads (`RoadBuilt`, `RoadLost`); `BuildRoadCommand` starts one.
- A road's condition (0-100%) scales its effects: per-unit charge -40% and encounter chance -50% at full
  condition. Paving a route also paves its reverse; both directions keep their own condition and upkeep
  (5 a week each).
- Map roads belong to the nation of the route's start node and are maintained from its bank reserve;
  roads the player builds are the player's and are maintained from the player's deposit with that
  nation's bank, then the coins carried in its currency. Unpaid upkeep costs 0.5% condition a week; at
  zero the road is unpaved again.
- Building: the player, at one end of a land route, pays 300 per travel turn of the route; the road is
  done after 8 weeks. Interest groups build roads once they exist (SYS-INTEREST-GROUPS).
- A ship sold at a port returns 70% of its price.

## State development
See SYS-STATE-ECONOMY (DEC-072, DEC-074).
