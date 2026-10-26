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
  placeholder data and config values.

## Edge cases
- License expiring while the player travels with licensed cargo: the cargo can be sold only after renewal.
- A licensed good in a foreign nation: needs that nation's license.

## Unity implementation notes
- `goods.csv` gains a `requires_license` flag per base good (Claude-chosen name, [Draft]); a persistent
  `LicenseTable` holds (holder, good, nation, expiry turn).
- The check sits in the shared sell validation used by player commands and merchant behavior.
- NPC license holders are picked deterministically at spawn (`DetRandom` with a dedicated stream).
- Buying a license is a natural `IGuidanceSource` (trade direction signal).
