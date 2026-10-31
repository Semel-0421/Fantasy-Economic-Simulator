using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// The player's own ship (DEC-034, DEC-053): on water routes it replaces passage and carries more than the
    /// cart. It waits docked at the port where the player last landed with it.
    /// </summary>
    public sealed class PlayerShip : IPersistentComponent
    {
        public const string SectionKey = "agents.ship";

        public bool Owned;
        /// <summary>Continent whose coins paid for it; its collateral value is counted in them.</summary>
        public int BoughtContinent;
        /// <summary>Port where the ship waits; -1 for a ship from before ports, which waits at whatever port the player is at.</summary>
        public int DockedAt = -1;

        public string Key => SectionKey;
        public int Version => 2;

        /// <summary>Whether the player can sail the ship from <paramref name="node"/>.</summary>
        public bool AvailableAt(int node) => Owned && (DockedAt == node || DockedAt < 0);

        public void Write(BinaryWriter writer)
        {
            writer.Write(Owned);
            writer.Write(BoughtContinent);
            writer.Write(DockedAt);
        }

        public void Read(BinaryReader reader)
        {
            Owned = reader.ReadBoolean();
            BoughtContinent = reader.ReadInt32();
            DockedAt = reader.ReadInt32();
        }
    }

    /// <summary>Version 1 had no dock: the ship travelled with the player.</summary>
    public sealed class PlayerShipV1Migration : ISaveMigration
    {
        public string SectionKey => PlayerShip.SectionKey;
        public int FromVersion => 1;

        public byte[] Migrate(byte[] section)
        {
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(-1); // DockedAt: waits at the next port the player is at
            }
            return output.ToArray();
        }
    }

    static class PlayerPort
    {
        public static string Check(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            return player.IsTravelling || !ctx.World.Get<NodePorts>().Contains(player.Node) ? "Ships are traded at ports." : null;
        }
    }

    /// <summary>Buys a ship at the port the player is at, for local coins; it docks there (DEC-053).</summary>
    public sealed class BuyShipCommand : ICommand
    {
        public CommandResult Validate(SimContext ctx)
        {
            if (ctx.World.Get<PlayerShip>().Owned) return CommandResult.Fail("The player already owns a ship.");
            var error = PlayerPort.Check(ctx);
            if (error != null) return CommandResult.Fail(error);
            return ctx.World.Get<PlayerState>().LocalCoins >= ctx.Services.Get<GameDatabase>().Config.ShipPrice
                ? CommandResult.Success
                : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            var ship = ctx.World.Get<PlayerShip>();
            player.LocalCoins -= ctx.Services.Get<GameDatabase>().Config.ShipPrice;
            ctx.World.Get<PurseState>().PayHouseholds(player.Node, ctx.Services.Get<GameDatabase>().Config.ShipPrice); // to the port (DEC-061)
            ship.Owned = true;
            ship.BoughtContinent = player.LocalContinent;
            ship.DockedAt = player.Node;
        }
    }

    /// <summary>Sells the ship at the port where it is docked, for a share of its price in local coins.</summary>
    public sealed class SellShipCommand : ICommand
    {
        public CommandResult Validate(SimContext ctx)
        {
            var error = PlayerPort.Check(ctx);
            if (error != null) return CommandResult.Fail(error);
            return ctx.World.Get<PlayerShip>().AvailableAt(ctx.World.Get<PlayerState>().Node)
                ? CommandResult.Success
                : CommandResult.Fail("The ship is not docked here.");
        }

        public void Apply(SimContext ctx)
        {
            var config = ctx.Services.Get<GameDatabase>().Config;
            var player = ctx.World.Get<PlayerState>();
            player.LocalCoins += config.ShipResaleShare.MulInt(config.ShipPrice);
            ctx.World.Get<PurseState>().PayHouseholds(player.Node, -config.ShipResaleShare.MulInt(config.ShipPrice)); // [Draft] the port buys it back
            var ship = ctx.World.Get<PlayerShip>();
            ship.Owned = false;
            ship.DockedAt = -1;
        }
    }
}
