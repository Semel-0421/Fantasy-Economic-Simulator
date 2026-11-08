//! The scenario switch (spec 5.2, 5.7): a service the scenario builder registers. Free mode never
//! registers it, so every guard below reads "off" there and the C# behaviour is untouched.
use crate::context::Services;

/// Teaching rules of a running scenario. Each flag is read at one site in the systems.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct ScenarioRules {
    /// A scenario is running.
    pub active: bool,
    /// The economy's own random world events and financial events do not roll.
    pub random_events_off: bool,
    /// Random route encounters (the player's trips and the merchants') do not roll.
    pub encounters_off: bool,
    /// Scripted raids waiting for the player's next trip on a route: (route, cargo loss permille).
    pub pending_raids: Vec<(i32, i32)>,
    /// The protected opening (spec 5.3): the first goods bought sell for 1.5 times what was paid,
    /// until the first sale.
    pub opening: Opening,
}

/// The protected opening's progress.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Opening {
    /// No protected opening in this scenario, or it ended with the first sale.
    #[default]
    Off,
    /// Waiting for the player's first purchase.
    Waiting,
    /// The first purchase: this good, at this price a unit.
    Bought { good: i32, price: i32 },
}

impl ScenarioRules {
    /// The rules every scenario starts with: active, no randomness.
    pub fn scenario() -> Self {
        Self {
            active: true,
            random_events_off: true,
            encounters_off: true,
            pending_raids: Vec::new(),
            opening: Opening::Off,
        }
    }
}

/// The player bought `good` at `price` a unit: the first purchase of a protected opening is noted.
pub fn note_purchase(services: &mut Services, good: i32, price: i32) {
    if let Some(r) = services.try_get_mut::<ScenarioRules>() {
        if r.active && r.opening == Opening::Waiting {
            r.opening = Opening::Bought { good, price };
        }
    }
}

/// The trader's offer for `good` during a protected opening: 1.5 times the price paid for the
/// first goods bought, when that is more than `offer`. Otherwise `offer`.
pub fn opening_offer(services: &Services, good: i32, offer: i32) -> i32 {
    match services.try_get::<ScenarioRules>() {
        Some(ScenarioRules {
            active: true,
            opening: Opening::Bought { good: g, price },
            ..
        }) if *g == good => offer.max(price.saturating_mul(3) / 2),
        _ => offer,
    }
}

/// The player sold something: the protected opening ends.
pub fn note_sale(services: &mut Services) {
    if let Some(r) = services.try_get_mut::<ScenarioRules>() {
        if matches!(r.opening, Opening::Bought { .. }) {
            r.opening = Opening::Off;
        }
    }
}

/// True when a scenario switched the random world and financial events off.
pub fn random_events_off(services: &Services) -> bool {
    services
        .try_get::<ScenarioRules>()
        .is_some_and(|r| r.active && r.random_events_off)
}

/// True when a scenario switched the random route encounters off.
pub fn encounters_off(services: &Services) -> bool {
    services
        .try_get::<ScenarioRules>()
        .is_some_and(|r| r.active && r.encounters_off)
}
