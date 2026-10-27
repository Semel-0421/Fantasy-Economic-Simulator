using System.Collections.Generic;
using FES.Core;

namespace FES.Data
{
    // Immutable static definitions. String keys are the stable identity in data files;
    // dense indices are assigned at load time in file order.

    public sealed class GoodCategoryDef
    {
        public readonly int Index;
        public readonly string Key;

        public GoodCategoryDef(int index, string key)
        {
            Index = index;
            Key = key;
        }
    }

    /// <summary>One tradable item: a base good at one quality grade (4,000 independent items at full scale).</summary>
    public sealed class GoodDef
    {
        public readonly GoodId Id;
        public readonly string BaseKey;
        public readonly int CategoryIndex;
        /// <summary>Reference price in the smallest coin unit, already scaled by the grade multiplier.</summary>
        public readonly int BasePrice;
        /// <summary>Weekly demand per village-sized market before level weighting.</summary>
        public readonly int BaseDemand;
        /// <summary>Weight of one unit; set per base good, so every grade shares it.</summary>
        public readonly int Weight;
        /// <summary>Size (volume) of one unit; set per base good, so every grade shares it.</summary>
        public readonly int Size;
        /// <summary>Selling it needs a license of the nation it is sold in (DEC-021, DEC-024); per base good.</summary>
        public readonly bool RequiresLicense;

        public GoodDef(GoodId id, string baseKey, int categoryIndex, int basePrice, int baseDemand, int weight, int size,
            bool requiresLicense)
        {
            Id = id;
            BaseKey = baseKey;
            CategoryIndex = categoryIndex;
            BasePrice = basePrice;
            BaseDemand = baseDemand;
            Weight = weight;
            Size = size;
            RequiresLicense = requiresLicense;
        }

        public QualityGrade Grade => Id.Grade;
        public string Key => $"{BaseKey}@{Grade.ToString().ToLowerInvariant()}";
    }

    public readonly struct RecipeLine
    {
        public readonly GoodId Good;
        public readonly int Quantity;

        public RecipeLine(GoodId good, int quantity)
        {
            Good = good;
            Quantity = quantity;
        }
    }

    /// <summary>Weekly inputs and outputs of one facility at level 1 and full efficiency.</summary>
    public sealed class RecipeDef
    {
        public readonly int Index;
        public readonly string Key;
        public readonly RecipeLine[] Inputs;
        public readonly RecipeLine[] Outputs;

        public RecipeDef(int index, string key, RecipeLine[] inputs, RecipeLine[] outputs)
        {
            Index = index;
            Key = key;
            Inputs = inputs;
            Outputs = outputs;
        }
    }

    /// <summary>A producer type: farms/mines (primary), workshops/factories (secondary), services (tertiary).</summary>
    public sealed class FacilityDef
    {
        public readonly int Index;
        public readonly string Key;
        public readonly IndustryTier Tier;
        public readonly RecipeDef Recipe;

        public FacilityDef(int index, string key, IndustryTier tier, RecipeDef recipe)
        {
            Index = index;
            Key = key;
            Tier = tier;
            Recipe = recipe;
        }
    }

    public enum ModifierTarget : byte
    {
        Supply = 0,
        Demand = 1,
        Value = 2,
    }

    public static class ModifierTargets
    {
        public const int Count = 3;
    }

    /// <summary>A random world event (disaster, festival, ...) that scales a market's supply, demand or value.</summary>
    public sealed class WorldEventDef
    {
        public readonly int Index;
        public readonly string Key;
        public readonly WorldLevel ScopeLevel;
        /// <summary>Affected category index, or -1 for all categories.</summary>
        public readonly int CategoryIndex;
        public readonly ModifierTarget Target;
        public readonly Fx Multiplier;
        public readonly int DurationTurns;
        /// <summary>Chance per eligible node per turn, in permille.</summary>
        public readonly int ChancePermille;

        public WorldEventDef(int index, string key, WorldLevel scopeLevel, int categoryIndex, ModifierTarget target,
            Fx multiplier, int durationTurns, int chancePermille)
        {
            Index = index;
            Key = key;
            ScopeLevel = scopeLevel;
            CategoryIndex = categoryIndex;
            Target = target;
            Multiplier = multiplier;
            DurationTurns = durationTurns;
            ChancePermille = chancePermille;
        }
    }

    /// <summary>One row of specialties.csv: a terrain tag makes a base good, or a whole category, a specialty.</summary>
    public sealed class SpecialtyRule
    {
        public readonly string Tag;
        /// <summary>Base good indices the tag makes specialties, sorted.</summary>
        public readonly int[] BaseGoods;

        public SpecialtyRule(string tag, int[] baseGoods)
        {
            Tag = tag;
            BaseGoods = baseGoods;
        }
    }

    /// <summary>Which goods the markets of one world level carry (drives the sparse market layout).</summary>
    public sealed class MarketProfileRule
    {
        public readonly WorldLevel Level;
        /// <summary>Category index, or -1 for all categories.</summary>
        public readonly int CategoryIndex;
        /// <summary>Bit i set = grade i carried.</summary>
        public readonly int GradeMask;

        public MarketProfileRule(WorldLevel level, int categoryIndex, int gradeMask)
        {
            Level = level;
            CategoryIndex = categoryIndex;
            GradeMask = gradeMask;
        }

        public bool Matches(GoodDef good) =>
            (CategoryIndex < 0 || CategoryIndex == good.CategoryIndex) && (GradeMask & (1 << (int)good.Grade)) != 0;
    }

    public sealed class MapNodeDef
    {
        public string Key;
        public string Name;
        public WorldLevel Level;
        /// <summary>Parent node key; empty for continents.</summary>
        public string ParentKey;
        /// <summary>Terrain tags (coast, mountain, ...); specialties follow from them (DEC-016).</summary>
        public string[] Tags = System.Array.Empty<string>();
        /// <summary>Toll on entering the node, in permille of the cargo's value; -1 = config default.</summary>
        public int TariffPermille = -1;
        /// <summary>Extra toll on goods in the node's specialty categories, in permille; -1 = config default.</summary>
        public int SpecialtySurchargePermille = -1;
        /// <summary>The continent's international bank sits here (DEC-036); one per continent at most.</summary>
        public bool InternationalBank;
        /// <summary>Road danger of a region, in permille of the normal level; -1 = config default (DEC-026).</summary>
        public int DangerPermille = -1;
        /// <summary>Ships are traded here and water routes may start and end here (DEC-053).</summary>
        public bool Port;
    }

    public sealed class RouteDef
    {
        public string FromKey;
        public string ToKey;
        public int TravelTurns;
        public int CostPerUnit;
        /// <summary>Paid once per trip, whatever the cargo (DEC-017).</summary>
        public int BaseFee;
        public RouteTerrain Terrain;
        /// <summary>Paved from the start (DEC-054).</summary>
        public bool Paved;
    }

    /// <summary>World layout. Produced by a handcrafted source today, by a procedural generator later.</summary>
    public sealed class MapDef
    {
        public readonly List<MapNodeDef> Nodes = new List<MapNodeDef>();
        public readonly List<RouteDef> Routes = new List<RouteDef>();

        /// <summary>Structural hash (keys, levels, parents, routes) so saves can detect a changed map.</summary>
        public ulong ComputeHash()
        {
            ulong h = DetHash.Of("map");
            foreach (var n in Nodes)
            {
                h = DetHash.Combine(h, DetHash.Combine(DetHash.Of(n.Key), DetHash.Combine((ulong)n.Level, DetHash.Of(n.ParentKey ?? string.Empty))));
                foreach (var tag in n.Tags) h = DetHash.Combine(h, DetHash.Of(tag));
                h = DetHash.Combine(h, DetHash.Combine((ulong)(uint)n.TariffPermille, (ulong)(uint)n.SpecialtySurchargePermille));
                if (n.InternationalBank) h = DetHash.Combine(h, DetHash.Of("international_bank"));
                h = DetHash.Combine(h, (ulong)(uint)n.DangerPermille);
                if (n.Port) h = DetHash.Combine(h, DetHash.Of("port"));
            }
            foreach (var r in Routes)
                h = DetHash.Combine(h, DetHash.Combine(DetHash.Of(r.FromKey), DetHash.Combine(DetHash.Of(r.ToKey),
                    DetHash.Combine((ulong)(uint)r.TravelTurns, DetHash.Combine((ulong)(uint)r.CostPerUnit, DetHash.Combine((ulong)(uint)r.BaseFee, DetHash.Combine((ulong)r.Terrain, r.Paved ? 1UL : 0UL)))))));
            return h;
        }
    }

    public interface IMapSource
    {
        MapDef LoadMap();
    }
}
