using System.Collections.Generic;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    public class SpecialtyTests
    {
        const string NoEvents = "key,level,category,target,mul_permille,duration,chance_permille\n";

        // No variance and no events, so one week's output is exactly the recipe quantity times the multipliers.
        static GameSimulation BuildExact(int gradeUpPermille)
        {
            string config = "key,value\nproduction.variance_permille,0\n" +
                            $"specialty.grade_up_permille,{gradeUpPermille}\n";
            return SimulationBuilder.FromData(TestWorlds.SampleWith(
                (GameDatabase.ConfigFile, config), (GameDatabase.EventsFile, NoEvents)), 42).Build();
        }

        static int PrimaryFacility(GameSimulation sim, bool specialty)
        {
            var facilities = sim.World.Get<FacilityTable>();
            var specialties = sim.World.Get<NodeSpecialties>();
            return Enumerable.Range(0, facilities.Count).First(f =>
            {
                var def = sim.Database.Facilities[facilities.DefIndex[f]];
                return def.Tier == IndustryTier.Primary
                       && specialties.IsSpecialty(facilities.Node[f], def.Recipe.Outputs[0].Good.BaseIndex) == specialty;
            });
        }

        static List<CargoItem> WeekOfOutput(GameSimulation sim, int facility)
        {
            sim.Actions.BuyFacility(new FacilityId(facility), 0, GuidanceAxis.Agriculture);
            sim.AdvanceTurn();
            var stored = new List<CargoItem>();
            sim.View.GetWarehouse(new FacilityId(facility), stored);
            return stored;
        }

        [Test]
        public void Specialties_ComeFromTags()
        {
            var db = GameDatabase.Load(TestWorlds.Sample());
            Assert.IsTrue(db.TryGetSpecialty("plains", out var plains));
            Assert.IsTrue(db.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            Assert.IsTrue(db.TryGetGood("barley", QualityGrade.Mid, out var barley));
            CollectionAssert.AreEqual(new[] { wheat.BaseIndex, barley.BaseIndex }, plains.BaseGoods);
            Assert.IsTrue(db.TryGetSpecialty("pasture", out var pasture));
            Assert.AreEqual(1, pasture.BaseGoods.Length);
            Assert.IsFalse(db.TryGetSpecialty("swamp", out _));

            using var sim = TestWorlds.BuildSample();
            var view = sim.View;
            Assert.IsTrue(view.TryFindNode("aur_n1_r1_v9", out var hamlet));
            var keys = new List<string>();
            view.GetSpecialties(hamlet, keys);
            CollectionAssert.AreEqual(new[] { "wheat", "barley", "wool" }, keys);
            Assert.IsTrue(view.IsSpecialty(hamlet, GoodId.From(wheat.BaseIndex, QualityGrade.Best)));
        }

        [Test]
        public void SpecialtyData_IsValidated()
        {
            var badGood = TestWorlds.SampleWith(GameDatabase.SpecialtiesFile, "tag,category,good\nplains,grain,iron_ore\n");
            StringAssert.Contains("not in category", Assert.Throws<System.FormatException>(() => GameDatabase.Load(badGood)).Message);

            var nodes = TestWorlds.Sample().ReadText(CsvMapSource.NodesFile).Replace(",plains", ",swamp");
            var unknownTag = TestWorlds.SampleWith(CsvMapSource.NodesFile, nodes);
            var e = Assert.Throws<System.FormatException>(() => SimulationBuilder.FromData(unknownTag, 1).Build());
            StringAssert.Contains("swamp", e.Message);
        }

        [Test]
        public void Specialty_IsAlwaysCarried_InTheLevelsGrades()
        {
            // A village profile has no tools; a 'smithing' village carries them in the village grades.
            var nodes = TestWorlds.Sample().ReadText(CsvMapSource.NodesFile)
                .Replace("aur_n1_r1_c1_v1,Aurelia Village 1-1-1,Village,aur_n1_r1_c1,plains",
                         "aur_n1_r1_c1_v1,Aurelia Village 1-1-1,Village,aur_n1_r1_c1,plains|smithing");
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(CsvMapSource.NodesFile, nodes), 1).Build();
            var view = sim.View;
            Assert.IsTrue(view.TryFindNode("aur_n1_r1_c1_v1", out var smithing));
            Assert.IsTrue(view.TryFindNode("aur_n1_r1_c1_v2", out var plain));
            Assert.IsTrue(sim.Database.TryGetGood("iron_tools", QualityGrade.Low, out var low));
            Assert.IsTrue(view.TryGetQuote(smithing, low, out _));
            Assert.IsTrue(view.TryGetQuote(smithing, GoodId.From(low.BaseIndex, QualityGrade.Mid), out _));
            Assert.IsFalse(view.TryGetQuote(smithing, GoodId.From(low.BaseIndex, QualityGrade.High), out _));
            Assert.IsFalse(view.TryGetQuote(plain, low, out _));
        }

        [Test]
        public void Specialty_IsAnchoredLowerInItsOwnMarket()
        {
            using var sim = TestWorlds.BuildSample();
            var view = sim.View;
            Assert.IsTrue(view.TryFindNode("aur_n1_r1_c1_v1", out var plains));
            Assert.IsTrue(view.TryFindNode("aur_n1_r1_c1_v2", out var mountain));
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            Assert.IsTrue(view.TryGetQuote(plains, wheat, out var cheap));
            Assert.IsTrue(view.TryGetQuote(mountain, wheat, out var normal));
            int basePrice = sim.Database.Good(wheat).BasePrice;
            Assert.AreEqual(sim.Database.Config.SpecialtyPriceMultiplier.MulInt(basePrice), cheap.Price);
            Assert.AreEqual(basePrice, normal.Price);
        }

        [Test]
        public void SpecialtyOutput_GetsTheBonus()
        {
            using var sim = BuildExact(gradeUpPermille: 0);
            var facilities = sim.World.Get<FacilityTable>();
            int special = PrimaryFacility(sim, specialty: true);
            int plain = PrimaryFacility(sim, specialty: false);
            var specialLine = sim.Database.Facilities[facilities.DefIndex[special]].Recipe.Outputs[0];
            var plainLine = sim.Database.Facilities[facilities.DefIndex[plain]].Recipe.Outputs[0];

            var specialOut = WeekOfOutput(sim, special);
            sim.Actions.BuyFacility(new FacilityId(plain), 0, GuidanceAxis.Agriculture);
            sim.AdvanceTurn();
            var plainOut = new List<CargoItem>();
            sim.View.GetWarehouse(new FacilityId(plain), plainOut);

            Assert.AreEqual(sim.Database.Config.SpecialtyOutputMultiplier.MulInt(specialLine.Quantity), specialOut.Single().Quantity);
            Assert.AreEqual(specialLine.Good, specialOut.Single().Good);
            Assert.AreEqual(plainLine.Quantity, plainOut.Single().Quantity);
        }

        [Test]
        public void SpecialtyOutput_CanComeOutOneGradeHigher()
        {
            using var sim = BuildExact(gradeUpPermille: 1000);
            var facilities = sim.World.Get<FacilityTable>();
            int special = PrimaryFacility(sim, specialty: true);
            var line = sim.Database.Facilities[facilities.DefIndex[special]].Recipe.Outputs[0];

            var output = WeekOfOutput(sim, special);
            var higher = GoodId.From(line.Good.BaseIndex, line.Good.Grade + 1);
            Assert.AreEqual(higher, output.Single().Good);
            Assert.AreEqual(sim.Database.Config.SpecialtyOutputMultiplier.MulInt(line.Quantity), output.Single().Quantity);
        }
    }
}
