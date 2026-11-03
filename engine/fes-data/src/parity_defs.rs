//! JSON of the definitions and the map in the C# `ParityJson` shape (feature `parity`).
use crate::database::GameDatabase;
use crate::defs::{GoodDef, RecipeDef, RecipeLine};
use crate::map::MapDef;
use serde_json::{json, Value};

fn lines(values: &[RecipeLine]) -> Value {
    Value::Array(
        values
            .iter()
            .map(|l| json!({"Good": l.good.0, "Quantity": l.quantity}))
            .collect(),
    )
}

fn good(g: &GoodDef) -> Value {
    json!({
        "Id": g.id.0,
        "BaseKey": g.base_key,
        "CategoryIndex": g.category_index,
        "BasePrice": g.base_price,
        "BaseDemand": g.base_demand,
        "Weight": g.traits.weight,
        "Size": g.traits.size,
        "RequiresLicense": g.traits.requires_license,
        "IsLuxury": g.traits.is_luxury,
        "IsSemi": g.traits.is_semi,
        "MadeToOrder": g.traits.made_to_order,
        "HomeOutput": g.traits.home_output,
        "IsPreserved": g.traits.is_preserved,
        "Grade": g.grade().name(),
        "Key": g.key(),
    })
}

fn recipe(r: &RecipeDef) -> Value {
    json!({
        "Index": r.index,
        "Key": r.key,
        "Inputs": lines(&r.inputs),
        "Outputs": lines(&r.outputs),
        "Aids": lines(&r.aids),
    })
}

impl GameDatabase {
    /// The categories, goods, recipes, facilities, events, profiles and specialties. The C#
    /// database keeps specialties in a dictionary, so the caller names the tags (and their order);
    /// the fixture lists them in `specialties.csv` file order.
    pub fn to_parity_json(&self, specialty_tags: &[String]) -> Value {
        let facilities: Vec<Value> = self
            .facilities
            .iter()
            .map(|f| {
                json!({
                    "Index": f.index,
                    "Key": f.key,
                    "Tier": f.tier.name(),
                    "Strategic": f.strategic,
                    "CoreOnly": f.core_only,
                    "Continent": f.continent,
                    "Recipe": recipe(&self.recipes[f.recipe]),
                    "StateOwned": f.state_owned,
                })
            })
            .collect();
        let events: Vec<Value> = self
            .events
            .iter()
            .map(|e| {
                json!({
                    "Index": e.index,
                    "Key": e.key,
                    "ScopeLevel": e.scope_level.name(),
                    "CategoryIndex": e.category_index,
                    "Target": e.target.name(),
                    "Multiplier": e.multiplier.raw().to_string(),
                    "DurationTurns": e.duration_turns,
                    "ChancePermille": e.chance_permille,
                })
            })
            .collect();
        json!({
            "Categories": self.categories.iter()
                .map(|c| json!({"Index": c.index, "Key": c.key})).collect::<Vec<_>>(),
            "Goods": self.goods.iter().map(good).collect::<Vec<_>>(),
            "Recipes": self.recipes.iter().map(recipe).collect::<Vec<_>>(),
            "Facilities": facilities,
            "Events": events,
            "MarketProfiles": self.market_profiles.iter().map(|p| json!({
                "Level": p.level.name(),
                "CategoryIndex": p.category_index,
                "GradeMask": p.grade_mask,
            })).collect::<Vec<_>>(),
            "Specialties": specialty_tags.iter()
                .filter_map(|tag| self.specialty(tag))
                .map(|s| json!({"Tag": s.tag, "BaseGoods": s.base_goods})).collect::<Vec<_>>(),
        })
    }
}

impl MapDef {
    /// `{Nodes, Routes}`.
    pub fn to_parity_json(&self) -> Value {
        json!({
            "Nodes": self.nodes.iter().map(|n| json!({
                "Key": n.key,
                "Name": n.name,
                "Level": n.level.name(),
                "ParentKey": n.parent_key,
                "CoreKey": n.core_key,
                "Tags": n.tags,
                "TariffPermille": n.tariff_permille,
                "SpecialtySurchargePermille": n.specialty_surcharge_permille,
                "InternationalBank": n.international_bank,
                "DangerPermille": n.danger_permille,
                "Port": n.port,
                "Population": n.population,
            })).collect::<Vec<_>>(),
            "Routes": self.routes.iter().map(|r| json!({
                "FromKey": r.from_key,
                "ToKey": r.to_key,
                "TravelTurns": r.travel_turns,
                "CostPerUnit": r.cost_per_unit,
                "BaseFee": r.base_fee,
                "Terrain": r.terrain.name(),
                "Paved": r.paved,
            })).collect::<Vec<_>>(),
        })
    }
}
