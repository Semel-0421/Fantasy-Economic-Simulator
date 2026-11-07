//! Shares, shorts, asset-market state and ventures (C# `Shares.cs`, `Shorts.cs`, `AssetMarkets.cs`,
//! `Ventures.cs`; sections `agents.shares`, `agents.shorts`, `agents.short_licences`,
//! `agents.asset_markets`, `agents.ventures`). Turn-0 state; the market systems come later.
use super::columns::{native_table, read_items, read_len, write_items};
use crate::world::{Component, NodeBanks, NodeTable, RouteTable};
use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::Fx;
use fes_data::SimulationConfig;

native_table! {
    /// Facility shares held outside the owner (C# `ShareTable`, `agents.shares`, version 1).
    ShareTable, "agents.shares", 1,
    {
        facility: i32,
        holder: i32,
        count: i32,
    }
}

native_table! {
    /// Short positions (C# `ShortTable`, `agents.shorts`, version 1).
    ShortTable, "agents.shorts", 1,
    {
        asset: i32,
        /// 1 for a venture share, 0 for a facility.
        venture: u8,
        shares: i32,
        bank: i32,
    }
}

native_table! {
    /// Ventures: sea-trade companies, one row each (C# `VentureTable`, `agents.ventures`, version 1).
    VentureTable, "agents.ventures", 1,
    {
        continent: i32,
        port: i32,
        far: i32,
        cash: i64,
        stake: i64,
        proceeds: i64,
        sail_until: i32,
        good: i32,
        units: i32,
        lost: u8,
        player_shares: i32,
        profit_year: i64,
        voyages: i32,
        wrecks: i32,
    }
}

impl VentureTable {
    /// C# `VentureTable.Add`: a venture at rest.
    pub fn add(&mut self, continent: i32, port: i32, far: i32) {
        self.continent.push(continent);
        self.port.push(port);
        self.far.push(far);
        self.cash.push(0);
        self.stake.push(0);
        self.proceeds.push(0);
        self.sail_until.push(-1);
        self.good.push(-1);
        self.units.push(0);
        self.lost.push(0);
        self.player_shares.push(0);
        self.profit_year.push(0);
        self.voyages.push(0);
        self.wrecks.push(0);
    }

    /// C# `VentureTable.Found`: each continent's first routes that leave it from a nation's node.
    pub fn found(
        &mut self,
        nodes: &NodeTable,
        routes: &RouteTable,
        banks: &NodeBanks,
        config: &SimulationConfig,
    ) {
        for c in 0..nodes.continent_count() {
            let leaving = (0..routes.count()).filter(|&r| {
                let (from, to) = (routes.from[r], routes.to[r]);
                nodes.continent[from as usize] == c
                    && nodes.continent[to as usize] != c
                    && banks.nation_of(nodes, from) >= 0
            });
            let per_continent = config.ventures_per_continent.max(0) as usize;
            for r in leaving.take(per_continent) {
                self.add(c, routes.from[r], routes.to[r]);
            }
        }
    }
}

/// Short-selling licences per market and the nations' margin calls (C# `ShortLicences`,
/// `agents.short_licences`, version 1).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ShortLicences {
    pub first_trade: Vec<i32>,
    pub licensed: Vec<u8>,
    pub called_turn: Vec<i32>,
}

impl ShortLicences {
    pub fn new(markets: usize, nations: usize) -> Self {
        Self {
            first_trade: vec![-1; markets],
            licensed: vec![0; markets],
            called_turn: vec![-1; nations],
        }
    }
}

impl Component for ShortLicences {
    fn key(&self) -> &'static str {
        "agents.short_licences"
    }

    fn version(&self) -> i32 {
        1
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let _ = w.write_i32(self.first_trade.len() as i32);
        for (trade, licensed) in self.first_trade.iter().zip(&self.licensed) {
            let _ = w.write_i32(*trade);
            let _ = w.write_u8(*licensed);
        }
        let _ = w.write_i32(self.called_turn.len() as i32);
        write_items(w, &self.called_turn);
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        read_len(r, self.first_trade.len())?;
        for (trade, licensed) in self.first_trade.iter_mut().zip(self.licensed.iter_mut()) {
            *trade = r.read_i32()?;
            *licensed = r.read_u8()?;
        }
        read_len(r, self.called_turn.len())?;
        read_items(r, &mut self.called_turn)
    }
}

/// How many saved turns of debt an asset market keeps (C# `AssetMarkets.History`).
pub const HISTORY: usize = 4;

/// DEC-131: each market's price over its fundamental, debt and phase. Markets run nations (stocks),
/// then nodes (property), then continents (ventures) (C# `AssetMarkets`, `agents.asset_markets`,
/// version 2).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AssetMarkets {
    pub nation_count: usize,
    pub node_count: usize,
    pub continent_count: usize,
    pub premium: Vec<i64>,
    pub expectation: Vec<i64>,
    pub quarter_premium: Vec<i64>,
    pub debt: Vec<i64>,
    pub debt_history: Vec<i64>,
    pub history_index: i32,
    pub phase: Vec<u8>,
    pub bursts: Vec<i32>,
    pub published_premium: Vec<i64>,
    pub published_leverage: Vec<i64>,
    pub published_credit_growth: Vec<i64>,
    pub idle: Vec<i64>,
    /// Per nation.
    pub written_off: Vec<i64>,
    /// Per nation; not saved.
    pub burst_nation: Vec<bool>,
}

impl AssetMarkets {
    pub fn new(nodes: &NodeTable, nation_count: usize) -> Self {
        let node_count = nodes.count();
        let continent_count = nodes.continent_count() as usize;
        let count = nation_count + node_count + continent_count;
        Self {
            nation_count,
            node_count,
            continent_count,
            premium: vec![Fx::ONE_RAW; count],
            expectation: vec![0; count],
            quarter_premium: vec![Fx::ONE_RAW; count],
            debt: vec![0; count],
            debt_history: vec![0; count * HISTORY],
            history_index: 0,
            phase: vec![0; count],
            bursts: vec![0; count],
            published_premium: vec![Fx::ONE_RAW; count],
            published_leverage: vec![0; count],
            published_credit_growth: vec![0; count],
            idle: vec![0; count],
            written_off: vec![0; nation_count],
            burst_nation: vec![false; nation_count],
        }
    }

    /// Number of markets.
    pub fn count(&self) -> usize {
        self.premium.len()
    }

    fn main(&self) -> usize {
        self.nation_count + self.node_count
    }

    fn longs(&self) -> [&Vec<i64>; 8] {
        [
            &self.premium,
            &self.expectation,
            &self.quarter_premium,
            &self.debt,
            &self.published_premium,
            &self.published_leverage,
            &self.published_credit_growth,
            &self.idle,
        ]
    }

    fn longs_mut(&mut self) -> [&mut Vec<i64>; 8] {
        [
            &mut self.premium,
            &mut self.expectation,
            &mut self.quarter_premium,
            &mut self.debt,
            &mut self.published_premium,
            &mut self.published_leverage,
            &mut self.published_credit_growth,
            &mut self.idle,
        ]
    }

    fn write_range(&self, w: &mut DotNetWriter<Vec<u8>>, from: usize, to: usize) {
        for values in self.longs() {
            write_items(w, &values[from..to]);
        }
        write_items(w, &self.debt_history[from * HISTORY..to * HISTORY]);
    }

    fn read_range(&mut self, r: &mut DotNetReader, from: usize, to: usize) -> Result<(), IoError> {
        for values in self.longs_mut() {
            read_items(r, &mut values[from..to])?;
        }
        read_items(r, &mut self.debt_history[from * HISTORY..to * HISTORY])
    }

    fn write_phases(&self, w: &mut DotNetWriter<Vec<u8>>, from: usize, to: usize) {
        for m in from..to {
            let _ = w.write_u8(self.phase[m]);
            let _ = w.write_i32(self.bursts[m]);
        }
    }

    fn read_phases(&mut self, r: &mut DotNetReader, from: usize, to: usize) -> Result<(), IoError> {
        for m in from..to {
            self.phase[m] = r.read_u8()?;
            self.bursts[m] = r.read_i32()?;
        }
        Ok(())
    }
}

impl Component for AssetMarkets {
    fn key(&self) -> &'static str {
        "agents.asset_markets"
    }

    fn version(&self) -> i32 {
        2
    }

    fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
        let main = self.main();
        let _ = w.write_i32(main as i32);
        let _ = w.write_i32(self.history_index);
        self.write_range(w, 0, main);
        write_items(w, &self.written_off);
        self.write_phases(w, 0, main);
        let _ = w.write_i32(self.continent_count as i32);
        self.write_range(w, main, self.count());
        self.write_phases(w, main, self.count());
    }

    fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
        let main = self.main();
        read_len(r, main)?;
        self.history_index = r.read_i32()?;
        self.read_range(r, 0, main)?;
        read_items(r, &mut self.written_off)?;
        self.read_phases(r, 0, main)?;
        let continents = r.read_i32()?;
        if continents == 0 {
            return Ok(()); // saved before the continental exchanges: they start fresh
        }
        if continents < 0 || continents as usize != self.continent_count {
            return Err(IoError::InvalidLength);
        }
        let count = self.count();
        self.read_range(r, main, count)?;
        self.read_phases(r, main, count)
    }
}
