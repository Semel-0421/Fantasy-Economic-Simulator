using FES.Core;
using FES.Data;

namespace FES.Agents
{
    /// <summary>
    /// What a merchant group starts with: its class comes from the seed and its row, not its node. The classes are what
    /// groups hold after twenty years, top, average and most common, and the player's own start.
    /// </summary>
    public static class MerchantCapital
    {
        public const int ClassCount = 4;
        public const int PlayerLike = 3;

        static readonly uint ClassStream = DetHash.Of("agents.merchant_capital");

        public static int ClassOf(SimulationConfig config, ulong seed, int merchant)
        {
            int roll = DetRandom.For(seed, 0, ClassStream, merchant).NextInt(1000), below = 0;
            for (int c = 0; c < PlayerLike; c++)
            {
                below += config.MerchantClassSharePermille[c];
                if (roll < below) return c;
            }
            return PlayerLike;
        }

        public static long Start(SimulationConfig config, ulong seed, int merchant)
        {
            int c = ClassOf(config, seed, merchant);
            return c == PlayerLike ? config.PlayerStartCash : config.MerchantClassCash[c];
        }
    }
}
