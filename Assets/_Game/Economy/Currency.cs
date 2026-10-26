using System.IO;
using FES.Core;
using FES.Data;
using FES.World;

namespace FES.Economy
{
    /// <summary>
    /// Per-continent coinage. One metal-standard coin system, but every continent mints its own coins
    /// yearly, so purchasing power and exchange rates diverge.
    /// </summary>
    public sealed class CurrencyState : IPersistentComponent
    {
        public readonly int ContinentCount;
        public readonly long[] GoldReserve;
        public readonly long[] SilverReserve;
        public readonly long[] CoinSupply;
        /// <summary>Coin supply at world start; the inflation baseline.</summary>
        public readonly long[] BaselineCoinSupply;
        /// <summary>Price level relative to the baseline (1.0 = no inflation). Scales market anchor prices.</summary>
        public readonly Fx[] PriceIndex;
        /// <summary>Value of one local coin in reference units (world-average purchasing power = 1.0).</summary>
        public readonly Fx[] ExchangeRate;

        public string Key => "economy.currency";
        public int Version => 1;

        public CurrencyState(int continentCount)
        {
            ContinentCount = continentCount;
            GoldReserve = new long[continentCount];
            SilverReserve = new long[continentCount];
            CoinSupply = new long[continentCount];
            BaselineCoinSupply = new long[continentCount];
            PriceIndex = new Fx[continentCount];
            ExchangeRate = new Fx[continentCount];
            for (int c = 0; c < continentCount; c++)
            {
                PriceIndex[c] = Fx.One;
                ExchangeRate[c] = Fx.One;
            }
        }

        /// <summary>Converts an amount of <paramref name="fromContinent"/> coins into <paramref name="toContinent"/> coins.</summary>
        public long Convert(long amount, int fromContinent, int toContinent) =>
            fromContinent == toContinent ? amount : (ExchangeRate[fromContinent] / ExchangeRate[toContinent]).MulInt(amount);

        public void Write(BinaryWriter writer)
        {
            writer.Write(ContinentCount);
            for (int c = 0; c < ContinentCount; c++)
            {
                writer.Write(GoldReserve[c]);
                writer.Write(SilverReserve[c]);
                writer.Write(CoinSupply[c]);
                writer.Write(BaselineCoinSupply[c]);
                writer.Write(PriceIndex[c].Raw);
                writer.Write(ExchangeRate[c].Raw);
            }
        }

        public void Read(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count != ContinentCount) throw new InvalidDataException($"Saved continent count {count} != {ContinentCount}.");
            for (int c = 0; c < count; c++)
            {
                GoldReserve[c] = reader.ReadInt64();
                SilverReserve[c] = reader.ReadInt64();
                CoinSupply[c] = reader.ReadInt64();
                BaselineCoinSupply[c] = reader.ReadInt64();
                PriceIndex[c] = Fx.FromRaw(reader.ReadInt64());
                ExchangeRate[c] = Fx.FromRaw(reader.ReadInt64());
            }
        }
    }

    /// <summary>Monetary rules. Replace the service to change how minting, inflation and exchange rates work.</summary>
    public interface ICurrencyModel
    {
        void Initialize(CurrencyState state, SimulationConfig config, ulong seed);
        void MintYear(CurrencyState state, int continent, SimulationConfig config);
        void UpdateRates(CurrencyState state);
    }

    /// <summary>
    /// Gold/silver standard. Each year a fixed share of the remaining metal reserves is mined and minted.
    /// Minting grows coin supply (inflation); as reserves deplete, minting slows and the coin appreciates
    /// against continents that still mint heavily.
    /// </summary>
    public sealed class GoldStandardCurrencyModel : ICurrencyModel
    {
        static readonly uint Stream = DetHash.Of("economy.currency.init");

        public void Initialize(CurrencyState state, SimulationConfig config, ulong seed)
        {
            for (int c = 0; c < state.ContinentCount; c++)
            {
                // Deterministic variation so continents start with different metal endowments.
                var rng = DetRandom.For(seed, 0, Stream, c);
                var goldScale = rng.NextSpread(config.CurrencyEndowmentSpreadPermille);
                var silverScale = rng.NextSpread(config.CurrencyEndowmentSpreadPermille);
                state.GoldReserve[c] = goldScale.MulInt(config.InitialGoldReserve);
                state.SilverReserve[c] = silverScale.MulInt(config.InitialSilverReserve);
                state.CoinSupply[c] = config.InitialCoinSupply;
                state.BaselineCoinSupply[c] = config.InitialCoinSupply;
            }
            UpdateRates(state);
        }

        public void MintYear(CurrencyState state, int continent, SimulationConfig config)
        {
            long gold = config.MintRatePerYear.MulInt(state.GoldReserve[continent]);
            long silver = config.MintRatePerYear.MulInt(state.SilverReserve[continent]);
            state.GoldReserve[continent] -= gold;
            state.SilverReserve[continent] -= silver;
            state.CoinSupply[continent] += gold * config.CoinsPerGoldUnit + silver * config.CoinsPerSilverUnit;
        }

        public void UpdateRates(CurrencyState state)
        {
            long sumIndexRaw = 0;
            for (int c = 0; c < state.ContinentCount; c++)
            {
                state.PriceIndex[c] = Fx.FromRatio(state.CoinSupply[c], state.BaselineCoinSupply[c]);
                sumIndexRaw += state.PriceIndex[c].Raw;
            }
            if (state.ContinentCount == 0) return;
            var average = Fx.FromRaw(sumIndexRaw / state.ContinentCount);
            for (int c = 0; c < state.ContinentCount; c++)
                state.ExchangeRate[c] = average / state.PriceIndex[c];
        }
    }

    public sealed class CurrencyMinted : ISimEvent
    {
        public readonly int Continent;
        public readonly int Year;

        public CurrencyMinted(int continent, int year)
        {
            Continent = continent;
            Year = year;
        }
    }

    /// <summary>
    /// Yearly minting at the first week of each year, plus rate updates every turn. Runs in the Finance
    /// phase after pricing, so markets see this turn's price index on the next turn (one-turn lag by design).
    /// </summary>
    public sealed class CurrencySystem : ISimSystem
    {
        CurrencyState _state;
        ICurrencyModel _model;
        SimulationConfig _config;

        public string Id => "economy.currency";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx)
        {
            _state = ctx.World.Get<CurrencyState>();
            _model = ctx.Services.Get<ICurrencyModel>();
            _config = ctx.Services.Get<GameDatabase>().Config;
        }

        public void Execute(SimContext ctx)
        {
            if (ctx.Clock.IsNewYear && ctx.Turn > 0)
            {
                for (int c = 0; c < _state.ContinentCount; c++)
                {
                    _model.MintYear(_state, c, _config);
                    ctx.Events.Publish(new CurrencyMinted(c, ctx.Clock.Year));
                }
            }
            _model.UpdateRates(_state);
        }
    }
}
