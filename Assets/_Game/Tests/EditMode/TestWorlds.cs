using System.IO;
using System.Text;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Simulation;
using UnityEngine;

namespace FES.Tests
{
    /// <summary>Data sources for tests: the shipped sample world and a generated full-scale world.</summary>
    public static class TestWorlds
    {
        public static IDataSource Sample() =>
            new FileDataSource(Path.Combine(Application.dataPath, "StreamingAssets", "Data"));

        public static GameSimulation BuildSample(ulong seed = 42) => SimulationBuilder.FromData(Sample(), seed).Build();

        /// <summary>The sample data with one file replaced.</summary>
        public static IDataSource SampleWith(string file, string text) => SampleWith((file, text));

        /// <summary>The sample data with some files replaced.</summary>
        public static IDataSource SampleWith(params (string file, string text)[] files)
        {
            var sample = Sample();
            var data = new InMemoryDataSource();
            foreach (var f in DataFiles.All)
                data.Add(f, sample.ReadText(f));
            foreach (var (file, text) in files) data.Add(file, text);
            return data;
        }

        /// <summary>The sample data with some config lines ("key,value") set.</summary>
        public static IDataSource SampleConfig(params string[] lines)
        {
            string text = Sample().ReadText(GameDatabase.ConfigFile);
            foreach (string line in lines)
                text = System.Text.RegularExpressions.Regex.Replace(text, "(?m)^" + line.Split(',')[0] + ",.*\r?\n?", "");
            return SampleWith(GameDatabase.ConfigFile, text.TrimEnd() + "\n" + string.Join("\n", lines) + "\n");
        }

        /// <summary>Queues the purchase of a facility at what its seller asks, with the coins given to the player.</summary>
        public static void BuyFacility(GameSimulation sim, int facility, GuidanceAxis axis)
        {
            long asked = sim.View.FacilityAskingPrice(new FacilityId(facility));
            var player = sim.World.Get<PlayerState>();
            player.Node = sim.World.Get<FacilityTable>().Node[facility];
            player.CoinsAt(player.Node) += asked;
            sim.Actions.BuyFacility(new FacilityId(facility), asked, axis);
        }

        /// <summary>The target-scale world under the rules the game ships with.</summary>
        public static IDataSource FullScaleShipped() =>
            ((InMemoryDataSource)FullScale()).Add(GameDatabase.ConfigFile, Sample().ReadText(GameDatabase.ConfigFile));

        /// <summary>
        /// Target scale: 5 continents x 5 nations x 2 regions x 5 cities x 5 villages
        /// (1,250 villages, 1,580 markets) and 20 categories x 50 goods x 4 grades (4,000 items).
        /// </summary>
        public static IDataSource FullScale()
        {
            const int categories = 20, goodsPerCategory = 50;
            var goods = new StringBuilder("key,category,base_price,base_demand\n");
            for (int c = 0; c < categories; c++)
                for (int g = 0; g < goodsPerCategory; g++)
                    goods.Append($"g{c}_{g},cat{c},{10 + c * 5 + g},{(g < 10 ? 20 : 2)}\n");

            var recipes = new StringBuilder("recipe,role,good,grade,qty\n");
            var facilities = new StringBuilder("key,tier,recipe\n");
            for (int c = 0; c < categories; c++)
            {
                recipes.Append($"raw{c},out,g{c}_0,Mid,40\nraw{c},out,g{c}_1,Low,30\n");
                facilities.Append($"producer{c},Primary,raw{c}\n");
            }
            for (int c = 0; c < 10; c++)
            {
                recipes.Append($"craft{c},in,g{c}_0,Mid,10\ncraft{c},out,g{c + 10}_5,Mid,5\n");
                facilities.Append($"workshop{c},Secondary,craft{c}\n");
            }
            for (int c = 0; c < 5; c++)
            {
                recipes.Append($"serve{c},in,g{c}_1,Low,8\nserve{c},out,g{c}_9,Mid,6\n");
                facilities.Append($"service{c},Tertiary,serve{c}\n");
            }

            var profiles = new StringBuilder("level,category,grades\n");
            for (int c = 0; c < 4; c++) profiles.Append($"Village,cat{c},Low|Mid\n");
            for (int c = 0; c < 10; c++) profiles.Append($"City,cat{c},Low|Mid|High\n");
            profiles.Append("Region,*,*\nNation,*,*\nContinent,*,*\n");

            var nodes = new StringBuilder("key,name,level,parent\n");
            var routes = new StringBuilder("from,to,travel_turns,cost_per_unit\n");
            for (int k = 0; k < 5; k++)
            {
                string cont = $"k{k}";
                nodes.Append($"{cont},,Continent,\n");
                for (int n = 0; n < 5; n++)
                {
                    string nation = $"{cont}n{n}";
                    nodes.Append($"{nation},,Nation,{cont}\n");
                    routes.Append($"{nation},{cont},2,3\n{cont},{nation},2,3\n");
                    for (int r = 0; r < 2; r++)
                    {
                        string region = $"{nation}r{r}";
                        nodes.Append($"{region},,Region,{nation}\n");
                        routes.Append($"{region},{nation},2,3\n{nation},{region},2,3\n");
                        for (int c = 0; c < 5; c++)
                        {
                            string city = $"{region}c{c}";
                            nodes.Append($"{city},,City,{region}\n");
                            routes.Append($"{city},{region},2,3\n{region},{city},2,3\n");
                            for (int v = 0; v < 5; v++)
                            {
                                string village = $"{city}v{v}";
                                nodes.Append($"{village},,Village,{city}\n");
                                routes.Append($"{village},{city},1,1\n{city},{village},1,1\n");
                            }
                        }
                    }
                }
            }

            return new InMemoryDataSource()
                .Add(GameDatabase.GoodsFile, goods.ToString())
                .Add(GameDatabase.RecipesFile, recipes.ToString())
                .Add(GameDatabase.FacilitiesFile, facilities.ToString())
                .Add(GameDatabase.MarketProfilesFile, profiles.ToString())
                .Add(GameDatabase.EventsFile,
                    "key,level,category,target,mul_permille,duration,chance_permille\n" +
                    "drought,Region,cat0,Supply,500,6,8\nfestival,City,*,Demand,1300,1,5\n")
                .Add(CsvMapSource.NodesFile, nodes.ToString())
                .Add(CsvMapSource.RoutesFile, routes.ToString());
        }
    }
}
