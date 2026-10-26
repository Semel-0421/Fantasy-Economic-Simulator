---
doc: SYS-ROUTE-HAZARDS
title: Terrain and road dangers
status: draft
updated: 2026-10-27
related: [SYS-TRAVEL-COSTS, SYS-MERCHANTS, SYS-INVENTORY, DEC-022, DEC-023, DEC-025, DEC-026, DEC-034]
---

# Terrain and road dangers

## Purpose
Routes differ by more than length: seas, rivers and mountains are hard to cross, and roads are not safe.
Choosing a route trades speed and cost against risk.

## Rules
- [Confirmed] Hard terrain adds travel turns and cost (DEC-025).
- [Confirmed] Sea and river routes need a ship (DEC-025).
- [Confirmed] Bandits, monsters and similar dangers occur as random encounters (DEC-023).
- [Confirmed] One encounter roll per trip (DEC-026).
- [Confirmed] Danger depends on route terrain, regional safety and cargo value (DEC-026).
- [Confirmed] A loss hits cash or cargo, chosen at random; loss ratios are config values (DEC-026).
- [Confirmed] Countermeasures: hiring guards and merchant-guild insurance (DEC-026).
- [Confirmed] NPC merchants in detailed regions roll encounters; far regions apply the expected loss to
  their trade flows (DEC-026).
- [Draft] Terrain multipliers, safety values, loss ratios, guard and insurance prices start as placeholder
  config and data values.
- [Confirmed] The player first crosses water by paying for passage, carrying only the cart's load
  (DEC-034).
- [Confirmed] Later the player can buy a ship, which carries more than the cart (DEC-034).
- [Confirmed] NPC merchants need no ship (DEC-034).
- [Draft] Passage fee, ship price and ship capacity start as placeholder config values.

## Formula
```
danger = base(route.terrain) * safety_factor(region) * value_factor(cargo_value) * (1 - guard_reduction)
loss   = cash * cash_loss_ratio  or  cargo * cargo_loss_ratio   (insurance refunds part of the loss)
```

## Unity implementation notes
- `map_routes.csv` gains a `terrain` column; `map_nodes.csv` gains a `safety` value per region node
  (Claude-chosen names, [Draft]).
- The roll uses `ctx.RandomFor(stream, entity)` once when the trip starts, so saves and replays stay
  deterministic; a `TripEncounter` event lets the UI report losses.
- Guards and insurance are bought with the trip (part of `TravelCommand`), so the cost function sees them.
- A water route needs either an owned ship (cargo holder with ship capacity) or passage (fee added to the
  trip cost, cart capacity applies); NPC merchants skip the check.

