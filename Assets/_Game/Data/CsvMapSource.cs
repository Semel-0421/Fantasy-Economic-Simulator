using FES.Core;

namespace FES.Data
{
    /// <summary>Loads the handcrafted map from map_nodes.csv and map_routes.csv.</summary>
    public sealed class CsvMapSource : IMapSource
    {
        public const string NodesFile = "map_nodes.csv";
        public const string RoutesFile = "map_routes.csv";

        readonly IDataSource _source;

        public CsvMapSource(IDataSource source) => _source = source;

        public MapDef LoadMap()
        {
            var map = new MapDef();
            var nodes = CsvTable.Parse(NodesFile, _source.ReadText(NodesFile));
            for (int r = 0; r < nodes.RowCount; r++)
            {
                map.Nodes.Add(new MapNodeDef
                {
                    Key = nodes.GetString(r, "key"),
                    Name = nodes.GetStringOr(r, "name", nodes.GetString(r, "key")),
                    Level = nodes.GetEnum<WorldLevel>(r, "level"),
                    ParentKey = nodes.GetString(r, "parent"),
                    Tags = ParseTags(nodes.GetStringOr(r, "tags", string.Empty)),
                    TariffPermille = nodes.GetIntOr(r, "tariff_permille", -1),
                    SpecialtySurchargePermille = nodes.GetIntOr(r, "specialty_surcharge_permille", -1),
                });
            }
            if (_source.Exists(RoutesFile))
            {
                var routes = CsvTable.Parse(RoutesFile, _source.ReadText(RoutesFile));
                for (int r = 0; r < routes.RowCount; r++)
                {
                    map.Routes.Add(new RouteDef
                    {
                        FromKey = routes.GetString(r, "from"),
                        ToKey = routes.GetString(r, "to"),
                        TravelTurns = routes.GetInt(r, "travel_turns"),
                        CostPerUnit = routes.GetInt(r, "cost_per_unit"),
                        BaseFee = routes.GetIntOr(r, "base_fee", 0),
                    });
                }
            }
            return map;
        }

        /// <summary>Tags are separated by '|'; blanks are ignored.</summary>
        static string[] ParseTags(string text)
        {
            var tags = new System.Collections.Generic.List<string>();
            foreach (var part in text.Split('|'))
                if (part.Trim().Length > 0) tags.Add(part.Trim());
            return tags.ToArray();
        }
    }
}
