//! Weekly facility production in facility order (C# `ProductionJob`, ported from the Burst job).
//! Two passes per facility: check every input's stock and the total cost, then buy, pay and produce.
use super::lines::{Lines, AID_LINE};
use crate::agents::facilities::OWNER_PLAYER;
use crate::agents::BillTable;
use crate::economy::MarketStore;
use fes_core::{DetRandom, Fx, SimDetail};

/// C# `BillHolders.Bank`.
pub const BILL_HOLDER_BANK: i32 = -1;

/// C# `BillHolders.Facility`.
pub fn bill_holder_facility(facility: i32) -> i32 {
    -3 - facility
}

/// Config and turn constants of the job.
#[derive(Clone, Copy, Debug)]
pub struct JobParams {
    pub seed: u64,
    pub turn: i32,
    pub stream: u32,
    pub grade_stream: u32,
    pub variance_permille: i32,
    pub good_count: usize,
    pub grade_up_chance_raw: i64,
    pub expansion_bonus_raw: i64,
    pub unaided_output_raw: i64,
    pub output_wage_raw: i64,
    pub spread_raw: i64,
    pub bill_producers: bool,
    pub bill_discount_raw: i64,
    pub bill_weeks: i32,
}

/// Output of a player-owned facility, stored in its warehouse after the job.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct PlayerOutput {
    pub facility: i32,
    pub good: i32,
    pub cell: i32,
    pub quantity: i32,
}

/// What the job reads and writes. Field names follow the C# job.
pub struct ProductionJob<'a> {
    pub p: JobParams,
    pub lines: &'a Lines,
    pub cell_plan: &'a [i64],
    pub good_base_price: &'a [i32],
    pub facility_node: &'a [i32],
    pub facility_owner: &'a [i32],
    pub facility_level: &'a [i32],
    pub facility_efficiency: &'a [i64],
    pub node_float: &'a [i32],
    pub float_reserve: &'a [i64],
    pub subsidy_rate: &'a [i64],
    pub node_detail: &'a [u8],
    pub modifier_mul: &'a [i64],
    pub price: &'a [i32],

    pub cell_produced: &'a mut [i64],
    pub facility_output_wage: &'a mut [i64],
    pub facility_input_cost: &'a mut [i64],
    pub facility_cash: &'a mut [i64],
    pub float: &'a mut [i64],
    pub subsidy_left: &'a mut [i64],
    pub stock: &'a mut [i32],
    pub supply: &'a mut [i32],
    pub demand: &'a mut [i32],
    pub input_bid: &'a mut [i32],
    pub bill_room: &'a mut [i64],
    pub bill_bank_room: &'a mut [i64],
    pub bill_bank_spent: &'a mut [i64],
    pub bills: &'a mut BillTable,
    pub player_output: &'a mut Vec<PlayerOutput>,
}

fn clamp(value: i64) -> i32 {
    MarketStore::clamp_to_non_negative_int(value)
}

impl ProductionJob<'_> {
    pub fn execute(&mut self) {
        for f in 0..self.facility_node.len() {
            self.run_facility(f);
        }
    }

    fn range(&self, f: usize) -> std::ops::Range<usize> {
        self.lines.start[f]..self.lines.start[f + 1]
    }

    /// Input and aid lines come first: the C# loops `k < end && LineIsInput[k] != 0`.
    fn input_range(&self, f: usize) -> std::ops::Range<usize> {
        let r = self.range(f);
        let end = r
            .clone()
            .find(|&k| self.lines.is_input[k] == 0)
            .unwrap_or(r.end);
        r.start..end
    }

    fn run_facility(&mut self, f: usize) {
        self.facility_output_wage[f] = 0;
        let node = self.facility_node[f] as usize;
        let detailed = self.node_detail[node] == SimDetail::Detailed as u8;
        let output = self.planned_output(f, detailed);
        let input_cost = self.input_cost(f, output);
        self.facility_input_cost[f] = input_cost;
        let Some(mut cost) = self.inputs_in_stock(f, output) else {
            self.bid_for_inputs(f, output, input_cost);
            return;
        };
        if cost > self.facility_cash[f] {
            return;
        }
        let (mut aided, has_aids, aid_cost) = self.try_aids(f, output, cost);
        if cost + aid_cost > self.facility_cash[f] {
            aided = false;
        }
        if aided {
            cost += aid_cost;
        }
        self.facility_cash[f] -= cost;
        self.float[self.node_float[node] as usize] += cost;
        self.make_goods(f, output, detailed, aided, has_aids && !aided);
    }

    /// Aggregate facilities use the expected output: no variance draw.
    fn planned_output(&self, f: usize, detailed: bool) -> Fx {
        let level = i64::from(self.facility_level[f]);
        let mut output = Fx::from_raw(self.facility_efficiency[f])
            * Fx::from_int(level)
            * (Fx::ONE + Fx::from_raw(self.p.expansion_bonus_raw) * Fx::from_int(level - 1));
        if detailed {
            output = output
                * DetRandom::for_key(self.p.seed, self.p.turn, self.p.stream, f as i32, 0)
                    .next_spread(self.p.variance_permille);
        }
        if self.facility_owner[f] == OWNER_PLAYER {
            return output;
        }
        if let Some(k) = self.range(f).find(|&k| self.lines.is_input[k] == 0) {
            let cell = self.lines.cell[k];
            if cell >= 0 {
                output = output * Fx::from_raw(self.cell_plan[cell as usize]);
            }
        }
        output
    }

    fn units(&self, k: usize, output: Fx) -> i64 {
        Fx::mul_int_raw(i64::from(self.lines.quantity[k]), output.raw())
    }

    fn worth(&self, k: usize, output: Fx) -> i64 {
        self.units(k, output) * i64::from(self.price[self.lines.cell[k] as usize])
    }

    fn input_cost(&self, f: usize, output: Fx) -> i64 {
        self.input_range(f)
            .filter(|&k| self.lines.cell[k] >= 0)
            .map(|k| self.worth(k, output))
            .sum()
    }

    /// The cost of the required inputs, or `None` when one is short of stock or has no market.
    fn inputs_in_stock(&self, f: usize, output: Fx) -> Option<i64> {
        let mut cost = 0;
        for k in self.input_range(f) {
            if self.lines.is_input[k] == AID_LINE {
                continue;
            }
            let cell = self.lines.cell[k];
            let need = self.units(k, output);
            if cell < 0 || i64::from(self.stock[cell as usize]) < need {
                return None;
            }
            cost += need * i64::from(self.price[cell as usize]);
        }
        Some(cost)
    }

    fn bid(&mut self, cell: usize, need: i64) {
        self.input_bid[cell] =
            clamp(i64::from(self.input_bid[cell]) + need - i64::from(self.stock[cell]));
    }

    /// What it could not buy still bids the price up, while it could pay and the output is worth
    /// the inputs (DEC-078).
    fn bid_for_inputs(&mut self, f: usize, output: Fx, input_cost: i64) {
        let output_value: i64 = self
            .range(f)
            .filter(|&k| self.lines.is_input[k] == 0 && self.lines.cell[k] >= 0)
            .map(|k| self.worth(k, output))
            .sum();
        if input_cost >= self.offer_on(output_value) || input_cost > self.facility_cash[f] {
            return;
        }
        for k in self.input_range(f) {
            let cell = self.lines.cell[k];
            if cell < 0 || self.lines.is_input[k] == AID_LINE {
                continue;
            }
            let need = self.units(k, output);
            if i64::from(self.stock[cell as usize]) < need {
                self.bid(cell as usize, need);
            }
        }
    }

    /// Tools and seed, all or none (DEC-081): `(aided, has_aids, aid_cost)`.
    fn try_aids(&mut self, f: usize, output: Fx, cost: i64) -> (bool, bool, i64) {
        let (mut aided, mut has_aids, mut aid_cost) = (true, false, 0i64);
        let may_bid = self.aids_are_worth_it(f, output, cost);
        for k in self.input_range(f) {
            if self.lines.is_input[k] != AID_LINE {
                continue;
            }
            has_aids = true;
            let cell = self.lines.cell[k];
            let need = self.units(k, output);
            let short = cell < 0 || i64::from(self.stock[cell as usize]) < need;
            if !short {
                aid_cost += need * i64::from(self.price[cell as usize]);
                continue;
            }
            aided = false;
            if cell >= 0 && may_bid {
                self.bid(cell as usize, need);
            }
        }
        (aided, has_aids, aid_cost)
    }

    fn aids_are_worth_it(&self, f: usize, output: Fx, cost: i64) -> bool {
        let (mut aid_value, mut output_worth) = (0i64, 0i64);
        for k in self.range(f) {
            if self.lines.cell[k] < 0 {
                continue;
            }
            let worth = self.worth(k, output);
            if self.lines.is_input[k] == AID_LINE {
                aid_value += worth;
            } else if self.lines.is_input[k] == 0 {
                output_worth += worth;
            }
        }
        aid_value < self.offer_on(output_worth) && cost + aid_value <= self.facility_cash[f]
    }

    fn make_goods(&mut self, f: usize, output: Fx, detailed: bool, aided: bool, unaided: bool) {
        for k in self.range(f) {
            let quantity = self.units(k, output);
            let kind = self.lines.is_input[k];
            if kind == AID_LINE && !aided {
                continue;
            }
            if kind != 0 {
                self.use_input(self.lines.cell[k] as usize, quantity);
            } else {
                self.make_output(f, k, quantity, detailed, unaided);
            }
        }
    }

    fn use_input(&mut self, cell: usize, quantity: i64) {
        // C# `Stock[cell] -= (int)quantity`: truncating and wrapping.
        self.stock[cell] = self.stock[cell].wrapping_sub(quantity as i32);
        self.supply[cell] = clamp(i64::from(self.supply[cell]) + quantity);
        self.demand[cell] = clamp(i64::from(self.demand[cell]) + quantity);
    }

    fn make_output(&mut self, f: usize, k: usize, quantity: i64, detailed: bool, unaided: bool) {
        let player_owned = self.facility_owner[f] == OWNER_PLAYER;
        let mut quantity = Fx::mul_int_raw(quantity, self.lines.output_mul[k]);
        if unaided {
            quantity = Fx::mul_int_raw(quantity, self.p.unaided_output_raw);
        }
        let mut upgraded = 0;
        if self.lines.up_good[k] >= 0 {
            if !detailed {
                upgraded = Fx::mul_int_raw(quantity, self.p.grade_up_chance_raw);
            } else if DetRandom::for_key(
                self.p.seed,
                self.p.turn,
                self.p.grade_stream,
                f as i32,
                k as u32,
            )
            .next_fx01()
            .raw()
                < self.p.grade_up_chance_raw
            {
                upgraded = quantity;
            }
            let (good, cell, modifier) = (
                self.lines.up_good[k],
                self.lines.up_cell[k],
                self.lines.up_modifier[k],
            );
            self.emit(f, good, cell, modifier, upgraded, player_owned);
        }
        let (good, cell, modifier) = (
            self.lines.good[k],
            self.lines.cell[k],
            self.lines.modifier[k],
        );
        self.emit(f, good, cell, modifier, quantity - upgraded, player_owned);
    }

    fn emit(
        &mut self,
        f: usize,
        good: i32,
        cell: i32,
        modifier: i32,
        mut quantity: i64,
        player_owned: bool,
    ) {
        if cell >= 0 {
            quantity = Fx::mul_int_raw(quantity, self.modifier_mul[modifier as usize]);
        }
        let clamped = clamp(quantity);
        if clamped == 0 {
            return;
        }
        // C# `clamped * GoodBasePrice[good]` is an `int` product, widened afterwards.
        let worth = i64::from(clamped.wrapping_mul(self.good_base_price[good as usize]));
        self.facility_output_wage[f] += Fx::mul_int_raw(worth, self.p.output_wage_raw);
        if cell >= 0 {
            self.cell_produced[cell as usize] += i64::from(clamped);
        }
        if player_owned {
            self.player_output.push(PlayerOutput {
                facility: f as i32,
                good,
                cell,
                quantity: clamped,
            });
        } else if cell >= 0 {
            self.sell_to_market(f, good, cell as usize, clamped);
        }
    }

    /// What traders pay a seller for goods at this list worth (DEC-098).
    fn offer_on(&self, worth: i64) -> i64 {
        worth - Fx::mul_int_raw(worth, self.p.spread_raw)
    }

    /// All of it reaches the market; the producer is paid as far as the float can (DEC-061).
    fn sell_to_market(&mut self, f: usize, good: i32, cell: usize, clamped: i32) {
        let traders = self.node_float[self.facility_node[f] as usize] as usize;
        // C# `clamped * Price[cell]` is an `int` product, widened afterwards.
        let list = i64::from(clamped.wrapping_mul(self.price[cell]));
        let worth = self.offer_on(list);
        let mut paid = worth;
        let payable = self.float[traders] - self.float_reserve[traders];
        if paid > payable {
            paid = payable.max(0);
        }
        self.supply[cell] = clamp(i64::from(self.supply[cell]) + i64::from(clamped));
        self.facility_cash[f] += paid;
        self.float[traders] -= paid;
        if self.p.bill_producers && self.facility_owner[f] != OWNER_PLAYER {
            self.take_bill(f, traders, worth - paid);
        }
        let rate = self.subsidy_rate[traders * self.p.good_count + good as usize];
        if rate <= 0 {
            return;
        }
        let grant = Fx::mul_int_raw(list, rate).min(self.subsidy_left[traders]);
        self.facility_cash[f] += grant;
        self.subsidy_left[traders] -= grant;
    }

    fn take_bill(&mut self, f: usize, traders: usize, unpaid: i64) {
        let face = unpaid.min(self.bill_room[traders]);
        if face <= 0 {
            return;
        }
        self.bill_room[traders] -= face;
        let discounted = face - Fx::mul_int_raw(face, self.p.bill_discount_raw);
        let bank = self.bill_bank_room[traders] >= discounted;
        self.bills.holder.push(if bank {
            BILL_HOLDER_BANK
        } else {
            bill_holder_facility(f as i32)
        });
        self.bills.purse.push(traders as i32);
        self.bills.face.push(face);
        self.bills.due_turn.push(self.p.turn + self.p.bill_weeks);
        if !bank {
            return;
        }
        self.bill_bank_room[traders] -= discounted;
        self.bill_bank_spent[traders] += discounted;
        self.facility_cash[f] += discounted;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use fes_core::Fx;

    /// One NPC facility with one 100-unit output line whose specialty can come out a grade higher.
    fn run(detailed: bool) -> (Vec<i32>, Vec<i64>, Vec<i64>) {
        let lines = Lines {
            start: vec![0, 1],
            cell: vec![0],
            good: vec![0],
            modifier: vec![0],
            quantity: vec![100],
            is_input: vec![0],
            output_mul: vec![Fx::ONE_RAW],
            up_good: vec![1],
            up_cell: vec![1],
            up_modifier: vec![1],
        };
        let p = JobParams {
            seed: 1,
            turn: 0,
            stream: 7,
            grade_stream: 8,
            variance_permille: 100,
            good_count: 2,
            grade_up_chance_raw: Fx::ONE_RAW / 4,
            expansion_bonus_raw: 0,
            unaided_output_raw: Fx::ONE_RAW,
            output_wage_raw: Fx::ONE_RAW / 2,
            spread_raw: 0,
            bill_producers: false,
            bill_discount_raw: 0,
            bill_weeks: 0,
        };
        let (mut produced, mut wage, mut input_cost, mut cash) =
            (vec![0; 2], vec![0], vec![0], vec![0]);
        let (mut float, mut left) = (vec![1_000_000], vec![0]);
        let (mut stock, mut supply, mut demand, mut bid) =
            (vec![0; 2], vec![0; 2], vec![0; 2], vec![0; 2]);
        let (mut room, mut bank_room, mut spent) = (vec![0], vec![0], vec![0]);
        let mut bills = BillTable::default();
        let mut player = Vec::new();
        ProductionJob {
            p,
            lines: &lines,
            cell_plan: &[Fx::ONE_RAW; 2],
            good_base_price: &[4, 8],
            facility_node: &[0],
            facility_owner: &[-1],
            facility_level: &[1],
            facility_efficiency: &[Fx::ONE_RAW],
            node_float: &[0],
            float_reserve: &[0],
            subsidy_rate: &[0, 0],
            node_detail: &[u8::from(detailed)],
            modifier_mul: &[Fx::ONE_RAW; 2],
            price: &[10, 20],
            cell_produced: &mut produced,
            facility_output_wage: &mut wage,
            facility_input_cost: &mut input_cost,
            facility_cash: &mut cash,
            float: &mut float,
            subsidy_left: &mut left,
            stock: &mut stock,
            supply: &mut supply,
            demand: &mut demand,
            input_bid: &mut bid,
            bill_room: &mut room,
            bill_bank_room: &mut bank_room,
            bill_bank_spent: &mut spent,
            bills: &mut bills,
            player_output: &mut player,
        }
        .execute();
        (supply, cash, wage)
    }

    #[test]
    fn an_aggregate_facility_makes_the_expected_output_and_grade_up_share() {
        // No variance: 100 units; a quarter of them one grade higher.
        let (supply, cash, wage) = run(false);
        assert_eq!(supply, vec![75, 25]);
        assert_eq!(cash, vec![75 * 10 + 25 * 20]);
        assert_eq!(wage, vec![25 * 8 / 2 + 75 * 4 / 2]);
    }

    #[test]
    fn a_detailed_facility_rolls_variance_and_grades_all_or_nothing() {
        let (supply, _, _) = run(true);
        let total = supply[0] + supply[1];
        assert!((90..=110).contains(&total), "{total}");
        assert!(supply[0] == 0 || supply[1] == 0, "{supply:?}");
    }
}
