//! Immutable static definitions. String keys are the stable identity in data files; dense indices
//! are assigned at load time in file order. Mirrors the C# `Definitions.cs`.
use crate::csv::CsvEnum;
use fes_core::{Fx, GoodId, IndustryTier, QualityGrade, WorldLevel};

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GoodCategoryDef {
    pub index: i32,
    pub key: String,
}

/// What every grade of a base good shares.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct GoodTraits {
    pub weight: i32,
    pub size: i32,
    pub requires_license: bool,
    pub is_luxury: bool,
    pub is_semi: bool,
    pub made_to_order: bool,
    pub home_output: i32,
    pub is_preserved: bool,
}

/// One tradable item: a base good at one quality grade.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GoodDef {
    pub id: GoodId,
    pub base_key: String,
    pub category_index: i32,
    /// Reference price in the smallest coin unit, already scaled by the grade multiplier.
    pub base_price: i32,
    /// Weekly demand per village-sized market before level weighting.
    pub base_demand: i32,
    pub traits: GoodTraits,
}

impl GoodDef {
    pub fn grade(&self) -> QualityGrade {
        self.id.grade()
    }

    /// `base_key@grade` with the grade in lower case.
    pub fn key(&self) -> String {
        format!("{}@{}", self.base_key, self.grade().name().to_lowercase())
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct RecipeLine {
    pub good: GoodId,
    pub quantity: i32,
}

/// Weekly inputs and outputs of one facility at level 1 and full efficiency.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct RecipeDef {
    pub index: i32,
    pub key: String,
    pub inputs: Vec<RecipeLine>,
    pub outputs: Vec<RecipeLine>,
    /// Bought when they can be: tools and seed. Without them the facility still works, at lower output.
    pub aids: Vec<RecipeLine>,
}

/// A producer type: farms and mines (primary), workshops (secondary), services (tertiary).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FacilityDef {
    pub index: i32,
    pub key: String,
    pub tier: IndustryTier,
    pub strategic: bool,
    pub core_only: bool,
    /// The key of the only continent it can stand on, or `None` for any.
    pub continent: Option<String>,
    /// Index into `GameDatabase::recipes`.
    pub recipe: usize,
    pub state_owned: bool,
}

/// What a world event scales.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ModifierTarget {
    Supply = 0,
    Demand = 1,
    Value = 2,
}

impl ModifierTarget {
    /// The C# enum name.
    pub fn name(self) -> &'static str {
        match self {
            ModifierTarget::Supply => "Supply",
            ModifierTarget::Demand => "Demand",
            ModifierTarget::Value => "Value",
        }
    }
}

impl CsvEnum for ModifierTarget {
    const TYPE_NAME: &'static str = "ModifierTarget";

    fn from_name(name: &str) -> Option<Self> {
        [Self::Supply, Self::Demand, Self::Value]
            .into_iter()
            .find(|t| t.name().eq_ignore_ascii_case(name))
    }
}

/// A random world event that scales a market's supply, demand or value.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct WorldEventDef {
    pub index: i32,
    pub key: String,
    pub scope_level: WorldLevel,
    /// Affected category index, or -1 for all categories.
    pub category_index: i32,
    pub target: ModifierTarget,
    pub multiplier: Fx,
    pub duration_turns: i32,
    /// Chance per eligible node per turn, in permille.
    pub chance_permille: i32,
}

/// A terrain tag makes a base good, or a whole category, a specialty.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SpecialtyRule {
    pub tag: String,
    /// Base good indices the tag makes specialties, sorted.
    pub base_goods: Vec<i32>,
}

/// Which goods the markets of one world level carry.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct MarketProfileRule {
    pub level: WorldLevel,
    /// Category index, or -1 for all categories.
    pub category_index: i32,
    /// Bit i set = grade i carried.
    pub grade_mask: i32,
}

impl MarketProfileRule {
    pub fn matches(&self, good: &GoodDef) -> bool {
        (self.category_index < 0 || self.category_index == good.category_index)
            && (self.grade_mask & (1 << good.grade() as i32)) != 0
    }
}
