---
doc: OVERVIEW
title: Game overview, goal and core loop
status: draft
updated: 2026-10-27
related: [SYS-PROGRESSION, SYS-AUTOMATION, SYS-PLAYER-START, DEC-040, DEC-063, DEC-064]
---

# Game overview

## Pitch
A peddler in one kingdom grows into a five-continent trading power; lower tiers automate as the business
scales. Turn-based (one turn is one week), 2D pixel art, Android, Windows and the web.

## Goal
- [Confirmed] An endless sandbox: the game has no victory and no fixed end (DEC-063).
- [Confirmed] Seven ranks mark the player's standing and give goals; they unlock nothing, since nothing in
  the game is gated (DEC-063). Rules in [SYS-PROGRESSION](systems/SYS_PROGRESSION.md).
- [Confirmed] Ranks are measured by reach and influence first; net worth joins once the economy is
  balanced (DEC-063).
- [Confirmed] Ruin ends the game (DEC-063).

## Core loop
Weekly:
1. Read prices and news where the player is (the view shows the current node's market and the published
   statistics).
2. Buy and sell; carry goods along routes, paying fees, tolls and risk (SYS-TRAVEL-COSTS,
   SYS-ROUTE-HAZARDS).
3. Run assets: facilities and their warehouses, hired merchant groups, loans and deposits
   (SYS-PRODUCTION, SYS-AUTOMATION, SYS-BANKING).

Yearly: statistics are published, licenses and guild memberships renew, nations review policy, interest
groups invest, population and income change, ranks are measured (SYS-BANKING, SYS-INTEREST-GROUPS,
SYS-MARKETS, SYS-PROGRESSION).

Growth: hands-on trading at first; as the business spans more places the player delegates facilities to
managers and trade to hired merchant groups (DEC-064), lobbies nations, and reaches other continents.

## Ranks at a glance
| Rank | Rough meaning |
|---|---|
| Peddler | The start: a cart and a purse in a village (DEC-040) |
| Trader | Trades across several markets, holds licenses or bank access |
| Merchant house | Owns a first facility or warehouse |
| Company | Founded company with several sites |
| National power | A share of a nation's policy influence |
| Continental house | Present in most nations of a continent |
| World house | Present on all five continents |

Rank names and the measures behind them are [Draft] (Claude named); thresholds in
[SYS-PROGRESSION](systems/SYS_PROGRESSION.md).

## Not yet decided
- What is out of scope (multiplayer, combat beyond encounters, character play) has not been discussed.
- Screens and UI are not designed (reviews/REVIEW_2026-10-27.md).
