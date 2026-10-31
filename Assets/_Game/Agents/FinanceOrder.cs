using FES.Core;

namespace FES.Agents
{
    /// <summary>The order of the Finance phase's late band: what each system's week may rely on having run before it.</summary>
    public static class FinanceOrder
    {
        public const int Banks = SimOrder.Late;
        public const int Roads = SimOrder.Late;
        public const int StateSpending = SimOrder.Late + 5;
        public const int Labor = SimOrder.Late + 6;
        public const int Defense = SimOrder.Late + 7;
        public const int Records = SimOrder.Late + 10;
        public const int IncomeTax = SimOrder.Late + 15;
        public const int Nobles = SimOrder.Late + 16;
        public const int Migration = SimOrder.Late + 17;
        public const int StateProjects = SimOrder.Late + 18;
        public const int Subsidies = SimOrder.Late + 19;
        public const int Households = SimOrder.Late + 20;
        public const int Investment = SimOrder.Late + 21;
        public const int MerchantVehicles = SimOrder.Late + 22;
        public const int Orders = SimOrder.Late + 23;
        public const int DemandForecast = SimOrder.Late + 24;
        public const int MerchantMembership = SimOrder.Late + 25;
        public const int Contracts = SimOrder.Late + 26;
        public const int StrategicSubsidy = SimOrder.Late + 27;
        public const int Policy = SimOrder.Late + 30;
        public const int MerchantGenerations = SimOrder.Late + 40;
    }
}
