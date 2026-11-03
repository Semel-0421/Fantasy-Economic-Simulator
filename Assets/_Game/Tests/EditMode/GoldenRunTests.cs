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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "3CB784B300AAB6F0574DD632B108949B"),
            ("Default_Seed2", 2, 5, new string[0], "5519382AAFB616A17FF3A0165F9E07A1"),
            ("Default_Seed3", 3, 5, new string[0], "D7F7E4C926E83324592E2FE0139A135E"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "A16E742B29C60EC7B38E663C64C8EE5F"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "A70208AA2A2301B34000F2138C3F0463"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "E3D139B18D8E1DC3AA83B4123AC4EADD"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "2A1D1AA0F3B0460EA801730EEB5E9EB4"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "82F05A4F39A6F8DF096BFE5D6B2AEF83"),
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
