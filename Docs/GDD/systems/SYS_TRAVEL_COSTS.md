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

- [Draft] The base fee and per-unit charge are paid in the departure node's coin, the toll in the entered
  node's coin (Claude's proposal, following DEC-031). Crossing to another continent with cargo therefore
  needs that continent's coins; with an empty cart the toll is zero.
- [Draft] Fees and tolls leave the economy until money totals exist (P9).
- [Draft] Sample rates: toll 30 permille and specialty surcharge 70 permille by default, cities 50 / 100;
  base fee 2 (village routes), 5 (city routes), 20 (between continents).

## Edge cases
- Not enough coins for fees or tolls at departure: the trip cannot start.
- No market up the hierarchy trades a carried good: [Draft] valued at zero (cannot be priced).

## Unity implementation notes
Implemented in P3.
- `map_routes.csv` has an optional `base_fee` column (default 0); `map_nodes.csv` has optional
  `tariff_permille` and `specialty_surcharge_permille` columns, empty meaning the config defaults
  `toll.tariff_permille` / `toll.specialty_surcharge_permille` (Claude-chosen names, [Draft]).
  `NodeTolls` (world component, not saved) holds the effective rates.
- Each route is one edge today, so a trip enters one node, its destination; fee and toll are paid when the
  trip starts, the toll valued at the destination's prices (or the nearest ancestor trading the good).
- `TravelCosts.Quote(route, cargo)` is the one cost function, for the player now and merchants in P5.
  `TravelCommand` pays it up front and publishes `TripStarted`; `SimulationView.TryQuoteTrip` shows it.
- The surcharge's specialty categories come from `NodeSpecialties` (P6): a category counts when one of the
  node's specialty base goods is in it.
