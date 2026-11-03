using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>DEC-133: the bank called for margin on the player's short sales with it; it buys them back on <see cref="CoverTurn"/>.</summary>
    public sealed class ShortCalled : ISimEvent
    {
        public readonly NodeId Nation;
        /// <summary>What the deposit lacks to the call line, in the bank's coin.</summary>
        public readonly long Shortfall;
        public readonly int CoverTurn;

        public ShortCalled(NodeId nation, long shortfall, int coverTurn)
        {
            Nation = nation;
            Shortfall = shortfall;
            CoverTurn = coverTurn;
        }
    }

    /// <summary>DEC-133: the bank bought back the player's short sales after a margin call; <see cref="Lent"/> of the cost became a loan.</summary>
    public sealed class ShortsCovered : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly long Cost;
        public readonly long Lent;
        public readonly int Turn;

        public ShortsCovered(NodeId nation, long cost, long lent, int turn)
        {
            Nation = nation;
            Cost = cost;
            Lent = lent;
            Turn = turn;
        }
    }

    /// <summary>DEC-133: the player's short sales, one row per asset: facility shares borrowed from its group, or venture shares from its households.</summary>
    public sealed class ShortTable : NativeTable
    {
        /// <summary>A facility, or a venture when <see cref="Venture"/> is set.</summary>
        public readonly NativeColumn<int> Asset;
        public readonly NativeColumn<byte> Venture;
        public readonly NativeColumn<int> Shares;
        /// <summary>The nation whose bank holds the margin: the facility's, or the continental exchange's.</summary>
        public readonly NativeColumn<int> Bank;

        public override string Key => "agents.shorts";
        public override int Version => 1;

        public ShortTable()
        {
            Asset = Column<int>();
            Venture = Column<byte>();
            Shares = Column<int>();
            Bank = Column<int>();
        }

        public int Find(bool venture, int asset)
        {
            for (int r = 0; r < Shares.Length; r++)
                if (Asset[r] == asset && (Venture[r] != 0) == venture) return r;
            return -1;
        }

        public int Held(bool venture, int asset)
        {
            int r = Find(venture, asset);
            return r < 0 ? 0 : Shares[r];
        }

        public void Add(bool venture, int asset, int bank, int count)
        {
            int r = Find(venture, asset);
            if (r >= 0)
            {
                Shares[r] += count;
                if (Shares[r] <= 0) RemoveRowSwapBack(r);
                return;
            }
            if (count <= 0) return;
            Asset.Add(asset);
            Venture.Add((byte)(venture ? 1 : 0));
            Shares.Add(count);
            Bank.Add(bank);
        }

        public void RemoveRow(int r) => RemoveRowSwapBack(r);
    }

    /// <summary>DEC-133: per asset market, the turn the player first traded there and whether they hold its licence to sell short; per nation, the turn its bank called for margin on them.</summary>
    public sealed class ShortLicences : IPersistentComponent
    {
        public readonly int[] FirstTrade;
        public readonly byte[] Licensed;
        public readonly int[] CalledTurn;

        public string Key => "agents.short_licences";
        public int Version => 1;

        public ShortLicences(int markets, int nations)
        {
            FirstTrade = new int[markets];
            Licensed = new byte[markets];
            CalledTurn = new int[nations];
            Array.Fill(FirstTrade, -1);
            Array.Fill(CalledTurn, -1);
        }

        public void Traded(int market, int turn)
        {
            if (market >= 0 && FirstTrade[market] < 0) FirstTrade[market] = turn;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(FirstTrade.Length);
            for (int m = 0; m < FirstTrade.Length; m++)
            {
                writer.Write(FirstTrade[m]);
                writer.Write(Licensed[m]);
            }
            writer.Write(CalledTurn.Length);
            foreach (int t in CalledTurn) writer.Write(t);
        }

        public void Read(BinaryReader reader)
        {
            int markets = reader.ReadInt32();
            if (markets != FirstTrade.Length) throw new InvalidDataException($"agents.short_licences: {markets} markets, the map has {FirstTrade.Length}.");
            for (int m = 0; m < markets; m++)
            {
                FirstTrade[m] = reader.ReadInt32();
                Licensed[m] = reader.ReadByte();
            }
            int nations = reader.ReadInt32();
            if (nations != CalledTurn.Length) throw new InvalidDataException($"agents.short_licences: {nations} nations, the map has {CalledTurn.Length}.");
            for (int n = 0; n < nations; n++) CalledTurn[n] = reader.ReadInt32();
        }
    }

    /// <summary>DEC-133: prices, markets and margins of short sales.</summary>
    public static class ShortRules
    {
        public static long Price(WorldState world, GameDatabase db, bool venture, int asset) =>
            venture ? VentureBook.Price(world, db.Config, asset) : ShareBook.Price(world, db, asset);

        /// <summary>The asset market an asset trades on: its nation's exchange or its continent's.</summary>
        public static int Market(WorldState world, bool venture, int asset)
        {
            var markets = world.Get<AssetMarkets>();
            if (venture) return markets.Venture(world.Get<VentureTable>().Continent[asset]);
            return AssetMarkets.Stock(world.Get<NodeBanks>().NationOf(world.Get<FacilityTable>().Node[asset]));
        }

        /// <summary>The nation whose bank holds a short's margin.</summary>
        public static int Bank(WorldState world, bool venture, int asset)
        {
            if (!venture) return world.Get<NodeBanks>().NationOf(world.Get<FacilityTable>().Node[asset]);
            return world.Get<AssetMarkets>().VentureLender(world.Get<NodeBanks>(), world.Get<VentureTable>().Continent[asset]);
        }

        /// <summary>The nation whose households buy and lend the asset.</summary>
        public static int Households(WorldState world, bool venture, int asset) =>
            venture ? VentureBook.Nation(world, asset) : world.Get<NodeBanks>().NationOf(world.Get<FacilityTable>().Node[asset]);

        /// <summary>What buying back all the short sales held at a bank would cost now.</summary>
        public static long Value(WorldState world, GameDatabase db, int bank)
        {
            var shorts = world.Get<ShortTable>();
            long value = 0;
            for (int r = 0; r < shorts.Shares.Length; r++)
                if (shorts.Bank[r] == bank) value += shorts.Shares[r] * Price(world, db, shorts.Venture[r] != 0, shorts.Asset[r]);
            return value;
        }

        /// <summary>The part of the deposit at a bank that its short sales hold: <paramref name="share"/> of what buying them back costs.</summary>
        public static long Required(WorldState world, GameDatabase db, int bank, Fx share) => share.MulInt(Value(world, db, bank));

        /// <summary>Shares the lender has lent out already: a group its facility's, the households a venture's.</summary>
        public static int Lent(WorldState world, bool venture, int asset) => world.Get<ShortTable>().Held(venture, asset);

        public static void Traded(WorldState world, bool venture, int asset, int turn) =>
            world.Get<ShortLicences>().Traded(Market(world, venture, asset), turn);
    }

    static class ShortDesk
    {
        /// <summary>Why the player may not trade the asset short here, or null.</summary>
        public static string Check(SimContext ctx, bool venture, int asset)
        {
            var error = venture ? ContinentalExchange.Check(ctx, asset) : Exchange.Check(ctx, new FacilityId(asset));
            if (error != null) return error;
            if (ShortRules.Bank(ctx.World, venture, asset) < 0) return "No bank holds the margin here.";
            return ctx.World.Get<ShortLicences>().Licensed[ShortRules.Market(ctx.World, venture, asset)] != 0 ? null : "Short selling needs this exchange's licence.";
        }
    }

    /// <summary>
    /// DEC-133: a licence to sell short on the exchange where the player stands, for a fee, once they have traded there for
    /// a year and hold a deposit at its bank.
    /// </summary>
    public sealed class BuyShortLicenceCommand : ICommand
    {
        public readonly AssetMarketKind Kind;

        public BuyShortLicenceCommand(AssetMarketKind kind) => Kind = kind;

        int Market(SimContext ctx, out int bank)
        {
            var player = ctx.World.Get<PlayerState>();
            var banks = ctx.World.Get<NodeBanks>();
            var markets = ctx.World.Get<AssetMarkets>();
            bank = -1;
            if (player.IsTravelling) return -1;
            if (Kind == AssetMarketKind.Stock)
            {
                bank = banks.BranchNation(player.Node);
                return bank < 0 ? -1 : AssetMarkets.Stock(bank);
            }
            if (Kind != AssetMarketKind.Venture) return -1;
            int continent = ctx.World.Get<NodeTable>().Continent[player.Node];
            if (markets.ExchangeNode(banks, continent) != player.Node) return -1;
            bank = markets.VentureLender(banks, continent);
            return bank < 0 ? -1 : markets.Venture(continent);
        }

        public CommandResult Validate(SimContext ctx)
        {
            int market = Market(ctx, out int bank);
            if (market < 0) return CommandResult.Fail("There is no such exchange here.");
            var licences = ctx.World.Get<ShortLicences>();
            if (licences.Licensed[market] != 0) return CommandResult.Fail("The player holds this licence.");
            var config = ctx.Services.Get<GameDatabase>().Config;
            if (licences.FirstTrade[market] < 0 || ctx.Turn - licences.FirstTrade[market] < config.ShortLicenceWeeks)
                return CommandResult.Fail("The licence needs a year of trading on this exchange.");
            if (ctx.World.Get<BankState>().PlayerDeposit[bank] < config.ShortLicenceDeposit) return CommandResult.Fail("The licence needs a deposit at this exchange's bank.");
            var player = ctx.World.Get<PlayerState>();
            return player.CoinsAt(player.Node) >= config.ShortLicenceFee ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            int market = Market(ctx, out int bank);
            var config = ctx.Services.Get<GameDatabase>().Config;
            var player = ctx.World.Get<PlayerState>();
            player.CoinsAt(player.Node) -= config.ShortLicenceFee;
            ctx.World.Get<BankState>().Reserve[bank] += config.ShortLicenceFee;
            ctx.World.Get<ShortLicences>().Licensed[market] = 1;
        }
    }

    /// <summary>
    /// DEC-133: the player borrows shares, a facility's from its group or a venture's from its households, and sells them to
    /// the households at the price. The proceeds go into the deposit at the exchange's bank, which must then hold the
    /// opening share of what buying all its shorts back would cost.
    /// </summary>
    public sealed class SellShortCommand : ICommand
    {
        public readonly bool Venture;
        public readonly int Asset;
        public readonly int Count;

        public SellShortCommand(bool venture, int asset, int count)
        {
            Venture = venture;
            Asset = asset;
            Count = count;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (Count <= 0) return CommandResult.Fail("Count must be positive.");
            var error = ShortDesk.Check(ctx, Venture, Asset);
            if (error != null) return CommandResult.Fail(error);
            var db = ctx.Services.Get<GameDatabase>();
            var config = db.Config;
            int lent = ShortRules.Lent(ctx.World, Venture, Asset) + Count;
            int room = Venture ? VentureBook.HouseholdShares(ctx.World.Get<VentureTable>(), config, Asset)
                : config.FacilityShares - 1 - ctx.World.Get<ShareTable>().Outside(Asset) + ShortRules.Lent(ctx.World, false, Asset);
            if (lent > config.ShortMaxLentShares || lent > room) return CommandResult.Fail("No one lends so many shares.");
            int bank = ShortRules.Bank(ctx.World, Venture, Asset);
            long proceeds = Count * ShortRules.Price(ctx.World, db, Venture, Asset);
            long required = config.ShortOpenShare.MulInt(ShortRules.Value(ctx.World, db, bank) + proceeds);
            return ctx.World.Get<BankState>().PlayerDeposit[bank] + proceeds >= required
                ? CommandResult.Success : CommandResult.Fail("The deposit at the exchange's bank does not cover the margin.");
        }

        public void Apply(SimContext ctx)
        {
            var db = ctx.Services.Get<GameDatabase>();
            long price = ShortRules.Price(ctx.World, db, Venture, Asset);
            int households = ShortRules.Households(ctx.World, Venture, Asset);
            long taken = BondDesk.TakeFromHouseholds(ctx.World, db.Config, ctx.Turn, households, Count * price);
            int sold = (int)(taken / price);
            if (taken > sold * price) MoneyLedger.PayNationHouseholds(ctx.World, households, taken - sold * price);
            if (sold <= 0) return;
            int bank = ShortRules.Bank(ctx.World, Venture, Asset);
            var bankState = ctx.World.Get<BankState>();
            bankState.PlayerDeposit[bank] += sold * price;
            bankState.Reserve[bank] += sold * price;
            // A facility's borrowed shares pass to the households; a venture's are lent and bought by the same households.
            if (!Venture) ctx.World.Get<ShareTable>().Add(Asset, ShareTable.Households(households), sold);
            ctx.World.Get<ShortTable>().Add(Venture, Asset, bank, sold);
            ShortRules.Traded(ctx.World, Venture, Asset, ctx.Turn);
        }
    }

    /// <summary>DEC-133: the player buys back shares sold short, from the households at the price, paid from the deposit at the exchange's bank, and returns them to the lender.</summary>
    public sealed class CoverShortCommand : ICommand
    {
        public readonly bool Venture;
        public readonly int Asset;
        public readonly int Count;

        public CoverShortCommand(bool venture, int asset, int count)
        {
            Venture = venture;
            Asset = asset;
            Count = count;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (Count <= 0) return CommandResult.Fail("Count must be positive.");
            var error = Venture ? ContinentalExchange.Check(ctx, Asset) : Exchange.Check(ctx, new FacilityId(Asset));
            if (error != null) return CommandResult.Fail(error);
            if (ctx.World.Get<ShortTable>().Held(Venture, Asset) < Count) return CommandResult.Fail("The player has sold fewer short.");
            int households = ShortRules.Households(ctx.World, Venture, Asset);
            if (!Venture && ctx.World.Get<ShareTable>().Held(Asset, ShareTable.Households(households)) < Count) return CommandResult.Fail("No one sells so many shares.");
            long cost = Count * ShortRules.Price(ctx.World, ctx.Services.Get<GameDatabase>(), Venture, Asset);
            int bank = ShortRules.Bank(ctx.World, Venture, Asset);
            return ctx.World.Get<BankState>().PayableDeposit(bank) >= cost ? CommandResult.Success : CommandResult.Fail("The deposit at the exchange's bank cannot pay for them.");
        }

        public void Apply(SimContext ctx)
        {
            var db = ctx.Services.Get<GameDatabase>();
            ShortRules.Traded(ctx.World, Venture, Asset, ctx.Turn);
            ShortBuyBack.Run(ctx.World, db, Venture, Asset, Count, fromDeposit: true);
        }
    }

    static class ShortBuyBack
    {
        /// <summary>Buys back shares sold short at the price, from the deposit at the bank as far as it pays, and returns them. Returns what the deposit could not pay.</summary>
        public static long Run(WorldState world, GameDatabase db, bool venture, int asset, int count, bool fromDeposit)
        {
            long cost = count * ShortRules.Price(world, db, venture, asset);
            int bank = ShortRules.Bank(world, venture, asset);
            int households = ShortRules.Households(world, venture, asset);
            var bankState = world.Get<BankState>();
            long paid = fromDeposit ? Math.Min(cost, bankState.PayableDeposit(bank)) : 0;
            bankState.PlayerDeposit[bank] -= paid;
            bankState.Reserve[bank] -= paid;
            MoneyLedger.PayNationHouseholds(world, households, cost); // what the deposit does not pay, the caller settles
            if (!venture)
            {
                var shares = world.Get<ShareTable>();
                int back = Math.Min(count, shares.Held(asset, ShareTable.Households(households)));
                shares.Add(asset, ShareTable.Households(households), -back); // returned to the group
            }
            world.Get<ShortTable>().Add(venture, asset, bank, -count);
            return cost - paid;
        }
    }

    /// <summary>
    /// DEC-133: each week the player pays the lenders a fee on their short sales, from the deposit at the exchange's bank.
    /// When the deposit falls under the call share of what buying them back costs, the bank calls for margin; after the
    /// call's weeks it buys them all back, from the deposit, then the coins of its continent, and lends the rest.
    /// </summary>
    public sealed class ShortSystem : ISimSystem
    {
        GameDatabase _db;
        WorldState _world;
        ShortTable _shorts;
        ShortLicences _licences;
        BankState _bank;
        NodeBanks _banks;
        NodeTable _nodes;

        public string Id => "agents.shorts";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Shorts;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _world = ctx.World;
            _shorts = ctx.World.Get<ShortTable>();
            _licences = ctx.World.Get<ShortLicences>();
            _bank = ctx.World.Get<BankState>();
            _banks = ctx.World.Get<NodeBanks>();
            _nodes = ctx.World.Get<NodeTable>();
        }

        public void Execute(SimContext ctx)
        {
            if (_shorts.Count == 0)
            {
                Array.Fill(_licences.CalledTurn, -1);
                return;
            }
            for (int r = 0; r < _shorts.Count; r++) PayFee(r);
            for (int n = 0; n < _licences.CalledTurn.Length; n++) Margin(ctx, n);
        }

        void PayFee(int r)
        {
            bool venture = _shorts.Venture[r] != 0;
            int asset = _shorts.Asset[r], bank = _shorts.Bank[r];
            long fee = _db.Config.ShortFee.MulInt(_shorts.Shares[r] * ShortRules.Price(_world, _db, venture, asset)) / SimClock.WeeksPerYear;
            fee = Math.Min(fee, _bank.PayableDeposit(bank));
            if (fee <= 0) return;
            _bank.PlayerDeposit[bank] -= fee;
            _bank.Reserve[bank] -= fee;
            if (venture) MoneyLedger.PayNationHouseholds(_world, ShortRules.Households(_world, true, asset), fee);
            else
            {
                int group = _world.Get<FacilityTable>().Group[asset];
                if (group >= 0) _world.Get<GroupTable>().Cash[group] += fee;
                else MoneyLedger.PayNationHouseholds(_world, bank, fee);
            }
        }

        void Margin(SimContext ctx, int bank)
        {
            var config = _db.Config;
            long value = ShortRules.Value(_world, _db, bank);
            long line = config.ShortCallShare.MulInt(value);
            if (value <= 0 || _bank.PlayerDeposit[bank] >= line)
            {
                _licences.CalledTurn[bank] = -1;
                return;
            }
            if (_licences.CalledTurn[bank] < 0)
            {
                _licences.CalledTurn[bank] = ctx.Turn;
                ctx.Events.Publish(new ShortCalled(new NodeId(_banks.NationNode(bank)), line - _bank.PlayerDeposit[bank], ctx.Turn + config.MarginCallWeeks));
                return;
            }
            if (ctx.Turn - _licences.CalledTurn[bank] < config.MarginCallWeeks) return;
            _licences.CalledTurn[bank] = -1;
            long unpaid = 0;
            for (int r = _shorts.Count - 1; r >= 0; r--)
                if (_shorts.Bank[r] == bank) unpaid += ShortBuyBack.Run(_world, _db, _shorts.Venture[r] != 0, _shorts.Asset[r], _shorts.Shares[r], fromDeposit: true);
            Settle(ctx, bank, value, unpaid);
        }

        // What the deposit could not pay comes from the coins of the bank's continent, and the bank lends the rest.
        void Settle(SimContext ctx, int bank, long cost, long unpaid)
        {
            var player = _world.Get<PlayerState>();
            int continent = _nodes.Continent[_banks.NationNode(bank)];
            long fromCoins = Math.Min(unpaid, Math.Max(0, player.Coins[continent]));
            player.Coins[continent] -= fromCoins;
            long lent = unpaid - fromCoins;
            _bank.Reserve[bank] -= lent;
            if (lent > 0)
            {
                var loans = _world.Get<LoanTable>();
                int r = loans.Find(bank);
                long term = _db.Config.LoanTermWeeks;
                if (r >= 0) loans.Balance[r] += lent;
                else loans.Add(bank, lent, (lent + term - 1) / term);
            }
            ctx.Events.Publish(new ShortsCovered(new NodeId(_banks.NationNode(bank)), cost, lent, ctx.Turn));
        }
    }
}
