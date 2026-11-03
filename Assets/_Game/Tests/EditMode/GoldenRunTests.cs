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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "D4911E1158EFFD2069AC1C4B16459290"),
            ("Default_Seed2", 2, 5, new string[0], "3A2ECFB91E263B80FBBBFC0B72CC28F7"),
            ("Default_Seed3", 3, 5, new string[0], "D6C4E8F2906C2A43A0AD4312923FB568"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "AB46D9E919BCA196BCE5E37A07F57A79"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "E8D9574BA0D4D10C4D4F10F86BE74B75"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "10FCF10FE3A9DD6C084B906C1EB76C03"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "97070FB3A8980F9DA3CAF475623BF56C"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "0FC4A6A9902D80757EB8BA54A14168AF"),
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
