//! C# `ApplyBankRatesAndFinance`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;
use fes_core::Fx;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.international_bank_reserve_share = r.permille(
        "bank.international_reserve_permille",
        c.international_bank_reserve_share,
    )?;
    c.target_reserve_ratio = r.permille("bank.target_reserve_permille", c.target_reserve_ratio)?;
    c.max_loan_rate_multiplier = r.permille(
        "bank.max_rate_multiplier_permille",
        c.max_loan_rate_multiplier,
    )?;
    if c.max_loan_rate_multiplier.raw() < Fx::ONE.raw() {
        return Err(bad(
            "config.csv: bank.max_rate_multiplier_permille must be >= 1000.",
        ));
    }
    c.npc_loan_term_weeks = r.int("bank.npc_loan_term_weeks", c.npc_loan_term_weeks)?;
    if c.npc_loan_term_weeks < 1 {
        return Err(bad("config.csv: bank.npc_loan_term_weeks must be >= 1."));
    }
    c.npc_loan_buffer_weeks = r.int("bank.npc_buffer_weeks", c.npc_loan_buffer_weeks)?;
    c.npc_loan_max_reserve_share = r.permille(
        "bank.npc_max_reserve_share_permille",
        c.npc_loan_max_reserve_share,
    )?;
    c.finance_history_quarters = r.int("finance.history_quarters", c.finance_history_quarters)?;
    if c.finance_history_quarters < 4 {
        return Err(bad("config.csv: finance.history_quarters must be >= 4."));
    }
    c.financial_event_chance_permille = r.int(
        "finance.event_chance_permille",
        c.financial_event_chance_permille,
    )?;
    c.financial_event_end_reserve_ratio = r.permille(
        "finance.event_end_reserve_permille",
        c.financial_event_end_reserve_ratio,
    )?;
    c.bank_run_drain = r.permille("finance.bank_run_drain_permille", c.bank_run_drain)?;
    c.bank_run_withdraw_share = r.permille(
        "finance.bank_run_withdraw_permille",
        c.bank_run_withdraw_share,
    )?;
    c.rate_spike_multiplier = r.permille("finance.rate_spike_permille", c.rate_spike_multiplier)?;
    c.bank_run_cover = r.permille("finance.run_cover_permille", c.bank_run_cover)?;
    Ok(())
}
