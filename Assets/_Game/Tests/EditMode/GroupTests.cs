using System.Collections.Generic;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    public class GroupTests
    {
        const string City = "aur_n1_r1_c1"; // has smithies, so a tools guild

        static NodeId Node(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.View.TryFindNode(key, out var node), key);
            return node;
        }

        static PlayerState PlaceAt(GameSimulation sim, string node, long coins)
        {
            var player = sim.World.Get<PlayerState>();
            System.Array.Clear(player.Coins, 0, player.Coins.Length);
            player.Node = Node(sim, node).Value;
            player.LocalCoins = coins;
            return player;
        }

        static int Guild(GameSimulation sim, string city, string category)
        {
            var guilds = new List<GroupInfo>();
            sim.View.GetGuildsAt(Node(sim, city), guilds);
            int index = sim.Database.Categories.First(c => c.Key == category).Index;
            return guilds.Single(g => g.Category == index).Id;
        }

        static GoodId Good(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.Database.TryGetGood(key, QualityGrade.Mid, out var good), key);
            return good;
        }

        static int Rejected(GameSimulation sim) => sim.LastTurnEvents.OfType<CommandRejected>().Count();

        [Test]
        public void EveryNpcBusiness_HasAnOwner_ByTheSetupRules()
        {
            using var sim = TestWorlds.BuildSample();
            var groups = sim.World.Get<GroupTable>();
            var nodes = sim.World.Get<NodeTable>();
            var facilities = sim.World.Get<FacilityTable>();
            var agents = sim.World.Get<AgentTable>();
            int nations = nodes.LevelCount(WorldLevel.Nation);
            Assert.AreEqual(nations, Enumerable.Range(0, groups.Count).Count(g => groups.KindOf(g) == GroupKind.StateEnterprise));
            Assert.AreEqual(nations * sim.Database.Config.CompaniesPerNation,
                Enumerable.Range(0, groups.Count).Count(g => groups.KindOf(g) == GroupKind.Company));
            Assert.Greater(Enumerable.Range(0, groups.Count).Count(g => groups.KindOf(g) == GroupKind.Guild), 0);

            for (int f = 0; f < facilities.Count; f++)
            {
                int g = facilities.Group[f];
                Assert.GreaterOrEqual(g, 0);
                var def = sim.Database.Facilities[facilities.DefIndex[f]];
                var expected = def.StateOwned ? GroupKind.StateEnterprise
                    : def.Tier == IndustryTier.Secondary ? GroupKind.Company
                    : GroupKind.PrivateEnterprise;
                Assert.AreEqual(expected, groups.KindOf(g), def.Key);
                if (expected == GroupKind.PrivateEnterprise) Assert.AreEqual(facilities.Node[f], groups.Node[g]);
            }
            for (int a = 0; a < agents.Count; a++)
            {
                int g = agents.Group[a];
                Assert.GreaterOrEqual(g, 0);
                bool large = agents.Scale[a] <= (byte)WorldLevel.Region && nodes.AncestorAt(agents.Home[a], WorldLevel.Nation) >= 0;
                Assert.AreEqual(large ? GroupKind.Company : GroupKind.PrivateEnterprise, groups.KindOf(g));
            }
        }

        [Test]
        public void FoundingTheCompany_MakesItTheOwnerOfThePlayersFacilities()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var player = PlaceAt(sim, City, 10_000);
            int first = Enumerable.Range(0, facilities.Count).First(f => facilities.Node[f] == player.Node);
            int second = Enumerable.Range(0, facilities.Count).Last(f => facilities.Node[f] == player.Node);
            TestWorlds.BuyFacility(sim, first, GuidanceAxis.Industry);
            sim.AdvanceTurn();
            Assert.AreEqual(-1, sim.View.PlayerCompany);
            Assert.AreEqual(-1, facilities.Group[first]);

            sim.Actions.FoundCompany(Node(sim, City), GuidanceAxis.Trade, 100);
            sim.Actions.FoundCompany(Node(sim, City), GuidanceAxis.Trade, 100); // only one company
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));
            int company = sim.View.PlayerCompany;
            Assert.GreaterOrEqual(company, 0);
            Assert.AreEqual(GroupKind.Company, sim.View.Group(company).Kind);
            Assert.IsTrue(sim.View.Group(company).IsPlayer);
            Assert.AreEqual(company, facilities.Group[first]);

            TestWorlds.BuyFacility(sim, second, GuidanceAxis.Industry);
            sim.AdvanceTurn();
            Assert.AreEqual(company, facilities.Group[second]);
        }

        [Test]
        public void Guilds_AreJoinedInTheirCity_ForAYear()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            int guild = Guild(sim, City, "tools");
            var player = PlaceAt(sim, "aur_n1_r1_c2", 1_000);
            sim.Actions.JoinGuild(guild);
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim)); // another city

            PlaceAt(sim, City, 1_000);
            long cash = sim.View.Group(guild).Cash;
            sim.Actions.JoinGuild(guild);
            sim.AdvanceTurn();
            var joined = sim.LastTurnEvents.OfType<GuildJoined>().Single();
            Assert.AreEqual(sim.Turn - 1 + SimClock.WeeksPerYear, joined.ExpiresTurn);
            Assert.AreEqual(1_000 - config.GuildFee, player.LocalCoins);
            Assert.AreEqual(cash + config.GuildFee, sim.View.Group(guild).Cash);
            Assert.IsTrue(sim.View.IsGuildMember(guild));

            sim.World.Get<GuildMembershipTable>().ExpiresTurn[0] = sim.Turn + 1;
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<GuildMembershipExpired>().Count());
            Assert.IsFalse(sim.View.IsGuildMember(guild));
        }

        [Test]
        public void GuildMembers_PayLessForInsuranceAndLicenses()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            PlaceAt(sim, City, 10_000);
            sim.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, Good(sim, "wheat"), 20);
            var region = Node(sim, "aur_n1_r1_c2");
            Assert.IsTrue(sim.View.TryQuoteTrip(region, false, true, out var before));
            long licenseBefore = sim.View.LicensePrice(Good(sim, "iron_tools"));

            sim.Actions.JoinGuild(Guild(sim, City, "tools"));
            sim.AdvanceTurn();
            Assert.IsTrue(sim.View.TryQuoteTrip(region, false, true, out var after));
            Assert.AreEqual(config.InsurancePremium.MulInt(before.CargoValue), before.Insurance);
            long full = config.InsurancePremium.MulInt(after.CargoValue); // prices moved a week: price this quote's cargo
            Assert.AreEqual(full - config.GuildInsuranceDiscount.MulInt(full), after.Insurance);
            Assert.AreEqual(licenseBefore - config.GuildLicenseDiscount.MulInt(licenseBefore), sim.View.LicensePrice(Good(sim, "iron_tools")));
            Assert.AreEqual(LicenseRules.Price(sim.Database, Good(sim, "wheat").BaseIndex), sim.View.LicensePrice(Good(sim, "wheat"))); // another trade
        }

        [Test]
        public void GuildHall_StoresMembersGoods_AndCountsAsCollateral()
        {
            using var sim = TestWorlds.BuildSample();
            PlaceAt(sim, City, 10_000);
            var wheat = Good(sim, "wheat");
            sim.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, wheat, 20);
            int guild = Guild(sim, City, "tools");
            sim.Actions.MoveGuildHallCargo(guild, wheat, 5, toHall: true); // not a member yet
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));

            long collateral = sim.View.CollateralValue;
            sim.Actions.JoinGuild(guild);
            sim.Actions.MoveGuildHallCargo(guild, wheat, 5, toHall: true);
            sim.AdvanceTurn();
            Assert.AreEqual(0, Rejected(sim));
            var hall = new List<CargoItem>();
            sim.View.GetGuildHall(guild, hall);
            Assert.AreEqual(5, hall.Single().Quantity);
            Assert.AreEqual(15, sim.World.Get<CargoTable>().QuantityOf(CargoHolders.PlayerCart, wheat));
            Assert.Greater(sim.View.CollateralValue, 0);
            Assert.AreNotEqual(0, collateral);
        }

        [Test]
        public void LostPlayerFacility_GoesToALocalFamily()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var groups = sim.World.Get<GroupTable>();
            var player = PlaceAt(sim, City, 10_000);
            int f = Enumerable.Range(0, facilities.Count).First(i => facilities.Node[i] == player.Node);
            sim.Actions.FoundCompany(Node(sim, City), GuidanceAxis.Trade, 0);
            TestWorlds.BuyFacility(sim, f, GuidanceAxis.Industry);
            sim.AdvanceTurn();
            facilities.Cash[f] = -1;
            for (int t = 0; t < sim.Database.Config.FacilityBankruptWeeks + 1 && facilities.Owner[f] == OwnerIds.Player; t++)
                sim.AdvanceTurn();
            Assert.AreEqual(OwnerIds.Npc, facilities.Owner[f]);
            Assert.AreEqual(GroupKind.PrivateEnterprise, groups.KindOf(facilities.Group[f]));
            Assert.AreEqual(facilities.Node[f], groups.Node[facilities.Group[f]]);
        }

        [Test]
        public void OwnersAreAssignedAgain_AfterLoadingAnOlderSave()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var agents = sim.World.Get<AgentTable>();
            var before = Enumerable.Range(0, facilities.Count).Select(f => facilities.Group[f]).ToArray();
            for (int f = 0; f < facilities.Count; f++) facilities.Group[f] = -1;
            for (int a = 0; a < agents.Count; a++) agents.Group[a] = -1;
            sim.Systems.OfType<GroupSystem>().Single().OnLoaded(sim.Context);
            CollectionAssert.AreEqual(before, Enumerable.Range(0, facilities.Count).Select(f => facilities.Group[f]).ToArray());
            Assert.IsTrue(Enumerable.Range(0, agents.Count).All(a => agents.Group[a] >= 0));
        }
    }
}
