//! The households behind each market's consumption (C# `HouseholdState`, section
//! `economy.households`, version 1; SYS-MARKETS, DEC-049, DEC-050, DEC-104).
use crate::world::{Component, NodeTable};
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::{Fx, WorldLevel};
use fes_data::{MapDef, SimulationConfig};

/// Every node's population and weekly income per head, plus how much of their necessity needs
/// households could afford since the last yearly update.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct HouseholdState {
    pub population: Vec<i64>,
    /// Raw Fx: coins per head and week.
    pub income_per_head: Vec<i64>,
    /// Per node: necessity units wanted at the anchor price since the last yearly update.
    pub necessity_wanted: Vec<i64>,
    /// Per node: necessity units demanded at the prices households met.
    pub necessity_met: Vec<i64>,
    demand_scale: Vec<Fx>,
}

impl HouseholdState {
    pub fn new(nodes: &NodeTable, map: &MapDef, config: &SimulationConfig) -> Self {
        let count = nodes.count();
        let mut state = Self {
            population: vec![0; count],
            income_per_head: vec![0; count],
            necessity_wanted: vec![0; count],
            necessity_met: vec![0; count],
            demand_scale: vec![Fx::ONE; count],
        };
        for n in 0..count {
            let level = usize::from(nodes.level[n]);
            state.population[n] = config.default_population[level];
            state.income_per_head[n] = config.default_income_per_head[level].raw();
        }
        for def in &map.nodes {
            if def.population < 0 {
                continue;
            }
            if let Some(node) = nodes.try_find(&def.key) {
                state.population[node.0 as usize] = i64::from(def.population);
            }
        }
        if nodes.absorbed() {
            state.absorb_classes(nodes, config);
        }
        state
    }

    pub fn node_count(&self) -> usize {
        self.population.len()
    }

    /// The share of its necessities a node's households could buy, in thousandths; full when they wanted none.
    pub fn necessity_supplied_permille(&self, node: usize) -> i64 {
        if self.necessity_wanted[node] > 0 {
            self.necessity_met[node] * 1000 / self.necessity_wanted[node]
        } else {
            1000
        }
    }

    /// A core town takes the people of its classes, and their weight of demand, on top of what being a
    /// core draws (DEC-104).
    fn absorb_classes(&mut self, nodes: &NodeTable, config: &SimulationConfig) {
        let count = self.node_count();
        let mut weight = vec![0i64; count];
        let mut people = vec![0i64; count];
        self.collect_classes(nodes, config, &mut weight, &mut people);
        for n in 0..count {
            if !nodes.is_seat(n as i32) {
                continue;
            }
            let level = usize::from(nodes.level[n]);
            let level_weight = config.level_demand_weight[level].raw();
            let default_people = config.default_population[level];
            let by_weight = Fx::from_ratio(weight[n] + level_weight, level_weight);
            let by_people = Fx::from_ratio(default_people, people[n] + default_people);
            let strength = Fx::from_int(i64::from(nodes.core_strength[n]));
            self.demand_scale[n] =
                by_weight * by_people * (Fx::ONE + strength * config.core_demand_per_point);
        }
    }

    /// Moves the people of every class below city level to its core and sums what each core absorbed.
    fn collect_classes(
        &mut self,
        nodes: &NodeTable,
        config: &SimulationConfig,
        weight: &mut [i64],
        people: &mut [i64],
    ) {
        let city = WorldLevel::City as u8;
        let continent = WorldLevel::Continent as u8;
        for n in 0..self.node_count() {
            let level = nodes.level[n];
            if level >= city {
                continue;
            }
            let core = nodes.core[n] as usize;
            // A continent has hardly any people.
            if level != continent && config.core_demand_weighted {
                weight[core] += config.level_demand_weight[usize::from(level)].raw();
                people[core] += config.default_population[usize::from(level)];
            }
            if core == n {
                continue;
            }
            self.population[core] += self.population[n];
            self.population[n] = 0;
        }
    }

    /// Necessity demand relative to a default node of the level: population over the default.
    pub fn necessity_weight(&self, node: usize, level: usize, config: &SimulationConfig) -> Fx {
        Fx::from_ratio(self.population[node], config.default_population[level])
            * self.demand_scale[node]
    }

    /// Luxury demand relative to a default node of the level: total income over the default.
    pub fn luxury_weight(&self, node: usize, level: usize, config: &SimulationConfig) -> Fx {
        Fx::from_ratio(
            self.population[node] * self.income_per_head[node],
            config.default_population[level] * config.default_income_per_head[level].raw(),
        ) * self.demand_scale[node]
    }
}

impl Component for HouseholdState {
    fn key(&self) -> &'static str {
        "economy.households"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.node_count() as i32);
        for n in 0..self.node_count() {
            let _ = w.write_i64(self.population[n]);
            let _ = w.write_i64(self.income_per_head[n]);
            let _ = w.write_i64(self.necessity_wanted[n]);
            let _ = w.write_i64(self.necessity_met[n]);
        }
    }

    /// A saved node count other than this state's is `IoError::InvalidLength`.
    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        let count = r.read_i32()?;
        if count < 0 || count as usize != self.node_count() {
            return Err(IoError::InvalidLength);
        }
        for n in 0..self.node_count() {
            self.population[n] = r.read_i64()?;
            self.income_per_head[n] = r.read_i64()?;
            self.necessity_wanted[n] = r.read_i64()?;
            self.necessity_met[n] = r.read_i64()?;
        }
        Ok(())
    }
}
