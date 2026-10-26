using FES.Core;
using FES.Data;
using FES.World;

namespace FES.Agents
{
    /// <summary>Moves the player along a route from their current node.</summary>
    public sealed class TravelCommand : ICommand
    {
        public readonly NodeId Destination;

        public TravelCommand(NodeId destination) => Destination = destination;

        public CommandResult Validate(SimContext ctx)
        {
            var nodes = ctx.World.Get<NodeTable>();
            if (!Destination.IsValid || Destination.Value >= nodes.Count) return CommandResult.Fail("Unknown destination.");
            var routes = ctx.World.Get<RouteTable>();
            int from = ctx.World.Get<PlayerState>().Node;
            int start = routes.OutStart[from];
            for (int k = start; k < start + routes.OutCount[from]; k++)
                if (routes.To[routes.RouteIndex[k]] == Destination.Value) return CommandResult.Success;
            return CommandResult.Fail("No route to destination.");
        }

        // TODO: multi-turn travel using the route's travel time and transport cost.
        public void Apply(SimContext ctx) => ctx.World.Get<PlayerState>().Node = Destination.Value;
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

    /// <summary>Buys an NPC facility. Signals an expansion direction toward its industry.</summary>
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

        public CommandResult Validate(SimContext ctx)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            if (!Facility.IsValid || Facility.Value >= facilities.Count) return CommandResult.Fail("Unknown facility.");
            if (Price < 0) return CommandResult.Fail("Price must not be negative.");
            if (facilities.Owner[Facility.Value] != OwnerIds.Npc) return CommandResult.Fail("Facility is not for sale.");
            if (ctx.World.Get<PlayerState>().Cash < Price) return CommandResult.Fail("Not enough cash.");
            return CommandResult.Success;
        }

        public void Apply(SimContext ctx)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            ctx.World.Get<PlayerState>().Cash -= Price;
            facilities.Owner[Facility.Value] = OwnerIds.Player;
            facilities.Mode[Facility.Value] = (byte)ControlMode.Manual;
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
            return ctx.World.Get<PlayerState>().Cash >= Cost ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx) => ctx.World.Get<PlayerState>().Cash -= Cost;

        public GuidanceSignal GetGuidance(SimContext ctx) =>
            new GuidanceSignal(Focus, ctx.Services.Get<GameDatabase>().Config.GuidanceWeightFoundCompany, Headquarters);
    }
}
