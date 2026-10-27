---
doc: SYS-TRAVEL-COSTS
title: Travel costs and tolls
status: draft
updated: 2026-10-27
related: [SYS-INVENTORY, SYS-MERCHANTS, SYS-ROUTE-HAZARDS, SYS-BANKING, DEC-007, DEC-014, DEC-017, DEC-027, DEC-031]
---

# Travel costs and tolls

## Purpose
Distance and borders make trade a decision rather than a reflex: a price gap is only worth chasing after
fees and tolls.

## Rules
- [Confirmed] Travel takes the route's travel turns (implemented); hard terrain adds turns and cost
  (DEC-025, SYS-ROUTE-HAZARDS).
- [Confirmed] A trip costs a base fee, paid once per trip, plus a charge per cargo unit (DEC-007, DEC-017).
- [Confirmed] Every time cargo enters a node along the route, that node charges a toll (DEC-014).
- [Confirmed] The toll is a share of the cargo's value: the node's base rate, plus a surcharge on goods in
  the node's own specialty categories, protecting local producers (DEC-014).
- [Confirmed] Cargo is valued at the entered node's market price, or the nearest ancestor market that
  trades the good (DEC-027).
- [Confirmed] NPC merchants pay the same fees and tolls (DEC-015).
- [Confirmed] Fees, tolls and trades are paid in the local continent's coin; there is no automatic
  conversion. Foreign coins come from international banks (DEC-031, SYS-BANKING).

## Formula
```
trip_cost = base_fee + cargo_units * route.cost_per_unit
          + sum over entered nodes n: value_n(cargo) * (n.base_rate + n.surcharge if category in n.specialties)
value_n(cargo) = sum(qty * market_price(n or nearest ancestor trading the good))
```

## Edge cases
- Not enough local coins for fees or tolls at departure: the trip cannot start.
- No market up the hierarchy trades a carried good: [Draft] valued at zero (cannot be priced).

## Unity implementation notes
- `map_routes.csv` gains a `base_fee` column; `map_nodes.csv` gains `tariff_permille` and
  `specialty_surcharge_permille` (Claude-chosen names, [Draft]).
- Each route is one edge today, so a trip enters one node; tolls are charged when the trip starts, valued
  at the destination's prices.
- Arbitrage (SYS-MERCHANTS) and the player's trip planning share one cost function.
- The surcharge's specialty categories come from `NodeSpecialties` (P6): a category counts when one of the
  node's specialty base goods is in it.
