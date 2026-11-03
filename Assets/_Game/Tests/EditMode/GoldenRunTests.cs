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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "DFD186F94CE80AFF7971C80B7B7314B4"),
            ("Default_Seed2", 2, 5, new string[0], "FDB26D54AAFCCA45CDF37A4253C93A8D"),
            ("Default_Seed3", 3, 5, new string[0], "986CC6E7636020FA23F272C150BC197F"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "298E5F67382521969A881DDBBB02AEF0"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "0ECDCCF94B923592FDD25801640BE5D3"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "01F55DD52E249BA1C01FFBCEA331979C"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "40E9B070A8DC88945C4392BA2A19F484"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "3673AFF917FC5F93251ACEF15E91CC77"),
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
