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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "70556C0EB09792888E5B155202F2ECF0"),
            ("Default_Seed2", 2, 5, new string[0], "41B94E50EB1F7AD1659E66149AF62E9E"),
            ("Default_Seed3", 3, 5, new string[0], "2DB0C9EC504814D115DB9457D644099D"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "C75C8B812861F3CE19DC6953D836CF42"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "72F1B40041B68CFD98C9F3AB14B67E89"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "B4F2889E316B7558CAB238B6CA7FAF4D"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "695087FA4601A1303C2E7D3A4F60EE20"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "F6EDAF050815D9062619D515AE231D14"),
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
