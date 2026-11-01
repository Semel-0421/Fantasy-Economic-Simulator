using System;
using FES.Core;
using FES.Data;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    public class DataAndWorldTests
    {
        [Test]
        public void Csv_HandlesQuotesCommentsAndBlankLines()
        {
            var t = CsvTable.Parse("t.csv", "# comment\nkey,name\n\na,\"Hello, \"\"World\"\"\"\nb,plain\n");
            Assert.AreEqual(2, t.RowCount);
            Assert.AreEqual("Hello, \"World\"", t.GetString(0, "name"));
            Assert.AreEqual("plain", t.GetString(1, "name"));
        }

        [Test]
        public void Csv_StrayQuotesInCommentsAndFields_DoNotMergeRows()
        {
            var t = CsvTable.Parse("t.csv", "key,name\n# a 5\" plank, \"oops\na,Wolf \"Fang\nb,c\n");
            Assert.AreEqual(2, t.RowCount);
            Assert.AreEqual("Wolf \"Fang", t.GetString(0, "name"));
            Assert.AreEqual("c", t.GetString(1, "name"));
        }

        [Test]
        public void Csv_RejectsMalformedValues()
        {
            Assert.Throws<FormatException>(() => CsvTable.Parse("t.csv", "a\n\"open\n"));
            var t = CsvTable.Parse("t.csv", "n,level\n3000000000,9\n");
            Assert.Throws<FormatException>(() => t.GetInt(0, "n"));
            Assert.Throws<FormatException>(() => t.GetEnum<WorldLevel>(0, "level"));
        }

        [Test]
        public void DataHash_IgnoresLineEndings()
        {
            Assert.AreEqual(GameDatabase.HashText("f.csv", "a,b\n1,2\n"), GameDatabase.HashText("f.csv", "﻿a,b\r\n1,2\r\n"));
            Assert.AreNotEqual(GameDatabase.HashText("f.csv", "a,b\n1,2\n"), GameDatabase.HashText("f.csv", "a,b\n1,3\n"));
        }

        [Test]
        public void EnumCountConstants_MatchEnums()
        {
            Assert.AreEqual(Enum.GetValues(typeof(WorldLevel)).Length, WorldLevels.Count);
            Assert.AreEqual(Enum.GetValues(typeof(QualityGrade)).Length, QualityGrades.Count);
            Assert.AreEqual(Enum.GetValues(typeof(IndustryTier)).Length, IndustryTiers.Count);
            Assert.AreEqual(Enum.GetValues(typeof(ModifierTarget)).Length, ModifierTargets.Count);
            Assert.AreEqual(Enum.GetValues(typeof(GuidanceAxis)).Length, GuidanceAxes.Count);
        }

        static SimulationConfig Config(string rows) => SimulationConfig.Load(CsvTable.Parse("config.csv", "key,value\n" + rows));

        [Test]
        public void ConfigHash_CoversEffectiveValues_NotFileText()
        {
            ulong defaults = SimulationConfig.Defaults().ComputeHash();
            Assert.AreEqual(defaults, Config("# restated defaults\nprice.min,1\nguidance.weight.buy_facility_permille,1000\n").ComputeHash());
            Assert.AreNotEqual(defaults, Config("price.min,2\n").ComputeHash());
            Assert.AreEqual("fx:65536", SimulationConfig.Defaults().EffectiveValues["guidance.weight.buy_facility_permille"]);
        }

        [Test]
        public void Config_RejectsUnknownKeys()
        {
            var e = Assert.Throws<FormatException>(() => Config("price.adjust_permile,250\n"));
            StringAssert.Contains("price.adjust_permile", e.Message);
        }

        [Test]
        public void SampleDatabase_ExpandsGradesAndAppliesConfig()
        {
            var db = GameDatabase.Load(TestWorlds.Sample());
            Assert.AreEqual(27 * QualityGrades.Count, db.GoodCount);
            Assert.AreEqual(11, db.Categories.Count);
            Assert.IsTrue(db.TryGetGood("wheat", QualityGrade.High, out var wheatHigh));
            Assert.AreEqual(18, db.Good(wheatHigh).BasePrice); // 10 * 1.8
            Assert.AreEqual(string.Empty, db.Config.PlayerStartNode);
            Assert.AreEqual(5, db.Good(wheatHigh).Weight);
            Assert.AreEqual(4, db.Good(wheatHigh).Size);
            Assert.AreEqual(400, db.Config.CartMaxWeight);
            Assert.AreEqual(4, db.Config.TierCadences[(int)WorldLevel.Nation].Period);
            Assert.AreNotEqual(0UL, db.ContentHash);
        }

        static MapDef Map(params (string key, WorldLevel level, string parent)[] nodes)
        {
            var map = new MapDef();
            foreach (var (key, level, parent) in nodes)
                map.Nodes.Add(new MapNodeDef { Key = key, Level = level, ParentKey = parent });
            return map;
        }

        [Test]
        public void WorldBuilder_OrdersByLevel_AndAllowsVillageUnderRegion()
        {
            var map = Map(
                ("v_region", WorldLevel.Village, "r"),
                ("v_city", WorldLevel.Village, "c"),
                ("c", WorldLevel.City, "r"),
                ("r", WorldLevel.Region, "n"),
                ("n", WorldLevel.Nation, "k"),
                ("k", WorldLevel.Continent, ""));
            using var nodes = WorldBuilder.BuildNodes(map);

            Assert.AreEqual(6, nodes.Count);
            for (int l = 0; l < WorldLevels.Count; l++)
                for (int i = nodes.LevelStart((WorldLevel)l); i < nodes.LevelStart((WorldLevel)l) + nodes.LevelCount((WorldLevel)l); i++)
                    Assert.AreEqual(l, nodes.Level[i]);

            Assert.IsTrue(nodes.TryFind("r", out var region));
            Assert.IsTrue(nodes.TryFind("v_region", out var hamlet));
            Assert.AreEqual(region.Value, nodes.Parent[hamlet.Value]);
            Assert.AreEqual(2, nodes.ChildCount[region.Value]); // city + hamlet
            Assert.IsTrue(nodes.TryFind("v_city", out var village));
            Assert.AreEqual(region.Value, nodes.AncestorAt(village.Value, WorldLevel.Region));
            Assert.AreEqual(0, nodes.Continent[village.Value]);
        }

        [Test]
        public void WorldBuilder_RejectsInvalidParent()
        {
            var map = Map(
                ("k", WorldLevel.Continent, ""),
                ("n", WorldLevel.Nation, "k"),
                ("v", WorldLevel.Village, "n"));
            Assert.Throws<FormatException>(() => WorldBuilder.BuildNodes(map).Dispose());
        }

        [Test]
        public void WorldBuilder_RejectsBadRoute_WithoutAllocating()
        {
            var map = Map(("k", WorldLevel.Continent, ""));
            map.Routes.Add(new RouteDef { FromKey = "k", ToKey = "missing", TravelTurns = 1 });
            using var nodes = WorldBuilder.BuildNodes(map);
            // A leak would surface as a native-collection leak error at the end of the test run.
            Assert.Throws<FormatException>(() => WorldBuilder.BuildRoutes(map, nodes).Dispose());
        }
    }
}
