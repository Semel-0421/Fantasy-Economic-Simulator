//! Service checks for the step-replay harness (behind the `testing` feature). The C# step records
//! hold only saved sections, so unsaved services cannot be compared with C# directly. Instead, every
//! service that C# rebuilds on load from saved state is compared with the same service freshly
//! rebuilt by the Rust load path (`on_loaded`) from the recorded state.
use crate::agents::lod_system::LodPolicyService;
use crate::agents::production::fill_subsidy_rates;
use crate::agents::{DevelopmentState, LodState, PolicyState};
use crate::context::SimContext;
use crate::economy::modifier_fold::fold_multipliers;
use crate::economy::{MarketFlows, MarketModifierTable, MarketStore};
use crate::world::{NodeBanks, NodeTable, NodeTolls};

/// One derivable service.
#[derive(Clone, Copy, Debug)]
pub struct ServiceCheck {
    /// Reported as the key of a `StepResult::Different`.
    pub label: &'static str,
    /// The system that writes the service each turn.
    pub writer: &'static str,
    /// Whether the Rust load path rebuilds it, so it is valid right after the load.
    pub rebuilt_on_load: bool,
    /// Saved sections it is derived from: a record that changes one makes the service stale (C#
    /// keeps it stale too) until `writer` runs again.
    pub sources: &'static [&'static str],
    /// Index of the first element where the live service differs from a fresh rebuild.
    pub differ: fn(&SimContext) -> Option<usize>,
}

/// The services C# rebuilds on load that ported systems read (see `plan3-audit.md`).
pub fn service_checks() -> Vec<ServiceCheck> {
    vec![
        ServiceCheck {
            label: "service:LodState.detail",
            writer: "agents.lod",
            rebuilt_on_load: true,
            sources: &["agents.player", "agents.facilities", "agents.merchants"],
            differ: lod_differs,
        },
        ServiceCheck {
            label: "service:MarketFlows.multipliers",
            writer: "economy.modifier_fold",
            rebuilt_on_load: true,
            sources: &["economy.modifiers"],
            differ: multipliers_differ,
        },
        ServiceCheck {
            label: "service:NodeTolls",
            writer: "agents.policy",
            rebuilt_on_load: true,
            sources: &["agents.policy"],
            differ: tolls_differ,
        },
        ServiceCheck {
            label: "service:DevelopmentState.subsidy_rate",
            writer: "agents.production",
            rebuilt_on_load: false,
            sources: &["agents.development"],
            differ: subsidy_rate_differs,
        },
    ]
}

fn first_difference<T: PartialEq>(a: &[T], b: &[T]) -> Option<usize> {
    a.iter()
        .zip(b)
        .position(|(x, y)| x != y)
        .or((a.len() != b.len()).then(|| a.len().min(b.len())))
}

/// `LodSystem::on_loaded` -> `evaluate_lod`.
fn lod_differs(ctx: &SimContext) -> Option<usize> {
    let live = &ctx.services.get::<LodState>().detail;
    let mut fresh = vec![0u8; live.len()];
    ctx.services
        .get::<LodPolicyService>()
        .0
        .evaluate(ctx, &mut fresh);
    first_difference(live, &fresh)
}

/// `ModifierFoldSystem::on_loaded` -> `rebuild_flows`.
fn multipliers_differ(ctx: &SimContext) -> Option<usize> {
    let flows = ctx.services.get::<MarketFlows>();
    let fresh = fold_multipliers(
        flows.category_count,
        ctx.services.get::<NodeTable>(),
        ctx.world.get::<MarketModifierTable>().active(),
    );
    first_difference(&flows.multipliers, &fresh)
}

/// C# `PolicySystem.ApplyTolls` from the saved toll steps: tariffs, then surcharges.
fn tolls_differ(ctx: &SimContext) -> Option<usize> {
    let live = ctx.services.get::<NodeTolls>();
    let mut fresh = live.clone();
    fresh.apply_steps(
        ctx.services.get::<NodeTable>(),
        ctx.services.get::<NodeBanks>(),
        &ctx.world.get::<PolicyState>().toll_step,
        ctx.services.database.config.policy_step,
    );
    first_difference(&live.tariff, &fresh.tariff).or_else(|| {
        first_difference(&live.specialty_surcharge, &fresh.specialty_surcharge)
            .map(|i| i + live.tariff.len())
    })
}

/// C# `SubsidySystem.OnLoaded` -> `RefreshSubsidyRates`; Rust refreshes it in production.
fn subsidy_rate_differs(ctx: &SimContext) -> Option<usize> {
    let development = ctx.world.get::<DevelopmentState>();
    let mut fresh = vec![0i64; development.subsidy_rate.len()];
    fill_subsidy_rates(
        &mut fresh,
        &development.subsidies,
        ctx.world.get::<MarketStore>().good_base_price.len(),
        ctx.services.database.config.subsidy_rate.raw(),
    );
    first_difference(&development.subsidy_rate, &fresh)
}

/// Tracks which services are valid to compare during one replay.
pub(crate) struct ServiceTracker {
    checks: Vec<ServiceCheck>,
    fresh: Vec<bool>,
}

impl ServiceTracker {
    pub(crate) fn new(checks: Vec<ServiceCheck>) -> Self {
        let fresh = checks.iter().map(|c| c.rebuilt_on_load).collect();
        Self { checks, fresh }
    }

    /// Updates freshness after the record `label` changed `changed`: the writer makes its service
    /// fresh when it ran in Rust (stale when its recorded changes were only applied), any other
    /// change to a source section makes it stale.
    pub(crate) fn note(&mut self, label: &str, changed: &[&str], ran_in_rust: bool) {
        let system = label.strip_prefix("system:");
        for (c, fresh) in self.checks.iter().zip(&mut self.fresh) {
            if system == Some(c.writer) {
                *fresh = ran_in_rust;
            } else if changed.iter().any(|k| c.sources.contains(k)) {
                *fresh = false;
            }
        }
    }

    /// The first fresh service that differs from its rebuild: (label, index).
    pub(crate) fn first_difference(&self, ctx: &SimContext) -> Option<(&'static str, usize)> {
        self.checks
            .iter()
            .zip(&self.fresh)
            .filter(|(_, fresh)| **fresh)
            .find_map(|(c, _)| (c.differ)(ctx).map(|i| (c.label, i)))
    }
}
