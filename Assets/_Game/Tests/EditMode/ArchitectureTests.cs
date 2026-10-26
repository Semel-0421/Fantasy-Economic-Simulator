using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FES.Tests
{
    /// <summary>
    /// The simulation must stay engine-independent (headless tests, worker threads, future server use).
    /// asmdef noEngineReferences cannot be used because NativeArray and the job system live in
    /// UnityEngine.CoreModule, so this test enforces the rule at the source level instead.
    /// </summary>
    public class ArchitectureTests
    {
        static readonly string[] SimulationFolders = { "Core", "Data", "World", "Economy", "Agents", "Persistence", "Simulation" };

        [Test]
        public void SimulationAssemblies_DoNotUseUnityEngine()
        {
            string root = Path.Combine(Application.dataPath, "_Game");
            var offenders = SimulationFolders
                .SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
                .Where(file => File.ReadAllText(file).Contains("UnityEngine"))
                .Select(file => file.Substring(root.Length + 1))
                .ToArray();
            CollectionAssert.IsEmpty(offenders, "Only FES.Unity may reference UnityEngine.");
        }

        [Test]
        public void SimulationAssemblies_DoNotUseNonDeterministicApis()
        {
            string root = Path.Combine(Application.dataPath, "_Game");
            string[] banned = { "System.Random", "DateTime.Now", "UnityEngine.Random", "GetHashCode()", "float ", "double " };
            var offenders = SimulationFolders
                .SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
                .SelectMany(file => File.ReadAllLines(file)
                    .Select((line, i) => (file, line, i))
                    .Where(x => !x.line.TrimStart().StartsWith("//") && !x.line.TrimStart().StartsWith("///"))
                    .Where(x => banned.Any(b => x.line.Contains(b)) && !x.line.Contains("ToDouble") && !x.line.Contains("override int GetHashCode"))
                    .Select(x => $"{x.file.Substring(root.Length + 1)}:{x.i + 1}: {x.line.Trim()}"))
                .ToArray();
            CollectionAssert.IsEmpty(offenders, "Simulation state must use integer / Fx math and DetRandom.");
        }
    }
}
