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
    /// <summary>Continental goods and trade between continents (DEC-066, DEC-096).</summary>
    public class InternationalTradeTests
    {
        const int Plenty = 1_000_000;

        // One group at a sea port, every other group still, and no stock to buy at the port.
        sealed class Port : System.IDisposable
        {
            public readonly GameSimulation Sim;
            public readonly AgentTable Agents;
            public readonly MarketStore Markets;
            public readonly PurseState Purses;
            public readonly NodeTable Nodes;
            public readonly BankState Bank;
            public readonly RouteTable Routes;
            public readonly GoodId Good;
            public readonly int A, Here, There;

            public Port(string goodKey = "wheat", string config = "lod.mode,1\nmarkets.trader_spread_permille,0")
            {
                Sim = SimulationBuilder.FromData(TestWorlds.SampleConfig(config.Split('\n')), 42).Build();
                Sim.Database.Config.MerchantMustAct = false; // the tests watch a group's own choice
                Sim.Database.Config.InternationalMerchantPermille = 1000;
                Sim.Database.Config.IntercontinentalMerchantPermille = 1000;
                Sim.AdvanceTurn();
                Agents = Sim.World.Get<AgentTable>();
                Markets = Sim.World.Get<MarketStore>();
                Purses = Sim.World.Get<PurseState>();
                Nodes = Sim.World.Get<NodeTable>();
                Bank = Sim.World.Get<BankState>();
                Routes = Sim.World.Get<RouteTable>();
                Assert.IsTrue(Sim.Database.TryGetGood(goodKey, QualityGrade.Mid, out Good));
                int route = Enumerable.Range(0, Routes.Count).First(r => Nodes.Continent[Routes.From[r]] != Nodes.Continent[Routes.To[r]]);
                Here = Routes.From[route];
                There = Routes.To[route];
                A = Enumerable.Range(0, Agents.Count).First(a => Agents.Owner[a] == OwnerIds.Npc && Agents.Scale[a] <= (byte)WorldLevel.Region
                    && Agents.Home[a] == Here);
                for (int a = 0; a < Agents.Count; a++)
                    if (a != A) Agents.Mode[a] = (byte)ControlMode.Manual;
                Agents.Node[A] = Here;
                Agents.Destination[A] = -1;
                Agents.ArrivalTurn[A] = -1;
                Agents.Trait[A] = MerchantTraits.Of(MerchantPersonality.Aggressive, MerchantOccupation.Expedition);
                Agents.Cash[A] = Plenty;
                Agents.NegativeCashWeeks[A] = 0;
                Sim.World.Get<CargoTable>().Clear(Holder);
                ClearStock(Here);
                ClearStock(There);
                Purses.Float[Purses.NodeFloat[Here]] = Plenty;
                Purses.Float[Purses.NodeFloat[There]] = Plenty;
                for (int c = 0; c < Bank.InternationalReserve.Length; c++) Bank.InternationalReserve[c] = Plenty;
            }

            public int Holder => CargoHolders.Merchant(new AgentId(A));

            public void ClearStock(int node)
            {
                int start = Markets.MarketCellStart[node];
                for (int c = start; c < start + Markets.MarketCellCount[node]; c++) Markets.Stock[c] = 0;
            }

            public int Cell(int node)
            {
                Assert.IsTrue(Markets.TryFindCell(node, Good, out int cell));
                return cell;
            }

            public void Run() => Sim.Context.Services.Get<IMerchantBehavior>().RunDetailed(Sim.Context);

            public long Carried()
            {
                var load = new List<CargoItem>();
                Sim.World.Get<CargoTable>().Collect(Holder, load);
                return load.Sum(i => (long)i.Quantity);
            }

            public void AssertCoinsConserved(long[] before)
            {
                var currency = Sim.World.Get<CurrencyState>();
                for (int c = 0; c < currency.ContinentCount; c++) Assert.AreEqual(before[c], MoneyLedger.ContinentMoney(Sim.World, c));
            }

            public long[] Coins()
            {
                var currency = Sim.World.Get<CurrencyState>();
                return Enumerable.Range(0, currency.ContinentCount).Select(c => MoneyLedger.ContinentMoney(Sim.World, c)).ToArray();
            }

            public void Dispose() => Sim.Dispose();
        }

        [Test]
        public void ContinentalProducers_StandOnlyOnTheirContinent()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var nodes = sim.World.Get<NodeTable>();
            var seen = new HashSet<string>();
            for (int f = 0; f < facilities.Count; f++)
            {
                var def = sim.Database.Facilities[facilities.DefIndex[f]];
                if (def.Continent == null) continue;
                seen.Add(def.Key);
                Assert.AreEqual(def.Continent, nodes.KeyOf(nodes.AncestorAt(facilities.Node[f], WorldLevel.Continent)));
            }
            Assert.AreEqual(sim.Database.Facilities.Count(d => d.Continent != null), seen.Count);
            Assert.GreaterOrEqual(sim.Database.Facilities.Select(d => d.Continent).Where(c => c != null).Distinct().Count(), 2, "each continent has its own");
        }

        [Test]
        public void ContinentalGoods_AreNeverBuiltAbroad_OverYearsOfInvestment()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var nodes = sim.World.Get<NodeTable>();
            for (int t = 0; t < 5 * SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            for (int f = 0; f < facilities.Count; f++)
                Assert.IsTrue(FacilityPlaces.Allowed(sim.Database.Facilities[facilities.DefIndex[f]], nodes, facilities.Node[f]));
        }

        [Test]
        public void LoadDearAbroad_SailsAcross_AndSellsThroughBothBanks()
        {
            using var p = new Port();
            var coins = p.Coins();
            int cell = p.Cell(p.Here), abroad = p.Cell(p.There);
            p.Markets.Price[cell] = 10;
            p.Markets.Price[abroad] = 500;
            p.Markets.Stock[cell] = 50;
            p.Run();
            Assert.AreEqual(p.There, p.Agents.Destination[p.A], "it loads and sails");
            Assert.Greater(p.Carried(), 0);
            p.AssertCoinsConserved(coins);

            long load = p.Carried(), cash = p.Agents.Cash[p.A];
            int home = p.Nodes.Continent[p.Here], foreign = p.Nodes.Continent[p.There];
            long homeBank = p.Bank.InternationalReserve[home], foreignBank = p.Bank.InternationalReserve[foreign];
            long floatThere = p.Purses.Float[p.Purses.NodeFloat[p.There]];
            p.ClearStock(p.There);
            p.Agents.ArrivalTurn[p.A] = p.Sim.Turn;
            p.Run();
            Assert.AreEqual(p.There, p.Agents.Node[p.A]);
            Assert.AreEqual(floatThere - load * 500, p.Purses.Float[p.Purses.NodeFloat[p.There]], "foreign traders pay in their coin");
            Assert.AreEqual(foreignBank + load * 500, p.Bank.InternationalReserve[foreign] + /* its upkeep and fees change coins too */ 0, 1_000_000);
            Assert.Greater(p.Agents.Cash[p.A], cash);
            Assert.Less(p.Bank.InternationalReserve[home], homeBank, "the home bank pays out its own coin");
            p.AssertCoinsConserved(coins);
        }

        [Test]
        public void HomeBankShort_LimitsWhatASaleAbroadBrings()
        {
            using var p = new Port();
            int abroad = p.Cell(p.There);
            p.Sim.World.Get<CargoTable>().Change(p.Holder, p.Good, 100);
            p.Markets.Price[abroad] = 100;
            int home = p.Nodes.Continent[p.Here];
            p.Agents.Node[p.A] = p.There;
            p.Bank.InternationalReserve[home] = 0;
            var coins = p.Coins();
            p.Run();
            Assert.AreEqual(100, p.Carried(), "no home coins to change into, no sale");
            p.AssertCoinsConserved(coins);
        }

        static void DearAbroad(Port p)
        {
            int cell = p.Cell(p.Here), abroad = p.Cell(p.There);
            p.Markets.Price[cell] = 10;
            p.Markets.Price[abroad] = 500;
            p.Markets.Stock[cell] = 50;
        }

        [Test]
        public void SmallGroups_DoNotCross_EvenFromAPort()
        {
            using var p = new Port();
            DearAbroad(p);
            p.Agents.Scale[p.A] = (byte)WorldLevel.City;
            p.Run();
            Assert.AreNotEqual(p.There, p.Agents.Destination[p.A]);
        }

        [Test]
        public void CautiousGroups_Cross_OnlyFromAPort()
        {
            using var port = new Port();
            DearAbroad(port);
            port.Agents.Trait[port.A] = MerchantTraits.Of(MerchantPersonality.Cautious, MerchantOccupation.Expedition);
            port.Run();
            Assert.AreEqual(port.There, port.Agents.Destination[port.A], "a group based at the port sails");

            using var inland = new Port();
            DearAbroad(inland);
            inland.Agents.Trait[inland.A] = MerchantTraits.Of(MerchantPersonality.Cautious, MerchantOccupation.Expedition);
            inland.Agents.Home[inland.A] = inland.Nodes.AncestorAt(inland.Here, WorldLevel.Nation);
            inland.Run();
            Assert.AreNotEqual(inland.There, inland.Agents.Destination[inland.A], "one based inland stays");

            using var off = new Port(config: "lod.mode,1\nmerchants.port_crossers,0");
            DearAbroad(off);
            off.Agents.Trait[off.A] = MerchantTraits.Of(MerchantPersonality.Cautious, MerchantOccupation.Expedition);
            off.Run();
            Assert.AreNotEqual(off.There, off.Agents.Destination[off.A], "the rule can be switched off");
        }

        [Test]
        public void MarketWhoseTradersCannotPay_IsNoMarket()
        {
            using var p = new Port();
            DearAbroad(p);
            int foreign = p.Nodes.Continent[p.There];
            for (int f = 0; f < p.Purses.Count; f++)
                if (p.Purses.ContinentOf(f) == foreign) p.Purses.Float[f] = 0;
            var coins = p.Coins();
            p.Run();
            Assert.AreNotEqual(p.There, p.Agents.Destination[p.A], "the price is high, but no one there has the money");
            Assert.AreEqual(0, p.Carried());
            p.AssertCoinsConserved(coins);

            using var off = new Port(config: "lod.mode,1\nmerchants.judge_coin_units,0");
            DearAbroad(off);
            for (int f = 0; f < off.Purses.Count; f++)
                if (off.Purses.ContinentOf(f) == foreign) off.Purses.Float[f] = 0;
            off.Run();
            Assert.AreEqual(off.There, off.Agents.Destination[off.A], "without the rule it goes by the price alone");
        }

        [Test]
        public void GroupLeftAbroad_WithNothingToCarry_SailsHomeEmpty()
        {
            using var p = new Port();
            p.Agents.Node[p.A] = p.There;
            p.Agents.Destination[p.A] = -1;
            var coins = p.Coins();
            p.Run();
            Assert.AreEqual(p.Here, p.Agents.Destination[p.A]);
            Assert.AreEqual(0, p.Carried());
            p.AssertCoinsConserved(coins);
        }

        [Test]
        public void GroupLeftAbroad_WithALeftoverItCannotSell_SailsHomeWithIt()
        {
            using var p = new Port();
            p.Agents.Node[p.A] = p.There;
            p.Agents.Destination[p.A] = -1;
            p.Sim.World.Get<CargoTable>().Change(p.Holder, p.Good, 1);
            p.Purses.Float[p.Purses.NodeFloat[p.There]] = 0;
            var coins = p.Coins();
            p.Run();
            Assert.AreEqual(p.Here, p.Agents.Destination[p.A]);
            Assert.AreEqual(1, p.Carried());
            p.AssertCoinsConserved(coins);
        }

        [Test]
        public void GroupStrandedAboveItsHome_ComesBack_OnlyWhenTheRuleIsOn()
        {
            int Village(Port p) => Enumerable.Range(0, p.Routes.Count).Where(r => p.Routes.From[r] == p.Here && p.Nodes.LevelOf(p.Routes.To[r]) == WorldLevel.Village).Select(r => p.Routes.To[r]).First();
            using var on = new Port();
            int village = Village(on);
            on.Agents.Home[on.A] = village;
            on.ClearStock(on.Here);
            on.Run();
            Assert.AreEqual(village, on.Agents.Destination[on.A]);

            using var off = new Port(config: "lod.mode,1\nmerchants.return_home,0");
            off.Agents.Home[off.A] = Village(off);
            off.ClearStock(off.Here);
            off.Run();
            Assert.IsFalse(off.Agents.IsTravelling(off.A));
        }

        [Test]
        public void AggregateFlows_CrossAtAShare_AndKeepEachContinentsCoins()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
                TestWorlds.Sample().ReadText(GameDatabase.ConfigFile).TrimEnd() + "\nlod.mode,2\n"), 42).Build();
            var currency = sim.World.Get<CurrencyState>();
            var bank = sim.World.Get<BankState>();
            long[] start = bank.InternationalReserve.ToArray();
            for (int t = 0; t < 3 * SimClock.WeeksPerYear; t++)
            {
                sim.AdvanceTurn();
                for (int c = 0; c < currency.ContinentCount; c++)
                    Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c), $"turn {t}");
            }
            Assert.IsTrue(bank.InternationalReserve.Where((v, c) => v != start[c]).Any(), "coins changed at the banks");
        }

        [Test]
        public void Judges_ThreeMarketsAhead()
        {
            using var sim = Build3Hops(3);
            var (a, village, city) = Stage(sim, far: 500);
            sim.Context.Services.Get<IMerchantBehavior>().RunDetailed(sim.Context);
            Assert.AreEqual(city, sim.World.Get<AgentTable>().Destination[a], "the third market pays for the load");

            using var near = Build3Hops(1);
            var (b, _, _) = Stage(near, far: 500);
            near.Context.Services.Get<IMerchantBehavior>().RunDetailed(near.Context);
            Assert.IsFalse(near.World.Get<AgentTable>().IsTravelling(b), "one market ahead sees no margin");
        }

        [Test]
        public void CarriedLoad_GoesOnPastAMarketThatPaysLess()
        {
            using var sim = Build3Hops(3);
            var (a, village, city) = Stage(sim, far: 500);
            var agents = sim.World.Get<AgentTable>();
            var behavior = sim.Context.Services.Get<IMerchantBehavior>();
            behavior.RunDetailed(sim.Context);
            var load = new List<CargoItem>();
            sim.World.Get<CargoTable>().Collect(CargoHolders.Merchant(new AgentId(a)), load);
            Assert.AreEqual(1, load.Count);
            agents.ArrivalTurn[a] = sim.Turn;
            behavior.RunDetailed(sim.Context);
            load.Clear();
            sim.World.Get<CargoTable>().Collect(CargoHolders.Merchant(new AgentId(a)), load);
            Assert.AreEqual(1, load.Count, "not sold at the first market");
            Assert.IsTrue(agents.IsTravelling(a));
        }

        [Test]
        public void MerchantsWish_BidsThePriceUp_OnlyWhileItPays()
        {
            using var p = new Port(config: "lod.mode,1\nmerchants.bids,1");
            int cell = p.Cell(p.Here);
            p.Markets.InputBid[cell] = 0;
            p.Markets.Price[cell] = 10;
            var routes = p.Routes;
            for (int k = routes.OutStart[p.Here]; k < routes.OutStart[p.Here] + routes.OutCount[p.Here]; k++)
                if (p.Markets.TryFindCell(routes.To[routes.RouteIndex[k]], p.Good, out int there)) p.Markets.Price[there] = 500;
            p.Run();
            Assert.Greater(p.Markets.InputBid[cell], 0, "it would carry the good if it were here");

            p.Markets.InputBid[cell] = 0;
            for (int k = routes.OutStart[p.Here]; k < routes.OutStart[p.Here] + routes.OutCount[p.Here]; k++)
                if (p.Markets.TryFindCell(routes.To[routes.RouteIndex[k]], p.Good, out int there)) p.Markets.Price[there] = 1;
            p.Agents.Destination[p.A] = -1;
            p.Agents.Node[p.A] = p.Here;
            p.Sim.World.Get<CargoTable>().Clear(p.Holder);
            p.Run();
            Assert.AreEqual(0, p.Markets.InputBid[cell]);
        }

        // Wheat pays a little abroad and is here; cheese would pay far more and is not.
        static int StageTwoLoads(Port p)
        {
            int cell = p.Cell(p.Here), abroad = p.Cell(p.There);
            p.Markets.Price[cell] = 10;
            p.Markets.Price[abroad] = 500;
            p.Markets.Stock[cell] = 50;
            Assert.IsTrue(p.Sim.Database.TryGetGood("cheese", QualityGrade.Mid, out var cheese));
            Assert.IsTrue(p.Markets.TryFindCell(p.Here, cheese, out int wanted));
            Assert.IsTrue(p.Markets.TryFindCell(p.There, cheese, out int dear));
            p.Markets.Price[wanted] = 10;
            p.Markets.Price[dear] = 50_000;
            p.Markets.InputBid[wanted] = 0;
            return wanted;
        }

        [Test]
        public void GroupLeavingWithALoad_StillBidsForABetterOne()
        {
            using var p = new Port();
            int wanted = StageTwoLoads(p);
            p.Run();
            Assert.AreEqual(p.There, p.Agents.Destination[p.A], "it takes what is here");
            Assert.Greater(p.Carried(), 0);
            Assert.Greater(p.Markets.InputBid[wanted], 0, "and says it would rather carry the other");

            using var off = new Port(config: "lod.mode,1\nmerchants.bids,1");
            int notBid = StageTwoLoads(off);
            off.Run();
            Assert.AreEqual(0, off.Markets.InputBid[notBid]);
        }

        [Test]
        public void NationsKeep_TheirInternationalBank_InCoins()
        {
            using var sim = TestWorlds.BuildSample();
            sim.AdvanceTurn();
            var bank = sim.World.Get<BankState>();
            var currency = sim.World.Get<CurrencyState>();
            var nodes = sim.World.Get<NodeTable>();
            var banks = sim.World.Get<NodeBanks>();
            int NationOn(int continent) => Enumerable.Range(0, banks.NationCount).First(n => nodes.Continent[banks.NationNode(n)] == continent);
            long share(int c) => sim.Database.Config.InternationalBankReserveShare.MulInt(currency.CoinSupply[c]);

            // Coins move inside each continent: one bank left empty, the other with far too many.
            bank.Reserve[NationOn(0)] += bank.InternationalReserve[0];
            bank.InternationalReserve[0] = 0;
            bank.Reserve[NationOn(1)] -= 3 * share(1);
            bank.InternationalReserve[1] += 3 * share(1);

            sim.AdvanceTurn();
            Assert.Greater(bank.InternationalReserve[0], share(0) / 2, "refilled from its nations");
            Assert.Less(bank.InternationalReserve[1], 2 * share(1) + share(1) / 2, "the excess handed back");
            for (int c = 0; c < currency.ContinentCount; c++)
                Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c));
        }

        static GameSimulation Build3Hops(int hops)
        {
            var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("lod.mode,1", "merchants.judge_hops," + hops, "merchants.must_act,0"), 42).Build();
            sim.AdvanceTurn();
            return sim;
        }

        // A village whose wheat is cheap everywhere within two hops, and dear three hops off, where traders can pay.
        static (int agent, int village, int city) Stage(GameSimulation sim, int far)
        {
            var nodes = sim.World.Get<NodeTable>();
            var agents = sim.World.Get<AgentTable>();
            var markets = sim.World.Get<MarketStore>();
            var routes = sim.World.Get<RouteTable>();
            var purses = sim.World.Get<PurseState>();
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            int village = nodes.LevelStart(WorldLevel.Village);
            int city = nodes.Parent[village];
            int sibling = Enumerable.Range(0, routes.Count).Where(r => routes.From[r] == city && nodes.LevelOf(routes.To[r]) == WorldLevel.City).Select(r => routes.To[r]).First();
            int farVillage = Enumerable.Range(0, routes.Count).Where(r => routes.From[r] == sibling && nodes.LevelOf(routes.To[r]) == WorldLevel.Village).Select(r => routes.To[r]).First();
            for (int n = 0; n < nodes.Count; n++)
                if (markets.TryFindCell(n, wheat, out int c)) markets.Price[c] = 5;
            // Three hops: village, city, the next city, its village.
            Assert.IsTrue(markets.TryFindCell(farVillage, wheat, out int dear));
            markets.Price[dear] = far;
            int a = Enumerable.Range(0, agents.Count).First(i => agents.Owner[i] == OwnerIds.Npc);
            for (int i = 0; i < agents.Count; i++)
                if (i != a) agents.Mode[i] = (byte)ControlMode.Manual;
            agents.Node[a] = village;
            agents.Home[a] = village;
            agents.Destination[a] = -1;
            agents.ArrivalTurn[a] = -1;
            agents.Trait[a] = MerchantTraits.Of(MerchantPersonality.Aggressive, MerchantOccupation.Expedition);
            agents.Cash[a] = Plenty;
            sim.World.Get<CargoTable>().Clear(CargoHolders.Merchant(new AgentId(a)));
            int start = markets.MarketCellStart[village];
            for (int c = start; c < start + markets.MarketCellCount[village]; c++) markets.Stock[c] = 0;
            Assert.IsTrue(markets.TryFindCell(village, wheat, out int own));
            markets.Stock[own] = 100;
            for (int p = 0; p < purses.Count; p++) purses.Float[p] = Plenty;
            return (a, village, city);
        }
    }
}
