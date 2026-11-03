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
            ("Default_Seed1_ThirtyYears", 1, 30, new string[0], "B4309C4C8D0BA4804F99977F147D5975"),
            ("Default_Seed2", 2, 5, new string[0], "01A245C9992C4EA1C08F6F777237C47B"),
            ("Default_Seed3", 3, 5, new string[0], "B3A1A056017ACEA15C61656B26373F55"),
            ("Aggregate", 1, 5, new[] { "lod.mode,2" }, "287B5A1C816D2F8ACB13BB6E6DACEE00"),
            ("Mixed", 1, 5, new[] { "lod.mode,0" }, "B52F7231D65EDD5B9CF03D65B746C081"),
            ("Rumors", 1, 5, new[] { "merchants.rumors,1" }, "436AACFC9BEF3C6C127D2552A7C9E94A"),
            ("PlainMerchants", 1, 5, new[] { "merchants.must_act,0", "merchants.bids,0", "merchants.weigh_sales,0", "merchants.judge_hops,1" }, "788620303CE99DD9DF3A93E07DAE02F6"),
            ("NoBills", 1, 5, new[] { "credit.bill_cap_weeks,0" }, "4E3939D634E91B225674C4ED14D56D9C"),
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
