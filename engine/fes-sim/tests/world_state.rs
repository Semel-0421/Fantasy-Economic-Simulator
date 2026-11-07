use fes_core::io::{DotNetReader, DotNetWriter, IoError};
use fes_core::{TierCadence, WorldLevel};
use fes_save::{world_hash, RawSection};
use fes_sim::testing::assert_round_trip;
use fes_sim::{Component, SimClock, SimError, TierSchedule, WorldState};

#[test]
fn clock_round_trips_start_save() {
    assert_round_trip::<SimClock>("core.clock");
}

macro_rules! fake {
    ($name:ident, $key:expr) => {
        #[derive(Default)]
        struct $name(i32);
        impl Component for $name {
            fn key(&self) -> &'static str {
                $key
            }
            fn version(&self) -> i32 {
                1
            }
            fn write(&self, w: &mut DotNetWriter<Vec<u8>>) {
                let _ = w.write_i32(self.0);
            }
            fn read(&mut self, r: &mut DotNetReader, _version: i32) -> Result<(), IoError> {
                self.0 = r.read_i32()?;
                Ok(())
            }
        }
    };
}
fake!(Halfwidth, "\u{FF61}");
fake!(Astral, "\u{1D504}");

#[test]
fn world_hash_orders_keys_by_utf16() {
    let mut w = WorldState::new();
    w.add(Halfwidth(1)).unwrap();
    w.add(Astral(2)).unwrap();
    let sections = w.sections();
    // UTF-16: U+1D504 is D835 DD04, which sorts before U+FF61 (UTF-8 order is the reverse).
    assert_eq!(sections[0].key, "\u{1D504}");
    assert_eq!(sections[1].key, "\u{FF61}");
    assert_eq!(w.hash(), world_hash(&sections));
}

#[test]
fn duplicate_key_is_rejected() {
    fake!(Other, "\u{FF61}");
    let mut w = WorldState::new();
    w.add(Halfwidth(1)).unwrap();
    assert!(matches!(w.add(Other(1)), Err(SimError::DuplicateKey(_))));
    assert!(matches!(
        w.add(Halfwidth(1)),
        Err(SimError::DuplicateKey(_))
    ));
}

#[test]
fn missing_section_on_load_is_an_error() {
    let mut w = WorldState::new();
    w.add(Halfwidth(1)).unwrap();
    w.add(Astral(2)).unwrap();
    let all = w.sections();
    let only_one = vec![all[0].clone()];
    assert!(matches!(
        w.load_sections(&only_one),
        Err(SimError::MissingSection(_))
    ));
    let mut extra = all.clone();
    extra.push(RawSection {
        key: "nope".into(),
        version: 1,
        bytes: vec![],
    });
    assert!(matches!(
        w.load_sections(&extra),
        Err(SimError::UnknownSection(_))
    ));
    assert!(w.load_sections(&all).is_ok());
}

#[test]
fn tier_schedule_matches_csharp() {
    let c = |p, o| TierCadence {
        period: p,
        offset: o,
    };
    let s = TierSchedule::new(&[c(4, 0), c(4, 1), c(2, 0), c(1, 0), c(1, 0)]).unwrap();
    let due: Vec<i32> = (0..8)
        .filter(|t| s.is_due(WorldLevel::Nation, *t))
        .collect();
    assert_eq!(due, vec![3, 7]);
    assert!(s.is_due(WorldLevel::Village, 5));
    assert!(!s.is_due(WorldLevel::Region, 1));
    assert_eq!(s.elapsed_turns(WorldLevel::Continent, 0), 1);
    assert_eq!(s.elapsed_turns(WorldLevel::Continent, 8), 4);
    assert!(TierSchedule::new(&[c(1, 0)]).is_err());
    assert_eq!(
        TierSchedule::every_turn().elapsed_turns(WorldLevel::City, 9),
        1
    );
}

#[test]
fn get2_mut_borrows_two_components_in_either_order() {
    let mut w = WorldState::new();
    w.add(Halfwidth(1)).unwrap();
    w.add(Astral(2)).unwrap();
    let (a, b) = w.get2_mut::<Astral, Halfwidth>();
    std::mem::swap(&mut a.0, &mut b.0);
    let (h, a) = w.get2_mut::<Halfwidth, Astral>();
    assert_eq!((h.0, a.0), (2, 1));
}

#[test]
#[should_panic(expected = "distinct")]
fn get2_mut_rejects_the_same_component() {
    let mut w = WorldState::new();
    w.add(Halfwidth(1)).unwrap();
    let _ = w.get2_mut::<Halfwidth, Halfwidth>();
}
