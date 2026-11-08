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
}

impl ScenarioRules {
    /// The rules every scenario starts with: active, no randomness.
    pub fn scenario() -> Self {
        Self {
            active: true,
            random_events_off: true,
            encounters_off: true,
            pending_raids: Vec::new(),
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
