//! Goods held by carts, warehouses and merchant groups (C# `Cargo.cs`, `agents.cargo`, version 1).
use super::columns::native_table;

native_table! {
    /// One row per (holder, good) with a positive quantity.
    CargoTable, "agents.cargo", 1,
    {
        holder: i32,
        good: i32,
        quantity: i32,
    }
}

/// C# `CargoHolders.Warehouse`: the holder id of a facility's warehouse.
pub fn warehouse_holder(facility: i32) -> i32 {
    (1 << 28) + facility
}

/// A carrier's or store's limits (C# `CargoCapacity`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct CargoCapacity {
    pub max_weight: i64,
    pub max_size: i64,
}

impl CargoCapacity {
    /// How many more units of this unit weight and size fit on top of `(weight, size)`.
    pub fn max_additional(&self, load: (i64, i64), unit_weight: i32, unit_size: i32) -> i32 {
        let mut fit = i64::from(i32::MAX);
        if unit_weight > 0 {
            fit = fit.min((self.max_weight - load.0) / i64::from(unit_weight));
        }
        if unit_size > 0 {
            fit = fit.min((self.max_size - load.1) / i64::from(unit_size));
        }
        if fit < 0 {
            0
        } else {
            fit as i32
        }
    }
}

impl CargoTable {
    fn find(&self, holder: i32, good: i32) -> Option<usize> {
        (0..self.count()).find(|&r| self.holder[r] == holder && self.good[r] == good)
    }

    /// Units of `good` the holder carries.
    pub fn quantity_of(&self, holder: i32, good: i32) -> i32 {
        self.find(holder, good).map_or(0, |r| self.quantity[r])
    }

    /// Adds (positive) or removes (negative) units; a row reaching zero is removed (C# `Change`).
    pub fn change(&mut self, holder: i32, good: i32, delta: i32) {
        let row = self.find(holder, good);
        let next = row.map_or(0, |r| i64::from(self.quantity[r])) + i64::from(delta);
        assert!(
            (0..=i64::from(i32::MAX)).contains(&next),
            "cargo of holder {holder}, good {good} would become {next}"
        );
        match row {
            None if next == 0 => {}
            None => {
                self.holder.push(holder);
                self.good.push(good);
                self.quantity.push(next as i32);
            }
            Some(r) if next == 0 => self.remove_row_swap_back(r),
            Some(r) => self.quantity[r] = next as i32,
        }
    }

    /// Removes everything the holder carries (C# `Clear`, from the last row down).
    pub fn clear(&mut self, holder: i32) {
        for r in (0..self.count()).rev() {
            if self.holder[r] == holder {
                self.remove_row_swap_back(r);
            }
        }
    }

    /// Total (weight, size) the holder carries.
    pub fn load_of(&self, holder: i32, unit_weight: &[i32], unit_size: &[i32]) -> (i64, i64) {
        let (mut weight, mut size) = (0i64, 0i64);
        for r in 0..self.count() {
            if self.holder[r] != holder {
                continue;
            }
            let g = self.good[r] as usize;
            weight += i64::from(self.quantity[r]) * i64::from(unit_weight[g]);
            size += i64::from(self.quantity[r]) * i64::from(unit_size[g]);
        }
        (weight, size)
    }
}
