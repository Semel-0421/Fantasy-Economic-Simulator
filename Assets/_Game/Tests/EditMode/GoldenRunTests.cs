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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "F0192F25685A8BF90F2646A6160A1490"),
            ("Default_Seed2", 2, 5, new string[0], "80D2296DF16C8E6DBAFC5F7B23AC6A38"),
            ("Default_Seed3", 3, 5, new string[0], "8CBA20A37EB92A4E9C12CC44800FF626"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "EE033C1B0C6CCBFDFA95A4ABFF8CF720"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "D5CD8E057341C7BE96C5E7936187A38F"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "3E6D8CE63A3B221E9C8E4B7B6F70ED0F"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "56390FF03008F28F8233C5D0DFFAEFFA"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "164EFE072BB314F8EEA54E0674D90893"),
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
