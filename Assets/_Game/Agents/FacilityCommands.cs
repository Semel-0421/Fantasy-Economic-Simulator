using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Pays coins into (positive amount) or takes them out of (negative) a player-owned facility at the
    /// player's node. Facilities pay for their inputs from this cash.
    /// </summary>
    public sealed class MoveFacilityCashCommand : ICommand
    {
        public readonly FacilityId Facility;
        public readonly long Amount;

        public MoveFacilityCashCommand(FacilityId facility, long amount)
        {
            Facility = facility;
            Amount = amount;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (Amount == 0 || Amount == long.MinValue) return CommandResult.Fail("Amount must not be zero.");
            var error = PlayerSite.Check(ctx, Facility);
            if (error != null) return CommandResult.Fail(error);
            if (Amount > 0 && ctx.World.Get<PlayerState>().LocalCoins < Amount) return CommandResult.Fail("Not enough cash.");
            if (Amount < 0 && Withdrawable(ctx) < -Amount) return CommandResult.Fail("The facility does not hold that much, less this year's tax.");
            return CommandResult.Success;
        }

        // Its cash, less the tax on its profit so far: what the owner takes out stays taxed (DEC-107).
        long Withdrawable(SimContext ctx)
        {
            long cash = ctx.World.Get<FacilityTable>().Cash[Facility.Value];
            long profit = ctx.World.Get<IncomeTaxState>().FacilityProfit(Facility.Value, cash);
            return cash - ctx.Services.Get<GameDatabase>().Config.TaxCompany.MulInt(Math.Max(0, profit));
        }

        public void Apply(SimContext ctx)
        {
            ctx.World.Get<PlayerState>().LocalCoins -= Amount;
            ctx.World.Get<FacilityTable>().Cash[Facility.Value] += Amount;
            ctx.World.Get<IncomeTaxState>().MoveFacilityCapital(Facility.Value, Amount);
        }
    }

    static class PlayerSite
    {
        /// <summary>
        /// The facility exists, the player owns it (or, with <paramref name="allowLostWarehouse"/>, still has a
        /// claim on its warehouse) and stands at its node.
        /// </summary>
        public static string Check(SimContext ctx, FacilityId facility, bool allowLostWarehouse = false)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            if (!facility.IsValid || facility.Value >= facilities.Count) return "Unknown facility.";
            bool claim = allowLostWarehouse && facilities.WarehouseClaimUntil[facility.Value] >= 0;
            if (facilities.Owner[facility.Value] != OwnerIds.Player && !claim) return "Facility is not owned by the player.";
            var player = ctx.World.Get<PlayerState>();
            if (player.IsTravelling || player.Node != facilities.Node[facility.Value]) return "The player is not at the facility.";
            return null;
        }
    }

    /// <summary>Switches a player-owned facility between direct management and automation.</summary>
    public sealed class SetFacilityControlCommand : ICommand
    {
        public readonly FacilityId Facility;
        public readonly ControlMode Mode;

        public SetFacilityControlCommand(FacilityId facility, ControlMode mode)
        {
            Facility = facility;
            Mode = mode;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            if (!Facility.IsValid || Facility.Value >= facilities.Count) return CommandResult.Fail("Unknown facility.");
            return facilities.Owner[Facility.Value] == OwnerIds.Player
                ? CommandResult.Success
                : CommandResult.Fail("Facility is not owned by the player.");
        }

        public void Apply(SimContext ctx) => ctx.World.Get<FacilityTable>().Mode[Facility.Value] = (byte)Mode;
    }

    /// <summary>Buys an NPC facility, with the cash it holds. Signals an expansion direction toward its industry.</summary>
    public sealed class BuyFacilityCommand : ICommand, IGuidanceSource
    {
        public readonly FacilityId Facility;
        public readonly long Price;
        public readonly GuidanceAxis Axis;

        public BuyFacilityCommand(FacilityId facility, long price, GuidanceAxis axis)
        {
            Facility = facility;
            Price = price;
            Axis = axis;
        }

        /// <summary>What a seller asks: the facility's value and the cash that goes with it (DEC-107).</summary>
        public static long AskingPrice(GameDatabase db, FacilityTable facilities, int f) =>
            NpcLending.FacilityValue(db, facilities, f) + Math.Max(0, facilities.Cash[f]);

        public CommandResult Validate(SimContext ctx)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            if (!Facility.IsValid || Facility.Value >= facilities.Count) return CommandResult.Fail("Unknown facility.");
            if (Price < 0) return CommandResult.Fail("Price must not be negative.");
            if (facilities.Owner[Facility.Value] != OwnerIds.Npc) return CommandResult.Fail("Facility is not for sale.");
            if (Price < AskingPrice(ctx.Services.Get<GameDatabase>(), facilities, Facility.Value)) return CommandResult.Fail("The seller asks more.");
            if (ctx.World.Get<PlayerState>().CoinsAt(facilities.Node[Facility.Value]) < Price) return CommandResult.Fail("Not enough cash.");
            return CommandResult.Success;
        }

        public void Apply(SimContext ctx)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            int node = facilities.Node[Facility.Value];
            ctx.World.Get<PlayerState>().CoinsAt(node) -= Price;
            // [Draft] The seller repays its bank loan from the price; the bank writes off the rest. The seller's
            // group gets what is left, or the purse there if it has none (DEC-061).
            long proceeds = Price;
            var loans = ctx.World.Get<NpcLoanTable>();
            int loan = loans.Find(NpcBorrowers.Facility(Facility.Value));
            if (loan >= 0)
            {
                long repaid = Math.Min(Price, loans.Balance[loan]);
                ctx.World.Get<BankState>().Reserve[loans.Nation[loan]] += repaid;
                proceeds -= repaid;
                loans.RemoveRow(loan);
            }
            int seller = facilities.Group[Facility.Value];
            if (seller >= 0) ctx.World.Get<GroupTable>().Cash[seller] += proceeds;
            else ctx.World.Get<PurseState>().PayHouseholds(node, proceeds);
            facilities.Owner[Facility.Value] = OwnerIds.Player;
            facilities.Mode[Facility.Value] = (byte)ControlMode.Manual;
            facilities.WarehouseClaimUntil[Facility.Value] = -1; // a lost warehouse bought back is the player's again
            facilities.Group[Facility.Value] = ctx.World.Get<GroupTable>().PlayerCompany();
        }

        public GuidanceSignal GetGuidance(SimContext ctx) => new GuidanceSignal(
            Axis,
            ctx.Services.Get<GameDatabase>().Config.GuidanceWeightBuyFacility,
            new NodeId(ctx.World.Get<FacilityTable>().Node[Facility.Value]));
    }

    /// <summary>Founds the player's company. Fixes the long-term direction of the business without gating anything.</summary>
    public sealed class FoundCompanyCommand : ICommand, IGuidanceSource
    {
        public readonly NodeId Headquarters;
        public readonly GuidanceAxis Focus;
        public readonly long Cost;

        public FoundCompanyCommand(NodeId headquarters, GuidanceAxis focus, long cost)
        {
            Headquarters = headquarters;
            Focus = focus;
            Cost = cost;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var nodes = ctx.World.Get<NodeTable>();
            if (!Headquarters.IsValid || Headquarters.Value >= nodes.Count) return CommandResult.Fail("Unknown headquarters node.");
            if (Cost < 0) return CommandResult.Fail("Cost must not be negative.");
            if (ctx.World.Get<GroupTable>().PlayerCompany() >= 0) return CommandResult.Fail("The company is already founded.");
            return ctx.World.Get<PlayerState>().CoinsAt(Headquarters.Value) >= Cost ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        /// <summary>The player's company becomes an interest group and owns the player's facilities (DEC-056).</summary>
        public void Apply(SimContext ctx)
        {
            ctx.World.Get<PlayerState>().CoinsAt(Headquarters.Value) -= Cost;
            // The founding money is the company's starting capital (DEC-061).
            int company = ctx.World.Get<GroupTable>().Add(GroupKind.Company, Headquarters.Value, -1, Cost, player: true);
            var facilities = ctx.World.Get<FacilityTable>();
            for (int f = 0; f < facilities.Count; f++)
                if (facilities.Owner[f] == OwnerIds.Player) facilities.Group[f] = company;
        }

        public GuidanceSignal GetGuidance(SimContext ctx) =>
            new GuidanceSignal(Focus, ctx.Services.Get<GameDatabase>().Config.GuidanceWeightFoundCompany, Headquarters);
    }
}
