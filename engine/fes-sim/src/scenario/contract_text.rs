//! Contract-only substitutions. One pass keeps inserted data from becoming another template.
use super::defs::{Effect, EventDef, ScenarioDef, Trigger};

const NAMES: [&str; 6] = ["reward", "paid", "good", "qty", "dest", "deadline"];

fn fill(
    text: &str,
    mut value: impl FnMut(&str) -> Result<String, String>,
) -> Result<String, String> {
    let mut out = String::new();
    let mut rest = text;
    while let Some(start) = rest.find('{') {
        let prefix = &rest[..start];
        if prefix.contains('}') {
            return Err("Unmatched '}' in dialog text.".into());
        }
        out.push_str(prefix);
        rest = &rest[start + 1..];
        let end = rest.find('}').ok_or("Unmatched '{' in dialog text.")?;
        out.push_str(&value(&rest[..end])?);
        rest = &rest[end + 1..];
    }
    if rest.contains('}') {
        return Err("Unmatched '}' in dialog text.".into());
    }
    out.push_str(rest);
    Ok(out)
}

pub(super) fn validate(def: &ScenarioDef) -> Result<(), String> {
    for event in &def.events {
        validate_event(def, event)?;
    }
    Ok(())
}

fn validate_event(def: &ScenarioDef, event: &EventDef) -> Result<(), String> {
    for effect in &event.effects {
        let text = match effect {
            Effect::Dialog(text)
            | Effect::OfferContract { text, .. }
            | Effect::PriceShock { text, .. }
            | Effect::CompanyNews { text, .. }
            | Effect::CompanyOrders { text, .. }
            | Effect::Dividend { text, .. }
            | Effect::BondIssue { text, .. }
            | Effect::Coupon { text, .. }
            | Effect::LicenceOffer { text, .. } => text,
            Effect::Raid { .. } => continue,
        };
        let offer = matches!(effect, Effect::OfferContract { .. });
        let contract = match (&event.trigger, effect) {
            (Trigger::ContractDone(id), Effect::Dialog(_)) => Some(id),
            _ => None,
        };
        if let Some(id) = contract.filter(|_| text.contains('{')) {
            let count = def
                .events
                .iter()
                .flat_map(|e| &e.effects)
                .filter(|e| matches!(e, Effect::OfferContract { id: found, .. } if found == id))
                .count();
            if count != 1 {
                return Err(format!(
                    "Event '{}' needs one contract definition for '{id}'.",
                    event.id
                ));
            }
        }
        fill(text, |name| {
            if !NAMES.contains(&name) {
                return Err(format!("Unknown contract placeholder '{{{name}}}'."));
            }
            if !offer && contract.is_none() {
                return Err(format!("Placeholder '{{{name}}}' needs a contract dialog."));
            }
            if name == "paid" && offer {
                return Err(
                    "Placeholder '{paid}' is only available in ContractDone dialogs.".into(),
                );
            }
            Ok(String::new())
        })
        .map_err(|error| format!("Event '{}': {error}", event.id))?;
    }
    Ok(())
}

pub(super) fn render(text: &str, contract: &Effect, paid: i64) -> String {
    let Effect::OfferContract {
        good,
        quantity,
        node,
        reward,
        deadline_week,
        ..
    } = contract
    else {
        unreachable!("contract text uses an OfferContract definition");
    };
    fill(text, |name| {
        Ok(match name {
            "reward" => reward.to_string(),
            "paid" => paid.to_string(),
            "good" => good.clone(),
            "qty" => quantity.to_string(),
            "dest" => node.clone(),
            "deadline" => deadline_week.to_string(),
            _ => unreachable!("contract placeholder was validated"),
        })
    })
    .expect("contract text was validated")
}

#[cfg(test)]
mod tests {
    #[test]
    fn inserted_values_are_not_parsed_again() {
        assert_eq!(
            super::fill("Value {good}; {reward}.", |name| Ok(if name == "good" {
                "{paid}"
            } else {
                "7"
            }
            .into()))
            .unwrap(),
            "Value {paid}; 7."
        );
    }
}
