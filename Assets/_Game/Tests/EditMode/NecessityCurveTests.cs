using FES.Core;
using FES.Economy;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Necessities follow a constant-elasticity curve below a choke price (DEC-126).</summary>
    public class NecessityCurveTests
    {
        static readonly long Elasticity = Fx.FromPermille(600).Raw, Max = Fx.FromPermille(2000).Raw, Choke = Fx.FromInt(10).Raw;

        static long At(double ratio) => DemandCurve.ConstantElasticity(Elasticity, (long)(ratio * Fx.OneRaw), Max, Choke);

        static void Near(double expected, long actual, string what) =>
            Assert.AreEqual(expected * Fx.OneRaw, actual, Fx.OneRaw / 500.0, what);

        [Test]
        public void AtTheAnchor_DemandIsWhole() => Near(1, At(1), "price at the anchor");

        [Test]
        public void AboveTheAnchor_DemandFallsByThePowerOfThePrice()
        {
            Near(0.6598, At(2), "2^-0.6");
            Near(0.5173, At(3), "3^-0.6");
            Near(0.3981, At(4.627), "4.627^-0.6");
        }

        [Test]
        public void HungryHouseholds_StillBuy_WhereTheLinearCurveWouldChoke()
        {
            Assert.Greater(At(3), 0, "the linear curve at 0.6 chokes at 2.67 times the anchor");
        }

        [Test]
        public void BeyondTheChoke_NothingIsBought() => Assert.AreEqual(0, At(10));

        [Test]
        public void BelowTheAnchor_DemandRises_UpToTheCap()
        {
            Near(1.5157, At(0.5), "0.5^-0.6");
            Assert.AreEqual(Max, At(0.1));
        }

        [Test]
        public void Demand_NeverRisesWithThePrice()
        {
            long last = long.MaxValue;
            for (int permille = 200; permille < 10_000; permille += 37)
            {
                long now = DemandCurve.ConstantElasticity(Elasticity, Fx.FromPermille(permille).Raw, Max, Choke);
                Assert.LessOrEqual(now, last, $"at {permille} permille");
                last = now;
            }
        }
    }
}
