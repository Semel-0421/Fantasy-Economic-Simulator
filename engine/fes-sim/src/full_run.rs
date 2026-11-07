//! Full-run harness (behind the `testing` feature): plays a kept C# golden run turn by turn and
//! compares each turn's world hash and per-section FNV hashes with `turns.json`.
use crate::error::SimError;
use crate::simulation::Simulation;
use crate::SimulationBuilder;
use fes_core::io::FnvSink;
use fes_data::database::GameDatabase;
use fes_data::MapDef;
use std::collections::BTreeMap;
use std::io::Write;
use std::path::Path;

/// The recorded hashes after one turn (or for the built world, turn 0).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TurnHashes {
    pub turn: i32,
    pub world_hash: u64,
    pub sections: BTreeMap<String, u64>,
}

/// A kept golden run: its seed, the hashes of turn 0 and of every turn, and the final save hash.
#[derive(Clone, Debug)]
pub struct RunFixture {
    pub name: String,
    pub seed: u64,
    pub turn0: TurnHashes,
    pub turns: Vec<TurnHashes>,
    pub final_sha256_32: String,
}

fn invalid(msg: impl Into<String>) -> SimError {
    SimError::Invalid(msg.into())
}

fn u64_of(v: &serde_json::Value, what: &str) -> Result<u64, SimError> {
    v.as_str()
        .and_then(|s| s.parse().ok())
        .ok_or_else(|| invalid(format!("{what}: not a decimal u64 string")))
}

fn hashes_of(turn: i32, v: &serde_json::Value) -> Result<TurnHashes, SimError> {
    let world_hash = u64_of(&v["world_hash"], "world_hash")?;
    let map = v["sections"]
        .as_object()
        .ok_or_else(|| invalid("no sections object"))?;
    let mut sections = BTreeMap::new();
    for (key, hash) in map {
        sections.insert(key.clone(), u64_of(hash, key)?);
    }
    Ok(TurnHashes {
        turn,
        world_hash,
        sections,
    })
}

fn read_json(dir: &Path, file: &str) -> Result<serde_json::Value, SimError> {
    let path = dir.join(file);
    let text =
        std::fs::read_to_string(&path).map_err(|e| invalid(format!("{}: {e}", path.display())))?;
    serde_json::from_str(&text).map_err(|e| invalid(format!("{}: {e}", path.display())))
}

impl RunFixture {
    /// Reads `turns.json` and `turn0.json` from a `runs/<name>` directory.
    pub fn load(dir: &Path) -> Result<RunFixture, SimError> {
        let turns_json = read_json(dir, "turns.json")?;
        let turn0_json = read_json(dir, "turn0.json")?;
        let entries = turns_json["turns"]
            .as_array()
            .ok_or_else(|| invalid("no turns"))?;
        let mut turns = Vec::new();
        for entry in entries {
            let turn = entry["turn"]
                .as_i64()
                .and_then(|t| i32::try_from(t).ok())
                .ok_or_else(|| invalid("bad turn number"))?;
            turns.push(hashes_of(turn, entry)?);
        }
        Ok(RunFixture {
            name: turns_json["name"].as_str().unwrap_or_default().to_string(),
            seed: u64_of(&turns_json["seed"], "seed")?,
            turn0: hashes_of(0, &turn0_json)?,
            turns,
            final_sha256_32: turns_json["final_sha256_32"]
                .as_str()
                .unwrap_or_default()
                .to_string(),
        })
    }
}

/// Where a run first differed from the fixture: the clock turn after which it did, and
/// `world_hash` or the first differing section key.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Divergence {
    pub turn: i32,
    pub key: String,
}

#[derive(Clone, Debug, Default)]
pub struct RunReport {
    /// Turns compared, turn 0 included.
    pub turns_compared: usize,
    pub divergence: Option<Divergence>,
    /// Set when every recorded turn matched: whether the final save hash equals the fixture's.
    pub final_hash_matches: Option<bool>,
}

impl RunReport {
    pub fn first_divergence(&self) -> Option<(i32, &str)> {
        self.divergence.as_ref().map(|d| (d.turn, d.key.as_str()))
    }
}

/// FNV-1a 64 of one section's bytes (as `turns.json` records).
pub fn section_hash(bytes: &[u8]) -> u64 {
    let mut sink = FnvSink::new();
    let _ = sink.write_all(bytes);
    sink.hash()
}

/// The first difference between the simulation and the recorded hashes, if any.
pub fn compare_turn(sim: &Simulation, expected: &TurnHashes) -> Option<Divergence> {
    let sections = sim.world().sections();
    let differs = |key: &str| Divergence {
        turn: expected.turn,
        key: key.to_string(),
    };
    for s in &sections {
        match expected.sections.get(&s.key) {
            Some(h) if *h == section_hash(&s.bytes) => {}
            _ => return Some(differs(&s.key)),
        }
    }
    if let Some(missing) = expected
        .sections
        .keys()
        .find(|k| !sections.iter().any(|s| &s.key == *k))
    {
        return Some(differs(missing));
    }
    (sim.world_hash() != expected.world_hash).then(|| differs("world_hash"))
}

/// Plays the run with the default systems; see `play_run_with`.
pub fn play_run(
    db: &GameDatabase,
    map: &MapDef,
    run: &RunFixture,
    until: Option<i32>,
) -> Result<RunReport, SimError> {
    play_run_with(db, map, run, until, |b| b)
}

/// Builds the run (`db` carries the run's config overrides), compares turn 0, then steps and
/// compares each turn up to `until` (default: every recorded turn), stopping at the first
/// divergence. `configure` is applied to the builder to swap in test systems.
pub fn play_run_with(
    db: &GameDatabase,
    map: &MapDef,
    run: &RunFixture,
    until: Option<i32>,
    configure: impl FnOnce(SimulationBuilder) -> SimulationBuilder,
) -> Result<RunReport, SimError> {
    let builder =
        SimulationBuilder::new(db.clone(), map.clone(), run.seed).with_default_modules()?;
    let mut sim = configure(builder).build()?;
    let mut report = RunReport {
        turns_compared: 1,
        divergence: compare_turn(&sim, &run.turn0),
        final_hash_matches: None,
    };
    let last = until.unwrap_or(i32::MAX);
    for expected in &run.turns {
        if report.divergence.is_some() || expected.turn > last {
            return Ok(report);
        }
        sim.step()?;
        report.turns_compared += 1;
        report.divergence = compare_turn(&sim, expected);
    }
    if report.divergence.is_none() && until.is_none() {
        report.final_hash_matches =
            Some(final_sha256_32(&sim).eq_ignore_ascii_case(&run.final_sha256_32));
    }
    Ok(report)
}

/// First 32 hex digits (upper case) of the SHA-256 of the save, as `GoldenRunTests`.
pub fn final_sha256_32(sim: &Simulation) -> String {
    let digest = sha256(&sim.save());
    digest[..16].iter().map(|b| format!("{b:02X}")).collect()
}

const K: [u32; 64] = [
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
    0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
    0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
    0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
    0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
];

fn compress(h: &mut [u32; 8], block: &[u8]) {
    let mut w = [0u32; 64];
    for (i, c) in block.as_chunks::<4>().0.iter().enumerate() {
        w[i] = u32::from_be_bytes(*c);
    }
    for i in 16..64 {
        let s0 = w[i - 15].rotate_right(7) ^ w[i - 15].rotate_right(18) ^ (w[i - 15] >> 3);
        let s1 = w[i - 2].rotate_right(17) ^ w[i - 2].rotate_right(19) ^ (w[i - 2] >> 10);
        w[i] = w[i - 16]
            .wrapping_add(s0)
            .wrapping_add(w[i - 7])
            .wrapping_add(s1);
    }
    let mut v = *h;
    for (k, wi) in K.iter().zip(w) {
        let s1 = v[4].rotate_right(6) ^ v[4].rotate_right(11) ^ v[4].rotate_right(25);
        let ch = (v[4] & v[5]) ^ (!v[4] & v[6]);
        let t1 = v[7]
            .wrapping_add(s1)
            .wrapping_add(ch)
            .wrapping_add(*k)
            .wrapping_add(wi);
        let s0 = v[0].rotate_right(2) ^ v[0].rotate_right(13) ^ v[0].rotate_right(22);
        let maj = (v[0] & v[1]) ^ (v[0] & v[2]) ^ (v[1] & v[2]);
        let t2 = s0.wrapping_add(maj);
        v = [
            t1.wrapping_add(t2),
            v[0],
            v[1],
            v[2],
            v[3].wrapping_add(t1),
            v[4],
            v[5],
            v[6],
        ];
    }
    for (x, y) in h.iter_mut().zip(v) {
        *x = x.wrapping_add(y);
    }
}

/// SHA-256 (FIPS 180-4); the workspace has no hashing crate and only the golden hash needs it.
pub fn sha256(data: &[u8]) -> [u8; 32] {
    let mut h: [u32; 8] = [
        0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab,
        0x5be0cd19,
    ];
    let (blocks, rest) = data.as_chunks::<64>();
    for b in blocks {
        compress(&mut h, b);
    }
    let mut tail = rest.to_vec();
    tail.push(0x80);
    while tail.len() % 64 != 56 {
        tail.push(0);
    }
    tail.extend_from_slice(&(data.len() as u64 * 8).to_be_bytes());
    for b in tail.as_chunks::<64>().0 {
        compress(&mut h, b);
    }
    let mut out = [0u8; 32];
    for (i, x) in h.iter().enumerate() {
        out[i * 4..i * 4 + 4].copy_from_slice(&x.to_be_bytes());
    }
    out
}
