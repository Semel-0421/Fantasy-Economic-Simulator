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
    /// <summary>A group weighs selling here against carrying the load on (DEC-093).</summary>
    public class MerchantSaleTests
    {
        const int Plenty = 1_000_000;

        // One group at the start of a route, every other group still, and no stock to buy at its node.
        sealed class Scene : System.IDisposable
        {
            public readonly GameSimulation Sim;
            public readonly AgentTable Agents;
            public readonly MarketStore Markets;
            public readonly PurseState Purses;
            public readonly MerchantTripStats Stats;
            public readonly GoodId Wheat;
            public readonly int A, Here, There;
            readonly RouteTable _routes;

            public Scene(bool samePurse = true, bool bills = false, int spread = 0)
            {
                Sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("lod.mode,1", bills ? "credit.bill_cap_weeks,8" : "credit.bill_cap_weeks,0", "markets.trader_spread_permille," + spread), 42).Build();
                Sim.AdvanceTurn(); // LOD is set on the first turn
                Agents = Sim.World.Get<AgentTable>();
                Markets = Sim.World.Get<MarketStore>();
                Purses = Sim.World.Get<PurseState>();
                Stats = Sim.World.Get<MerchantTripStats>();
                _routes = Sim.World.Get<RouteTable>();
                Assert.IsTrue(Sim.Database.TryGetGood("wheat", QualityGrade.Mid, out Wheat));
                var nodes = Sim.World.Get<NodeTable>();
                int route = Enumerable.Range(0, _routes.Count).First(r =>
                    nodes.Continent[_routes.From[r]] == nodes.Continent[_routes.To[r]]
                    && _routes.TravelTurns[r] <= 2
                    && Purses.NodeFloat[_routes.From[r]] == Purses.NodeFloat[_routes.To[r]] == samePurse
                    && Markets.TryFindCell(_routes.From[r], Wheat, out _) && Markets.TryFindCell(_routes.To[r], Wheat, out _));
                Here = _routes.From[route];
                There = _routes.To[route];

                A = Enumerable.Range(0, Agents.Count).First(a => Agents.Owner[a] == OwnerIds.Npc);
                for (int a = 0; a < Agents.Count; a++)
                    if (a != A) Agents.Mode[a] = (byte)ControlMode.Manual;
                Agents.Node[A] = Here;
                Agents.Home[A] = Here;
                Agents.Destination[A] = -1;
                Agents.ArrivalTurn[A] = -1;
                Agents.Trait[A] = MerchantTraits.Of(MerchantPersonality.Aggressive, MerchantOccupation.Expedition);
                Agents.Cash[A] = Plenty;
                Agents.NegativeCashWeeks[A] = 0;
                Sim.World.Get<CargoTable>().Clear(Holder);
                int start = Markets.MarketCellStart[Here];
                for (int c = start; c < start + Markets.MarketCellCount[Here]; c++) Markets.Stock[c] = 0;
                SetFloat(Here, Plenty);
            }

            public int Holder => CargoHolders.Merchant(new AgentId(A));

            public void SetFloat(int node, long coins) => Purses.Float[Purses.NodeFloat[node]] = coins;

            public void Load(int units, long cost)
            {
                Sim.World.Get<CargoTable>().Change(Holder, Wheat, units);
                Agents.LoadCost[A] = cost;
            }

            // The price of wheat here, and in every market a route from here reaches.
            public void Prices(int here, int elsewhere)
            {
                int first = _routes.OutStart[Here];
                for (int k = first; k < first + _routes.OutCount[Here]; k++)
                    if (Markets.TryFindCell(_routes.To[_routes.RouteIndex[k]], Wheat, out int cell)) Markets.Price[cell] = elsewhere;
                Assert.IsTrue(Markets.TryFindCell(Here, Wheat, out int own));
                Markets.Price[own] = here;
            }

            public void Run() => Sim.Context.Services.Get<IMerchantBehavior>().RunDetailed(Sim.Context);

            public long Carried()
            {
                var load = new List<CargoItem>();
                Sim.World.Get<CargoTable>().Collect(Holder, load);
                return load.Sum(i => (long)i.Quantity);
            }

            public long Money()
            {
                long money = 0;
                var currency = Sim.World.Get<CurrencyState>();
                for (int c = 0; c < currency.ContinentCount; c++) money += MoneyLedger.ContinentMoney(Sim.World, c);
                return money;
            }

            public void Dispose() => Sim.Dispose();
        }

        [Test]
        public void LoadWorthMoreElsewhere_IsCarriedOn_AndMoneyStaysPut()
        {
            using var s = new Scene();
            s.Load(10, 1_000);
            s.Prices(10, 1_000);
            long money = s.Money(), cash = s.Agents.Cash[s.A];
            s.Run();
            Assert.IsTrue(s.Agents.IsTravelling(s.A));
            Assert.Greater(s.Carried(), 0);
            Assert.Less(s.Agents.Cash[s.A], cash, "the trip is paid for");
            Assert.AreEqual(1, s.Stats.CarriedOn);
            Assert.AreEqual(0, s.Stats.LossSales);
            Assert.AreEqual(money, s.Money());
        }

        [Test]
        public void GapSmallerThanTheTrip_SellsHereBelowCost_AndCountsTheLoss()
        {
            using var s = new Scene();
            s.Load(100, 10_000);
            s.Prices(50, 50);
            long money = s.Money(), cash = s.Agents.Cash[s.A];
            s.Run();
            Assert.IsFalse(s.Agents.IsTravelling(s.A));
            Assert.AreEqual(0, s.Carried());
            Assert.AreEqual(1, s.Stats.LossSales);
            Assert.AreEqual(5_000, s.Stats.LossAmount, "paid 10,000 and got 5,000");
            Assert.AreEqual(0, s.Agents.LoadCost[s.A]);
            Assert.Greater(s.Agents.Cash[s.A], cash);
            Assert.AreEqual(money, s.Money());
        }

        [Test]
        public void SaleAboveCost_IsNoLoss()
        {
            using var s = new Scene();
            s.Load(100, 1_000);
            s.Prices(50, 50);
            s.Run();
            Assert.AreEqual(0, s.Carried());
            Assert.AreEqual(0, s.Stats.LossSales);
            Assert.AreEqual(0, s.Stats.LossAmount);
        }

        [Test]
        public void GroupThatWouldNotLastTheTrip_SellsHere_WhateverItLoses()
        {
            using var s = new Scene();
            s.Load(100, 10_000);
            s.Prices(10, 1_000);
            s.Agents.Cash[s.A] = -1;
            s.Agents.NegativeCashWeeks[s.A] = s.Sim.Database.Config.MerchantBankruptWeeks - 1;
            s.Run();
            Assert.IsFalse(s.Agents.IsTravelling(s.A));
            Assert.AreEqual(0, s.Carried());
            Assert.AreEqual(1, s.Stats.ForcedSales);
            Assert.AreEqual(1, s.Stats.LossSales);
            Assert.Greater(s.Agents.Cash[s.A], 0, "the sale saved it");
            Assert.AreEqual(0, s.Agents.NegativeCashWeeks[s.A]);
        }

        [Test]
        public void GroupInDebtWithTimeLeft_MayStillCarryOn()
        {
            using var s = new Scene();
            s.Load(100, 10_000);
            s.Prices(10, 1_000);
            s.Agents.Cash[s.A] = -1;
            s.Run();
            Assert.IsTrue(s.Agents.IsTravelling(s.A));
            Assert.AreEqual(0, s.Stats.ForcedSales);
        }

        [Test]
        public void TradersWhoCannotPay_AreNotWorthATrip()
        {
            using var s = new Scene();
            s.Load(100, 10_000);
            s.Prices(10, 1_000);
            s.SetFloat(s.Here, 0); // the same purse pays at both ends
            long cash = s.Agents.Cash[s.A];
            s.Run();
            Assert.IsFalse(s.Agents.IsTravelling(s.A), "it waits instead of paying for a trip to no buyer");
            Assert.AreEqual(100, s.Carried());
            Assert.Greater(s.Agents.Cash[s.A], cash - 1_000, "only upkeep went");
        }

        [Test]
        public void PartialSale_KeepsTheBooksOfWhatIsLeft()
        {
            using var s = new Scene();
            s.Load(100, 2_000);
            s.Prices(10, 10);
            s.SetFloat(s.Here, 500);
            s.Run();
            Assert.AreEqual(50, s.Carried());
            Assert.AreEqual(1_000, s.Agents.LoadCost[s.A]);
            Assert.AreEqual(1, s.Stats.LossSales);
            Assert.AreEqual(500, s.Stats.LossAmount);
        }

        [Test]
        public void TradersWithoutCoins_PayWithABill_ThatTheBankDiscounts()
        {
            using var s = new Scene(bills: true);
            s.Load(100, 500);
            s.Prices(10, 10);
            s.SetFloat(s.Here, 0);
            var room = s.Sim.World.Get<BillRoom>();
            var bills = s.Sim.World.Get<BillTable>();
            int purse = s.Purses.NodeFloat[s.Here];
            room.Room[purse] = Plenty;
            room.BankRoom[purse] = Plenty;
            int before = bills.Count;
            long cash = s.Agents.Cash[s.A], money = s.Money();
            s.Run();
            Assert.AreEqual(0, s.Carried(), "all of it sold");
            Assert.AreEqual(before + 1, bills.Count);
            Assert.AreEqual(1_000, bills.Face[before]);
            Assert.AreEqual(BillHolders.Bank, bills.Holder[before]);
            Assert.Greater(s.Agents.Cash[s.A], cash + 800, "the bank paid at a discount, less the week's upkeep");
            Assert.AreEqual(0, s.Purses.Float[purse], "the traders still owe it");
            Assert.AreEqual(money, s.Money());
        }

        [Test]
        public void BankWithoutRoom_LeavesTheBillWithTheSeller()
        {
            using var s = new Scene(bills: true);
            s.Load(100, 500);
            s.Prices(10, 10);
            s.SetFloat(s.Here, 0);
            var room = s.Sim.World.Get<BillRoom>();
            var bills = s.Sim.World.Get<BillTable>();
            int purse = s.Purses.NodeFloat[s.Here];
            room.Room[purse] = Plenty;
            room.BankRoom[purse] = 0;
            int before = bills.Count;
            long cash = s.Agents.Cash[s.A];
            s.Run();
            Assert.AreEqual(0, s.Carried());
            Assert.AreEqual(s.A, bills.Holder[before]);
            Assert.Less(s.Agents.Cash[s.A], cash + 1, "nothing until it falls due");
        }

        [Test]
        public void PriceCover_MakesUpASaleBelowTheFloor()
        {
            using var s = new Scene();
            s.Load(100, 500);
            s.Prices(10, 10);
            s.SetFloat(s.Here, Plenty);
            var agents = s.Sim.World.Get<AgentTable>();
            var bank = s.Sim.World.Get<BankState>();
            s.Sim.Database.Config.HedgeFee = Fx.FromPermille(50);
            int nation = s.Purses.NodeFloat[s.Here];
            bank.Reserve[nation] = 1_000_000;
            agents.HedgeFloor[s.A] = 1_500;
            long cash = agents.Cash[s.A], money = s.Money(), reserve = bank.Reserve[nation];
            s.Run();
            Assert.AreEqual(0, s.Carried());
            Assert.AreEqual(0, agents.HedgeFloor[s.A]);
            Assert.AreEqual(reserve - 500, bank.Reserve[nation], "the bank pays the 500 short");
            Assert.Greater(agents.Cash[s.A], cash + 1_400);
            Assert.AreEqual(money, s.Money());
        }

        [Test]
        public void Traders_PayTheSellerThePriceLessTheirMargin()
        {
            using var s = new Scene(spread: 300);
            s.Load(100, 500);
            s.Prices(10, 10);
            s.SetFloat(s.Here, Plenty);
            long cash = s.Agents.Cash[s.A], money = s.Money();
            s.Run();
            Assert.AreEqual(0, s.Carried());
            Assert.AreEqual(Plenty - 100 * 7, s.Purses.Float[s.Purses.NodeFloat[s.Here]], "seven of the ten");
            Assert.Greater(s.Agents.Cash[s.A], cash + 600);
            Assert.Less(s.Agents.Cash[s.A], cash + 701);
            Assert.AreEqual(money, s.Money());
        }

        [Test]
        public void Margin_CountsWhenJudgingAMarket()
        {
            // The price here pays for the trip only before the margin.
            void Stage(Scene scene)
            {
                for (int n = 0; n < scene.Sim.World.Get<NodeTable>().Count; n++)
                    if (scene.Markets.TryFindCell(n, scene.Wheat, out int any)) scene.Markets.Price[any] = 19;
                scene.Prices(10, 19);
                Assert.IsTrue(scene.Markets.TryFindCell(scene.Here, scene.Wheat, out int cell));
                scene.Markets.Stock[cell] = 100;
                for (int f = 0; f < scene.Purses.Count; f++) scene.Purses.Float[f] = Plenty;
            }

            using var s = new Scene(spread: 500);
            Stage(s);
            s.Run();
            Assert.AreEqual(0, s.Carried(), "ten coins for a unit that costs ten");

            using var none = new Scene();
            Stage(none);
            none.Run();
            Assert.Greater(none.Carried(), 0);
        }

        [Test]
        public void BuyingALoad_EntersItsCostInTheBooks()
        {
            using var s = new Scene();
            s.Prices(10, 1_000);
            Assert.IsTrue(s.Markets.TryFindCell(s.Here, s.Wheat, out int cell));
            s.Markets.Stock[cell] = 100;
            s.Run();
            Assert.IsTrue(s.Agents.IsTravelling(s.A));
            Assert.AreEqual((100 - s.Markets.Stock[cell]) * 10L, s.Agents.LoadCost[s.A]);
            Assert.Greater(s.Agents.LoadCost[s.A], 0);
        }

        [Test]
        public void Off_SellsAllOnArrival()
        {
            using var s = new Scene();
            s.Sim.Database.Config.MerchantWeighSales = false;
            s.Load(100, 10_000);
            s.Prices(10, 1_000);
            s.Run();
            Assert.IsFalse(s.Agents.IsTravelling(s.A));
            Assert.AreEqual(0, s.Carried());
            Assert.AreEqual(0, s.Stats.CarriedOn);
        }

        [Test]
        public void WeighedSales_BurstMatchesManaged_AndConserveCoins()
        {
            using var burst = TestWorlds.BuildSample(seed: 5);
            using var managed = TestWorlds.BuildSample(seed: 5);
            ((ArbitrageMerchantBehavior)managed.Context.Services.Get<IMerchantBehavior>()).ForceManaged = true;
            var currency = burst.World.Get<CurrencyState>();
            for (int t = 0; t < 2 * SimClock.WeeksPerYear; t++)
            {
                burst.AdvanceTurn();
                managed.AdvanceTurn();
                for (int c = 0; c < currency.ContinentCount; c++)
                    Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(burst.World, c));
            }
            Assert.AreEqual(managed.ComputeHash(), burst.ComputeHash());
        }
    }
}
