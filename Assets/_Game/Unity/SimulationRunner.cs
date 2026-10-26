using System.IO;
using FES.Data;
using FES.Simulation;
using UnityEngine;

namespace FES.Unity
{
    /// <summary>
    /// Bridge between Unity and the simulation: builds a <see cref="GameSimulation"/> from
    /// StreamingAssets data and advances turns on request. The only place game code touches UnityEngine.
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [SerializeField] string dataFolder = "Data";
        [SerializeField] ulong seed = 12345;

        public GameSimulation Simulation { get; private set; }

        void Awake()
        {
            // TODO: on Android StreamingAssets lives inside the APK; copy the data files to
            // persistentDataPath (UnityWebRequest) before building, or ship them as TextAssets.
            var source = new FileDataSource(Path.Combine(Application.streamingAssetsPath, dataFolder));
            Simulation = SimulationBuilder.FromData(source, seed).Build();
            Debug.Log($"[FES] Simulation ready: {Simulation.Systems.Count} systems, turn {Simulation.Turn}.");
        }

        [ContextMenu("Advance Turn")]
        public void AdvanceTurn()
        {
            Simulation.AdvanceTurn();
            Debug.Log($"[FES] Turn {Simulation.Turn} done, {Simulation.LastTurnEvents.Count} events, hash {Simulation.ComputeHash():X16}.");
        }

        void OnDestroy()
        {
            Simulation?.Dispose();
            Simulation = null;
        }
    }
}
