# Scenario data files

One file per teaching scenario (spec section 5), written in
[RON](https://github.com/ron-rs/ron) and read by `fes_sim::scenario::ScenarioDef::parse`
(crate feature `scenario`, which the wasm build enables). Player-facing text lives here, not in code.

**Shipping.** `web/scripts/prepare.mjs` copies every `*.ron` here to `web/public/data/scenarios/`, so the
web build serves them next to the game's CSV data; the app passes a file's text to
`WasmSim.new_scenario(files, text)`.

**Keys.** Places are map node keys (`map_nodes.csv`), goods are base good keys (`goods.csv`, the middle
grade is used). Companies are facility row numbers of the scenario's seeded world. A key the world does
not know fails the scenario's build.

## Shape

```ron
(
    id: "s1_first_venture",
    title: "First Venture",
    intro: "Shown when the scenario opens.",   // optional
    seed: 11,                  // fixed seed: every player sees the same world
    start_node: "aur_n1_r1_c1_v1",
    start_coins: 500,          // in the start node's coin
    weeks: 20,                 // ScenarioEnded fires at the end of this week
    warmup_weeks: 4,           // optional: the world runs this long before the player arrives
    protected_opening: true,   // optional: the first goods bought sell for >= 1.5x the price paid
    objectives: [
        (id: "deliver", text: "Deliver the grain order.", goal: ContractDone("grain")),
        (id: "profit", text: "Make 1,000 coins of profit.", goal: NetProfit(1000)),
    ],
    events: [
        (
            id: "offer",
            trigger: Week(2),
            effects: [OfferContract(id: "grain", good: "wheat", quantity: 20,
                node: "aur_n1_r1_c1", reward: 1500, deadline_week: 12,
                text: "Deliver {qty} sacks of {good} to {dest} by week {deadline}. Promised {reward} coins.")],
        ),
        (id: "warn", trigger: PlayerAt("aur_n1_r1_c1"), fallback_week: 8,
            effects: [Dialog("Robbers were seen on the city road.")]),
    ],
)
```

`//` comments are allowed. An `Option` field takes a bare value (`fallback_week: 8`).

## Weeks

A week is one press of **Next week**. Week `n` is the end of the `n`-th turn after the start; the script,
contract deliveries and objectives are checked then, after the NPCs moved.

## Triggers

Each event fires once: when its trigger holds or at its `fallback_week`, whichever comes first. Every
trigger except `Week` must have a `fallback_week` (the file is rejected otherwise), so a player who goes
elsewhere still sees the event.

| Trigger | Holds when |
|---|---|
| `Week(n)` | week `n` has ended |
| `PlayerAt("node")` | the player stands at the node (not travelling) |
| `Holds(good: "wheat", quantity: 10)` | the cart holds that many |
| `ContractDone("id")` | the scenario contract was delivered (same week as the delivery) |
| `Never` | never; only the fallback week fires it |

## Effects

| Effect | What it does (existing system it uses) |
|---|---|
| `Dialog("text")` | a message (`ScenarioDialog` event) |
| `OfferContract(id, good, quantity, node, reward, deadline_week, text)` | offers a delivery request; once the player accepts it (`accept_contract`), it is delivered the week the player stands at `node` with the goods in the cart: the goods leave the cart and `reward` coins are paid in `node`'s coin, from its households, the rest from its nation's spendable reserve, never more than they hold. Not delivered by the end of `deadline_week`: expired |
| `Raid(from, to, loss_permille)` | the player's next trip on that route meets robbers: without guards they take `loss_permille` of every good in the cart (an encounter, so insurance refunds as usual); guards beat them off (an encounter that took nothing). No message: the trip's `TripEncounter` shows it |
| `PriceShock(node, good, percent, weeks, text)` | a market modifier on value for the good's category under the node, as a world event makes |
| `CompanyNews(facility, percent, text)` | the company's cash becomes `percent`% (0 to 100) of what it was; the lost cash goes to its node's households; its share price follows its value |
| `CompanyOrders(facility, coins, text)` | the company wins orders: `coins` move from its node's households into its cash (at most what they hold); its share price follows its value |
| `Dividend(facility, per_share, text)` | pays `per_share` coins per share the player holds, from the facility's cash then its group's, in its node's coin |
| `BondIssue(issuer, text)` | announces bonds of the issuer (bond-desk issuer number) for the player to buy with `BuyBond` |
| `Coupon(issuer, percent, text)` | pays `percent`% of the face the player holds of the issuer's bonds, from the nation's spendable reserve or the company group's cash, in the issuer's coin |
| `LicenceOffer(good, nation, text)` | grants the good's licence in that nation (node key) for a year |

Issuers are bond-desk numbers: `-1 - n` (written `-1`, `-2`, ...) for the nation with index `n`, otherwise
a company group; an unknown issuer fails the build.

Every effect with text publishes a `ScenarioDialog { event, text, coins, continent, turn }`; `coins` is
what the effect paid the player (a dividend, a contract reward), counted as revenue on the statement.

## Money rule

A scenario never makes or destroys coins. Every coin an effect pays comes from someone who holds it
(households, a company, a group, a nation's reserve) and is capped at what they hold; every coin
it takes goes to someone. The player's start coins come from the start continent's households
(the start node's first); a scenario whose `start_coins` they cannot cover does not build. The
protected opening pays 1.5 times the first purchase's price for that purchase's units only; more
units sold in the same sale fetch the market's offer.

## Objectives

| Goal | Done when |
|---|---|
| `NetProfit(coins)` | the statement's net profit reaches it |
| `ContractDone("id")` | the contract was delivered |
| `Holds(good, quantity)` | the cart holds that many |
| `BackAt(node, after)` | the player stands at `node` after the event `after` has fired (a return trip) |

Checked at the end of each week; once done an objective stays done (`ObjectiveDone` event).

## Contract dialog placeholders

Contract numbers belong in `OfferContract` fields, not duplicated in dialog strings.
Substitution is deterministic and uses plain decimal integers without grouping separators.

| Placeholder | Value |
| --- | --- |
| `{reward}` | The contract's promised `reward`, unchanged by a short payment |
| `{paid}` | The amount actually paid by delivery, including a partial payment or zero |
| `{good}` | The contract's base good key, e.g. `wheat` or `g6_0` |
| `{qty}` | The contract's `quantity` |
| `{dest}` | The destination node key from the contract's `node` |
| `{deadline}` | The scenario-relative, 1-based `deadline_week` |

`OfferContract.text` supports all of these except `{paid}`: no delivery has happened
at the time of an offer. Both the offer event and the offered-contract view receive
the filled text. For example:

```ron
OfferContract(id: "grain", good: "wheat", quantity: 10, node: "aur_n1_r1_c2",
    reward: 1200, deadline_week: 12,
    text: "Deliver {qty} crates of {good} to {dest} by week {deadline}. Promised {reward} coins.")
```

A `Dialog` effect in an event triggered by `ContractDone("grain")` supports all six
placeholders, bound to that exact contract id. Its referenced contract must have
exactly one `OfferContract` definition in the scenario. Multiple contracts retain
their own actual payments; rendering a dialog does not pay money again.

```ron
(id: "delivered", trigger: ContractDone("grain"), fallback_week: 30,
    effects: [Dialog("Delivery complete. Promised {reward} coins; you received {paid} coins.")])
```

The normal delivery runs before `ContractDone` events, so `{paid}` is the return
value of the actual clamped payment, not the promise. If the event fires early via
its existing fallback, values still come from the contract definition and `{paid}`
is zero until delivery. Choose a fallback outside the playable weeks for a message
that should only celebrate delivery, as S1 does. Fallback/expiry rules are unchanged.

Unknown names, empty/nested/unmatched braces, `{paid}` in an offer, or contract
placeholders in other effect text fail scenario validation. This is not a general
template language: no brace escapes, formatting options or recursive expansion.
Intro/title/objective strings are not substituted. Inserted good/node keys are
copied literally. The existing separate payment event and UI receipt remain intact;
the delivery dialog now also states both amounts, including a zero payment.

## Scenario rules

`warmup_weeks` runs the world before the player is placed, so markets hold stock and traders hold
money at week 0 (the full-scale world starts empty). `protected_opening` (spec 5.3): the first
purchase is noted (good and unit price); the first sale of that good is paid at least 1.5 times that
price, wherever it is sold. The opening ends with the player's first sale of anything.
`licensed: [(good: "g19_0", nation: "k0n1")]` makes those base goods need that nation's licence to be
sold there from the start (the shipped data licenses none). `licences_anywhere: true` sells goods and
trade licences at every town of a nation, not only at its core towns.

A scenario world has no random world events, financial events or route encounters (spec 5.2): only the
script happens. Free mode never reads these files, so its results stay equal to the C# reference.
