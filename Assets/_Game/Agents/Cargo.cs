using System.Collections.Generic;
using FES.Core;
using Unity.Collections;

namespace FES.Agents
{
    /// <summary>
    /// Who holds a cargo row. One id space for every carrier and store: the player's cart today,
    /// warehouses and merchant groups later.
    /// </summary>
    public static class CargoHolders
    {
        public const int PlayerCart = 0;
        const int WarehouseBase = 1 << 28;

        /// <summary>The warehouse at a facility; it holds goods while the player owns the facility.</summary>
        public static int Warehouse(FacilityId facility) => WarehouseBase + facility.Value;

        public const int MerchantBase = 2 << 28;

        /// <summary>The load of a merchant group on a trip.</summary>
        public static int Merchant(AgentId merchant) => MerchantBase + merchant.Value;

        const int GuildHallBase = 3 << 28;

        /// <summary>A guild hall warehouse the player uses as a member (DEC-057).</summary>
        public static int GuildHall(int guild) => GuildHallBase + guild;

        public static bool IsGuildHall(int holder, out int guild)
        {
            guild = holder - GuildHallBase;
            return holder >= GuildHallBase && guild < (1 << 28);
        }
    }

    public readonly struct CargoItem
    {
        public readonly GoodId Good;
        public readonly int Quantity;

        public CargoItem(GoodId good, int quantity)
        {
            Good = good;
            Quantity = quantity;
        }
    }

    /// <summary>Total weight and size of what a holder carries.</summary>
    public readonly struct CargoLoad
    {
        public readonly long Weight;
        public readonly long Size;

        public CargoLoad(long weight, long size)
        {
            Weight = weight;
            Size = size;
        }
    }

    /// <summary>A carrier's or store's limits. Cargo must fit both the total weight and the total size.</summary>
    public readonly struct CargoCapacity
    {
        public readonly long MaxWeight;
        public readonly long MaxSize;

        public CargoCapacity(long maxWeight, long maxSize)
        {
            MaxWeight = maxWeight;
            MaxSize = maxSize;
        }

        /// <summary>How many more units of a good with this unit weight and size fit on top of <paramref name="load"/>.</summary>
        public int MaxAdditional(CargoLoad load, int unitWeight, int unitSize)
        {
            long fit = int.MaxValue;
            if (unitWeight > 0) fit = System.Math.Min(fit, (MaxWeight - load.Weight) / unitWeight);
            if (unitSize > 0) fit = System.Math.Min(fit, (MaxSize - load.Size) / unitSize);
            return fit < 0 ? 0 : (int)fit;
        }
    }

    /// <summary>
    /// Goods held by carts, warehouses and merchant groups (SoA). One row per (holder, good) with a
    /// positive quantity; a row that reaches zero is removed.
    /// </summary>
    public sealed class CargoTable : NativeTable
    {
        public readonly NativeColumn<int> Holder;
        public readonly NativeColumn<int> Good;
        public readonly NativeColumn<int> Quantity;

        public override string Key => "agents.cargo";
        public override int Version => 1;

        public CargoTable()
        {
            Holder = Column<int>();
            Good = Column<int>();
            Quantity = Column<int>();
        }

        public int QuantityOf(int holder, GoodId good)
        {
            int row = Find(holder, good);
            return row < 0 ? 0 : Quantity[row];
        }

        /// <summary>Adds (positive) or removes (negative) units. Removing more than held is a bug and throws.</summary>
        public void Change(int holder, GoodId good, int delta)
        {
            int row = Find(holder, good);
            long next = (row < 0 ? 0 : Quantity[row]) + (long)delta;
            if (next < 0 || next > int.MaxValue)
                throw new System.InvalidOperationException($"Cargo of holder {holder}, good {good.Value} would become {next}.");
            if (row < 0)
            {
                if (next == 0) return;
                Holder.Add(holder);
                Good.Add(good.Value);
                Quantity.Add((int)next);
            }
            else if (next == 0) RemoveRowSwapBack(row);
            else Quantity[row] = (int)next;
        }

        /// <summary>Removes everything the holder carries.</summary>
        public void Clear(int holder)
        {
            for (int r = Count - 1; r >= 0; r--)
                if (Holder[r] == holder) RemoveRowSwapBack(r);
        }

        public CargoLoad LoadOf(int holder, NativeArray<int> unitWeight, NativeArray<int> unitSize)
        {
            long weight = 0, size = 0;
            for (int r = 0; r < Count; r++)
            {
                if (Holder[r] != holder) continue;
                weight += (long)Quantity[r] * unitWeight[Good[r]];
                size += (long)Quantity[r] * unitSize[Good[r]];
            }
            return new CargoLoad(weight, size);
        }

        /// <summary>Everything the holder carries, sorted by good id.</summary>
        public void Collect(int holder, List<CargoItem> into)
        {
            into.Clear();
            for (int r = 0; r < Count; r++)
                if (Holder[r] == holder) into.Add(new CargoItem(new GoodId(Good[r]), Quantity[r]));
            into.Sort((a, b) => a.Good.Value.CompareTo(b.Good.Value));
        }

        /// <summary>Distinct holders with any cargo, in row order.</summary>
        public void CollectHolders(List<int> into)
        {
            into.Clear();
            for (int r = 0; r < Count; r++)
                if (!into.Contains(Holder[r])) into.Add(Holder[r]);
        }

        // Linear: fine while only the player's cart holds cargo. Index by holder before merchants carry goods (P5).
        int Find(int holder, GoodId good)
        {
            for (int r = 0; r < Count; r++)
                if (Holder[r] == holder && Good[r] == good.Value) return r;
            return -1;
        }
    }
}
