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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "184CEE5393D933578FB2F9DD579B559A"),
            ("Default_Seed2", 2, 5, new string[0], "A8406D56D6DC8ADBB92A78E6D4DC602E"),
            ("Default_Seed3", 3, 5, new string[0], "9C1597EA99DBAD056135DD3451579FEC"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "A16E742B29C60EC7B38E663C64C8EE5F"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "9B773E8703E68990B9432A50415C3AAA"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "7FD3FBD5EA8956A9D783FFFC1F330972"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "7CA0A2861390D6A31E4664B2F25A8F0E"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "A0E61C4F8AEDD3FD3E86D890F3DD5EC6"),
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
