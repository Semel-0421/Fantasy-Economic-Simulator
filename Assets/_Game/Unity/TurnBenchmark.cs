#if FES_BENCHMARK
using System.Collections;
using System.Diagnostics;
using System.Linq;
using FES.Data;
using FES.Simulation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FES.Unity
{
    /// <summary>
    /// Built only into the benchmark player (scripting define FES_BENCHMARK): runs the full-scale world from
    /// StreamingAssets/Benchmark through a new year and logs what each turn costs on the real platform.
    /// </summary>
    public sealed class TurnBenchmark : MonoBehaviour
    {
        const int LastTurn = 54;
        const long TimeLimitMs = 1500000;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Begin() => new GameObject(nameof(TurnBenchmark)).AddComponent<TurnBenchmark>();

        IEnumerator Start()
        {
            Application.runInBackground = true;
            IDataSource source = null;
            yield return StreamingAssetsData.Load("Benchmark", loaded => source = loaded);
            var all = Stopwatch.StartNew();
            using var sim = SimulationBuilder.FromData(source, 1).Build();
            Debug.Log($"[FES bench] build={all.ElapsedMilliseconds}ms lod={sim.Database.Config.LodMode}");
            all.Restart();
            while (sim.Turn < LastTurn && all.ElapsedMilliseconds < TimeLimitMs)
            {
                int turn = sim.Turn;
                var one = Stopwatch.StartNew();
                sim.AdvanceTurn();
                var systems = sim.Systems.Select((s, i) => (s.Id, ms: sim.LastSystemDuration(i).TotalMilliseconds)).OrderByDescending(x => x.ms).Take(5);
                Debug.Log($"[FES bench] turn={turn} ms={one.ElapsedMilliseconds} {string.Join(", ", systems.Select(x => $"{x.Id}={x.ms:0}"))}");
                yield return null;
            }
            Debug.Log($"[FES bench] done turn={sim.Turn} total={all.ElapsedMilliseconds}ms");
        }
    }
}
#endif
