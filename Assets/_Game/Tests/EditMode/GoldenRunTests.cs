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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "7A6273507D58BEB1866A746F084125BE"),
            ("Default_Seed2", 2, 5, new string[0], "BF864D82ACF493D7278E67BF76C2A66D"),
            ("Default_Seed3", 3, 5, new string[0], "935523FE5131D1378CA9F878E353F5B5"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "D7E85567C681125263E4B37770E8EB54"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "C2BFF82FAC0DDF268B98D7CF8C3B38E8"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "E880FE6C191F81EA76A0470EECE80BB4"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "22124325D666216DE2AEEF3AEB546F52"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "5360225731BAA60B12C9903722D13168"),
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
