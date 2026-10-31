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
    /// <summary>The money loop is closed (DEC-061): a continent's money changes only when coins are minted.</summary>
    public class MoneyTests
    {
        const string City = "aur_n1_r1_c1"; // a branch and the international bank

        static NodeId Node(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.View.TryFindNode(key, out var node), key);
            return node;
        }

        static void AssertConserved(GameSimulation sim, string when)
        {
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++)
                Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c), $"continent {c}, {when}");
        }

        static void Step(GameSimulation sim, string what)
        {
            sim.AdvanceTurn();
            Assert.IsFalse(sim.LastTurnEvents.OfType<CommandRejected>().Any(), what + ": " +
                string.Join("; ", sim.LastTurnEvents.OfType<CommandRejected>().Select(r => r.Reason)));
            AssertConserved(sim, what);
        }

        /// <summary>Puts the player at <paramref name="node"/> with only these local coins; households absorb the difference.</summary>
        static void SetCoins(GameSimulation sim, NodeId node, long coins)
        {
            var player = sim.World.Get<PlayerState>();
            var nodes = sim.World.Get<NodeTable>();
            var purses = sim.World.Get<PurseState>();
            for (int c = 0; c < player.Coins.Length; c++)
            {
                int anyNode = Enumerable.Range(0, nodes.Count).First(n => nodes.Continent[n] == c);
                purses.PayHouseholds(anyNode, player.Coins[c]);
                player.Coins[c] = 0;
            }
            player.Node = node.Value;
            player.LocalCoins = coins;
            purses.PayHouseholds(node.Value, -coins);
        }

        static int NationOf(GameSimulation sim, NodeId node) =>
            sim.World.Get<PurseState>().NodeFloat[node.Value];

        /// <summary>Moves a float's money to households of the same nation, so nothing is created.</summary>
        static void EmptyFloat(GameSimulation sim, NodeId node)
        {
            var purses = sim.World.Get<PurseState>();
            int p = NationOf(sim, node);
            purses.PayHouseholds(node.Value, purses.Float[p]);
            purses.Float[p] = 0;
        }

        [Test]
        public void Seeding_MakesEveryContinentsMoneyItsCoinSupply()
        {
            using var sim = TestWorlds.BuildSample();
            AssertConserved(sim, "start");
            var purses = sim.World.Get<PurseState>();
            for (int p = 0; p < purses.Count; p++) Assert.GreaterOrEqual(purses.Float[p], 0);
            var households = sim.World.Get<HouseholdCash>();
            var population = sim.World.Get<HouseholdState>().Population;
            for (int n = 0; n < households.Cash.Length; n++)
                if (population[n] > 0) Assert.Greater(households.Cash[n], 0, $"node {n}");
            var nation = new NodeId(sim.World.Get<NodeTable>().AncestorAt(Node(sim, City).Value, WorldLevel.Nation));
            Assert.Greater(sim.View.TraderFloat(nation), 0);
            // Households hold most of it: the floats get a tenth of the money nobody holds.
            Assert.Greater(sim.View.HouseholdCash(Node(sim, City)), 0);
            Assert.Greater(sim.World.Get<BankState>().InternationalReserve[0], 0);
        }

        [Test]
        public void NpcEconomy_ConservesMoney_EveryTurn_ForYears()
        {
            // Frequent financial events: bank runs, crunches and spikes all move money too.
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
                "key,value\nfinance.event_chance_permille,500\n"), 7).Build();
            int defaults = 0, bankruptcies = 0;
            var purses = sim.World.Get<PurseState>();
            var households = sim.World.Get<HouseholdCash>();
            for (int t = 0; t < 3 * SimClock.WeeksPerYear + 2; t++)
            {
                sim.AdvanceTurn();
                defaults += sim.LastTurnEvents.OfType<NpcLoanDefaulted>().Count();
                bankruptcies += sim.LastTurnEvents.OfType<FacilityBankrupt>().Count() + sim.LastTurnEvents.OfType<MerchantBankrupt>().Count();
                AssertConserved(sim, $"turn {sim.Turn - 1}");
                // Markets never pay out more than the traders hold, and spending is handed over every turn.
                for (int p = 0; p < purses.Count; p++) Assert.GreaterOrEqual(purses.Float[p], 0, $"float {p}, turn {sim.Turn - 1}");
                Assert.IsTrue(households.Spent.All(s => s == 0));
            }
            Assert.Greater(defaults + bankruptcies, 0, "the run should exercise defaults or bankruptcies");
        }

        [Test]
        public void PlayerActions_ConserveMoney()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            var city = Node(sim, City);
            SetCoins(sim, city, 50_000);
            AssertConserved(sim, "setup");

            Assert.IsTrue(sim.Database.TryGetGood("iron_tools", QualityGrade.Mid, out var tools));
            var facilities = sim.World.Get<FacilityTable>();
            int facility = Enumerable.Range(0, facilities.Count).First(f => facilities.Node[f] == city.Value);

            // Goods, not money: stock put on the shelf changes no continent's money.
            var markets = sim.World.Get<MarketStore>();
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var good));
            Assert.IsTrue(markets.TryFindCell(city.Value, good, out int cell));
            markets.Stock[cell] += 100;
            sim.Actions.Deposit(5_000);
            sim.Actions.BuyGoods(good, 1);
            Step(sim, "deposit and buy");
            sim.Actions.SellGoods(good, 1);
            sim.Actions.BuyLicense(tools);
            sim.Actions.FoundCompany(city, GuidanceAxis.Trade, 1_000);
            Step(sim, "sell, license, company");
            sim.Actions.BuyFacility(new FacilityId(facility), 2_000, GuidanceAxis.Industry);
            var groups = sim.World.Get<GroupTable>();
            sim.Actions.JoinGuild(Enumerable.Range(0, groups.Count).First(g => groups.KindOf(g) == GroupKind.Guild && groups.Node[g] == city.Value));
            Step(sim, "facility and guild");
            sim.Actions.TakeLoan(500);
            sim.Actions.Lobby(new NodeId(sim.World.Get<NodeTable>().AncestorAt(city.Value, WorldLevel.Nation)), PolicyKind.Tolls, 1, 300);
            Step(sim, "loan and lobbying");
            sim.Actions.Exchange(player.LocalContinent, 1 - player.LocalContinent, 1_000);
            Step(sim, "exchange");
            sim.Actions.BuildRoad(Node(sim, "aur_n1_r1_c1_v1"));
            Step(sim, "road");
            sim.Actions.Travel(Node(sim, "aur_n1_r1"), guards: true, insurance: true);
            Step(sim, "travel");
            for (int t = 0; t < 12; t++) Step(sim, $"after, week {t}");
        }

        [Test]
        public void Households_PayForWhatTheyConsume_IntoTheFloat()
        {
            using var sim = TestWorlds.BuildSample();
            var households = sim.World.Get<HouseholdCash>();
            var city = Node(sim, City);
            for (int t = 0; t < 12; t++) sim.AdvanceTurn();
            Assert.Greater(households.SpentYear.Sum(), 0);
            Assert.Greater(households.Desired[city.Value], 0);
            AssertConserved(sim, "after spending");
        }

        [Test]
        public void Households_WithoutCash_BuyLess()
        {
            using var broke = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\nlabor.village_wage_away_permille,0\n"), 42).Build();
            using var paid = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\nlabor.village_wage_away_permille,0\n"), 42).Build();
            var city = Node(broke, City);
            broke.AdvanceTurn();
            paid.AdvanceTurn();
            // The city's households hand all their coins to the traders; only this week's wages come back.
            var households = broke.World.Get<HouseholdCash>();
            broke.World.Get<PurseState>().ChangeFloat(city.Value, households.Cash[city.Value]);
            households.Cash[city.Value] = 0;
            long Met(GameSimulation sim) => sim.World.Get<HouseholdState>().NecessityMet[city.Value];
            foreach (var sim in new[] { broke, paid })
            {
                var markets = sim.World.Get<MarketStore>();
                int end = markets.MarketCellStart[city.Value] + markets.MarketCellCount[city.Value];
                for (int c = markets.MarketCellStart[city.Value]; c < end; c++) markets.Stock[c] = 100000;
            }
            long brokeMet = Met(broke), paidMet = Met(paid);
            // The week's budget follows what they wanted last week, so it binds from the second week.
            for (int t = 0; t < 3; t++)
            {
                broke.AdvanceTurn();
                paid.AdvanceTurn();
            }
            Assert.Less(Met(broke) - brokeMet, Met(paid) - paidMet);
            Assert.GreaterOrEqual(households.Cash[city.Value], 0);
            AssertConserved(broke, "broke households");
        }

        [Test]
        public void Selling_IsCappedByTheTradersFloat()
        {
            using var sim = TestWorlds.BuildSample();
            var city = Node(sim, City);
            SetCoins(sim, city, 0);
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            sim.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, wheat, 10);
            EmptyFloat(sim, city);
            sim.Actions.SellGoods(wheat, 10);
            sim.AdvanceTurn();
            StringAssert.Contains("no money", sim.LastTurnEvents.OfType<CommandRejected>().Single().Reason);
            Assert.AreEqual(10, sim.World.Get<CargoTable>().QuantityOf(CargoHolders.PlayerCart, wheat));

            // With coins for three units the traders buy three.
            var purses = sim.World.Get<PurseState>();
            Assert.IsTrue(sim.World.Get<MarketStore>().TryFindCell(city.Value, wheat, out int cell));
            EmptyFloat(sim, city);
            long price = sim.World.Get<MarketStore>().Price[cell];
            purses.Float[NationOf(sim, city)] = 3 * price;
            purses.PayHouseholds(city.Value, -3 * price);
            new SellGoodsCommand(wheat, 10).Apply(sim.Context);
            Assert.AreEqual(7, sim.World.Get<CargoTable>().QuantityOf(CargoHolders.PlayerCart, wheat));
            Assert.AreEqual(3 * price, sim.World.Get<PlayerState>().LocalCoins);
            Assert.AreEqual(0, purses.Float[NationOf(sim, city)]);
        }

        [Test]
        public void NpcOutput_ReachesTheMarket_EvenWhenTheTradersCannotPay()
        {
            using var normal = TestWorlds.BuildSample();
            using var dry = TestWorlds.BuildSample();
            var purses = dry.World.Get<PurseState>();
            var bank = dry.World.Get<BankState>();
            for (int p = 0; p < purses.Count; p++)
            {
                bank.InternationalReserve[purses.ContinentOf(p)] += purses.Float[p];
                purses.Float[p] = 0;
            }
            long Paid(GameSimulation sim)
            {
                var facilities = sim.World.Get<FacilityTable>();
                return Enumerable.Range(0, facilities.Count).Sum(f => facilities.Cash[f]);
            }
            long Stock(GameSimulation sim)
            {
                var stock = sim.World.Get<MarketStore>().Stock;
                long total = 0;
                for (int k = 0; k < stock.Length; k++) total += stock[k];
                return total;
            }

            normal.AdvanceTurn();
            dry.AdvanceTurn();
            Assert.AreEqual(Stock(normal), Stock(dry));
            Assert.Less(Paid(dry), Paid(normal));
            for (int p = 0; p < purses.Count; p++) Assert.GreaterOrEqual(purses.Float[p], 0);
            AssertConserved(dry, "dry floats");
        }

        [Test]
        public void Producers_LeaveTheirShareOfTheFloat_ForMerchants()
        {
            using var sim = TestWorlds.BuildSample();
            var purses = sim.World.Get<PurseState>();
            var bank = sim.World.Get<BankState>();
            for (int p = 0; p < purses.Count; p++)
            {
                bank.InternationalReserve[purses.ContinentOf(p)] += purses.Float[p] - 100;
                purses.Float[p] = 100;
            }
            var job = sim.Systems.OfType<ProductionSystem>().Single();
            job.Execute(sim.Context);
            for (int p = 0; p < purses.NationCount; p++) Assert.GreaterOrEqual(purses.Float[p], 50);
        }

        [Test]
        public void RichMerchant_PaysWages_ScaledByEmployment()
        {
            long Wage(int employmentPermille)
            {
                using var sim = TestWorlds.BuildSample();
                var agents = sim.World.Get<AgentTable>();
                var purses = sim.World.Get<PurseState>();
                var labor = sim.World.Get<LaborState>();
                var households = sim.World.Get<HouseholdCash>();
                int a = Enumerable.Range(0, agents.Count).First(i => purses.NodeFloat[agents.Node[i]] < purses.NationCount);
                long start = sim.Database.Config.MerchantStartCash[agents.Scale[a]];
                agents.Cash[a] = start + 1_000_000;
                households.Cash[agents.Node[a]] -= 1_000_000;
                labor.Employment[purses.NodeFloat[agents.Node[a]]] = Fx.FromPermille(employmentPermille).Raw;
                sim.Systems.OfType<LaborMarketSystem>().Single().Execute(sim.Context);
                AssertConserved(sim, "wages");
                return start + 1_000_000 - agents.Cash[a];
            }
            long full = Wage(1000), half = Wage(500);
            Assert.AreEqual(100_000, full, 20);
            Assert.AreEqual(50_000, half, 20);
        }

        [Test]
        public void Employment_FollowsRealSpending()
        {
            using var sim = TestWorlds.BuildSample();
            var labor = sim.World.Get<LaborState>();
            var nation = 0;
            for (int t = 0; t < SimClock.WeeksPerQuarter * 2; t++) sim.AdvanceTurn();
            var steady = labor.Rate(nation);

            labor.LongRunActivity[nation] = labor.LongRunActivity[nation] * 4;
            for (int t = 0; t < SimClock.WeeksPerQuarter; t++) sim.AdvanceTurn();
            Assert.Less(labor.Rate(nation).Raw, steady.Raw, "spending far below its long-run level");

            var low = labor.Rate(nation);
            labor.LongRunActivity[nation] = labor.LongRunActivity[nation] / 16;
            for (int t = 0; t < SimClock.WeeksPerQuarter; t++) sim.AdvanceTurn();
            Assert.Greater(labor.Rate(nation).Raw, low.Raw, "spending far above its long-run level");
            Assert.LessOrEqual(labor.Rate(nation).Raw, sim.Database.Config.LaborMaxEmployment.Raw);
        }

        [Test]
        public void IncomeTax_IsCollectedYearly_AndConserved()
        {
            using var sim = TestWorlds.BuildSample();
            var books = sim.World.Get<IncomeTaxState>();
            IncomeTaxCollected tax = null;
            for (int t = 0; t <= SimClock.WeeksPerYear && tax == null; t++)
            {
                sim.AdvanceTurn();
                tax = sim.LastTurnEvents.OfType<IncomeTaxCollected>().SingleOrDefault();
            }
            Assert.IsNotNull(tax);
            Assert.Greater(tax.Households, 0);
            Assert.AreEqual(tax.Households, books.LastHouseholds.Sum());
            Assert.AreEqual(tax.Companies, books.LastCompanies.Sum());
            AssertConserved(sim, "income tax");
        }

        [Test]
        public void IncomeTax_TakesTenPercentOfProfit_NothingOnALoss_AndCountsDividends()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var agents = sim.World.Get<AgentTable>();
            var books = sim.World.Get<IncomeTaxState>();
            sim.AdvanceTurn();
            int gain = Enumerable.Range(0, facilities.Count).First(i => facilities.Owner[i] == OwnerIds.Npc);
            int loss = Enumerable.Range(0, facilities.Count).Last(i => facilities.Owner[i] == OwnerIds.Npc);
            while (!sim.Context.Clock.IsNewYear) sim.AdvanceTurn();

            books.RestartFacility(gain, 1_000);
            facilities.Cash[gain] = 11_000; // profit 10,000 and 5,000 of dividends paid
            books.FacilityDividends[gain] = 5_000;
            books.RestartFacility(loss, 5_000);
            facilities.Cash[loss] = 1_000;
            books.RestartMerchant(0, 1_000);
            agents.Cash[0] = 3_000;
            sim.Systems.OfType<IncomeTaxSystem>().Single().Execute(sim.Context);

            Assert.AreEqual(11_000 - 1_500, facilities.Cash[gain]);
            Assert.AreEqual(1_000, facilities.Cash[loss]);
            Assert.AreEqual(3_000 - 200, agents.Cash[0]);
            Assert.AreEqual(9_500, books.FacilityStart[gain], "the new year's books");
        }

        [Test]
        public void RichFacility_PaysDividends_ThatItsGroupPassesOn()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var groups = sim.World.Get<GroupTable>();
            var households = sim.World.Get<HouseholdCash>();
            int f = Enumerable.Range(0, facilities.Count).First(i => facilities.Owner[i] == OwnerIds.Npc && facilities.Group[i] >= 0
                && groups.KindOf(facilities.Group[i]) != GroupKind.StateEnterprise);
            int g = facilities.Group[f], home = groups.Node[g];
            // The households of the group's home hand the facility ten million coins of profit.
            facilities.Cash[f] += 10_000_000;
            households.Cash[home] -= 10_000_000;
            long groupCash = groups.Cash[g], homeCash = households.Cash[home], facilityCash = facilities.Cash[f];
            sim.AdvanceTurn();
            AssertConserved(sim, "dividends");
            long dividend = facilityCash - facilities.Cash[f];
            Assert.That(dividend, Is.InRange(900_000, 1_100_000), "about 10% of the profit");
            Assert.That(groups.Cash[g] - groupCash, Is.InRange(dividend / 5 - 10_000, dividend / 5 + 10_000), "the group keeps a fifth");
            Assert.Greater(households.Cash[home] - homeCash, dividend / 2, "most goes to its home's households");
        }

        [Test]
        public void State_SpendsReservesAboveItsTarget_AsPublicWages()
        {
            using var sim = TestWorlds.BuildSample();
            var bank = sim.World.Get<BankState>();
            var households = sim.World.Get<HouseholdCash>();
            var city = Node(sim, City);
            int n = NationOf(sim, city);
            // The nation's households hand the state ten million coins.
            bank.Reserve[n] += 10_000_000;
            households.Cash[city.Value] -= 10_000_000;
            long reserve = bank.Reserve[n];
            sim.AdvanceTurn();
            AssertConserved(sim, "state spending");
            Assert.That(reserve - bank.Reserve[n], Is.InRange(180_000, 230_000), "about 2% of the excess a week");

            // The player's deposits are not the state's to spend.
            bank.PlayerDeposit[n] = bank.Reserve[n];
            reserve = bank.Reserve[n];
            sim.AdvanceTurn();
            Assert.Greater(bank.Reserve[n], reserve - 10_000);
        }

        [Test]
        public void Needs_CountUsuallyTradedGoods_NotGoodsNeverSold()
        {
            using var sim = TestWorlds.BuildSample();
            var households = sim.World.Get<HouseholdState>();
            var cash = sim.World.Get<HouseholdCash>();
            var markets = sim.World.Get<MarketStore>();
            var city = Node(sim, City);
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            Assert.IsTrue(markets.TryFindCell(city.Value, wheat, out int cell));
            int start = markets.MarketCellStart[city.Value], end = start + markets.MarketCellCount[city.Value];

            // Nothing offered, ever: no need counts.
            for (int c = start; c < end; c++)
            {
                markets.Stock[c] = 0;
                cash.LastOffered[c] = HouseholdCash.NeverOffered;
            }
            var merchantless = households.NecessityWanted[city.Value];
            for (int c = start; c < end; c++) markets.Supply[c] = 0;
            int weeksAgo = sim.Database.Config.NeedMemoryWeeks / 2;

            // Wheat sold half a year ago and gone now counts: it is a shortage, not a good nobody sells.
            cash.LastOffered[cell] = sim.Turn - weeksAgo;
            sim.AdvanceTurn();
            Assert.Greater(households.NecessityWanted[city.Value], merchantless);

            // Forgotten after the memory runs out, unless the market offered it meanwhile.
            cash.LastOffered[cell] = sim.Turn - sim.Database.Config.NeedMemoryWeeks - 5;
            markets.Stock[cell] = 0;
            markets.Supply[cell] = 0;
            int before = cash.LastOffered[cell];
            sim.AdvanceTurn();
            Assert.IsTrue(cash.LastOffered[cell] == before || cash.LastOffered[cell] == sim.Turn - 1);
        }

        [Test]
        public void UpperMarkets_GetGoods_AndTheirHouseholdsBuy()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            var markets = sim.World.Get<MarketStore>();
            var cash = sim.World.Get<HouseholdCash>();
            int nation = nodes.AncestorAt(Node(sim, City).Value, WorldLevel.Nation);
            long start = cash.Cash[nation];
            for (int t = 0; t < 2 * SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            Assert.Greater(cash.SpentYear[nation], 0, "the nation's households bought something this year");
            int first = markets.MarketCellStart[nation];
            Assert.IsTrue(Enumerable.Range(first, markets.MarketCellCount[nation]).Any(c => cash.LastOffered[c] != HouseholdCash.NeverOffered));
            AssertConserved(sim, "upper markets");
        }

        [Test]
        public void Exchange_NeedsCoinsAtTheInternationalBanks()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            SetCoins(sim, Node(sim, City), 1_000);
            var bank = sim.World.Get<BankState>();
            int other = 1 - player.LocalContinent;
            bank.InternationalReserve[other] = 0;
            sim.Actions.Exchange(player.LocalContinent, other, 1_000);
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());
        }
    }
}
