//! C# `ApplyLicensesAndSpecialties`.

use super::reader::{bad, Reader};
use super::SimulationConfig;
use crate::DataError;
use fes_core::Fx;

pub(super) fn apply(c: &mut SimulationConfig, r: &mut Reader) -> Result<(), DataError> {
    c.license_price_multiplier =
        r.permille("license.price_permille", c.license_price_multiplier)?;
    c.licensed_merchant_permille = r.int(
        "license.merchant_share_permille",
        c.licensed_merchant_permille,
    )?;
    c.international_merchant_permille = r.int(
        "license.international_merchant_share_permille",
        c.international_merchant_permille,
    )?;
    c.intercontinental_merchant_permille = r.int(
        "license.intercontinental_merchant_share_permille",
        c.intercontinental_merchant_permille,
    )?;
    c.international_license_price =
        r.long("license.international_price", c.international_license_price)?;
    c.intercontinental_license_price = r.long(
        "license.intercontinental_price",
        c.intercontinental_license_price,
    )?;
    c.toll_rate = r.permille("toll.tariff_permille", c.toll_rate)?;
    c.toll_specialty_surcharge = r.permille(
        "toll.specialty_surcharge_permille",
        c.toll_specialty_surcharge,
    )?;
    c.specialty_output_multiplier =
        r.permille("specialty.output_permille", c.specialty_output_multiplier)?;
    c.specialty_grade_up_chance =
        r.permille("specialty.grade_up_permille", c.specialty_grade_up_chance)?;
    c.specialty_price_multiplier =
        r.permille("specialty.price_permille", c.specialty_price_multiplier)?;
    if c.specialty_grade_up_chance.raw() < 0 || c.specialty_grade_up_chance.raw() > Fx::ONE.raw() {
        return Err(bad(
            "config.csv: specialty.grade_up_permille must be within 0..1000.",
        ));
    }
    c.facility_bankrupt_weeks = r.int("facility.bankrupt_weeks", c.facility_bankrupt_weeks)?;
    Ok(())
}
