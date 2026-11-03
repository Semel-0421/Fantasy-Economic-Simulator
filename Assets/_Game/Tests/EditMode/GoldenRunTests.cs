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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "34308F7666066EB10EB7F1F5F7272908"),
            ("Default_Seed2", 2, 5, new string[0], "90954091B491029B5909AEDC7454FB0F"),
            ("Default_Seed3", 3, 5, new string[0], "0153B389445CC5BCC494B104E423EA5E"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "6C7650A1E5D49D3911A1932F51871460"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "3BA6EE35E26CC22010063B655208C0A6"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "5D9E55C9256510ED8336C1441B0B4AB1"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "E6BD526BECD0DE5F0290FE70573BC19D"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "D0FE60333F974FAEE9ACAC6F23A7E7AA"),
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
