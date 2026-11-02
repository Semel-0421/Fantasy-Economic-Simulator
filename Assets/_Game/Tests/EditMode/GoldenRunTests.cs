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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "3DE62A675E31147D3AD5CA975D94B5F2"),
            ("Default_Seed2", 2, 5, new string[0], "53B142A15D2595FA2B25E0C6041C9B04"),
            ("Default_Seed3", 3, 5, new string[0], "9D73FE81B252BD686B1E0304D3E92443"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "E1B08723876B12B4F114459D4D60965B"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "F31F8724FD0AC1B210232C3642349D77"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "2F6FE7C8B2DFF54775C9174DADD5C42E"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "B53FB1133CF8BC2B0D4D9C944679A433"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "490E0778A5F769ED576147846B164CF0"),
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
