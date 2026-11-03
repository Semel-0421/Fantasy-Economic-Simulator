using System.Linq;
using FES.Agents;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>A state weighs what it spends on imports (SYS-STATE-ECONOMY), so detailed merchants must record them too (C29).</summary>
    public class StateImportsTests
    {
        // Within the year, before the state clears the count.
        [Test]
        public void DetailedMerchants_RecordImports_WhenSellingAbroad()
        {
            using var sim = TestWorlds.BuildSample();
            var development = sim.World.Get<DevelopmentState>();
            long most = 0;
            for (int t = 0; t < 104 && most == 0; t++)
            {
                sim.AdvanceTurn();
                most = Enumerable.Range(0, development.Imports.Length).Max(p => development.Imports[p]);
            }
            Assert.Greater(most, 0);
        }
    }
}
