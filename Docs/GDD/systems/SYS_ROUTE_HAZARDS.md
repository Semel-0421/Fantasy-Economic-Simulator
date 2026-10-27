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
- [Draft] Placeholders built in P8 (`terrain.*`, `danger.*`, `encounter.*`, `guard.*`, `insurance.*`,
  `passage.*`, `ship.*`):
  - Terrains: road, mountain (+1 turn, fees x1.5), river (fees x1.2), sea (fees x1.5); river and sea are
    water. Encounter chance per trip: road 5%, mountain 10%, river 6%, sea 8%.
  - Region danger factor 1.0 by default (sample: Aurelia region 2 x1.5, Borealis region 2 x2.0); cargo
    worth 2,000 doubles the chance; the chance is capped at 100%.
  - A loss takes 30% of the coins or 30% of every carried good.
  - Guards: 10 + 2% of the cargo's value, cutting the chance by 70%. Insurance: 3% of the cargo's value,
    refunding 80% of a loss.
  - Passage: 20 + 2 per cargo unit. Ship: 5,000, carrying 3,000 weight / 2,400 size.
- [Draft] Claude's proposals, built in P8:
  - The roll happens and its loss applies when the trip starts.
  - A cash loss takes from the departure coins left after paying; the insurance refund is paid in them.
  - The region of a route is the destination's region.
  - Ships are sold in any node of city level or above (no ports yet); the ship travels with the player,
    is used only on water routes, and on land the cart's limits still apply. (Replaced by DEC-053: ships
    are bought at ports and stay docked where the player lands; see SYS-INFRASTRUCTURE.)
  - The ship is collateral at its price and is seized after the cart, before facilities, at the auction
    share of its price.
  - Detailed merchants roll without guards or insurance; aggregate flows lose the expected share
    (chance without the value term, half of it on cargo, times the cargo loss share).

## Formula
```
danger = base(route.terrain) * safety_factor(region) * value_factor(cargo_value) * (1 - guard_reduction)
loss   = cash * cash_loss_ratio  or  cargo * cargo_loss_ratio   (insurance refunds part of the loss)
```

## Unity implementation notes
Implemented in P8.
- `map_routes.csv` has an optional `terrain` column (`Road`, `Mountain`, `River`, `Sea`; default Road);
  `map_nodes.csv` has an optional `danger_permille` per region node (Claude-chosen names, [Draft]).
  `WorldBuilder.BuildRoutes` applies the terrain's extra turns and fee multiplier; `RouteHazards` holds
  each route's base danger.
- `TravelCosts.Quote(route, cargo, TripOptions)` adds passage, guards, insurance, the cargo's value and
  the encounter chance; `TripQuote.DepartureCost` is everything paid in the departure coin.
- `Encounters.Roll` (dedicated stream, entity and salt) runs once when a trip starts, so saves and replays
  stay deterministic; `TripEncounter` reports the loss and refund.
- `TravelCommand(destination, guards, insurance)` checks the load against the cart, or the ship on water.
  `PlayerShip` (persistent) and `BuyShipCommand` hold the ship.

