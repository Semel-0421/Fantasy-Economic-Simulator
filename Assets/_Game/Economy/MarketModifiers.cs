using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.World;

namespace FES.Economy
{
    /// <summary>A temporary multiplier on supply, demand or value for a node's subtree and one or all categories.</summary>
    public struct MarketModifier
    {
        public int Node;
        /// <summary>Category index, or -1 for all categories.</summary>
        public int Category;
        public ModifierTarget Target;
        public Fx Multiplier;
        /// <summary>The modifier is removed at the start of this turn.</summary>
        public int ExpiresTurn;
        /// <summary>Source event definition index, or -1 when created by code (for UI and history; pricing ignores it).</summary>
        public int SourceEvent;
    }

    /// <summary>
    /// Active market modifiers. Anything can add one (world events today; wars, policies, player actions later);
    /// the pricing kernel only sees the folded multipliers, never what caused them.
    /// </summary>
    public sealed class MarketModifierTable : IPersistentComponent
    {
        readonly List<MarketModifier> _active = new List<MarketModifier>();

        public string Key => "economy.modifiers";
        public int Version => 1;

        public IReadOnlyList<MarketModifier> Active => _active;

        public void Add(MarketModifier modifier) => _active.Add(modifier);

        /// <summary>Removes expired modifiers, preserving the order of the rest.</summary>
        public void RemoveExpired(int turn) => _active.RemoveAll(m => m.ExpiresTurn <= turn);

        public void Write(BinaryWriter writer)
        {
            writer.Write(_active.Count);
            foreach (var m in _active)
            {
                writer.Write(m.Node);
                writer.Write(m.Category);
                writer.Write((byte)m.Target);
                writer.Write(m.Multiplier.Raw);
                writer.Write(m.ExpiresTurn);
                writer.Write(m.SourceEvent);
            }
        }

        public void Read(BinaryReader reader)
        {
            _active.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                _active.Add(new MarketModifier
                {
                    Node = reader.ReadInt32(),
                    Category = reader.ReadInt32(),
                    Target = (ModifierTarget)reader.ReadByte(),
                    Multiplier = Fx.FromRaw(reader.ReadInt64()),
                    ExpiresTurn = reader.ReadInt32(),
                    SourceEvent = reader.ReadInt32(),
                });
            }
        }
    }

    public sealed class WorldEventStarted : ISimEvent
    {
        public readonly WorldEventDef Definition;
        public readonly NodeId Node;
        public readonly int Turn;

        public WorldEventStarted(WorldEventDef definition, NodeId node, int turn)
        {
            Definition = definition;
            Node = node;
            Turn = turn;
        }
    }

    /// <summary>Expires old modifiers and rolls data-driven world events (events.csv) for each eligible node.</summary>
    public sealed class WorldEventSystem : ISimSystem
    {
        const string SystemId = "economy.world_events";
        static readonly uint Stream = DetHash.Of(SystemId);

        GameDatabase _db;
        NodeTable _nodes;
        MarketModifierTable _modifiers;

        public string Id => SystemId;
        public SimPhase Phase => SimPhase.Environment;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _nodes = ctx.World.Get<NodeTable>();
            _modifiers = ctx.World.Get<MarketModifierTable>();
        }

        public void Execute(SimContext ctx)
        {
            _modifiers.RemoveExpired(ctx.Turn);
            foreach (var def in _db.Events)
            {
                int start = _nodes.LevelStart(def.ScopeLevel);
                int end = start + _nodes.LevelCount(def.ScopeLevel);
                for (int n = start; n < end; n++)
                {
                    var rng = ctx.RandomFor(Stream, n, (uint)def.Index);
                    if (!rng.ChancePermille(def.ChancePermille)) continue;
                    _modifiers.Add(new MarketModifier
                    {
                        Node = n,
                        Category = def.CategoryIndex,
                        Target = def.Target,
                        Multiplier = def.Multiplier,
                        ExpiresTurn = ctx.Turn + def.DurationTurns,
                        SourceEvent = def.Index,
                    });
                    ctx.Events.Publish(new WorldEventStarted(def, new NodeId(n), ctx.Turn));
                }
            }
        }
    }
}
