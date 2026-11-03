using System.Collections.Generic;
using System.IO;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Trading ventures and the continental exchange (DEC-132).</summary>
    public class VentureTests
    {
        const long Plenty = 1_000_000_000;

        static int Exchange(GameSimulation sim, int continent) =>
            sim.World.Get<AssetMarkets>().ExchangeNode(sim.World.Get<NodeBanks>(), continent);

        [Test]
        public void Ventures_AreFounded_AtPortsWithARouteAcrossTheSea()
        {
            using var sim = TestWorlds.BuildSample();
            var ventures = sim.World.Get<VentureTable>();
            var nodes = sim.World.Get<NodeTable>();
            Assert.Greater(ventures.Count, 0);
            for (int v = 0; v < ventures.Count; v++)
            {
                Assert.AreEqual(ventures.Continent[v], nodes.Continent[ventures.Port[v]]);
                Assert.AreNotEqual(nodes.Continent[ventures.Port[v]], nodes.Continent[ventures.Far[v]]);
                Assert.GreaterOrEqual(VentureBook.Nation(sim.World, v), 0);
            }
        }

        [Test]
        public void AVenture_RaisesItsCapital_SailsAndComesHome()
        {
            using var sim = TestWorlds.BuildSample();
            var ventures = sim.World.Get<VentureTable>();
            bool sailed = false;
            for (int t = 0; t < 3 * SimClock.WeeksPerYear && !(sailed && ventures.Voyages[0] > 0); t++)
            {
                sim.AdvanceTurn();
                sailed |= ventures.AtSea(0, sim.World.Get<SimClock>().Turn);
            }
            Assert.IsTrue(sailed, "it fitted out a voyage");
            Assert.Greater(ventures.Voyages[0], 0, "and came home");
        }

        [Test]
        public void AShare_IsTheVenturesValue_OverItsShares_TimesTheExchangePremium()
        {
            using var sim = TestWorlds.BuildSample();
            var ventures = sim.World.Get<VentureTable>();
            var config = sim.Database.Config;
            ventures.Cash[0] = 10_000;
            ventures.Stake[0] = 4_000;
            ventures.ProfitYear[0] = 3_000;
            long fundamental = (10_000 + 4_000 + config.VentureValueYears * 3_000) / config.VentureShares;
            Assert.AreEqual(fundamental, VentureBook.Fundamental(ventures, config, 0));
            var markets = sim.World.Get<AssetMarkets>();
            markets.Premium[markets.Venture(ventures.Continent[0])] = Fx.FromInt(3).Raw;
            Assert.AreEqual(3 * fundamental, VentureBook.Price(sim.World, config, 0));
        }

        [Test]
        public void ThePlayer_TradesVentureShares_AtTheContinentalExchange()
        {
            using var sim = TestWorlds.BuildSample();
            var ventures = sim.World.Get<VentureTable>();
            var player = sim.World.Get<PlayerState>();
            int exchange = Exchange(sim, ventures.Continent[0]);
            Assert.GreaterOrEqual(exchange, 0);
            player.Node = exchange == 0 ? 1 : 0;
            player.CoinsAt(exchange) = Plenty;
            Assert.AreEqual("Venture shares trade at the continent's exchange.", new BuyVentureShareCommand(0, 1).Validate(sim.Context).Error);

            player.Node = exchange;
            ventures.Cash[0] = 50_000;
            long price = VentureBook.Price(sim.World, sim.Database.Config, 0);
            var buy = new BuyVentureShareCommand(0, 20);
            Assert.IsNull(buy.Validate(sim.Context).Error);
            buy.Apply(sim.Context);
            Assert.AreEqual(20, ventures.PlayerShares[0]);
            Assert.AreEqual(Plenty - 20 * price, player.CoinsAt(exchange));
            Assert.IsNotNull(new BuyVentureShareCommand(0, sim.Database.Config.VentureShares).Validate(sim.Context).Error, "only what the households hold");
            Assert.IsNotNull(new SellVentureShareCommand(0, 21).Validate(sim.Context).Error);
            Assert.IsNull(new SellVentureShareCommand(0, 5).Validate(sim.Context).Error);
        }

        [Test]
        public void AContinentalBurst_StopsCreditInEveryNationOfTheContinent()
        {
            using var sim = TestWorlds.BuildSample();
            var markets = sim.World.Get<AssetMarkets>();
            var banks = sim.World.Get<NodeBanks>();
            var nodes = sim.World.Get<NodeTable>();
            int continent = sim.World.Get<VentureTable>().Continent[0];
            int m = markets.Venture(continent);
            var events = new List<ISimEvent>();
            for (int t = 0; t <= SimClock.WeeksPerQuarter; t++)
            {
                markets.Premium[m] = Fx.FromInt(3).Raw;
                sim.AdvanceTurn();
                events.AddRange(sim.LastTurnEvents);
                if (markets.PublishedPremium[m] != Fx.OneRaw) break;
            }
            Assert.AreEqual(AssetMarketKind.Venture, events.OfType<MarketBurst>().Single().Kind);
            var finance = sim.World.Get<FinanceState>();
            for (int n = 0; n < banks.NationCount; n++)
                if (nodes.Continent[banks.NationNode(n)] == continent)
                    Assert.AreEqual(FinancialEventKind.CreditCrunch, finance.EventOf(n), $"nation {n}");
        }

        [Test]
        public void Ventures_KeepTheCoins_AndAreSaved()
        {
            using var sim = TestWorlds.BuildSample();
            var currency = sim.World.Get<CurrencyState>();
            for (int t = 0; t < 80; t++)
            {
                sim.AdvanceTurn();
                for (int c = 0; c < currency.ContinentCount; c++)
                    Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c), $"turn {t}");
            }
            ulong hash = sim.ComputeHash();
            using var buffer = new MemoryStream();
            sim.Save(buffer);
            buffer.Position = 0;
            sim.Load(buffer);
            Assert.AreEqual(hash, sim.ComputeHash());
        }

        [Test]
        public void AnOlderMarketsSection_LoadsWithFreshContinentalExchanges()
        {
            using var sim = TestWorlds.BuildSample();
            var markets = sim.World.Get<AssetMarkets>();
            markets.Premium[0] = Fx.FromInt(2).Raw;
            markets.Premium[markets.Venture(0)] = Fx.FromInt(4).Raw;
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) markets.Write(writer);
            // Version 1 ended before the continental block: four bytes of count, then per continent eight longs, the debt history, a phase and a count of bursts.
            int block = sizeof(int) + markets.ContinentCount * ((8 + AssetMarkets.History) * sizeof(long) + 1 + sizeof(int));
            var v1 = buffer.ToArray().Take((int)buffer.Length - block).ToArray();
            var migrated = new AssetMarketsV1Migration().Migrate(v1);
            markets.Premium[0] = Fx.OneRaw;
            using var reader = new BinaryReader(new MemoryStream(migrated));
            markets.Read(reader);
            Assert.AreEqual(Fx.FromInt(2).Raw, markets.Premium[0]);
            Assert.AreEqual(Fx.FromInt(4).Raw, markets.Premium[markets.Venture(0)], "left as it was: the old save had none");
        }
    }
}
