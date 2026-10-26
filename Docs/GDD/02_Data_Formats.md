# 02 — Data Formats

All files live in `Assets/StreamingAssets/Data/`. CSV with a header row; lines starting with `#` and blank lines
are ignored; quote fields containing commas (a quote only opens a field as its first character).
Integers outside the int range, numeric enum values and unterminated quotes are load errors. Keys are the stable identity; dense ids are assigned in
file order at load. Any change to these files changes the data hash, so older saves are rejected —
except `config.csv`, which is hashed by its effective values (see below).

## goods.csv (required)

| column | meaning |
|---|---|
| key | base good key (unique) |
| category | category key; categories are created in order of first appearance |
| base_price | Mid-grade reference price in coins |
| base_demand | weekly demand of a village market (scaled by level weight) |

Each row expands to 4 items (`key@low`, `@mid`, `@high`, `@best`) with prices scaled by
`grade.<grade>.price_permille` (defaults 600 / 1000 / 1800 / 3500).
Good id = `baseIndex * 4 + grade`.

## recipes.csv

`recipe,role,good,grade,qty` — one row per input (`in`) or output (`out`), weekly at level 1.

## facilities.csv

`key,tier,recipe` — tier is `Primary`, `Secondary` or `Tertiary`. Spawn counts per node come from
`producers.<level>.<tier>` in config; the facility type is picked deterministically among that tier's defs.

## events.csv

`key,level,category,target,mul_permille,duration,chance_permille` — rolled per node of `level` each
turn; `category` may be `*`; `target` is `Supply`, `Demand` or `Value`. Supply/Demand modifiers scale
weekly flows, so any duration >= 1 works. A `Value` event must last at least the longest repricing
period among its level and the levels below it, or loading fails.

## market_profiles.csv (required)

`level,category,grades` — which goods markets of a level carry. `category` and `grades` accept `*`;
grades may be a list such as `Low|Mid`. Drives the sparse market layout and therefore memory.

## map_nodes.csv / map_routes.csv

- Nodes: `key,name,level,parent`. Continents have an empty parent. A node's parent must be one level
  up, except villages, which may hang off a city or a region.
- Routes: `from,to,travel_turns,cost_per_unit` (directed; add both directions for two-way travel).

## config.csv

`key,value` pairs; every key has a code default (`SimulationConfig`). Unknown keys are load errors
(typos would otherwise fall back to the default silently). The data hash uses the effective value of
every key, defaults included: comments, row order and restating a default do not affect saves, while
changing a default in code does.

| key | default | meaning |
|---|---|---|
| tier.<level>.period / .offset | see 00_Architecture | price-update cadence |
| demand.<level>.weight_permille | continent 40000 ... village 1000 | base demand weight |
| merchants.<level> | 50/50/30/10/3 | merchant groups per node |
| merchants.<level>.start_cash | 5000/4000/3000/2000/1000 | starting cash per merchant group |
| production.variance_permille | 100 | weekly output variance of detailed facilities |
| producers.<level>.<tier> | village primary 4, tertiary 2; city secondary 4 | producer groups per node |
| grade.<grade>.price_permille | 600/1000/1800/3500 | grade price multipliers |
| price.adjust_permille / max_step_permille / anchor_permille / stock_pressure_permille / min | 250 / 150 / 20 / 250 / 1 | pricing |
| currency.mint_permille, gold_reserve, silver_reserve, coin_supply, coins_per_gold, coins_per_silver | 30, 1e6, 1e7, 5e6, 10, 1 | currency |
| currency.endowment_spread_permille | 300 | +- variation of continents' starting reserves |
| guidance.decay_permille | 10 | weekly guidance decay |
| guidance.weight.buy_facility_permille, guidance.weight.found_company_permille | 1000, 5000 | guidance signal weights |
| lod.focus_level | Region | default LOD policy: detailed subtree around the player |
| player.start_node, player.start_cash | first village, 500 | player start |

`<level>` = continent, nation, region, city, village. `<tier>` = primary, secondary, tertiary.
`<grade>` = low, mid, high, best.
