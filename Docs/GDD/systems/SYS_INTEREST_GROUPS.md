---
doc: SYS-INTEREST-GROUPS
title: Economic interest groups
status: draft
updated: 2026-10-27
related: [SYS-PRODUCTION, SYS-MERCHANTS, SYS-BANKING, SYS-LICENSES, SYS-TRAVEL-COSTS, SYS-INFRASTRUCTURE, DEC-041, DEC-055]
---

# Economic interest groups

## Purpose
The economy has owners with interests. Companies, guilds, state enterprises and private businesses own
the facilities and merchant groups, compete for markets, and push nations' policy their way, so the
player meets rivals and allies instead of an anonymous market.

## Rules
- [Confirmed] Interest groups are both asset owners (facilities, merchant groups, cash) and political
  forces that influence policy such as tariffs and licenses (DEC-055).
- [Confirmed] Company: a large business with facilities and merchant groups across regions; the player's
  company is one (DEC-055).
- [Confirmed] Guild: an association per city and trade, with membership; it runs the insurance, guild
  hall warehouses and license recommendations (DEC-055).
- [Confirmed] State enterprise: owned by a nation; the state banks and monopolies on strategic goods such
  as mines (DEC-055, DEC-041).
- [Confirmed] Private enterprise: a small business owned by a person or family, as today's NPC facilities
  and merchant groups are (DEC-055).
- [Confirmed] At the start every NPC facility and merchant group belongs to an interest group (DEC-055).
- [TBD] How the player relates to groups beyond founding a company: guild membership, company shares,
  bidding when state enterprises are privatized (Q-033).
- [TBD] How political influence works: what groups can change, how strong they are, how nations decide
  (Q-034).

## Unity implementation notes
Planned as P12, after Q-033 and Q-034.
- A persistent `GroupTable` (kind, home node, nation, cash, influence) and an owner group column on
  `FacilityTable` and `AgentTable`, replacing the plain NPC owner; `OwnerIds.Player` stays the player.
- Existing per-entity cash stays where it is; group cash funds expansion, road building and lobbying.
- `FoundCompanyCommand` creates the player's company row.
