use fes_core::det::det_hash;
use fes_core::testing::fixture_json;
use fes_core::{DetRandom, TierCadence, WorldLevel};
use fes_data::database::GameDatabase;
use fes_data::{FileDataSource, MapDef};
use fes_sim::{
    module_order, sim_order, Command, CommandApplied, CommandRejected, CommandResult,
    CommandSystem, Module, Services, SimBuilder, SimClock, SimContext, SimError, SimPhase, System,
    TierSchedule, TurnPipeline, WorldState,
};
use std::path::PathBuf;
use std::sync::{Arc, Mutex};

type Log = Arc<Mutex<Vec<String>>>;

fn new_ctx(seed: u64) -> SimContext {
    let dir = PathBuf::from(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../Assets/StreamingAssets/Data"
    ));
    let source = FileDataSource::new(dir);
    let db = GameDatabase::load(&source).expect("database");
    let map = MapDef::load(&source).expect("map");
    let mut world = WorldState::new();
    world.add(SimClock::new(seed)).unwrap();
    let services = Services::new(Arc::new(db), Arc::new(map));
    SimContext::new(world, services, TierSchedule::every_turn()).unwrap()
}

struct Fake {
    id: &'static str,
    phase: SimPhase,
    order: i32,
    log: Log,
}

impl System for Fake {
    fn id(&self) -> &'static str {
        self.id
    }
    fn phase(&self) -> SimPhase {
        self.phase
    }
    fn order(&self) -> i32 {
        self.order
    }
    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        self.log
            .lock()
            .unwrap()
            .push(format!("{}@{}", self.id, ctx.turn()));
        Ok(())
    }
}

fn fake(id: &'static str, phase: SimPhase, order: i32, log: &Log) -> Box<dyn System> {
    Box::new(Fake {
        id,
        phase,
        order,
        log: Arc::clone(log),
    })
}

#[derive(Debug)]
struct Ping;
#[derive(Debug)]
struct Pong;

struct Publisher {
    id: &'static str,
    phase: SimPhase,
    log: Log,
}

impl System for Publisher {
    fn id(&self) -> &'static str {
        self.id
    }
    fn phase(&self) -> SimPhase {
        self.phase
    }
    fn execute(&mut self, ctx: &mut SimContext) -> Result<(), SimError> {
        self.log.lock().unwrap().push(format!("run:{}", self.id));
        ctx.events.publish(Ping);
        Ok(())
    }
}

#[test]
fn systems_run_in_phase_order_id_order() {
    let log: Log = Log::default();
    let mut p = TurnPipeline::new(vec![
        fake("b", SimPhase::Markets, 0, &log),
        fake("late", SimPhase::Commands, sim_order::LATE, &log),
        fake("z", SimPhase::Commands, 0, &log),
        fake("a", SimPhase::Markets, 0, &log),
        fake("early", SimPhase::Cleanup, sim_order::EARLY, &log),
        fake("c", SimPhase::Cleanup, 0, &log),
    ]);
    assert_eq!(p.system_ids(), ["z", "late", "a", "b", "early", "c"]);
    let mut ctx = new_ctx(1);
    p.advance(&mut ctx).unwrap();
    let ran: Vec<String> = log.lock().unwrap().clone();
    assert_eq!(ran, ["z@0", "late@0", "a@0", "b@0", "early@0", "c@0"]);
}

#[test]
fn events_dispatch_after_each_phase() {
    let log: Log = Log::default();
    let mut ctx = new_ctx(1);
    let l = Arc::clone(&log);
    ctx.events.subscribe::<Ping>(move |_, _| {
        l.lock().unwrap().push("handler".into());
        Ok(())
    });
    let mut p = TurnPipeline::new(vec![
        Box::new(Publisher {
            id: "pub",
            phase: SimPhase::Lod,
            log: Arc::clone(&log),
        }),
        fake("after", SimPhase::Environment, 0, &log),
    ]);
    p.advance(&mut ctx).unwrap();
    assert_eq!(*log.lock().unwrap(), ["run:pub", "handler", "after@0"]);
}

#[test]
fn handler_publishing_during_dispatch_is_delivered_same_turn() {
    let log: Log = Log::default();
    let mut ctx = new_ctx(1);
    let l = Arc::clone(&log);
    ctx.events.subscribe::<Ping>(move |_, ctx| {
        l.lock().unwrap().push("ping".into());
        ctx.events.publish(Pong);
        Ok(())
    });
    let l = Arc::clone(&log);
    ctx.events.subscribe::<Pong>(move |_, _| {
        l.lock().unwrap().push("pong".into());
        Ok(())
    });
    // A handler may also subscribe another handler while dispatching; it sees later events.
    let l = Arc::clone(&log);
    ctx.events.subscribe::<Ping>(move |_, ctx| {
        let l2 = Arc::clone(&l);
        ctx.events.subscribe::<Pong>(move |_, _| {
            l2.lock().unwrap().push("late-pong".into());
            Ok(())
        });
        Ok(())
    });
    ctx.events.publish(Ping);
    ctx.dispatch_events().unwrap();
    assert_eq!(*log.lock().unwrap(), ["ping", "pong", "late-pong"]);
    assert_eq!(ctx.events.turn_log().len(), 2);
}

#[test]
fn handler_subscribed_for_same_type_during_dispatch_sees_current_event() {
    // C#'s index loop reaches a handler appended to the list it is walking.
    let log: Log = Log::default();
    let mut ctx = new_ctx(1);
    let l = Arc::clone(&log);
    let mut once = true;
    ctx.events.subscribe::<Ping>(move |_, ctx| {
        l.lock().unwrap().push("first".into());
        if std::mem::take(&mut once) {
            let l2 = Arc::clone(&l);
            ctx.events.subscribe::<Ping>(move |_, _| {
                l2.lock().unwrap().push("added".into());
                Ok(())
            });
        }
        Ok(())
    });
    ctx.events.publish(Ping);
    ctx.events.publish(Ping);
    ctx.dispatch_events().unwrap();
    assert_eq!(*log.lock().unwrap(), ["first", "added", "first", "added"]);
}

#[test]
fn handler_mutates_world_state() {
    let mut ctx = new_ctx(1);
    ctx.events.subscribe::<Ping>(|_, ctx| {
        ctx.clock_mut().turn += 5;
        Ok(())
    });
    let before = ctx.world.hash();
    ctx.events.publish(Ping);
    ctx.dispatch_events().unwrap();
    assert_eq!(ctx.turn(), 5);
    assert_ne!(ctx.world.hash(), before);
}

#[test]
fn cascade_limit_stops_endless_republishing() {
    let mut ctx = new_ctx(1);
    ctx.events.subscribe::<Ping>(|_, ctx| {
        ctx.events.publish(Ping);
        Ok(())
    });
    ctx.events.publish(Ping);
    let err = ctx.dispatch_events().unwrap_err();
    assert!(matches!(err, SimError::Invalid(s) if s.contains("cascade")));
    // Pending events are dropped and handlers survive the failure.
    ctx.events.publish(Pong);
    ctx.dispatch_events().unwrap();
}

#[test]
fn simulation_is_send() {
    fn assert_send<T: Send>() {}
    assert_send::<fes_sim::Simulation>();
    assert_send::<fes_sim::TurnReport>();
}

#[test]
fn clock_advances_after_cleanup() {
    let log: Log = Log::default();
    let mut p = TurnPipeline::new(vec![fake("c", SimPhase::Cleanup, 0, &log)]);
    let mut ctx = new_ctx(1);
    p.advance(&mut ctx).unwrap();
    p.advance(&mut ctx).unwrap();
    assert_eq!(*log.lock().unwrap(), ["c@0", "c@1"]);
    assert_eq!(ctx.turn(), 2);
}

#[derive(Debug)]
struct Cmd(bool);

impl Command for Cmd {
    fn validate(&self, _ctx: &SimContext) -> CommandResult {
        if self.0 {
            CommandResult::Ok
        } else {
            CommandResult::Error("nope".into())
        }
    }

    /// Changes the world, so a wrongly applied rejected command shows in the hash.
    fn apply(&self, ctx: &mut SimContext) {
        ctx.clock_mut().seed += 1;
    }
}

#[test]
fn rejected_command_leaves_world_unchanged() {
    let mut ctx = new_ctx(1);
    ctx.commands.enqueue(Cmd(false));
    let before = ctx.world.hash();
    // Run only the Commands system, so the clock does not advance.
    CommandSystem.execute(&mut ctx).unwrap();
    assert_eq!(ctx.world.hash(), before);
    assert!(ctx.commands.applied_this_turn().is_empty());
    ctx.commands.enqueue(Cmd(true));
    CommandSystem.execute(&mut ctx).unwrap();
    assert_ne!(ctx.world.hash(), before);
}

#[test]
fn rejected_command_publishes_rejection() {
    let mut ctx = new_ctx(1);
    let seen: Log = Log::default();
    let s = Arc::clone(&seen);
    ctx.events.subscribe::<CommandRejected>(move |e, _| {
        s.lock()
            .unwrap()
            .push(format!("rejected:{}:{}", e.reason, e.turn));
        Ok(())
    });
    let s = Arc::clone(&seen);
    ctx.events.subscribe::<CommandApplied>(move |e, _| {
        s.lock().unwrap().push(format!("applied:{}", e.turn));
        Ok(())
    });
    ctx.commands.enqueue(Cmd(false));
    ctx.commands.enqueue(Cmd(true));
    assert_eq!(ctx.commands.len(), 2);
    let mut p = TurnPipeline::new(vec![Box::new(CommandSystem)]);
    p.advance(&mut ctx).unwrap();
    assert_eq!(*seen.lock().unwrap(), ["rejected:nope:0", "applied:0"]);
    assert_eq!(ctx.commands.len(), 0);
    assert_eq!(ctx.commands.applied_this_turn().len(), 1);
}

struct Mod(&'static str, Vec<&'static str>);

impl Module for Mod {
    fn id(&self) -> &'static str {
        self.0
    }
    fn requires(&self) -> &[&'static str] {
        &self.1
    }
    fn register(&self, _b: &mut SimBuilder) -> Result<(), SimError> {
        Ok(())
    }
}

fn mods(spec: &[(&'static str, &[&'static str])]) -> Vec<Box<dyn Module>> {
    spec.iter()
        .map(|(id, req)| Box::new(Mod(id, req.to_vec())) as Box<dyn Module>)
        .collect()
}

#[test]
fn module_order_puts_requires_first_and_rejects_cycles() {
    let m = mods(&[
        ("agents", &["world", "economy"]),
        ("economy", &["world"]),
        ("world", &[]),
    ]);
    assert_eq!(module_order(&m).unwrap(), [2, 1, 0]);
    // Stable: independent modules keep insertion order.
    let m = mods(&[("x", &[]), ("y", &[]), ("z", &["x"])]);
    assert_eq!(module_order(&m).unwrap(), [0, 1, 2]);
    let m = mods(&[("a", &["b"]), ("b", &["a"])]);
    assert!(matches!(module_order(&m), Err(SimError::Invalid(s)) if s.contains("cycle")));
    let m = mods(&[("a", &["ghost"])]);
    assert!(matches!(module_order(&m), Err(SimError::Invalid(s)) if s.contains("ghost")));
}

#[test]
fn random_for_equals_det_random_for_key() {
    let mut ctx = new_ctx(0xDEAD_BEEF_u64);
    ctx.clock_mut().turn = 7;
    let mut a = ctx.random_for("economy.pricing", 12, 3);
    let mut b = DetRandom::for_key(0xDEAD_BEEF, 7, det_hash::of("economy.pricing"), 12, 3);
    for _ in 0..8 {
        assert_eq!(a.next_u64(), b.next_u64());
    }
}

#[test]
fn is_due_uses_schedule_and_turn() {
    let mut ctx = new_ctx(1);
    let mut cadence = vec![
        TierCadence {
            period: 1,
            offset: 0
        };
        WorldLevel::COUNT
    ];
    cadence[0] = TierCadence {
        period: 4,
        offset: 0,
    };
    ctx.schedule = TierSchedule::new(&cadence).unwrap();
    ctx.clock_mut().turn = 8;
    assert!(ctx.is_due(WorldLevel::Continent));
    ctx.clock_mut().turn = 9;
    assert!(!ctx.is_due(WorldLevel::Continent));
}

#[test]
fn builder_overrides_replace_and_remove() {
    let log: Log = Log::default();
    let ctx = new_ctx(1);
    let SimContext {
        world, services, ..
    } = ctx;
    let mut b = SimBuilder::new(world, services);
    b.replace_system(fake("s1", SimPhase::Lod, 5, &log))
        .unwrap();
    b.remove_system("s2").unwrap();
    assert!(b.remove_system("s2").is_err());
    b.add_system(fake("s1", SimPhase::Lod, 0, &log)).unwrap();
    b.add_system(fake("s2", SimPhase::Lod, 0, &log)).unwrap();
    b.add_system(fake("s3", SimPhase::Lod, 0, &log)).unwrap();
    assert!(b.add_system(fake("s3", SimPhase::Lod, 0, &log)).is_err());
    let p = b.build_pipeline().unwrap();
    assert_eq!(p.system_ids(), ["s3", "s1"]);
    b.remove_system("ghost").unwrap();
    assert!(b.build_pipeline().is_err());
}

#[test]
fn csharp_system_list_sorts_to_fixture_order() {
    let json = fixture_json("build/systems.json");
    let log: Log = Log::default();
    let mut ids: Vec<&'static str> = Vec::new();
    let mut systems: Vec<Box<dyn System>> = Vec::new();
    for s in json["systems"].as_array().unwrap().iter().rev() {
        let id: &'static str = Box::leak(s["id"].as_str().unwrap().to_string().into_boxed_str());
        let phase = match s["phase"].as_str().unwrap() {
            "Commands" => SimPhase::Commands,
            "Lod" => SimPhase::Lod,
            "Environment" => SimPhase::Environment,
            "Production" => SimPhase::Production,
            "Agents" => SimPhase::Agents,
            "Markets" => SimPhase::Markets,
            "Finance" => SimPhase::Finance,
            "Guidance" => SimPhase::Guidance,
            "Cleanup" => SimPhase::Cleanup,
            other => panic!("phase {other}"),
        };
        systems.push(fake(id, phase, s["order"].as_i64().unwrap() as i32, &log));
    }
    for s in json["systems"].as_array().unwrap() {
        ids.push(Box::leak(
            s["id"].as_str().unwrap().to_string().into_boxed_str(),
        ));
    }
    let got = TurnPipeline::new(systems).system_ids();
    let got: Vec<&str> = got.to_vec();
    let want: Vec<&str> = ids.to_vec();
    // Within equal (phase, order) the id ordinal decides, so compare as sorted by that key.
    assert_eq!(got.len(), want.len());
    let pos = |id: &str| {
        json["systems"]
            .as_array()
            .unwrap()
            .iter()
            .position(|s| s["id"] == id)
            .unwrap()
    };
    for w in got.windows(2) {
        let (a, b) = (&json["systems"][pos(w[0])], &json["systems"][pos(w[1])]);
        assert!(
            pos(w[0]) < pos(w[1]),
            "{} before {} ({:?} {:?})",
            w[0],
            w[1],
            a,
            b
        );
    }
}

#[test]
fn ordinal_cmp_uses_utf16_code_units() {
    // U+FF61 sorts after U+10000 in UTF-16 (0xFF61 > 0xD800) but before it in UTF-8 bytes.
    assert_eq!(
        fes_sim::ordinal_cmp("\u{FF61}", "\u{10000}"),
        std::cmp::Ordering::Greater
    );
    assert_eq!(fes_sim::ordinal_cmp("a.b", "a.c"), std::cmp::Ordering::Less);
}

#[test]
fn config_tables_cover_every_index_seeding_uses() {
    // Seeding indexes these with enum values; fixed array lengths make bad data unrepresentable.
    let ctx = new_ctx(1);
    let c = &ctx.services.database.config;
    assert_eq!(c.producers_per_node.len(), WorldLevel::COUNT);
    assert!(c
        .producers_per_node
        .iter()
        .all(|r| r.len() == fes_core::IndustryTier::COUNT));
    assert_eq!(c.facility_start_cash.len(), fes_core::IndustryTier::COUNT);
    assert_eq!(c.group_start_cash.len(), 4);
}
