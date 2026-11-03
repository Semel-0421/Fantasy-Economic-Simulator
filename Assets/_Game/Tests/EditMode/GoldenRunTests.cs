using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FES.Simulation;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>
    /// A refactoring keeps behaviour (Docs/GDD/05_Refactoring_Standards.md), so these runs must save the same bytes.
    /// A change meant to alter the simulation records the new hashes in the same commit.
    /// </summary>
    public class GoldenRunTests
    {
        const int Year = 52;

        static readonly (string name, ulong seed, int years, string[] config, string hash)[] Runs =
        {
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "9F68205A9A297FF41F97CC303A7303DE"),
            ("Default_Seed2", 2, 5, new string[0], "17C87D6456FA0E67F873379BE19616A6"),
            ("Default_Seed3", 3, 5, new string[0], "2558AA24FDCAC96081F9ABCF4EAB3661"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "D4F1A0815453F1C0F9E02EE776033EED"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "B66E50E165B8CB575D9B5E920F7A2956"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "3AD2A2A7DD0C56AF13F0F05F30EF7C99"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "BB25063804C331F63CEEE3923035AD7B"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "F6CAC8FEFE0776BDF86E988316A32264"),
        };

        static IEnumerable<TestCaseData> Cases() => Runs.Select(r => new TestCaseData(r.seed, r.years, r.config, r.hash).SetName("GoldenRun_" + r.name));

        [TestCaseSource(nameof(Cases))]
        public void SavedState_IsUnchanged(ulong seed, int years, string[] config, string hash)
        {
            Assert.AreEqual(hash, StateHash(seed, years * Year, config),
                "The simulation changed. A refactoring must not change it; a change meant to alter it records the new hash here.");
        }

        static string StateHash(ulong seed, int turns, string[] config)
        {
            var data = config.Length == 0 ? TestWorlds.Sample() : TestWorlds.SampleConfig(config);
            using var sim = SimulationBuilder.FromData(data, seed).Build();
            for (int t = 0; t < turns; t++) sim.AdvanceTurn();
            using var buffer = new MemoryStream();
            sim.Save(buffer);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(buffer.ToArray())).Replace("-", "").Substring(0, 32);
        }
    }
}
