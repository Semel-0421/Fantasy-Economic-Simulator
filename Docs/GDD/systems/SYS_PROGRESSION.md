---
doc: SYS-PROGRESSION
title: Ranks and ruin
status: draft
updated: 2026-10-27
related: [OVERVIEW, SYS-BANKING, SYS-INTEREST-GROUPS, DEC-063]
---

# Ranks and ruin

## Purpose
Give the endless sandbox a direction: ranks name how far the business has come, and ruin is the one way
the game ends.

## Rules
- [Confirmed] There are seven ranks; they unlock nothing and only set goals, news and guidance (DEC-063).
- [Confirmed] Ranks are measured by reach and influence first; net worth joins once the economy is
  balanced (after P13, DEC-061) (DEC-063).
- [Confirmed] Ruin ends the game (DEC-063).
- [Draft] Claude's proposals (names and measures):
  - Peddler: the start.
  - Trader: has traded in at least 3 markets, or holds a license or a bank deposit.
  - Merchant house: owns a facility or uses a guild hall.
  - Company: has founded the company and owns 3 sites.
  - National power: the company's influence is at least 10% of a nation's total at a policy review.
  - Continental house: owns sites in most nations of one continent.
  - World house: owns sites on all five continents.
  - Ranks are measured once a year with the statistics and never go down, except at ruin (the game ends).
- [Draft] Ruin (Claude's proposal): the player's debts exceed everything they own (the balance sheet's
  collateral at full value) and they hold no coins of any continent for 8 consecutive weeks.

## Unity implementation notes
Not built (P14). A yearly `RankSystem` reads the player's sites, the policy review's influence and the
balance sheet; a ruin check runs weekly next to loan servicing and publishes a game-over event that the
presentation layer handles.
