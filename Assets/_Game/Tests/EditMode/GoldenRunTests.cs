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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "B1C85DA2776319B77F66BF0B39F46358"),
            ("Default_Seed2", 2, 5, new string[0], "C480A95CA1B2006AD84DE49E9171AADA"),
            ("Default_Seed3", 3, 5, new string[0], "87871B57E443B46FCE3A26E7761172F2"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "A0B082E1756B808CCB7F48C3106BFEF9"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "CD369B9A5BAD1EA51536A5E77AC2ACAD"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "6B699D347A6B89A7DC34C41B69DB387B"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "211F8F654ECE3E58D58E228470530C2E"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "0B9C7AC3D751E690637370A9208BD4C6"),
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
