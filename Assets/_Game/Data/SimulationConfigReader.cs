using System;
using System.Collections.Generic;
using System.Globalization;
using FES.Core;

namespace FES.Data
{
    // Reads config.csv into the fields, topic by topic; a key missing from the file keeps the field's default.
    public sealed partial class SimulationConfig
    {
        void Apply(Dictionary<string, string> v)
        {
            ApplyLevels(v);
            ApplyGradesVehiclesAndFood(v);
            ApplyContractsAndAssociations(v);
            ApplyCoresHedgesAndForecast(v);
            ApplyTiersAndTerrain(v);
            ApplyDangerTravelAndRoads(v);
            ApplyGroupsAndBanks(v);
            ApplyPolicy(v);
            ApplyDemandAndHouseholds(v);
            ApplyTradersAndNobles(v);
            ApplyMigrationAndInvestment(v);
            ApplyLaborTaxAndDefense(v);
            ApplyStateProjects(v);
            ApplyBankRatesAndFinance(v);
            ApplyLicensesAndSpecialties(v);
            ApplyMerchantJudgement(v);
            ApplyCredit(v);
            ApplyMerchantTraits(v);
            ApplyDividendsAndCargo(v);
            ApplyPricesAndCurrency(v);
            ApplyGuidance(v);
            ApplyStart(v);
        }

        void ApplyLevels(Dictionary<string, string> v)
        {
            for (int l = 0; l < WorldLevels.Count; l++)
            {
                string level = ((WorldLevel)l).ToString().ToLowerInvariant();
                TierCadences[l] = new TierCadence(
                    Int(v, $"tier.{level}.period", TierCadences[l].Period),
                    Int(v, $"tier.{level}.offset", TierCadences[l].Offset));
                LevelDemandWeight[l] = Permille(v, $"demand.{level}.weight_permille", LevelDemandWeight[l]);
                DefaultPopulation[l] = Long(v, $"households.{level}.population", DefaultPopulation[l]);
                DefaultIncomePerHead[l] = Permille(v, $"households.{level}.income_permille", DefaultIncomePerHead[l]);
                if (DefaultPopulation[l] <= 0 || DefaultIncomePerHead[l].Raw <= 0)
                    throw new FormatException($"config.csv: households.{level}.population and income must be positive.");
                LevelWageWeight[l] = Permille(v, $"labor.{level}.wage_weight_permille", LevelWageWeight[l]);
                MerchantsPerNode[l] = Int(v, $"merchants.{level}", MerchantsPerNode[l]);
                MerchantMaxWeight[l] = Long(v, $"merchants.{level}.max_weight", MerchantMaxWeight[l]);
                MerchantMaxSize[l] = Long(v, $"merchants.{level}.max_size", MerchantMaxSize[l]);
                MerchantUpkeep[l] = Long(v, $"merchants.{level}.upkeep", MerchantUpkeep[l]);
                for (int t = 0; t < IndustryTiers.Count; t++)
                {
                    string tier = ((IndustryTier)t).ToString().ToLowerInvariant();
                    ProducersPerNode[l, t] = Int(v, $"producers.{level}.{tier}", ProducersPerNode[l, t]);
                }
            }
        }

        void ApplyGradesVehiclesAndFood(Dictionary<string, string> v)
        {
            for (int g = 0; g < QualityGrades.Count; g++)
            {
                string grade = ((QualityGrade)g).ToString().ToLowerInvariant();
                GradePriceMultiplier[g] = Permille(v, $"grade.{grade}.price_permille", GradePriceMultiplier[g]);
            }
            ProductionVariancePermille = Int(v, "production.variance_permille", ProductionVariancePermille);
            MerchantBankruptWeeks = Int(v, "merchants.bankrupt_weeks", MerchantBankruptWeeks);
            if (MerchantBankruptWeeks < 1) throw new FormatException("config.csv: merchants.bankrupt_weeks must be >= 1.");
            MerchantReserveUpkeepWeeks = Int(v, "merchants.reserve_upkeep_weeks", MerchantReserveUpkeepWeeks);
            if (MerchantReserveUpkeepWeeks < 0) throw new FormatException("config.csv: merchants.reserve_upkeep_weeks must be >= 0.");
            MerchantSellToHouseholds = Int(v, "merchants.sell_to_households", MerchantSellToHouseholds ? 1 : 0) != 0;
            MerchantFlowShare = Permille(v, "merchants.flow_permille", MerchantFlowShare);
            for (int k = 0; k < MerchantVehicleCapacity.Length; k++)
            {
                MerchantVehicleCapacity[k] = Permille(v, $"merchants.vehicle{k}.capacity_permille", MerchantVehicleCapacity[k]);
                MerchantVehicleCashTimes[k] = Int(v, $"merchants.vehicle{k}.cash_times", MerchantVehicleCashTimes[k]);
                MerchantVehicleCost[k] = Permille(v, $"merchants.vehicle{k}.cost_permille", MerchantVehicleCost[k]);
                MerchantVehicleDanger[k] = Permille(v, $"merchants.vehicle{k}.danger_permille", MerchantVehicleDanger[k]);
                MerchantVehiclePremium[k] = Permille(v, $"merchants.vehicle{k}.premium_permille", MerchantVehiclePremium[k]);
            }
            OrderLeadWeeks = Int(v, "orders.lead_weeks", OrderLeadWeeks);
            for (int g = 0; g < FoodGradePrice.Length; g++)
            {
                string grade = ((QualityGrade)g).ToString().ToLowerInvariant();
                FoodGradePrice[g] = Permille(v, $"food.{grade}.price_permille", FoodGradePrice[g]);
                FoodGradeDemand[g] = Permille(v, $"food.{grade}.demand_permille", FoodGradeDemand[g]);
            }
        }

        void ApplyContractsAndAssociations(Dictionary<string, string> v)
        {
            ContractsPerNation = Int(v, "contract.per_nation", ContractsPerNation);
            ContractMinStock = Int(v, "contract.min_stock", ContractMinStock);
            ContractStockWeeks = Int(v, "contract.stock_weeks", ContractStockWeeks);
            ContractDemandShare = Permille(v, "contract.demand_share_permille", ContractDemandShare);
            ContractCreditWeeks = Int(v, "contract.credit_weeks", ContractCreditWeeks);
            ContractMinWeeks = Int(v, "contract.min_weeks", ContractMinWeeks);
            ContractMaxWeeks = Int(v, "contract.max_weeks", ContractMaxWeeks);
            ContractDepositShare = Permille(v, "contract.deposit_permille", ContractDepositShare);
            ContractBaseDiscount = Permille(v, "contract.base_discount_permille", ContractBaseDiscount);
            ContractDiscountPerUnit = Permille(v, "contract.discount_per_unit_permille", ContractDiscountPerUnit);
            ContractMaxDiscount = Permille(v, "contract.max_discount_permille", ContractMaxDiscount);
            StrategicSubsidyShare = Permille(v, "state.strategic_subsidy_permille", StrategicSubsidyShare);
            MerchantJoinShare = Permille(v, "merchants.join_permille", MerchantJoinShare);
            MerchantMemberFee = Permille(v, "merchants.member_fee_permille", MerchantMemberFee);
            MerchantSupportFloor = Permille(v, "merchants.support_floor_permille", MerchantSupportFloor);
            MerchantSupportShare = Permille(v, "merchants.support_share_permille", MerchantSupportShare);
            MerchantMemberPremium = Permille(v, "merchants.member_premium_permille", MerchantMemberPremium);
        }

        void ApplyCoresHedgesAndForecast(Dictionary<string, string> v)
        {
            LodMode = Int(v, "lod.mode", LodMode);
            CoreAbsorb = Int(v, "core.absorb", CoreAbsorb ? 1 : 0) != 0;
            for (int c = 0; c < MerchantClassCash.Length; c++)
            {
                MerchantClassCash[c] = Long(v, $"merchants.class{c}.start_cash", MerchantClassCash[c]);
                MerchantClassSharePermille[c] = Int(v, $"merchants.class{c}.share_permille", MerchantClassSharePermille[c]);
            }
            for (int c = 0; c < MerchantClassVehicle.Length; c++)
            {
                int vehicle = Int(v, $"merchants.class{c}.vehicle", MerchantClassVehicle[c]);
                if (vehicle < 0 || vehicle > 2) throw new FormatException($"config.csv: merchants.class{c}.vehicle must be 0, 1 or 2.");
                MerchantClassVehicle[c] = (byte)vehicle;
            }
            FacilityCloseLossYears = Int(v, "facility.close_loss_years", FacilityCloseLossYears);
            FacilityRepurposeShare = Permille(v, "facility.repurpose_cost_permille", FacilityRepurposeShare);
            FacilityShares = Int(v, "facility.shares", FacilityShares);
            FacilityMaxListedShares = Int(v, "facility.max_listed_shares", FacilityMaxListedShares);
            if (FacilityShares < 1 || FacilityMaxListedShares < 0 || FacilityMaxListedShares >= FacilityShares)
                throw new FormatException("config.csv: facility.shares must be positive and facility.max_listed_shares below it.");
            CoreStacking = Int(v, "core.stacking", CoreStacking ? 1 : 0) != 0;
            CoreDemandWeighted = Int(v, "core.demand_weighted", CoreDemandWeighted ? 1 : 0) != 0;
            CoreWeight[0] = Int(v, "core.continent.weight", CoreWeight[0]);
            CoreWeight[1] = Int(v, "core.nation.weight", CoreWeight[1]);
            CoreWeight[2] = Int(v, "core.region.weight", CoreWeight[2]);
            CoreWagePerPoint = Permille(v, "core.wage_permille", CoreWagePerPoint);
            CoreDemandPerPoint = Permille(v, "core.demand_permille", CoreDemandPerPoint);
            MerchantRiskAversion = Int(v, "merchants.risk_aversion", MerchantRiskAversion);
            MerchantCountTripCosts = Int(v, "merchants.count_trip_costs", MerchantCountTripCosts ? 1 : 0) != 0;
            MerchantMustAct = Int(v, "merchants.must_act", MerchantMustAct ? 1 : 0) != 0;
            HedgeFloor = Permille(v, "merchants.hedge_floor_permille", HedgeFloor);
            HedgeFee = Permille(v, "merchants.hedge_fee_permille", HedgeFee);
            HedgeFall = Permille(v, "merchants.hedge_fall_permille", HedgeFall);
            HedgeFallChance = Permille(v, "merchants.hedge_chance_permille", HedgeFallChance);
            HedgeBankShare = Permille(v, "merchants.hedge_bank_share_permille", HedgeBankShare);
            OrderExpiryWeeks = Int(v, "orders.expiry_weeks", OrderExpiryWeeks);
            ForecastEnabled = Int(v, "production.forecast_enabled", ForecastEnabled ? 1 : 0) != 0;
            ForecastMinPlan = Permille(v, "production.forecast_min_permille", ForecastMinPlan);
            ForecastHouseholdWeight = Permille(v, "production.forecast_household_permille", ForecastHouseholdWeight);
            ForecastCover = Permille(v, "production.forecast_cover_permille", ForecastCover);
            ForecastPrimary = Int(v, "production.forecast_primary", ForecastPrimary ? 1 : 0) != 0;
            InvestmentMinPlan = Permille(v, "investment.min_plan_permille", InvestmentMinPlan);
            MerchantVehicleWear = Permille(v, "merchants.vehicle_wear_permille", MerchantVehicleWear);
        }

        void ApplyTiersAndTerrain(Dictionary<string, string> v)
        {
            for (int t = 0; t < IndustryTiers.Count; t++)
            {
                string tier = ((IndustryTier)t).ToString().ToLowerInvariant();
                FacilityStartCash[t] = Long(v, $"facility.{tier}.start_cash", FacilityStartCash[t]);
                FacilityUpkeep[t] = Long(v, $"facility.{tier}.upkeep", FacilityUpkeep[t]);
                FacilityValue[t] = Long(v, $"facility.{tier}.value", FacilityValue[t]);
            }
            for (int l = 0; l < WorldLevels.Count; l++)
            {
                string level = ((WorldLevel)l).ToString().ToLowerInvariant();
                LandPlots[l] = Int(v, $"land.{level}.plots", LandPlots[l]);
                LandValue[l] = Long(v, $"land.{level}.value", LandValue[l]);
            }
            for (int t = 0; t < RouteTerrains.Count; t++)
            {
                string terrain = ((RouteTerrain)t).ToString().ToLowerInvariant();
                TerrainExtraTurns[t] = Int(v, $"terrain.{terrain}.extra_turns", TerrainExtraTurns[t]);
                TerrainCostMultiplier[t] = Permille(v, $"terrain.{terrain}.cost_permille", TerrainCostMultiplier[t]);
                TerrainDanger[t] = Permille(v, $"terrain.{terrain}.danger_permille", TerrainDanger[t]);
                if (TerrainExtraTurns[t] < 0) throw new FormatException($"config.csv: terrain.{terrain}.extra_turns must not be negative.");
            }
        }

        void ApplyDangerTravelAndRoads(Dictionary<string, string> v)
        {
            RegionDanger = Permille(v, "danger.region_permille", RegionDanger);
            DangerValueScale = Long(v, "danger.value_scale", DangerValueScale);
            if (DangerValueScale < 1) throw new FormatException("config.csv: danger.value_scale must be >= 1.");
            EncounterCashLoss = Permille(v, "encounter.cash_loss_permille", EncounterCashLoss);
            EncounterCargoLoss = Permille(v, "encounter.cargo_loss_permille", EncounterCargoLoss);
            GuardBaseCost = Long(v, "guard.base_cost", GuardBaseCost);
            GuardCostShare = Permille(v, "guard.cost_permille", GuardCostShare);
            GuardReduction = Permille(v, "guard.reduction_permille", GuardReduction);
            InsurancePremium = Permille(v, "insurance.premium_permille", InsurancePremium);
            InsuranceRefund = Permille(v, "insurance.refund_permille", InsuranceRefund);
            PassageBaseFee = Long(v, "passage.base_fee", PassageBaseFee);
            PassagePerUnit = Long(v, "passage.per_unit", PassagePerUnit);
            ShipPrice = Long(v, "ship.price", ShipPrice);
            ShipMaxWeight = Long(v, "ship.max_weight", ShipMaxWeight);
            ShipMaxSize = Long(v, "ship.max_size", ShipMaxSize);
            ShipResaleShare = Permille(v, "ship.resale_permille", ShipResaleShare);
            RoadCostReduction = Permille(v, "road.cost_reduction_permille", RoadCostReduction);
            RoadDangerReduction = Permille(v, "road.danger_reduction_permille", RoadDangerReduction);
            RoadUpkeep = Long(v, "road.upkeep", RoadUpkeep);
            RoadDecayPermille = Int(v, "road.decay_permille", RoadDecayPermille);
            RoadBuildCostPerTurn = Long(v, "road.build_cost_per_turn", RoadBuildCostPerTurn);
            RoadBuildWeeks = Int(v, "road.build_weeks", RoadBuildWeeks);
            if (RoadBuildWeeks < 1) throw new FormatException("config.csv: road.build_weeks must be >= 1.");
        }

        void ApplyGroupsAndBanks(Dictionary<string, string> v)
        {
            string[] groupKeys = { "company", "guild", "state", "private" };
            for (int k = 0; k < GroupStartCash.Length; k++)
                GroupStartCash[k] = Long(v, $"groups.{groupKeys[k]}.start_cash", GroupStartCash[k]);
            CompaniesPerNation = Int(v, "groups.companies_per_nation", CompaniesPerNation);
            GuildFee = Long(v, "guild.fee", GuildFee);
            GuildInsuranceDiscount = Permille(v, "guild.insurance_discount_permille", GuildInsuranceDiscount);
            GuildLicenseDiscount = Permille(v, "guild.license_discount_permille", GuildLicenseDiscount);
            GuildHallMaxWeight = Long(v, "guild.hall_max_weight", GuildHallMaxWeight);
            GuildHallMaxSize = Long(v, "guild.hall_max_size", GuildHallMaxSize);
            BankInitialReserveShare = Permille(v, "bank.initial_reserve_permille", BankInitialReserveShare);
            DepositInterestPerYear = Permille(v, "bank.deposit_interest_permille", DepositInterestPerYear);
            ExchangeFee = Permille(v, "bank.exchange_fee_permille", ExchangeFee);
            LoanInterestPerYear = Permille(v, "bank.loan_interest_permille", LoanInterestPerYear);
            LoanTermWeeks = Int(v, "bank.loan_term_weeks", LoanTermWeeks);
            if (LoanTermWeeks < 1) throw new FormatException("config.csv: bank.loan_term_weeks must be >= 1.");
            LoanToValue = Permille(v, "bank.loan_to_value_permille", LoanToValue);
            LoanGraceWeeks = Int(v, "bank.grace_weeks", LoanGraceWeeks);
            MarginCallShare = Permille(v, "bank.margin_call_permille", MarginCallShare);
            MarginCallWeeks = Int(v, "bank.margin_call_weeks", MarginCallWeeks);
            if (MarginCallShare.Raw < LoanToValue.Raw || MarginCallWeeks < 0)
                throw new FormatException("config.csv: bank.margin_call_permille must be >= bank.loan_to_value_permille, and bank.margin_call_weeks >= 0.");
            AuctionWeeks = Int(v, "bank.auction_weeks", AuctionWeeks);
            if (AuctionWeeks < 1) throw new FormatException("config.csv: bank.auction_weeks must be >= 1.");
            AuctionReserveShare = Permille(v, "bank.auction_reserve_permille", AuctionReserveShare);
        }

        void ApplyPolicy(Dictionary<string, string> v)
        {
            PolicyStep = Permille(v, "policy.step_permille", PolicyStep);
            PolicyMaxSteps = Int(v, "policy.max_steps", PolicyMaxSteps);
            if (PolicyMaxSteps < 0 || PolicyStep.MulInt(PolicyMaxSteps * 1000L) >= 1000)
                throw new FormatException("config.csv: policy.step_permille x policy.max_steps must stay below 1000.");
            PolicyMoveThreshold = Permille(v, "policy.move_threshold_permille", PolicyMoveThreshold);
            PolicyResistance = Permille(v, "policy.resistance_permille", PolicyResistance);
            InfluenceAssetWeight = Permille(v, "policy.asset_weight_permille", InfluenceAssetWeight);
            InfluenceLobbyWeight = Permille(v, "policy.lobby_weight_permille", InfluenceLobbyWeight);
            InfluencePerJob = Long(v, "policy.influence_per_job", InfluencePerJob);
            JobsPerFacilityLevel = Int(v, "policy.jobs_per_facility_level", JobsPerFacilityLevel);
            JobsPerMerchantGroup = Int(v, "policy.jobs_per_merchant_group", JobsPerMerchantGroup);
            NpcLobbyShare = Permille(v, "policy.npc_lobby_share_permille", NpcLobbyShare);
            PolicyLossTilt = Permille(v, "policy.loss_tilt_permille", PolicyLossTilt);
            InflationOpposition = Permille(v, "policy.inflation_opposition_permille", InflationOpposition);
        }

        void ApplyDemandAndHouseholds(Dictionary<string, string> v)
        {
            NecessityElasticity = Permille(v, "demand.necessity_elasticity_permille", NecessityElasticity);
            NecessityChoke = Permille(v, "demand.necessity_choke_permille", NecessityChoke);
            if (NecessityChoke.Raw <= Fx.OneRaw) throw new FormatException("config.csv: demand.necessity_choke_permille must be above 1000.");
            LuxuryElasticity = Permille(v, "demand.luxury_elasticity_permille", LuxuryElasticity);
            MaxCheapDemand = Permille(v, "demand.max_cheap_permille", MaxCheapDemand);
            if (MaxCheapDemand.Raw < Fx.One.Raw) throw new FormatException("config.csv: demand.max_cheap_permille must be >= 1000.");
            for (int g = 0; g < QualityGrades.Count; g++)
            {
                GradeLuxuryShare[g] = Permille(v, $"demand.{((QualityGrade)g).ToString().ToLowerInvariant()}.luxury_share_permille", GradeLuxuryShare[g]);
                SemiGradeLuxuryShare[g] = Permille(v, $"demand.semi.{((QualityGrade)g).ToString().ToLowerInvariant()}.luxury_share_permille", SemiGradeLuxuryShare[g]);
            }
            long shares = 0;
            for (int q = 0; q < QuintileIncomeShare.Length; q++)
            {
                QuintileIncomeShare[q] = Permille(v, $"households.q{q + 1}.income_share_permille", QuintileIncomeShare[q]);
                if (QuintileIncomeShare[q].Raw <= 0) throw new FormatException("config.csv: household income shares must be positive.");
                shares += QuintileIncomeShare[q].Raw;
            }
            if (Math.Abs(shares - Fx.One.Raw) > QuintileIncomeShare.Length)
                throw new FormatException("config.csv: households.q1..q5.income_share_permille must sum to 1000.");
            PopulationBaseGrowth = Permille(v, "households.base_growth_permille", PopulationBaseGrowth);
            PopulationGrowthResponse = Permille(v, "households.growth_response_permille", PopulationGrowthResponse);
            PopulationMaxGrowth = Permille(v, "households.max_growth_permille", PopulationMaxGrowth);
            PopulationMaxDecline = Permille(v, "households.max_decline_permille", PopulationMaxDecline);
            IncomeResponse = Permille(v, "households.income_response_permille", IncomeResponse);
            IncomeMaxChange = Permille(v, "households.income_max_change_permille", IncomeMaxChange);
            NeedMemoryWeeks = Int(v, "households.need_memory_weeks", NeedMemoryWeeks);
            if (NeedMemoryWeeks < 0) throw new FormatException("config.csv: households.need_memory_weeks must be >= 0.");
            HouseholdBudgetWeeks = Int(v, "households.budget_weeks", HouseholdBudgetWeeks);
            if (HouseholdBudgetWeeks < 1) throw new FormatException("config.csv: households.budget_weeks must be >= 1.");
            HouseholdAlmsShare = Permille(v, "households.alms_permille", HouseholdAlmsShare);
            LuxuryWealthPerYear = Permille(v, "households.luxury_wealth_permille", LuxuryWealthPerYear);
            LuxuryWealthCap = Permille(v, "households.luxury_wealth_cap_permille", LuxuryWealthCap);
            if (LuxuryWealthCap.Raw < Fx.OneRaw) throw new FormatException("config.csv: households.luxury_wealth_cap_permille must be at least 1000.");
            LuxuryTax = Permille(v, "households.luxury_tax_permille", LuxuryTax);
        }

        void ApplyTradersAndNobles(Dictionary<string, string> v)
        {
            TraderFloatShare = Permille(v, "markets.trader_float_permille", TraderFloatShare);
            TraderLoanShare = Permille(v, "traders.loan_share_permille", TraderLoanShare);
            TraderLoanKeepWeeks = Int(v, "traders.loan_keep_weeks", TraderLoanKeepWeeks);
            TraderBankLoanShare = Permille(v, "traders.bank_loan_share_permille", TraderBankLoanShare);
            TraderLoanInterest = Permille(v, "traders.loan_interest_permille", TraderLoanInterest);
            TraderLoanWeeks = Int(v, "traders.loan_weeks", TraderLoanWeeks);
            if (TraderLoanWeeks < 1) throw new FormatException("config.csv: traders.loan_weeks must be >= 1.");
            TraderLoanRepayShare = Permille(v, "traders.loan_repay_permille", TraderLoanRepayShare);
            ProducerFloatShare = Permille(v, "markets.producer_float_permille", ProducerFloatShare);
            NobleRoyalShare = Permille(v, "noble.royal_share_permille", NobleRoyalShare);
            NobleCityShare = Permille(v, "noble.city_share_permille", NobleCityShare);
            NobleVillageShare = Permille(v, "noble.village_share_permille", NobleVillageShare);
            NobleAidShare = Permille(v, "noble.aid_permille", NobleAidShare);
            NobleProtectionShare = Permille(v, "noble.protection_permille", NobleProtectionShare);
            NobleSpendShare = Permille(v, "noble.spend_permille", NobleSpendShare);
            VillageWageAwayShare = Permille(v, "labor.village_wage_away_permille", VillageWageAwayShare);
        }

        void ApplyMigrationAndInvestment(Dictionary<string, string> v)
        {
            MigrationMaxShare = Permille(v, "migration.max_share_permille", MigrationMaxShare);
            MigrationEmancipationEarliestYears = Int(v, "migration.emancipation_earliest_years", MigrationEmancipationEarliestYears);
            FacilityExpansionBonus = Permille(v, "facility.expansion_bonus_permille", FacilityExpansionBonus);
            FacilityOutputWage = Permille(v, "facility.output_wage_permille", FacilityOutputWage);
            StatePublicWageShare = Permille(v, "state.public_wage_permille", StatePublicWageShare);
            FacilityMaxLevel = Int(v, "facility.max_level", FacilityMaxLevel);
            FacilityUnaidedOutput = Permille(v, "facility.unaided_output_permille", FacilityUnaidedOutput);
            InvestmentPaybackYears = Int(v, "investment.payback_years", InvestmentPaybackYears);
            InvestmentMaxPerNode = Int(v, "investment.max_per_node", InvestmentMaxPerNode); InvestmentProjectsPerNode = Int(v, "investment.projects_per_node", InvestmentProjectsPerNode);
            MigrationStartFreeShare = Permille(v, "migration.start_free_permille", MigrationStartFreeShare);
            MigrationFleeShare = Permille(v, "migration.flee_permille", MigrationFleeShare);
            MigrationBrokenContractFactor = Int(v, "migration.broken_contract_factor", MigrationBrokenContractFactor);
            MigrationJobsPerFacilityLevel = Int(v, "migration.jobs_per_facility_level", MigrationJobsPerFacilityLevel);
            MigrationVacancyShare = Permille(v, "migration.vacancy_permille", MigrationVacancyShare);
            MigrationEmancipationLeadYears = Int(v, "migration.emancipation_lead_years", MigrationEmancipationLeadYears);
            MigrationFreedPerYear = Permille(v, "migration.freed_per_year_permille", MigrationFreedPerYear);
            MigrationRedemptionYears = Int(v, "migration.redemption_years", MigrationRedemptionYears);
            MigrationRedemptionShare = Permille(v, "migration.redemption_permille", MigrationRedemptionShare);
            PopulationUrbanPenalty = Permille(v, "households.urban_penalty_permille", PopulationUrbanPenalty);
        }

        void ApplyLaborTaxAndDefense(Dictionary<string, string> v)
        {
            LaborBaseEmployment = Permille(v, "labor.base_employment_permille", LaborBaseEmployment);
            LaborMinEmployment = Permille(v, "labor.min_employment_permille", LaborMinEmployment);
            LaborMaxEmployment = Permille(v, "labor.max_employment_permille", LaborMaxEmployment);
            LaborResponse = Permille(v, "labor.response_permille", LaborResponse);
            LaborLongRunResponse = Permille(v, "labor.long_run_response_permille", LaborLongRunResponse);
            LaborMerchantWage = Permille(v, "labor.merchant_wage_permille", LaborMerchantWage);
            LaborMerchantBuffer = Permille(v, "labor.merchant_buffer_permille", LaborMerchantBuffer);
            TaxHousehold = Permille(v, "tax.household_permille", TaxHousehold);
            TaxCompany = Permille(v, "tax.company_permille", TaxCompany);
            DefenseShare = Permille(v, "state.defense_permille", DefenseShare);
            DefenseMaintenance = Permille(v, "state.defense_maintenance_permille", DefenseMaintenance);
            DefenseIntervalWeeks = Int(v, "state.defense_interval_weeks", DefenseIntervalWeeks);
            DefenseSafetyCostPerRoute = Int(v, "state.defense_safety_cost_per_route", DefenseSafetyCostPerRoute);
            DefenseMaxSafety = Permille(v, "state.defense_max_safety_permille", DefenseMaxSafety);
            SubsidyShare = Permille(v, "state.subsidy_share_permille", SubsidyShare);
            SubsidyRate = Permille(v, "state.subsidy_rate_permille", SubsidyRate);
            SubsidyYears = Int(v, "state.subsidy_years", SubsidyYears);
            SubsidyMaxPerNation = Int(v, "state.subsidy_max_per_nation", SubsidyMaxPerNation);
            StateTradeScore = Int(v, "state.trade_score_permille", StateTradeScore);
            if (DefenseIntervalWeeks < 1) throw new FormatException("config.csv: state.defense_interval_weeks must be >= 1.");
        }

        void ApplyStateProjects(Dictionary<string, string> v)
        {
            StateProjectShare = Permille(v, "state.project_share_permille", StateProjectShare);
            StateProjectsPerYear = Int(v, "state.projects_per_year", StateProjectsPerYear);
            StateProjectWeeks = Int(v, "state.project_weeks", StateProjectWeeks);
            StateRoadScore = Int(v, "state.road_score_permille", StateRoadScore);
            StateFarmlandCost = Long(v, "state.farmland_cost", StateFarmlandCost);
            StateMineCost = Long(v, "state.mine_cost", StateMineCost);
            StateHarborCost = Long(v, "state.harbor_cost", StateHarborCost);
            StateHighwayCostPerTurn = Long(v, "state.highway_cost_per_turn", StateHighwayCostPerTurn);
            StateLandBonus = Permille(v, "state.land_bonus_permille", StateLandBonus);
            StateLandMax = Int(v, "state.land_max", StateLandMax);
            StateHarborBonus = Permille(v, "state.harbor_bonus_permille", StateHarborBonus);
            StateHarborMax = Int(v, "state.harbor_max", StateHarborMax);
            StateHighwayBonus = Permille(v, "state.highway_bonus_permille", StateHighwayBonus);
            StateFacilityLevy = Permille(v, "state.facility_levy_permille", StateFacilityLevy);
        }

        void ApplyBankRatesAndFinance(Dictionary<string, string> v)
        {
            InternationalBankReserveShare = Permille(v, "bank.international_reserve_permille", InternationalBankReserveShare);
            TargetReserveRatio = Permille(v, "bank.target_reserve_permille", TargetReserveRatio);
            MaxLoanRateMultiplier = Permille(v, "bank.max_rate_multiplier_permille", MaxLoanRateMultiplier);
            if (MaxLoanRateMultiplier.Raw < Fx.One.Raw) throw new FormatException("config.csv: bank.max_rate_multiplier_permille must be >= 1000.");
            NpcLoanTermWeeks = Int(v, "bank.npc_loan_term_weeks", NpcLoanTermWeeks);
            if (NpcLoanTermWeeks < 1) throw new FormatException("config.csv: bank.npc_loan_term_weeks must be >= 1.");
            NpcLoanBufferWeeks = Int(v, "bank.npc_buffer_weeks", NpcLoanBufferWeeks);
            NpcLoanMaxReserveShare = Permille(v, "bank.npc_max_reserve_share_permille", NpcLoanMaxReserveShare);
            FinanceHistoryQuarters = Int(v, "finance.history_quarters", FinanceHistoryQuarters);
            if (FinanceHistoryQuarters < 4) throw new FormatException("config.csv: finance.history_quarters must be >= 4.");
            FinancialEventChancePermille = Int(v, "finance.event_chance_permille", FinancialEventChancePermille);
            FinancialEventEndReserveRatio = Permille(v, "finance.event_end_reserve_permille", FinancialEventEndReserveRatio);
            BankRunDrain = Permille(v, "finance.bank_run_drain_permille", BankRunDrain);
            BankRunWithdrawShare = Permille(v, "finance.bank_run_withdraw_permille", BankRunWithdrawShare);
            RateSpikeMultiplier = Permille(v, "finance.rate_spike_permille", RateSpikeMultiplier);
            BankRunCover = Permille(v, "finance.run_cover_permille", BankRunCover);
            VenturesPerContinent = Int(v, "venture.per_continent", VenturesPerContinent);
            VentureShares = Int(v, "venture.shares", VentureShares);
            VentureCapital = Long(v, "venture.capital", VentureCapital);
            VentureVoyageWeeks = Int(v, "venture.voyage_weeks", VentureVoyageWeeks);
            VentureLossPermille = Int(v, "venture.loss_permille", VentureLossPermille);
            VentureMinMargin = Permille(v, "venture.min_margin_permille", VentureMinMargin);
            VentureValueYears = Int(v, "venture.value_years", VentureValueYears);
            if (VenturesPerContinent < 0 || VentureShares < 1 || VentureCapital < 1 || VentureVoyageWeeks < 1 || VentureLossPermille < 0 || VentureLossPermille > 1000 || VentureValueYears < 0)
                throw new FormatException("config.csv: venture values out of range.");
            MarketCreditShare = Permille(v, "market.credit_share_permille", MarketCreditShare);
            MarketCreditWeight = Permille(v, "market.credit_weight_permille", MarketCreditWeight);
            MarketMomentum = Permille(v, "market.momentum_permille", MarketMomentum);
            MarketReversion = Permille(v, "market.reversion_permille", MarketReversion);
            MarketWeeklyMove = Permille(v, "market.weekly_move_permille", MarketWeeklyMove);
            MarketPremiumFloor = Permille(v, "market.premium_floor_permille", MarketPremiumFloor);
            MarketPremiumCap = Permille(v, "market.premium_cap_permille", MarketPremiumCap);
            MarketIdleWeight = Permille(v, "market.idle_weight_permille", MarketIdleWeight);
            MarketPropertyIdleWeight = Permille(v, "market.property_idle_weight_permille", MarketPropertyIdleWeight);
            MarketVentureIdleWeight = Permille(v, "market.venture_idle_weight_permille", MarketVentureIdleWeight);
            MarketRepayShare = Permille(v, "market.repay_permille", MarketRepayShare);
            MarketCrunchShare = Permille(v, "market.crunch_debt_share_permille", MarketCrunchShare);
            MarketHeatedPremium = Permille(v, "market.heated_permille", MarketHeatedPremium);
            MarketBurstPremium = Permille(v, "market.burst_permille", MarketBurstPremium);
            MarketLeverageLimit = Permille(v, "market.leverage_limit_permille", MarketLeverageLimit);
            MarketBurstDrop = Permille(v, "market.burst_drop_permille", MarketBurstDrop);
            if (MarketPremiumFloor.Raw <= 0 || MarketPremiumFloor > Fx.One || MarketPremiumCap < MarketBurstPremium
                || MarketHeatedPremium > MarketBurstPremium || MarketBurstDrop.Raw < 0 || MarketBurstDrop >= Fx.One)
                throw new FormatException("config.csv: market lines must keep floor <= 1 <= heated <= burst <= cap, and a drop below 1.");
        }

        void ApplyLicensesAndSpecialties(Dictionary<string, string> v)
        {
            LicensePriceMultiplier = Permille(v, "license.price_permille", LicensePriceMultiplier);
            LicensedMerchantPermille = Int(v, "license.merchant_share_permille", LicensedMerchantPermille);
            InternationalMerchantPermille = Int(v, "license.international_merchant_share_permille", InternationalMerchantPermille);
            IntercontinentalMerchantPermille = Int(v, "license.intercontinental_merchant_share_permille", IntercontinentalMerchantPermille);
            InternationalLicensePrice = Long(v, "license.international_price", InternationalLicensePrice);
            IntercontinentalLicensePrice = Long(v, "license.intercontinental_price", IntercontinentalLicensePrice);
            TollRate = Permille(v, "toll.tariff_permille", TollRate);
            TollSpecialtySurcharge = Permille(v, "toll.specialty_surcharge_permille", TollSpecialtySurcharge);
            SpecialtyOutputMultiplier = Permille(v, "specialty.output_permille", SpecialtyOutputMultiplier);
            SpecialtyGradeUpChance = Permille(v, "specialty.grade_up_permille", SpecialtyGradeUpChance);
            SpecialtyPriceMultiplier = Permille(v, "specialty.price_permille", SpecialtyPriceMultiplier);
            if (SpecialtyGradeUpChance.Raw < 0 || SpecialtyGradeUpChance.Raw > Fx.One.Raw)
                throw new FormatException("config.csv: specialty.grade_up_permille must be within 0..1000.");
            FacilityBankruptWeeks = Int(v, "facility.bankrupt_weeks", FacilityBankruptWeeks);
        }

        void ApplyMerchantJudgement(Dictionary<string, string> v)
        {
            MerchantTraitsEnabled = Int(v, "merchants.traits", MerchantTraitsEnabled ? 1 : 0) != 0;
            MerchantRumorsEnabled = Int(v, "merchants.rumors", MerchantRumorsEnabled ? 1 : 0) != 0;
            MerchantWeighSales = Int(v, "merchants.weigh_sales", MerchantWeighSales ? 1 : 0) != 0;
            MerchantJudgeHops = Int(v, "merchants.judge_hops", MerchantJudgeHops);
            if (MerchantJudgeHops < 1) throw new FormatException("config.csv: merchants.judge_hops must be >= 1.");
            MerchantBids = Int(v, "merchants.bids", MerchantBids ? 1 : 0) != 0;
            MerchantBidsWhileLoaded = Int(v, "merchants.bids", MerchantBids ? 1 : 0) >= 2;
            MerchantReturnHome = Int(v, "merchants.return_home", MerchantReturnHome ? 1 : 0) != 0;
            MerchantJudgeCoinUnits = Int(v, "merchants.judge_coin_units", MerchantJudgeCoinUnits);
            MerchantPortCrossers = Int(v, "merchants.port_crossers", MerchantPortCrossers ? 1 : 0) != 0;
            InternationalBalance = Int(v, "bank.international_balance", InternationalBalance ? 1 : 0) != 0;
            TraderSpread = Permille(v, "markets.trader_spread_permille", TraderSpread);
            TraderFloatKeepWeeks = Int(v, "traders.float_keep_weeks", TraderFloatKeepWeeks);
            TraderFloatPayout = Permille(v, "traders.float_payout_permille", TraderFloatPayout);
            MerchantFlowWage = Permille(v, "merchants.flow_wage_permille", MerchantFlowWage);
            MerchantCrossLevel = Int(v, "merchants.cross_level", MerchantCrossLevel);
            MerchantCrossFlow = Permille(v, "merchants.cross_flow_permille", MerchantCrossFlow);
            ContinentalProducers = Int(v, "producers.per_continent", ContinentalProducers);
        }

        void ApplyCredit(Dictionary<string, string> v)
        {
            BillCapWeeks = Int(v, "credit.bill_cap_weeks", BillCapWeeks);
            BillWeeks = Int(v, "credit.bill_weeks", BillWeeks);
            BillDiscount = Permille(v, "credit.bill_discount_permille", BillDiscount);
            BillRepayShare = Permille(v, "credit.bill_repay_permille", BillRepayShare);
            BillDefaultWeeks = Int(v, "credit.bill_default_weeks", BillDefaultWeeks);
            BillBankFloor = Permille(v, "credit.bill_bank_floor_permille", BillBankFloor);
            BillSaleDiscount = Permille(v, "credit.bill_sale_discount_permille", BillSaleDiscount);
            BillProducers = Int(v, "credit.bill_producers", BillProducers ? 1 : 0) != 0;
            BondBuyShare = Permille(v, "credit.bond_buy_permille", BondBuyShare);
            BondKeepWeeks = Int(v, "credit.bond_keep_weeks", BondKeepWeeks);
            BondGroupKeepTimes = Int(v, "credit.bond_group_keep_times", BondGroupKeepTimes);
            StateDebtShare = Permille(v, "credit.state_debt_permille", StateDebtShare);
            StateBondInterest = Permille(v, "credit.state_bond_interest_permille", StateBondInterest);
            StateBondWeeks = Int(v, "credit.state_bond_weeks", StateBondWeeks);
            CompanyDebtShare = Permille(v, "credit.company_debt_permille", CompanyDebtShare);
            CompanyBondInterest = Permille(v, "credit.company_bond_interest_permille", CompanyBondInterest);
            CompanyBondWeeks = Int(v, "credit.company_bond_weeks", CompanyBondWeeks);
            CompanyDefaultWeeks = Int(v, "credit.company_default_weeks", CompanyDefaultWeeks);
            WorkingWeeks = Int(v, "credit.working_weeks", WorkingWeeks);
            if (BillWeeks < 1 || StateBondWeeks < 1 || CompanyBondWeeks < 1) throw new FormatException("config.csv: credit weeks must be >= 1.");
        }

        void ApplyMerchantTraits(Dictionary<string, string> v)
        {
            MerchantGenerationShare = Permille(v, "merchants.generation_permille", MerchantGenerationShare);
            MerchantMutationShare = Permille(v, "merchants.mutation_permille", MerchantMutationShare);
            MerchantFitnessCapital = Permille(v, "merchants.fitness_capital_permille", MerchantFitnessCapital);
            string[] personalities = { "cautious", "normal", "aggressive" };
            for (int i = 0; i < personalities.Length; i++)
            {
                PersonalitySpend[i] = Permille(v, $"merchants.{personalities[i]}.spend_permille", PersonalitySpend[i]);
                PersonalityMinMargin[i] = Permille(v, $"merchants.{personalities[i]}.min_margin_permille", PersonalityMinMargin[i]);
                PersonalityLossAversion[i] = Int(v, $"merchants.{personalities[i]}.loss_aversion", PersonalityLossAversion[i]);
                string rangeKey = $"merchants.{personalities[i]}.range_level";
                if (v.TryGetValue(rangeKey, out var range))
                    PersonalityRange[i] = CsvTable.TryParseEnumName(range, out WorldLevel rangeLevel)
                        ? rangeLevel
                        : throw new FormatException($"config.csv: {rangeKey} '{range}' is not a WorldLevel.");
                Record(rangeKey, PersonalityRange[i].ToString());
            }
            string[] occupations = { "local", "expedition", "peddler" };
            for (int i = 0; i < occupations.Length; i++)
            {
                OccupationCapacity[i] = Permille(v, $"merchants.{occupations[i]}.capacity_permille", OccupationCapacity[i]);
                OccupationUpkeep[i] = Permille(v, $"merchants.{occupations[i]}.upkeep_permille", OccupationUpkeep[i]);
                OccupationDistancePower[i] = Int(v, $"merchants.{occupations[i]}.distance_power", OccupationDistancePower[i]);
                if (OccupationDistancePower[i] < 0 || OccupationDistancePower[i] > 2)
                    throw new FormatException($"config.csv: merchants.{occupations[i]}.distance_power must be 0, 1 or 2.");
            }
        }

        void ApplyDividendsAndCargo(Dictionary<string, string> v)
        {
            DividendBufferWeeks = Int(v, "facility.dividend_buffer_weeks", DividendBufferWeeks);
            if (DividendBufferWeeks < 0) throw new FormatException("config.csv: facility.dividend_buffer_weeks must be >= 0.");
            DividendShare = Permille(v, "facility.dividend_permille", DividendShare);
            GroupPayoutShare = Permille(v, "groups.payout_permille", GroupPayoutShare);
            StateReserveTarget = Permille(v, "bank.state_reserve_target_permille", StateReserveTarget);
            StateSpendingShare = Permille(v, "bank.state_spending_permille", StateSpendingShare);
            if (FacilityBankruptWeeks < 1) throw new FormatException("config.csv: facility.bankrupt_weeks must be >= 1.");
            LostWarehouseWeeks = Int(v, "cargo.lost_warehouse_weeks", LostWarehouseWeeks);
            if (LostWarehouseWeeks < 0) throw new FormatException("config.csv: cargo.lost_warehouse_weeks must be >= 0.");
        }

        void ApplyPricesAndCurrency(Dictionary<string, string> v)
        {
            PriceAdjustRate = Permille(v, "price.adjust_permille", PriceAdjustRate);
            PriceMaxStep = Permille(v, "price.max_step_permille", PriceMaxStep);
            PriceAnchorRate = Permille(v, "price.anchor_permille", PriceAnchorRate);
            StockPressure = Permille(v, "price.stock_pressure_permille", StockPressure);
            MinPrice = Int(v, "price.min", MinPrice);
            MintRatePerYear = Permille(v, "currency.mint_permille", MintRatePerYear);
            InitialGoldReserve = Long(v, "currency.gold_reserve", InitialGoldReserve);
            InitialSilverReserve = Long(v, "currency.silver_reserve", InitialSilverReserve);
            InitialCoinSupply = Long(v, "currency.coin_supply", InitialCoinSupply);
            CoinsPerGoldUnit = Long(v, "currency.coins_per_gold", CoinsPerGoldUnit);
            CoinsPerSilverUnit = Long(v, "currency.coins_per_silver", CoinsPerSilverUnit);
            CurrencyEndowmentSpreadPermille = Int(v, "currency.endowment_spread_permille", CurrencyEndowmentSpreadPermille);
        }

        void ApplyGuidance(Dictionary<string, string> v)
        {
            GuidanceDecayPerTurn = Permille(v, "guidance.decay_permille", GuidanceDecayPerTurn);
            GuidanceWeightBuyFacility = Permille(v, "guidance.weight.buy_facility_permille", GuidanceWeightBuyFacility);
            GuidanceWeightFoundCompany = Permille(v, "guidance.weight.found_company_permille", GuidanceWeightFoundCompany);
            GuidanceWeightBuyLicense = Permille(v, "guidance.weight.buy_license_permille", GuidanceWeightBuyLicense);
        }

        void ApplyStart(Dictionary<string, string> v)
        {
            if (v.TryGetValue("lod.focus_level", out var focus))
                LodFocusLevel = CsvTable.TryParseEnumName(focus, out WorldLevel level)
                    ? level
                    : throw new FormatException($"config.csv: lod.focus_level '{focus}' is not a WorldLevel.");
            Record("lod.focus_level", LodFocusLevel.ToString());
            PlayerStartNode = v.TryGetValue("player.start_node", out var start) ? start : PlayerStartNode;
            Record("player.start_node", PlayerStartNode);
            PlayerStartCash = Long(v, "player.start_cash", PlayerStartCash);
            CartMaxWeight = Long(v, "cargo.cart.max_weight", CartMaxWeight);
            CartMaxSize = Long(v, "cargo.cart.max_size", CartMaxSize);
            WarehouseMaxWeight = Long(v, "cargo.warehouse.max_weight", WarehouseMaxWeight);
            WarehouseMaxSize = Long(v, "cargo.warehouse.max_size", WarehouseMaxSize);
        }

        void Record(string key, string value) => _effective[key] = value;

        int Int(Dictionary<string, string> v, string key, int fallback)
        {
            long value = Long(v, key, fallback);
            if (value < int.MinValue || value > int.MaxValue)
                throw new FormatException($"config.csv: '{key}' = {value} is out of int range.");
            return (int)value;
        }

        long Long(Dictionary<string, string> v, string key, long fallback)
        {
            long value = v.TryGetValue(key, out var s) ? Parse(key, s) : fallback;
            Record(key, value.ToString(CultureInfo.InvariantCulture));
            return value;
        }

        // Recorded as the raw Fx value, so a file value of 1000 and the default Fx.One hash the same.
        Fx Permille(Dictionary<string, string> v, string key, Fx fallback)
        {
            var value = v.TryGetValue(key, out var s) ? Fx.FromPermille(Parse(key, s)) : fallback;
            Record(key, "fx:" + value.Raw.ToString(CultureInfo.InvariantCulture));
            return value;
        }

        static long Parse(string key, string s) =>
            long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? value
                : throw new FormatException($"config.csv: '{key}' = '{s}' is not an integer.");
    }
}
