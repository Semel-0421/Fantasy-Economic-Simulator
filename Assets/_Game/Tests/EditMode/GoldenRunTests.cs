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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "D00B63D2442ACB85663308CB08A1CDE6"),
            ("Default_Seed2", 2, 5, new string[0], "CCA65F1290659AC1BFCC7B6A860F663E"),
            ("Default_Seed3", 3, 5, new string[0], "0D4E07B6EBC366A9B14C116C3888269E"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "670AA573A324096FDBFE331646183F7D"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "6F3ADABC1B0EF9319358CE1A18C6C3F9"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "D1E4A1A2F47D742317FE291B2CA01621"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "FBD85D71C87CB6140561746FDC833E2E"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "ED6C965B4C8A6557144F61A93CCC6EC8"),
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
