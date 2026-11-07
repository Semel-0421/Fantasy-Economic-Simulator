using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FES.Core;
using FES.Data;
using FES.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace FES.Tests
{
    /// <summary>
    /// Writes the parity fixtures the Rust port compares against (engine/fixtures, see its README).
    /// Explicit: run it by name when the C# reference changes. It reads simulation state only through
    /// public members and changes nothing.
    /// </summary>
    public class ParityFixtureExporter
    {
        const int Year = 52;
        const ulong PrimitiveSeed = 0xF1E5;
        const int FxRows = 300;
        const int HashRows = 100;

        static readonly string[] KeptRuns = { "Default_Seed1_ThirtyYears", "Default_Seed2", "Default_Seed3", "NoBills" };
        static readonly int[] StepTurns = { 0, 1, 3, 51, 52, 53, 103, 104 };
        const string SteppedRun = "Default_Seed1_ThirtyYears";

        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "engine", "fixtures"));

        [Test, Explicit, Timeout(int.MaxValue)]
        public void WritesParityFixtures()
        {
            Directory.CreateDirectory(Root);
            WritePrimitives();
            WriteData();
            WriteBuild();
            foreach (var run in GoldenRuns().Where(r => KeptRuns.Contains(r.name)))
                ExportRun(run.name, run.seed, run.years, run.config, run.hash);
        }

        // The golden runs, read from GoldenRunTests so the hashes have one source.
        static IEnumerable<(string name, ulong seed, int years, string[] config, string hash)> GoldenRuns()
        {
            var field = typeof(GoldenRunTests).GetField("Runs", BindingFlags.NonPublic | BindingFlags.Static);
            return ((string, ulong, int, string[], string)[])field.GetValue(null);
        }

        // ---------------------------------------------------------------- runs

        static void ExportRun(string name, ulong seed, int years, string[] config, string expectedHash)
        {
            string dir = Path.Combine(Root, "runs", name);
            Directory.CreateDirectory(dir);
            var data = config.Length == 0 ? TestWorlds.Sample() : TestWorlds.SampleConfig(config);
            bool stepped = name == SteppedRun;
            int lastStep = StepTurns.Max();

            using var sim = SimulationBuilder.FromData(data, seed).Build();
            var twin = stepped ? SimulationBuilder.FromData(data, seed).Build() : null;
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "start.save"), SaveBytes(sim));
                File.WriteAllText(Path.Combine(dir, "turn0.json"), SectionRecord(sim) + "\n", new UTF8Encoding(false));
                if (stepped) Directory.CreateDirectory(Path.Combine(dir, "steps"));

                using var turns = new StreamWriter(Path.Combine(dir, "turns.json"), false, new UTF8Encoding(false));
                turns.Write("{\"name\":" + ParityJson.Serialize(name) + ",\"seed\":" + ParityJson.Serialize(seed) +
                            ",\"config\":" + ParityJson.Serialize(config) + ",\"turns\":[\n");
                int total = years * Year;
                for (int t = 0; t < total; t++)
                {
                    if (stepped && StepTurns.Contains(t)) StepTurn(sim, Path.Combine(dir, "steps", $"turn_{t}.bin.gz"));
                    else sim.AdvanceTurn();

                    if (twin != null)
                    {
                        twin.AdvanceTurn();
                        Assert.AreEqual(twin.ComputeHash(), sim.ComputeHash(), $"{name}: turn {t} differs from AdvanceTurn on a twin.");
                        if (t >= lastStep) { twin.Dispose(); twin = null; }
                    }

                    turns.Write(TurnRecord(sim));
                    turns.Write(t + 1 < total ? ",\n" : "\n");
                }

                string hash = Sha256_32(SaveBytes(sim));
                turns.Write("],\"final_sha256_32\":" + ParityJson.Serialize(hash) + "}\n");
                Assert.AreEqual(expectedHash, hash, $"{name}: final save hash differs from GoldenRunTests.");
            }
            finally
            {
                twin?.Dispose();
            }
        }

        static string TurnRecord(GameSimulation sim) => SectionRecord(sim, true);

        static string SectionRecord(GameSimulation sim, bool withTurn = false)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            if (withTurn) sb.Append("\"turn\":").Append(sim.Turn).Append(',');
            sb.Append("\"world_hash\":").Append(ParityJson.Serialize(sim.ComputeHash()));
            sb.Append(",\"sections\":{");
            bool first = true;
            foreach (var p in sim.World.PersistentComponents)
            {
                if (!first) sb.Append(',');
                first = false;
                using var stream = new HashingStream();
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) p.Write(writer);
                ParityJson.WriteString(sb, p.Key);
                sb.Append(':').Append(ParityJson.Serialize(stream.Hash));
            }
            sb.Append("}}");
            return sb.ToString();
        }

        /// <summary>
        /// One turn as TurnPipeline.Advance runs it, from public members, recording every section whose bytes
        /// change after each system and after each phase's dispatch. EventBus.BeginTurn (internal) is skipped:
        /// it only clears the presentation turn log, which no system reads.
        /// SimClock.Advance (internal) is replaced by rewriting the clock through its public Write/Read.
        /// </summary>
        static void StepTurn(GameSimulation sim, string path)
        {
            var ctx = sim.Context;
            int turn = ctx.Clock.Turn;
            byte[] before = SaveBytes(sim);
            var last = Sections(sim);
            var records = new List<(string label, ulong hash, List<(string key, int version, byte[] bytes)> changed)>();

            void Snapshot(string label)
            {
                var now = Sections(sim);
                var changed = new List<(string, int, byte[])>();
                foreach (var kv in now)
                    if (!last.TryGetValue(kv.Key, out var old) || !old.bytes.AsSpan().SequenceEqual(kv.Value.bytes))
                        changed.Add((kv.Key, kv.Value.version, kv.Value.bytes));
                records.Add((label, sim.ComputeHash(), changed));
                last = now;
            }

            var systems = sim.Systems;
            int i = 0;
            while (i < systems.Count)
            {
                var phase = systems[i].Phase;
                for (; i < systems.Count && systems[i].Phase == phase; i++)
                {
                    systems[i].Execute(ctx);
                    Snapshot("system:" + systems[i].Id);
                }
                ctx.Events.Dispatch();
                Snapshot("dispatch:" + phase);
            }
            AdvanceClock(ctx.Clock);
            Snapshot("clock");

            using var file = File.Create(path);
            using var gz = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal);
            using var w = new BinaryWriter(gz, Encoding.UTF8);
            w.Write(Encoding.ASCII.GetBytes("FESSTEP1"));
            w.Write(turn);
            w.Write(before.Length);
            w.Write(before);
            w.Write(records.Count);
            foreach (var (label, hash, changed) in records)
            {
                w.Write(label);
                w.Write(hash);
                w.Write(changed.Count);
                foreach (var (key, version, bytes) in changed)
                {
                    w.Write(key);
                    w.Write(version);
                    w.Write(bytes.Length);
                    w.Write(bytes);
                }
            }
        }

        static void AdvanceClock(SimClock clock)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(clock.Turn + 1);
                writer.Write(clock.Seed);
            }
            buffer.Position = 0;
            using var reader = new BinaryReader(buffer, Encoding.UTF8);
            clock.Read(reader);
        }

        static Dictionary<string, (int version, byte[] bytes)> Sections(GameSimulation sim)
        {
            var result = new Dictionary<string, (int, byte[])>(StringComparer.Ordinal);
            foreach (var p in sim.World.PersistentComponents)
            {
                using var buffer = new MemoryStream();
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true)) p.Write(writer);
                result.Add(p.Key, (p.Version, buffer.ToArray()));
            }
            return result;
        }

        static byte[] SaveBytes(GameSimulation sim)
        {
            using var buffer = new MemoryStream();
            sim.Save(buffer);
            return buffer.ToArray();
        }

        static string Sha256_32(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 32);
        }

        // ---------------------------------------------------------------- build

        static void WriteBuild()
        {
            string dir = Path.Combine(Root, "build");
            Directory.CreateDirectory(dir);
            using var sim = SimulationBuilder.FromData(TestWorlds.Sample(), 1).Build();
            var nodes = sim.World.Get<FES.World.NodeTable>();
            var routes = sim.World.Get<FES.World.RouteTable>();

            var nodeRows = new List<object>();
            for (int n = 0; n < nodes.Count; n++)
            {
                var children = new List<int>();
                for (int k = nodes.ChildStart[n]; k < nodes.ChildStart[n] + nodes.ChildCount[n]; k++) children.Add(nodes.Children[k]);
                nodeRows.Add(new
                {
                    key = nodes.KeyOf(n),
                    level = (int)nodes.Level[n],
                    parent = nodes.Parent[n],
                    continent = nodes.Continent[n],
                    core = nodes.Core[n],
                    child_start = nodes.ChildStart[n],
                    child_count = nodes.ChildCount[n],
                    children,
                });
            }
            var routeRows = new List<object>();
            for (int r = 0; r < routes.Count; r++)
                routeRows.Add(new { from = routes.From[r], to = routes.To[r], travel_turns = routes.TravelTurns[r], cost_per_unit = routes.CostPerUnit[r], base_fee = routes.BaseFee[r], terrain = (int)routes.Terrain[r] });
            var outgoing = new List<object>();
            for (int n = 0; n < nodes.Count; n++)
            {
                var idx = new List<int>();
                for (int k = routes.OutStart[n]; k < routes.OutStart[n] + routes.OutCount[n]; k++) idx.Add(routes.RouteIndex[k]);
                outgoing.Add(new { out_start = routes.OutStart[n], out_count = routes.OutCount[n], routes = idx });
            }
            Write(Path.Combine(dir, "nodes.json"), new { nodes = nodeRows, routes = routeRows, outgoing });

            var systems = new List<object>();
            foreach (var s in sim.Systems) systems.Add(new { id = s.Id, phase = s.Phase, order = s.Order });
            var modules = new List<object>();
            foreach (var m in ModuleOrder.Sort(new List<ISimModule>(SimulationBuilder.DefaultModules())))
                modules.Add(new { id = m.Id, requires = m.Requires });
            Write(Path.Combine(dir, "systems.json"), new { systems, modules });
        }

        // ---------------------------------------------------------------- data

        static void WriteData()
        {
            string dir = Path.Combine(Root, "data");
            Directory.CreateDirectory(dir);
            var source = TestWorlds.Sample();
            var db = GameDatabase.Load(source);
            var map = new CsvMapSource(source).LoadMap();

            Write(Path.Combine(dir, "config_effective.json"), db.Config.EffectiveValues);
            Write(Path.Combine(dir, "config_fields.json"), db.Config);
            Write(Path.Combine(dir, "config_defaults_effective.json"), SimulationConfig.Load(null).EffectiveValues);

            var specialties = new List<SpecialtyRule>();
            foreach (string tag in SpecialtyTags(source.ReadText(GameDatabase.SpecialtiesFile)))
                if (db.TryGetSpecialty(tag, out var rule)) specialties.Add(rule);

            Write(Path.Combine(dir, "definitions.json"), new
            {
                db.Categories,
                db.Goods,
                db.Recipes,
                db.Facilities,
                db.Events,
                db.MarketProfiles,
                Specialties = specialties,
                Map = new { map.Nodes, map.Routes },
            });

            ulong mapHash = map.ComputeHash();
            Write(Path.Combine(dir, "hashes.json"), new
            {
                content_hash = db.ContentHash,
                map_hash = mapHash,
                data_hash = DetHash.Combine(db.ContentHash, mapHash),
                config_hash = db.Config.ComputeHash(),
            });
        }

        // Distinct first-column values of specialties.csv, in file order (comments and header skipped).
        static IEnumerable<string> SpecialtyTags(string csv)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool header = true;
            foreach (string raw in csv.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (header) { header = false; continue; }
                string tag = line.Split(',')[0].Trim();
                if (seen.Add(tag)) yield return tag;
            }
        }

        static void Write(string path, object value) =>
            File.WriteAllText(path, ParityJson.Serialize(value) + "\n", new UTF8Encoding(false));

        // ---------------------------------------------------------------- primitives

        static readonly long[] Edges =
        {
            0, 1, -1, 1L << 31, -(1L << 31), (1L << 31) + 1, -((1L << 31) + 1),
            long.MaxValue >> 16, -(long.MaxValue >> 16), 0x8000, -0x8000,
        };

        static void WritePrimitives()
        {
            var rng = new DetRandom(PrimitiveSeed);
            var fx = new List<object>();
            var skipped = new List<object>();
            var unary = new (string op, Func<long, long> f)[]
            {
                ("FromInt", a => Fx.FromInt(a).Raw),
                ("FromPermille", a => Fx.FromPermille(a).Raw),
                ("RoundShift", Fx.RoundShift),
            };
            var binary = new (string op, Func<long, long, long> f)[]
            {
                ("FromRatio", (a, b) => Fx.FromRatio(a, b).Raw),
                ("MulInt", (a, b) => Fx.FromRaw(a).MulInt(b)), // a: Fx raw, b: integer value
                ("MulIntRaw", Fx.MulIntRaw), // a: integer value, b: Fx raw
                ("+", (a, b) => (Fx.FromRaw(a) + Fx.FromRaw(b)).Raw),
                ("-", (a, b) => (Fx.FromRaw(a) - Fx.FromRaw(b)).Raw),
                ("*", (a, b) => (Fx.FromRaw(a) * Fx.FromRaw(b)).Raw),
                ("/", (a, b) => (Fx.FromRaw(a) / Fx.FromRaw(b)).Raw),
            };

            foreach (var (op, f) in unary)
                FxRowsFor(op, (a, _) => f(a), false, ref rng, fx, skipped);
            foreach (var (op, f) in binary)
                FxRowsFor(op, f, true, ref rng, fx, skipped);

            var json = new
            {
                fx,
                fx_skipped = skipped,
                det_hash = DetHashRows(ref rng),
                det_random = DetRandomRows(ref rng),
            };
            Write(Path.Combine(Root, "primitives.json"), json);
        }

        static void FxRowsFor(string op, Func<long, long, long> f, bool isBinary, ref DetRandom rng, List<object> rows, List<object> skipped)
        {
            var inputs = new List<(long a, long b)>();
            foreach (long a in Edges)
            {
                if (!isBinary) { inputs.Add((a, 0)); continue; }
                foreach (long b in Edges) inputs.Add((a, b));
            }

            int produced = 0, next = 0;
            while (produced < FxRows)
            {
                var (a, b) = next < inputs.Count ? inputs[next] : (RandomLong(ref rng), isBinary ? RandomLong(ref rng) : 0);
                next++;
                try
                {
                    long result = f(a, b);
                    rows.Add(new { op, a, b, result });
                    produced++;
                }
                catch (OverflowException e)
                {
                    skipped.Add(new { op, a, b, exception = e.GetType().Name });
                }
            }
        }

        // Any magnitude: a random 64-bit pattern shifted right 0..63 bits, then a random sign.
        static long RandomLong(ref DetRandom rng)
        {
            long v = (long)(rng.NextULong() >> rng.NextInt(64));
            return rng.NextInt(2) == 0 ? v : unchecked(-v);
        }

        static object DetHashRows(ref DetRandom rng)
        {
            var mix = new List<object[]>();
            var combine = new List<object[]>();
            var hash = new List<object[]>();
            var of = new List<object[]>();

            foreach (ulong x in new[] { 0UL, 1UL, ulong.MaxValue }) mix.Add(new object[] { x, DetHash.Mix(x) });
            while (mix.Count < HashRows)
            {
                ulong x = rng.NextULong();
                mix.Add(new object[] { x, DetHash.Mix(x) });
            }

            combine.Add(new object[] { 0UL, 0UL, DetHash.Combine(0, 0) });
            combine.Add(new object[] { ulong.MaxValue, ulong.MaxValue, DetHash.Combine(ulong.MaxValue, ulong.MaxValue) });
            while (combine.Count < HashRows)
            {
                ulong a = rng.NextULong(), b = rng.NextULong();
                combine.Add(new object[] { a, b, DetHash.Combine(a, b) });
            }

            hash.Add(new object[] { 0UL, 0, 0U, 0, 0U, DetHash.Hash(0, 0, 0, 0, 0) });
            hash.Add(new object[] { ulong.MaxValue, -1, uint.MaxValue, -1, uint.MaxValue, DetHash.Hash(ulong.MaxValue, -1, uint.MaxValue, -1, uint.MaxValue) });
            while (hash.Count < HashRows)
            {
                ulong seed = rng.NextULong();
                int turn = rng.NextInt(2) == 0 ? rng.NextInt(5000) : (int)rng.NextUInt();
                uint stream = rng.NextUInt();
                int entity = rng.NextInt(2) == 0 ? rng.NextInt(100000) : (int)rng.NextUInt();
                uint salt = rng.NextInt(2) == 0 ? 0 : rng.NextUInt();
                hash.Add(new object[] { seed, turn, stream, entity, salt, DetHash.Hash(seed, turn, stream, entity, salt) });
            }

            var texts = new List<string> { "", "a", "economy.pricing", "core.clock", "가나다", "é", "\u0001￿", "config.csv" };
            while (texts.Count < HashRows)
            {
                var sb = new StringBuilder();
                int length = rng.NextInt(1, 24);
                for (int i = 0; i < length; i++) sb.Append((char)rng.NextInt(0x20, 0x7f));
                texts.Add(sb.ToString());
            }
            foreach (string text in texts) of.Add(new object[] { text, DetHash.Of(text) });

            return new { mix, combine, hash, of };
        }

        static object DetRandomRows(ref DetRandom rng)
        {
            var seeds = new List<ulong> { 0, 1, 42, PrimitiveSeed, ulong.MaxValue };
            while (seeds.Count < 10) seeds.Add(rng.NextULong());
            var rows = new List<object>();
            foreach (ulong seed in seeds)
            {
                rows.Add(new
                {
                    seed,
                    next_ulong = Draw(seed, r => (object)r.NextULong()),
                    next_int_1000 = Draw(seed, r => (object)r.NextInt(1000)),
                    next_fx01_raw = Draw(seed, r => (object)r.NextFx01()),
                    chance_300 = Draw(seed, r => (object)r.ChancePermille(300)),
                    spread_250_raw = Draw(seed, r => (object)r.NextSpread(250)),
                });
            }
            return rows;
        }

        // Eight draws from a fresh generator seeded with seed.
        static object[] Draw(ulong seed, Func<DetRandomBox, object> draw)
        {
            var box = new DetRandomBox(seed);
            var result = new object[8];
            for (int i = 0; i < 8; i++) result[i] = draw(box);
            return result;
        }

        // Lets a lambda advance one generator across calls.
        sealed class DetRandomBox
        {
            DetRandom _r;
            public DetRandomBox(ulong seed) => _r = new DetRandom(seed);
            public ulong NextULong() => _r.NextULong();
            public int NextInt(int max) => _r.NextInt(max);
            public Fx NextFx01() => _r.NextFx01();
            public bool ChancePermille(int permille) => _r.ChancePermille(permille);
            public Fx NextSpread(int spread) => _r.NextSpread(spread);
        }
    }
}
