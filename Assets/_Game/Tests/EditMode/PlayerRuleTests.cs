using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>What the player may do is checked in one place per command, on the same terms as everything else they do (C05, C07, C16, C17).</summary>
    public class PlayerRuleTests
    {
        static int ForSale(GameSimulation sim, bool stateOwned)
        {
            var facilities = sim.World.Get<FacilityTable>();
            return Enumerable.Range(0, facilities.Count).First(f => facilities.Owner[f] == OwnerIds.Npc
                && sim.Database.Facilities[facilities.DefIndex[f]].StateOwned == stateOwned);
        }

        static BuyFacilityCommand Offer(GameSimulation sim, int f, GuidanceAxis axis = GuidanceAxis.Trade)
        {
            long asked = sim.View.FacilityAskingPrice(new FacilityId(f));
            sim.World.Get<PlayerState>().CoinsAt(sim.World.Get<FacilityTable>().Node[f]) += asked;
            return new BuyFacilityCommand(new FacilityId(f), asked, axis);
        }

        static int Elsewhere(GameSimulation sim, int node) =>
            Enumerable.Range(0, sim.World.Get<NodeTable>().Count).First(n => n != node);

        [Test]
        public void AFacility_IsBought_StandingAtIt()
        {
            using var sim = TestWorlds.BuildSample();
            int f = ForSale(sim, false);
            var player = sim.World.Get<PlayerState>();
            int node = sim.World.Get<FacilityTable>().Node[f];
            var offer = Offer(sim, f);

            player.Node = Elsewhere(sim, node);
            Assert.IsFalse(offer.Validate(sim.Context).Ok);

            player.Node = node;
            Assert.IsTrue(offer.Validate(sim.Context).Ok);

            player.TravelDestination = Elsewhere(sim, node);
            Assert.IsFalse(offer.Validate(sim.Context).Ok, "not while travelling");
        }

        [Test]
        public void TheAskingPrice_FollowsWhatAFacilityEarns()
        {
            using var sim = TestWorlds.BuildSample();
            int f = ForSale(sim, false);
            var facilities = sim.World.Get<FacilityTable>();
            var markets = sim.World.Get<MarketStore>();
            var output = sim.Database.Facilities[facilities.DefIndex[f]].Recipe.Outputs[0];
            Assert.IsTrue(sim.World.Get<MarketFlows>().TryFindNearestMarket(facilities.Node[f], output.Good, out _, out int cell));
            long value = NpcLending.FacilityValue(sim.Database, facilities, f) + System.Math.Max(0, facilities.Cash[f]);

            markets.Price[cell] = 1;
            long poor = BuyFacilityCommand.AskingPrice(sim.World, sim.Database, f);
            markets.Price[cell] = 1000;
            long rich = BuyFacilityCommand.AskingPrice(sim.World, sim.Database, f);

            Assert.GreaterOrEqual(poor, value, "a seller never asks less than its value");
            Assert.Greater(rich, poor + 52 * 2 * output.Quantity * 500);
        }

        [Test]
        public void TheState_DoesNotSellItsFacilities()
        {
            using var sim = TestWorlds.BuildSample();
            int f = ForSale(sim, true);
            sim.World.Get<PlayerState>().Node = sim.World.Get<FacilityTable>().Node[f];
            Assert.IsFalse(Offer(sim, f).Validate(sim.Context).Ok);
        }

        [Test]
        public void ABid_IsMade_StandingAtTheFacility()
        {
            using var sim = TestWorlds.BuildSample();
            int f = ForSale(sim, false);
            int node = sim.World.Get<FacilityTable>().Node[f];
            sim.World.Get<AuctionTable>().Add(f, 0, sim.Turn + 10, 1);
            var bid = new BidCommand(new FacilityId(f), 5);
            var player = sim.World.Get<PlayerState>();

            player.Node = Elsewhere(sim, node);
            Assert.IsFalse(bid.Validate(sim.Context).Ok);

            player.Node = node;
            Assert.IsTrue(bid.Validate(sim.Context).Ok);
        }

        [Test]
        public void ACompany_IsFounded_StandingAtItsHeadquarters()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            int node = player.Node;
            player.CoinsAt(node) += 100;
            var found = new FoundCompanyCommand(new NodeId(node), GuidanceAxis.Trade, 100);
            Assert.IsTrue(found.Validate(sim.Context).Ok);

            player.Node = Elsewhere(sim, node);
            Assert.IsFalse(found.Validate(sim.Context).Ok);
        }

        [Test]
        public void ArgumentsOutsideTheirRange_AreRefused()
        {
            using var sim = TestWorlds.BuildSample();
            int f = ForSale(sim, false);
            var facilities = sim.World.Get<FacilityTable>();
            var player = sim.World.Get<PlayerState>();
            player.Node = facilities.Node[f];
            Assert.IsFalse(Offer(sim, f, (GuidanceAxis)99).Validate(sim.Context).Ok);

            player.CoinsAt(player.Node) += 100;
            Assert.IsFalse(new FoundCompanyCommand(new NodeId(player.Node), (GuidanceAxis)99, 100).Validate(sim.Context).Ok);

            facilities.Owner[f] = OwnerIds.Player;
            Assert.IsFalse(new SetFacilityControlCommand(new FacilityId(f), (ControlMode)9).Validate(sim.Context).Ok);
            Assert.IsTrue(new SetFacilityControlCommand(new FacilityId(f), ControlMode.Automated).Validate(sim.Context).Ok);
        }

        static (GameSimulation sim, GoodId good, int nation, int cell) AtABranchWithDeposit(long deposit)
        {
            var sim = TestWorlds.BuildSample();
            var banks = sim.World.Get<NodeBanks>();
            var nodes = sim.World.Get<NodeTable>();
            var markets = sim.World.Get<MarketStore>();
            var player = sim.World.Get<PlayerState>();
            player.Node = Enumerable.Range(0, nodes.Count).First(n => banks.BranchNation(n) >= 0 && markets.MarketCellCount[n] > 0);
            int nation = banks.BranchNation(player.Node);
            var bank = sim.World.Get<BankState>();
            bank.PlayerDeposit[nation] = deposit;
            bank.Reserve[nation] += deposit;
            int cell = markets.MarketCellStart[player.Node];
            markets.Stock[cell] = 1_000_000;
            markets.Price[cell] = 1;
            for (int c = 0; c < player.Coins.Length; c++) player.Coins[c] = 0;
            return (sim, new GoodId(markets.CellGood[cell]), nation, cell);
        }

        [Test]
        public void BuyingFromADeposit_KeepsTheBankRunLimit()
        {
            var (sim, good, nation, cell) = AtABranchWithDeposit(100);
            using (sim)
            {
                var finance = sim.World.Get<FinanceState>();
                finance.Event[nation] = (byte)FinancialEventKind.BankRun;
                sim.Database.Config.CartMaxWeight = long.MaxValue / 4;
                sim.Database.Config.CartMaxSize = long.MaxValue / 4;
                long cap = sim.Database.Config.BankRunWithdrawShare.MulInt(100);
                var bank = sim.World.Get<BankState>();

                var buy = new BuyGoodsCommand(good, 100, payFromDeposit: true);
                Assert.IsTrue(buy.Validate(sim.Context).Ok);
                buy.Apply(sim.Context);
                Assert.AreEqual(100 - cap, bank.PlayerDeposit[nation]);

                Assert.IsFalse(new BuyGoodsCommand(good, 1, payFromDeposit: true).Validate(sim.Context).Ok, "once a week");
            }
        }

        [Test]
        public void ADockedShip_TakesMoreThanTheCart()
        {
            var (sim, good, nation, cell) = AtABranchWithDeposit(0);
            using (sim)
            {
                var config = sim.Database.Config;
                var markets = sim.World.Get<MarketStore>();
                var player = sim.World.Get<PlayerState>();
                var cargo = sim.World.Get<CargoTable>();
                player.CoinsAt(player.Node) = 1_000_000;
                long weight = System.Math.Max(1, markets.GoodWeight[good.Value]), size = System.Math.Max(1, markets.GoodSize[good.Value]);
                int cartFits = (int)System.Math.Min(config.CartMaxWeight / weight, config.CartMaxSize / size);
                var buy = new BuyGoodsCommand(good, cartFits + 10, payFromDeposit: false);

                buy.Apply(sim.Context);
                Assert.AreEqual(cartFits, cargo.QuantityOf(CargoHolders.PlayerCart, good));

                var ship = sim.World.Get<PlayerShip>();
                ship.Owned = true;
                ship.DockedAt = player.Node;
                buy.Apply(sim.Context);
                Assert.AreEqual(2 * cartFits + 10, cargo.QuantityOf(CargoHolders.PlayerCart, good));
            }
        }
    }
}
