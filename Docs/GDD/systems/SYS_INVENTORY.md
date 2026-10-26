---
doc: SYS-INVENTORY
title: Cargo, storage and player trading
status: draft
updated: 2026-10-27
related: [SYS-MERCHANTS, SYS-PRODUCTION, SYS-TRAVEL-COSTS, SYS-LICENSES, SYS-BANKING, DEC-005, DEC-006, DEC-009, DEC-031]
---

# Cargo, storage and player trading

## Purpose
The peddler's core loop: buy goods, load the cart, travel, sell. Storage grows from one cart to
warehouses at owned sites as the business expands.

## Rules
- [Confirmed] Every item has a weight and a size (DEC-009), set per base good; all four grades share
  them (DEC-012).
- [Confirmed] A carrier has a max total size and a max total weight; cargo must fit both (DEC-009).
- [Confirmed] On paid passage over water the cart's capacity applies; an owned ship carries more (DEC-034).
- [Confirmed] Warehouse cargo, cart cargo and owned ships can be pledged as loan collateral (DEC-038).
- [Draft] Cart, merchant and warehouse capacities start as placeholder config values, tuned in
  balancing (DEC-013).
- [Confirmed] Player goods live in the cart, which travels with the player, and in warehouses at owned
  sites such as facilities and guild halls (DEC-005).
- [Confirmed] Output of player-owned facilities always goes to that site's warehouse (DEC-005).
- [Confirmed] The player buys and sells at the market's current price; the traded quantity is added to
  that market's demand (buy) or supply (sell) and moves the price at the next repricing (DEC-006).
- [Confirmed] An order larger than the market stock fills up to the stock (DEC-020).
- [Confirmed] Selling special goods needs a license (DEC-021, DEC-024); details in SYS-LICENSES.
- [Confirmed] Output that does not fit a warehouse is sold at the market (DEC-029).
- [Confirmed] Cash is carried in continents' coins and trades are paid in the market's local coin
  (DEC-031); exchange happens at international banks (SYS-BANKING).

## Formula
```
fits = sum(qty * size) <= max_size  and  sum(qty * weight) <= max_weight
```

## Edge cases
- Selling goods the market does not carry: impossible (the market has no cell for that item).
- Warehouse full when a player facility produces: the overflow is sold (DEC-029).
- Buy order limited by stock, capacity and cash at once: the smallest limit wins.

## Unity implementation notes
- `goods.csv` gains `weight` and `size` columns (names Claude-chosen, [Draft]); `GameDatabase` copies them
  per base good into native arrays next to `GoodBasePrice`.
- `PlayerState.Cash` becomes a per-continent coin balance (save migration).
- One persistent `CargoTable` (holder id, good id, quantity) can serve the cart, warehouses and merchant
  groups; capacity checks live in one helper so every holder uses the same rule.
- New commands `BuyGoodsCommand` / `SellGoodsCommand` (and `PlayerActions` methods) validate license,
  stock, capacity and cash, then go through `MarketFlows` so event modifiers still apply.

