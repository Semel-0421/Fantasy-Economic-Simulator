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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "F93CA215B3713C04CCA453D5880C069F"),
            ("Default_Seed2", 2, 5, new string[0], "50B290AEDC1475F3DB92A1545948DC6F"),
            ("Default_Seed3", 3, 5, new string[0], "7367C3FF22BA54F9E1CCD369A3CA9281"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "6C4F0BAE0832F219CC347EA7331194CA"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "2D8CD3EA42F9B11EBCC3E5BB8C7DBB75"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "0FEDE5A723EE765E3667561A8A66CD3F"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "230096B4055A117ACB54EF6BF053082D"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "41F726CDC8C1325FF2D26E4B02212618"),
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
