//! All static definitions, immutable after load, matching C# `GameDatabase`.
use crate::defs::{
    FacilityDef, GoodCategoryDef, GoodDef, GoodTraits, MarketProfileRule, ModifierTarget,
    RecipeDef, RecipeLine, SpecialtyRule, WorldEventDef,
};
use crate::map::MapDef;
use crate::{try_parse_enum_name, CsvTable, DataError, DataSource, SimulationConfig};
use fes_core::det::det_hash::{combine, of};
use fes_core::{Fx, GoodId, IndustryTier, QualityGrade, WorldLevel};
use std::collections::{BTreeMap, BTreeSet, HashMap, HashSet};

pub const GOODS_FILE: &str = "goods.csv";
pub const RECIPES_FILE: &str = "recipes.csv";
pub const FACILITIES_FILE: &str = "facilities.csv";
pub const EVENTS_FILE: &str = "events.csv";
pub const MARKET_PROFILES_FILE: &str = "market_profiles.csv";
pub const CONFIG_FILE: &str = "config.csv";
pub const SPECIALTIES_FILE: &str = "specialties.csv";

const FOOD_CATEGORY: &str = "provisions";
const DRINK_CATEGORY: &str = "drink";
const PRESERVED_CATEGORY: &str = "preserved";

fn bad(message: String) -> DataError {
    DataError::Format(message)
}

/// Hash of one file: line endings and leading byte order marks are normalized first, so the same
/// data checked out with CRLF or LF yields the same hash.
pub fn hash_text(file: &str, text: &str) -> u64 {
    let normalized = text.replace("\r\n", "\n").replace('\r', "\n");
    let normalized = normalized.trim_start_matches('\u{feff}');
    combine(u64::from(of(file)), u64::from(of(normalized)))
}

/// The hash a save header carries: the definitions plus the map.
pub fn data_hash(db: &GameDatabase, map: &MapDef) -> u64 {
    combine(db.content_hash(), map.compute_hash())
}

#[derive(Debug)]
pub struct GameDatabase {
    pub config: SimulationConfig,
    pub categories: Vec<GoodCategoryDef>,
    /// Indexed by `GoodId.0`.
    pub goods: Vec<GoodDef>,
    pub recipes: Vec<RecipeDef>,
    pub facilities: Vec<FacilityDef>,
    pub events: Vec<WorldEventDef>,
    pub market_profiles: Vec<MarketProfileRule>,
    specialties: BTreeMap<String, SpecialtyRule>,
    category_by_key: HashMap<String, i32>,
    base_good_by_key: HashMap<String, i32>,
    content_hash: u64,
}

/// Reads data files in order and folds their text into the content hash.
struct Reader<'a> {
    source: &'a dyn DataSource,
    hash: u64,
}

impl Reader<'_> {
    fn read(
        &mut self,
        file: &str,
        required: bool,
        hash_the_text: bool,
    ) -> Result<Option<CsvTable>, DataError> {
        if !self.source.exists(file) {
            if required {
                return Err(DataError::NotFound(format!(
                    "Required data file '{file}' is missing."
                )));
            }
            return Ok(None);
        }
        let text = self.source.read_text(file)?;
        if hash_the_text {
            self.hash = combine(self.hash, hash_text(file, &text));
        }
        CsvTable::parse(file, &text).map(Some)
    }
}

impl GameDatabase {
    /// Loads every definition file, config first (hashed by its effective values, not its text).
    pub fn load(source: &dyn DataSource) -> Result<GameDatabase, DataError> {
        let mut reader = Reader { source, hash: 0 };
        let config_table = reader.read(CONFIG_FILE, false, false)?;
        let config = SimulationConfig::load(config_table.as_ref())?;
        reader.hash = combine(reader.hash, config.compute_hash());
        let mut db = GameDatabase {
            config,
            categories: Vec::new(),
            goods: Vec::new(),
            recipes: Vec::new(),
            facilities: Vec::new(),
            events: Vec::new(),
            market_profiles: Vec::new(),
            specialties: BTreeMap::new(),
            category_by_key: HashMap::new(),
            base_good_by_key: HashMap::new(),
            content_hash: 0,
        };
        let goods = reader.read(GOODS_FILE, true, true)?;
        db.load_goods(&goods.expect("required file is present"))?;
        let recipes = reader.read(RECIPES_FILE, false, true)?;
        let facilities = reader.read(FACILITIES_FILE, false, true)?;
        db.load_recipes_and_facilities(recipes.as_ref(), facilities.as_ref())?;
        let events = reader.read(EVENTS_FILE, false, true)?;
        db.load_events(events.as_ref())?;
        let profiles = reader.read(MARKET_PROFILES_FILE, true, true)?;
        db.load_market_profiles(&profiles.expect("required file is present"))?;
        let specialties = reader.read(SPECIALTIES_FILE, false, true)?;
        db.load_specialties(specialties.as_ref())?;
        db.content_hash = reader.hash;
        Ok(db)
    }

    /// Hash of every definition file's text plus the effective config values (defaults included).
    /// Saves refuse to load against different data.
    pub fn content_hash(&self) -> u64 {
        self.content_hash
    }

    pub fn good_count(&self) -> usize {
        self.goods.len()
    }

    pub fn base_good_count(&self) -> usize {
        self.base_good_by_key.len()
    }

    /// The specialties a terrain tag gives; `None` for a tag specialties.csv does not list.
    pub fn specialty(&self, tag: &str) -> Option<&SpecialtyRule> {
        self.specialties.get(tag)
    }

    /// Every specialty rule, ordered by tag.
    pub fn specialties(&self) -> impl Iterator<Item = &SpecialtyRule> {
        self.specialties.values()
    }

    pub fn good(&self, id: GoodId) -> &GoodDef {
        &self.goods[id.0 as usize]
    }

    pub fn try_get_good(&self, base_key: &str, grade: QualityGrade) -> Option<GoodId> {
        self.base_good_by_key
            .get(base_key)
            .map(|base| GoodId::from(*base, grade))
    }

    fn load_goods(&mut self, t: &CsvTable) -> Result<(), DataError> {
        for r in 0..t.row_count() {
            let key = t.get_string(r, "key")?;
            let category = t.get_string(r, "category")?;
            let category_index = self.category_index_of(&category);
            if self.base_good_by_key.contains_key(&key) {
                return Err(bad(format!("goods.csv: duplicate key '{key}'.")));
            }
            let base_index = self.base_good_by_key.len() as i32;
            self.base_good_by_key.insert(key.clone(), base_index);
            let base_price = t.get_long(r, "base_price")?;
            let base_demand = t.get_int_or(r, "base_demand", 0)?;
            let traits = read_traits(t, r)?;
            let food = (category == FOOD_CATEGORY
                || category == DRINK_CATEGORY
                || category == PRESERVED_CATEGORY)
                && self.config.food_grade_demand[QualityGrade::Mid as usize].raw() > 0;
            for grade in QualityGrade::ALL {
                let g = *grade as usize;
                let price = self.grade_price(base_price, g, food);
                self.goods.push(GoodDef {
                    id: GoodId::from(base_index, *grade),
                    base_key: key.clone(),
                    category_index,
                    base_price: price.min(i64::from(i32::MAX)) as i32,
                    base_demand: self.grade_demand(base_demand, g, food),
                    traits,
                });
            }
        }
        Ok(())
    }

    fn category_index_of(&mut self, category: &str) -> i32 {
        if let Some(index) = self.category_by_key.get(category) {
            return *index;
        }
        let index = self.categories.len() as i32;
        self.category_by_key.insert(category.to_string(), index);
        self.categories.push(GoodCategoryDef {
            index,
            key: category.to_string(),
        });
        index
    }

    fn grade_price(&self, base_price: i64, grade: usize, food: bool) -> i64 {
        let multiplier = if food {
            self.config.food_grade_price[grade]
        } else {
            self.config.grade_price_multiplier[grade]
        };
        multiplier.mul_int(base_price).max(1)
    }

    fn grade_demand(&self, base_demand: i32, grade: usize, food: bool) -> i32 {
        let mid = QualityGrade::Mid as usize;
        if !food {
            return base_demand;
        }
        let shares = &self.config.food_grade_demand;
        if grade != mid {
            return rounded_share(base_demand, shares[grade]);
        }
        let mut other = 0i32;
        for (g, share) in shares.iter().enumerate() {
            if g != mid {
                other = other.wrapping_add(rounded_share(base_demand, *share));
            }
        }
        base_demand.wrapping_sub(other).max(0)
    }

    fn load_recipes_and_facilities(
        &mut self,
        recipes: Option<&CsvTable>,
        facilities: Option<&CsvTable>,
    ) -> Result<(), DataError> {
        let recipe_by_key = self.load_recipes(recipes)?;
        if let Some(facilities) = facilities {
            self.load_facilities(facilities, &recipe_by_key)?;
        }
        Ok(())
    }

    fn load_recipes(
        &mut self,
        recipes: Option<&CsvTable>,
    ) -> Result<HashMap<String, usize>, DataError> {
        let mut by_key: HashMap<String, RecipeRows> = HashMap::new();
        let mut order: Vec<String> = Vec::new();
        if let Some(recipes) = recipes {
            for r in 0..recipes.row_count() {
                self.read_recipe_row(recipes, r, &mut by_key, &mut order)?;
            }
        }
        let mut recipe_by_key = HashMap::new();
        for key in order {
            let rows = by_key.remove(&key).expect("ordered key has rows");
            recipe_by_key.insert(key.clone(), self.recipes.len());
            self.recipes.push(RecipeDef {
                index: self.recipes.len() as i32,
                key,
                inputs: rows.inputs,
                outputs: rows.outputs,
                aids: rows.aids,
            });
        }
        Ok(recipe_by_key)
    }

    fn read_recipe_row(
        &self,
        recipes: &CsvTable,
        r: usize,
        by_key: &mut HashMap<String, RecipeRows>,
        order: &mut Vec<String>,
    ) -> Result<(), DataError> {
        let key = recipes.get_string(r, "recipe")?;
        if !by_key.contains_key(&key) {
            by_key.insert(key.clone(), RecipeRows::default());
            order.push(key.clone());
        }
        let lines = by_key.get_mut(&key).expect("just inserted");
        let good_key = recipes.get_string(r, "good")?;
        let grade: QualityGrade = recipes.get_enum(r, "grade")?;
        let good = self.try_get_good(&good_key, grade).ok_or_else(|| {
            bad(format!(
                "recipes.csv row {}: unknown good '{good_key}'.",
                r + 1
            ))
        })?;
        let line = RecipeLine {
            good,
            quantity: recipes.get_int(r, "qty")?,
        };
        if line.quantity < 0 {
            return Err(bad(format!(
                "recipes.csv row {}: qty must not be negative.",
                r + 1
            )));
        }
        let role = recipes.get_string(r, "role")?;
        let role_lines = match role.as_str() {
            "in" => &mut lines.inputs,
            "out" => &mut lines.outputs,
            "aid" => &mut lines.aids,
            _ => {
                return Err(bad(format!(
                    "recipes.csv row {}: role must be 'in', 'aid' or 'out'.",
                    r + 1
                )))
            }
        };
        if role_lines.iter().any(|l| l.good == good) {
            return Err(bad(format!(
                "recipes.csv row {}: '{good_key}' is listed twice as '{role}' in recipe '{key}'.",
                r + 1
            )));
        }
        role_lines.push(line);
        Ok(())
    }

    fn load_facilities(
        &mut self,
        t: &CsvTable,
        recipe_by_key: &HashMap<String, usize>,
    ) -> Result<(), DataError> {
        let mut keys = HashSet::new();
        for r in 0..t.row_count() {
            let key = t.get_string(r, "key")?;
            let recipe_key = t.get_string(r, "recipe")?;
            let recipe = *recipe_by_key.get(&recipe_key).ok_or_else(|| {
                bad(format!(
                    "facilities.csv row {}: unknown recipe '{recipe_key}'.",
                    r + 1
                ))
            })?;
            if !keys.insert(key.clone()) {
                return Err(bad(format!("facilities.csv: duplicate key '{key}'.")));
            }
            let tier: IndustryTier = t.get_enum(r, "tier")?;
            let state_owned = t.get_int_or(r, "state_owned", 0)? != 0;
            let strategic = t.get_int_or(r, "strategic", 0)? != 0;
            let core_only = t.get_int_or(r, "core_only", 0)? != 0;
            let continent = t.get_string_or(r, "continent", "")?;
            self.facilities.push(FacilityDef {
                index: self.facilities.len() as i32,
                key,
                tier,
                strategic,
                core_only,
                continent: (!continent.is_empty()).then_some(continent),
                recipe,
                state_owned,
            });
        }
        Ok(())
    }

    fn load_events(&mut self, t: Option<&CsvTable>) -> Result<(), DataError> {
        let Some(t) = t else { return Ok(()) };
        for r in 0..t.row_count() {
            let def = WorldEventDef {
                index: self.events.len() as i32,
                key: t.get_string(r, "key")?,
                scope_level: t.get_enum(r, "level")?,
                category_index: self.category_or_all(t, r)?,
                target: t.get_enum::<ModifierTarget>(r, "target")?,
                multiplier: Fx::from_permille(t.get_long(r, "mul_permille")?),
                duration_turns: t.get_int(r, "duration")?,
                chance_permille: t.get_int(r, "chance_permille")?,
            };
            if def.duration_turns < 1 {
                return Err(bad(format!(
                    "events.csv row {}: duration must be >= 1.",
                    r + 1
                )));
            }
            // Value modifiers are read only when a market reprices, so they must outlast the
            // slowest tier they cover.
            let slowest = self.config.tier_cadences[def.scope_level as usize..]
                .iter()
                .fold(1, |slowest, c| slowest.max(c.period));
            if def.target == ModifierTarget::Value && def.duration_turns < slowest {
                return Err(bad(format!(
                    "events.csv row {}: Value event '{}' lasts {} turns but its markets reprice every {slowest}.",
                    r + 1,
                    def.key,
                    def.duration_turns
                )));
            }
            self.events.push(def);
        }
        Ok(())
    }

    fn load_market_profiles(&mut self, t: &CsvTable) -> Result<(), DataError> {
        for r in 0..t.row_count() {
            let mask = grade_mask(t, r)?;
            let level: WorldLevel = t.get_enum(r, "level")?;
            let category_index = self.category_or_all(t, r)?;
            self.market_profiles.push(MarketProfileRule {
                level,
                category_index,
                grade_mask: mask,
            });
        }
        Ok(())
    }

    /// `tag,category,good`: an empty good means every base good of the category. Rows of one tag add up.
    fn load_specialties(&mut self, t: Option<&CsvTable>) -> Result<(), DataError> {
        let Some(t) = t else { return Ok(()) };
        let mut by_tag: BTreeMap<String, BTreeSet<i32>> = BTreeMap::new();
        for r in 0..t.row_count() {
            self.read_specialty_row(t, r, &mut by_tag)?;
        }
        for (tag, goods) in by_tag {
            self.specialties.insert(
                tag.clone(),
                SpecialtyRule {
                    tag,
                    base_goods: goods.into_iter().collect(),
                },
            );
        }
        Ok(())
    }

    fn read_specialty_row(
        &self,
        t: &CsvTable,
        r: usize,
        by_tag: &mut BTreeMap<String, BTreeSet<i32>>,
    ) -> Result<(), DataError> {
        let tag = t.get_string(r, "tag")?;
        let category = t.get_string(r, "category")?;
        let category_index = *self.category_by_key.get(&category).ok_or_else(|| {
            bad(format!(
                "{} row {}: unknown category '{category}'.",
                t.name(),
                r + 1
            ))
        })?;
        let goods = by_tag.entry(tag).or_default();
        let good_key = t.get_string_or(r, "good", "")?;
        if good_key.is_empty() {
            goods.extend(self.base_goods_in(category_index));
            return Ok(());
        }
        let base_index = *self.base_good_by_key.get(&good_key).ok_or_else(|| {
            bad(format!(
                "{} row {}: unknown good '{good_key}'.",
                t.name(),
                r + 1
            ))
        })?;
        if self.goods[base_index as usize * QualityGrade::COUNT].category_index != category_index {
            return Err(bad(format!(
                "{} row {}: '{good_key}' is not in category '{category}'.",
                t.name(),
                r + 1
            )));
        }
        goods.insert(base_index);
        Ok(())
    }

    /// The base good indices of a category, in index order.
    fn base_goods_in(&self, category_index: i32) -> impl Iterator<Item = i32> + '_ {
        (0..self.base_good_count())
            .filter(move |b| self.goods[b * QualityGrade::COUNT].category_index == category_index)
            .map(|b| b as i32)
    }

    fn category_or_all(&self, t: &CsvTable, row: usize) -> Result<i32, DataError> {
        let category = t.get_string(row, "category")?;
        if category == "*" {
            return Ok(-1);
        }
        self.category_by_key.get(&category).copied().ok_or_else(|| {
            bad(format!(
                "{} row {}: unknown category '{category}'.",
                t.name(),
                row + 1
            ))
        })
    }
}

#[derive(Default)]
struct RecipeRows {
    inputs: Vec<RecipeLine>,
    outputs: Vec<RecipeLine>,
    aids: Vec<RecipeLine>,
}

/// The grades a market profile row carries: `*` or names separated by `|`.
fn grade_mask(t: &CsvTable, r: usize) -> Result<i32, DataError> {
    let grades = t.get_string(r, "grades")?;
    if grades == "*" {
        return Ok((1 << QualityGrade::COUNT) - 1);
    }
    let mut mask = 0i32;
    for g in grades.split('|') {
        let grade = try_parse_enum_name::<QualityGrade>(g.trim())
            .ok_or_else(|| bad(format!("{} row {}: unknown grade '{g}'.", t.name(), r + 1)))?;
        mask |= 1 << grade as i32;
    }
    Ok(mask)
}

fn rounded_share(amount: i32, share: Fx) -> i32 {
    let product = i64::from(amount).wrapping_mul(share.raw());
    (product.wrapping_add(Fx::ONE_RAW / 2) / Fx::ONE_RAW) as i32
}

fn read_traits(t: &CsvTable, r: usize) -> Result<GoodTraits, DataError> {
    let weight = t.get_int_or(r, "weight", 1)?;
    let size = t.get_int_or(r, "size", 1)?;
    let license = t.get_int_or(r, "requires_license", 0)?;
    let row = r + 1;
    if license != 0 && license != 1 {
        return Err(bad(format!(
            "goods.csv row {row}: requires_license must be 0 or 1."
        )));
    }
    if weight < 0 || size < 0 {
        return Err(bad(format!(
            "goods.csv row {row}: weight and size must not be negative."
        )));
    }
    let mut good_class = t.get_string_or(r, "class", "necessity")?;
    if good_class.is_empty() {
        good_class = "necessity".to_string();
    }
    if !matches!(good_class.as_str(), "necessity" | "luxury" | "semi") {
        return Err(bad(format!(
            "goods.csv row {row}: class must be necessity, semi or luxury, not '{good_class}'."
        )));
    }
    let home_output = t.get_int_or(r, "home_output", 0)?;
    if home_output < 0 {
        return Err(bad(format!(
            "goods.csv row {row}: home_output must not be negative."
        )));
    }
    Ok(GoodTraits {
        weight,
        size,
        requires_license: license == 1,
        is_luxury: good_class == "luxury",
        is_semi: good_class == "semi",
        made_to_order: t.get_int_or(r, "made_to_order", 0)? != 0,
        home_output,
        is_preserved: t.get_string(r, "category")? == PRESERVED_CATEGORY,
    })
}
