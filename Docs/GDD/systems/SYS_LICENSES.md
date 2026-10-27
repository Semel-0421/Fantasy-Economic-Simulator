---
doc: SYS-LICENSES
title: Trade licenses
status: draft
updated: 2026-10-27
related: [SYS-INVENTORY, SYS-MERCHANTS, DEC-021, DEC-024]
---

# Trade licenses

## Purpose
Special goods cannot be sold freely; a license turns them into a goal the player works toward and a
lever nations hold.

## Rules
- [Confirmed] Selling special goods requires a license (DEC-021, DEC-024).
- [Confirmed] A license covers one individual good (DEC-024).
- [Confirmed] A nation issues it, and it is valid only inside that nation (DEC-024).
- [Confirmed] It is bought and must be renewed every year (DEC-024).
- [Confirmed] It gates selling only; anyone may buy the good (DEC-024).
- [Confirmed] Only some NPC merchant groups hold licenses (DEC-024).
- [Draft] Which goods are special, license prices and the share of licensed NPC groups start as
  placeholder data and config values: iron tools only; price 20 x the good's Mid base price
  (`license.price_permille`); 20% of merchant groups (`license.merchant_share_permille`).
- [Draft] "One individual good" is read as one base good, every grade included, like weight and size
  (DEC-012) (Claude's proposal).
- [Draft] A license is bought from the nation the player is in, paid in local coins; it is valid for 52
  weeks, and buying it again adds a year to the current expiry (Claude's proposal).
- [Draft] Facilities selling their output (NPC sales, a player facility's overflow) need no license; the
  rule gates traders (Claude's proposal).
- [Draft] A licensed merchant group holds every license of the nation it trades in (Claude's proposal).

## Edge cases
- License expiring while the player travels with licensed cargo: the cargo can be sold only after renewal.
- A licensed good in a foreign nation: needs that nation's license.

## Unity implementation notes
Implemented in P7.
- `goods.csv` has an optional `requires_license` column (0/1, per base good; Claude-chosen name, [Draft]).
- The persistent `LicenseTable` holds the player's licenses as (base good, nation node, expiry turn);
  `LicenseSystem` (Cleanup phase) drops expired rows and publishes `LicenseExpired`.
- `BuyLicenseCommand` (`PlayerActions.BuyLicense`) buys or renews, publishes `LicenseBought` and signals
  the Trade guidance axis (`guidance.weight.buy_license_permille`, 2000).
- `LicenseRules.PlayerMaySell` is the check in `SellGoodsCommand`; `SimulationView.MaySell`,
  `LicensePrice` and `GetLicenses` show it.
- NPC holders are not stored: `LicenseRules.MerchantHoldsLicenses(seed, merchant, share)` picks them from
  the seed with a dedicated stream. P5's merchant behavior calls it before selling licensed goods.
