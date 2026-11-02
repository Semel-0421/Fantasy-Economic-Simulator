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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "F9C05F8C6B0B9EF1B6B5EFEC8ED0BF89"),
            ("Default_Seed2", 2, 5, new string[0], "D96DFF2B3AA1B6BFC476F87A5C1E630B"),
            ("Default_Seed3", 3, 5, new string[0], "ACFBFE85A00CECA9B3BDF0227FC473B1"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "8D34F651E1040D76C9926082A9318A54"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "E92C8F213998094BCBB3DF131FBE0E88"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "FA21B8290511C95E77758A7CD82AEFF5"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "4D30AFA6D21CFE8B75C3D7E9E2E4DEC4"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "519FE6EB667B5D4273801C28AE1E77D6"),
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
