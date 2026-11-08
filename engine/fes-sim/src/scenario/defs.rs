//! A scenario as written in its data file (`data/scenarios/*.ron`, format in that folder's README).
//! Places and goods are named by key; `resolve` turns them into indices of a built world.

/// One scenario.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct ScenarioDef {
    pub id: String,
    pub title: String,
    /// Shown when the scenario opens, before the first week.
    #[cfg_attr(feature = "serde", serde(default))]
    pub intro: String,
    pub seed: u64,
    /// Node key where the player starts.
    pub start_node: String,
    /// Coins in the start node's currency.
    pub start_coins: i64,
    /// The scenario ends after this many weeks.
    pub weeks: i32,
    /// The first goods bought sell for 1.5 times the price paid, anywhere, until the first sale
    /// (spec 5.3).
    #[cfg_attr(feature = "serde", serde(default))]
    pub protected_opening: bool,
    /// Weeks the world runs before the player arrives, so markets hold stock at week 0.
    #[cfg_attr(feature = "serde", serde(default))]
    pub warmup_weeks: i32,
    pub objectives: Vec<ObjectiveDef>,
    pub events: Vec<EventDef>,
}

/// A target shown to the player; done once its goal holds at the end of a week.
#[derive(Clone, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct ObjectiveDef {
    pub id: String,
    pub text: String,
    pub goal: Goal,
}

#[derive(Clone, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub enum Goal {
    /// The statement's net profit reaches this many coins.
    NetProfit(i64),
    /// The scenario contract with this id was delivered.
    ContractDone(String),
    /// The cart holds at least `quantity` of the good.
    Holds { good: String, quantity: i32 },
}

/// A scripted event: fires once, when its trigger holds or at its fallback week, whichever is first.
#[derive(Clone, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub struct EventDef {
    pub id: String,
    pub trigger: Trigger,
    /// The week it fires anyway; required for every trigger except `Week`.
    #[cfg_attr(feature = "serde", serde(default))]
    pub fallback_week: Option<i32>,
    pub effects: Vec<Effect>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub enum Trigger {
    /// At the end of this week (weeks played since the start).
    Week(i32),
    /// The player stands at this node (not travelling).
    PlayerAt(String),
    /// The cart holds at least `quantity` of the good.
    Holds { good: String, quantity: i32 },
    /// The scenario contract with this id was delivered.
    ContractDone(String),
    /// Only the fallback week fires it.
    Never,
}

#[derive(Clone, Debug, PartialEq, Eq)]
#[cfg_attr(feature = "serde", derive(serde::Serialize, serde::Deserialize))]
pub enum Effect {
    /// A message to the player.
    Dialog(String),
    /// A delivery request the player may accept: bring `quantity` of `good` to `node` by
    /// `deadline_week` for `reward` coins (in the start currency).
    OfferContract {
        id: String,
        good: String,
        quantity: i32,
        node: String,
        reward: i64,
        deadline_week: i32,
        text: String,
    },
    /// The player's next trip from `from` to `to` meets robbers who take `loss_permille` of the
    /// cart unless guards ride along (insurance refunds as for any encounter).
    Raid {
        from: String,
        to: String,
        loss_permille: i32,
    },
    /// The good's category is worth `percent`% at the node's markets for `weeks` weeks (a market
    /// modifier on value, as a world event makes).
    PriceShock {
        node: String,
        good: String,
        percent: i32,
        weeks: i32,
        text: String,
    },
    /// A company's cash changes by `percent`% (its share price follows its value).
    CompanyNews {
        facility: i32,
        percent: i32,
        text: String,
    },
    /// The company pays `per_share` coins on each share the player holds, from its cash.
    Dividend {
        facility: i32,
        per_share: i64,
        text: String,
    },
    /// Announces bonds of an issuer (the bond desk's issuer number) for the player to buy.
    BondIssue { issuer: i32, text: String },
    /// Grants the licence for `good` in the nation `nation` (node key) for a year.
    LicenceOffer {
        good: String,
        nation: String,
        text: String,
    },
}

impl ScenarioDef {
    /// Checks what a world is not needed for: every trigger but `Week` has a fallback week.
    pub fn validate(&self) -> Result<(), String> {
        if self.warmup_weeks < 0 {
            return Err(format!("Scenario '{}' needs warmup_weeks >= 0.", self.id));
        }
        if self.weeks <= 0 {
            return Err(format!("Scenario '{}' needs weeks > 0.", self.id));
        }
        for e in &self.events {
            if !matches!(e.trigger, Trigger::Week(_)) && e.fallback_week.is_none() {
                return Err(format!("Event '{}' needs a fallback_week.", e.id));
            }
        }
        Ok(())
    }

    /// Parses a scenario data file (RON; an `Option` field takes a bare value).
    #[cfg(feature = "scenario")]
    pub fn parse(text: &str) -> Result<Self, String> {
        let def: Self = ron::Options::default()
            .with_default_extension(ron::extensions::Extensions::IMPLICIT_SOME)
            .from_str(text)
            .map_err(|e| e.to_string())?;
        def.validate()?;
        Ok(def)
    }
}
