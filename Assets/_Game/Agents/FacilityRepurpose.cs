using FES.Core;
using FES.Data;
using FES.World;

namespace FES.Agents
{
    public sealed class FacilityRepurposed : ISimEvent
    {
        public readonly FacilityId Facility;
        public readonly int DefIndex;

        public FacilityRepurposed(FacilityId facility, int defIndex)
        {
            Facility = facility;
            DefIndex = defIndex;
        }
    }

    /// <summary>
    /// DEC-125: a facility taken over after a bankruptcy, at auction or from a ruined player, may change its use once.
    /// [Draft] It becomes any type a group could found there, at level one, for a share of that type's value, paid to
    /// the builders of the node.
    /// </summary>
    public static class FacilityRepurpose
    {
        /// <summary>Whether a group could found this type at the node: only villages have land for primary producers.</summary>
        public static bool MayStand(FacilityDef def, NodeTable nodes, int node) =>
            !def.StateOwned && (def.Tier != IndustryTier.Primary || nodes.LevelOf(node) == WorldLevel.Village)
            && FacilityPlaces.Allowed(def, nodes, node);

        public static long Cost(FacilityDef def, SimulationConfig config) =>
            config.FacilityRepurposeShare.MulInt(config.FacilityValue[(int)def.Tier]);

        public static void Apply(SimContext ctx, int f, FacilityDef def)
        {
            var facilities = ctx.World.Get<FacilityTable>();
            facilities.ChangeUse(f, def.Index);
            ctx.Events.Publish(new FacilityRepurposed(new FacilityId(f), def.Index));
        }
    }

    /// <summary>The player changes the use of a facility they took over after a bankruptcy (DEC-125).</summary>
    public sealed class RepurposeFacilityCommand : ICommand
    {
        public readonly FacilityId Facility;
        public readonly string DefKey;

        public RepurposeFacilityCommand(FacilityId facility, string defKey)
        {
            Facility = facility;
            DefKey = defKey;
        }

        FacilityDef Target(SimContext ctx)
        {
            foreach (var def in ctx.Services.Get<GameDatabase>().Facilities)
                if (def.Key == DefKey) return def;
            return null;
        }

        public CommandResult Validate(SimContext ctx)
        {
            var error = PlayerSite.Check(ctx, Facility);
            if (error != null) return CommandResult.Fail(error);
            var facilities = ctx.World.Get<FacilityTable>();
            int f = Facility.Value;
            if (facilities.Repurposable[f] == 0) return CommandResult.Fail("The facility may not change its use.");
            var def = Target(ctx);
            if (def == null || def.Index == facilities.DefIndex[f] || !FacilityRepurpose.MayStand(def, ctx.World.Get<NodeTable>(), facilities.Node[f]))
                return CommandResult.Fail("That use is not possible here.");
            long cost = FacilityRepurpose.Cost(def, ctx.Services.Get<GameDatabase>().Config);
            return ctx.World.Get<PlayerState>().CoinsAt(facilities.Node[f]) >= cost ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            var def = Target(ctx);
            int node = ctx.World.Get<FacilityTable>().Node[Facility.Value];
            long cost = FacilityRepurpose.Cost(def, ctx.Services.Get<GameDatabase>().Config);
            ctx.World.Get<PlayerState>().CoinsAt(node) -= cost;
            ctx.World.Get<PurseState>().PayHouseholds(node, cost);
            FacilityRepurpose.Apply(ctx, Facility.Value, def);
        }
    }
}
