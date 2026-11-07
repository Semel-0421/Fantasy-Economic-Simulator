//! Lays out the sparse market cells from the per-level market profiles (C# `MarketLayoutBuilder`).
//! A node's specialties are always carried (DEC-016), in every grade its level carries for any good,
//! and anchored lower.
use super::markets::MarketStore;
use crate::error::SimError;
use crate::world::{NodeSpecialties, NodeTable};
use fes_core::{Fx, GoodId, QualityGrade, WorldLevel};
use fes_data::database::GameDatabase;
use fes_data::defs::GoodDef;
use std::collections::BTreeSet;

pub struct MarketLayoutBuilder;

impl MarketLayoutBuilder {
    pub fn build(
        nodes: &NodeTable,
        db: &GameDatabase,
        specialties: &NodeSpecialties,
    ) -> Result<MarketStore, SimError> {
        let goods_per_level = goods_per_level(db);
        let mut grade_mask = [0i32; WorldLevel::COUNT];
        for rule in &db.market_profiles {
            grade_mask[rule.level as usize] |= rule.grade_mask;
        }
        let mut goods_per_node: Vec<Vec<i32>> = Vec::with_capacity(nodes.count());
        let mut cells: i64 = 0;
        for n in 0..nodes.count() {
            let level = usize::from(nodes.level[n]);
            let mut goods = goods_per_level[level].clone();
            if specialties.count[n] > 0 {
                goods = with_specialties(&goods, specialties, n, grade_mask[level]);
            }
            cells += goods.len() as i64;
            goods_per_node.push(goods);
        }
        if cells > i64::from(i32::MAX) {
            return Err(SimError::Invalid(
                "Market cell count exceeds int range.".into(),
            ));
        }
        let mut store = MarketStore::new(nodes.count(), cells as usize, db.good_count());
        fill_goods(&mut store, db);
        let mut cell = 0;
        for (n, goods) in goods_per_node.iter().enumerate() {
            store.market_cell_start[n] = cell as i32;
            store.market_cell_count[n] = goods.len() as i32;
            for &g in goods {
                let specialty = specialties.is_specialty(n as i32, GoodId(g).base_index());
                fill_cell(&mut store, cell, g, db, specialty);
                cell += 1;
            }
        }
        Ok(store)
    }
}

/// Every market of a level carries the same goods, so resolve the profile once per level.
fn goods_per_level(db: &GameDatabase) -> Vec<Vec<i32>> {
    (0..WorldLevel::COUNT)
        .map(|l| {
            db.goods
                .iter()
                .filter(|good| carried_at(db, good, l))
                .map(|good| good.id.0)
                .collect()
        })
        .collect()
}

fn carried_at(db: &GameDatabase, good: &GoodDef, level: usize) -> bool {
    db.market_profiles
        .iter()
        .any(|rule| rule.level as usize == level && rule.matches(good))
}

fn with_specialties(
    goods: &[i32],
    specialties: &NodeSpecialties,
    node: usize,
    grade_mask: i32,
) -> Vec<i32> {
    let mut merged: BTreeSet<i32> = goods.iter().copied().collect();
    let start = specialties.start[node] as usize;
    for k in start..start + specialties.count[node] as usize {
        for g in 0..QualityGrade::COUNT {
            if grade_mask & (1 << g) == 0 {
                continue;
            }
            let grade = QualityGrade::from_u8(g as u8).expect("grade index");
            merged.insert(GoodId::from(specialties.base_good[k], grade).0);
        }
    }
    merged.into_iter().collect()
}

fn fill_goods(store: &mut MarketStore, db: &GameDatabase) {
    for (g, good) in db.goods.iter().enumerate().take(db.good_count()) {
        store.good_base_price[g] = good.base_price;
        // Made-to-order goods are ordered instead of bought at demand (DEC-085).
        store.good_base_demand[g] = if good.traits.made_to_order {
            0
        } else {
            good.base_demand
        };
        store.good_category[g] = good.category_index;
        store.good_weight[g] = good.traits.weight;
        store.good_size[g] = good.traits.size;
    }
}

fn fill_cell(store: &mut MarketStore, cell: usize, good: i32, db: &GameDatabase, specialty: bool) {
    let anchor = if specialty {
        db.config.specialty_price_multiplier
    } else {
        Fx::ONE
    };
    let base = i64::from(db.goods[good as usize].base_price);
    let price = anchor.mul_int(base).max(1) as i32;
    store.cell_good[cell] = good;
    store.cell_specialty[cell] = u8::from(specialty);
    store.price[cell] = price;
    store.prev_price[cell] = price;
}
