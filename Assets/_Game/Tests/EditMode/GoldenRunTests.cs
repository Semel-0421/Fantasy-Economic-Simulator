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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "A4D013DBAE5475D392BC6C1876F19190"),
            ("Default_Seed2", 2, 5, new string[0], "16C66BF286A7ABCAADA9933D32102B49"),
            ("Default_Seed3", 3, 5, new string[0], "0DD538A1E49F6E08B0395596974DE096"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "A16E742B29C60EC7B38E663C64C8EE5F"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "FF8BFAB953756AC5901B1B5314E9A885"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "4809F7BB658C4371B4F0ACAF9F7678FF"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "C7775CD3FFA61D1DFD5ADD6EF2B2E92D"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "3CD6DC560A4C27F0685B2B63E8DFCF68"),
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
