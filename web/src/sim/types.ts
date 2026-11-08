// Types of everything that crosses the Worker boundary. Each is inferred from a schema that
// tests/types.test.ts checks against JSON the engine writes (engine/fes-sim/tests/web_fixtures.rs),
// so a change to a Rust serde shape fails a test here. Rust i32/i64/u8 and raw Fx values are all
// plain numbers; tuples are arrays; an enum is its name or `{Name: body}` (the serde default).
import { arr, bool, type Infer, lit, num, obj, opt, str, tag, tup } from './schema';

export const playerCommandSchema = tag({
  BankTransfer: obj({ amount: num }),
  Exchange: obj({ from_continent: num, to_continent: num, amount: num }),
  TakeLoan: obj({ amount: num }),
  RepayLoan: obj({ amount: num }),
  Bid: obj({ facility: num, amount: num }),
  BuyBond: obj({ issuer: num, amount: num }),
  SellBond: obj({ issuer: num, amount: num }),
  MoveFacilityCash: obj({ facility: num, amount: num }),
  SetFacilityControl: obj({ facility: num, mode: num }),
  BuyFacility: obj({ facility: num, price: num, axis: num }),
  FoundCompany: obj({ headquarters: num, focus: num, cost: num }),
  ExpandFacility: obj({ facility: num }),
  RepurposeFacility: obj({ facility: num, def_key: str }),
  JoinGuild: obj({ guild: num }),
  MoveGuildHallCargo: obj({ guild: num, good: num, quantity: num, to_hall: bool }),
  BuyPlot: null,
  SellPlot: null,
  BuyLicense: obj({ good: num }),
  BuyTradeLicense: obj({ kind: num }),
  BuyShip: null,
  SellShip: null,
  Lobby: obj({ nation: num, policy: num, direction: num, amount: num, base_good: num }),
  BuildRoad: obj({ destination: num }),
  BuyShare: obj({ facility: num, count: num }),
  SellShare: obj({ facility: num, count: num }),
  BuyShortLicence: obj({ kind: num }),
  SellShort: obj({ venture: bool, asset: num, count: num }),
  CoverShort: obj({ venture: bool, asset: num, count: num }),
  BuyGoods: obj({ good: num, quantity: num, pay_from_deposit: bool }),
  SellGoods: obj({ good: num, quantity: num }),
  MoveCargo: obj({ facility: num, good: num, quantity: num, to_warehouse: bool }),
  Travel: obj({ destination: num, guards: bool, insurance: bool }),
  BuyVentureShare: obj({ venture: num, count: num }),
  SellVentureShare: obj({ venture: num, count: num }),
});
export type PlayerCommand = Infer<typeof playerCommandSchema>;

const collateralKind = lit('Deposit', 'WarehouseCargo', 'CartCargo', 'Facility', 'Ship', 'Shares', 'Land');
const assetMarketKind = lit('Stock', 'Property', 'Venture');
const tripQuote = obj({
  route: num,
  fee: num,
  fee_continent: num,
  toll: num,
  toll_continent: num,
  cargo_units: num,
  passage: num,
  guards: num,
  insurance: num,
  cargo_value: num,
  encounter_chance: num,
});
export type TripQuote = Infer<typeof tripQuote>;

const edgeTurn = obj({ from: num, to: num, turn: num });
const yearOnly = obj({ year: num });

export const simEventSchema = tag({
  AuctionClosed: obj({ facility: num, won_by_player: bool, price: num, turn: num }),
  BondDefaulted: obj({ group: num, turn: num }),
  BondTraded: obj({ issuer: num, face: num }),
  CargoMoved: obj({ facility: num, good: num, to_warehouse: bool, requested: num, moved: num }),
  CoinsExchanged: obj({ from_continent: num, to_continent: num, paid: num, received: num, fee: num }),
  CollateralSeized: obj({ nation_node: num, kind: collateralKind, facility: num, value: num, turn: num }),
  CurrencyMinted: obj({ continent: num, year: num }),
  EmancipationEnacted: obj({ nation: num, year: num }),
  FacilityBankrupt: obj({ facility: num, was_player_owned: bool, warehouse_claim_until: num, turn: num }),
  FacilityClosed: obj({ facility: num, turn: num }),
  FacilityExpanded: obj({ facility: num, level: num }),
  FacilityFounded: obj({ facility: num, group: num }),
  FacilityRepurposed: obj({ facility: num, def_index: num }),
  FinancialEventEnded: obj({ nation: num, kind: num, turn: num }),
  FinancialEventStarted: obj({ nation: num, kind: num, turn: num }),
  GoodsTraded: obj({ market: num, good: num, bought: bool, requested: num, filled: num, price: num, turn: num }),
  GuildJoined: obj({ guild: num, expires_turn: num, fee: num }),
  GuildMembershipExpired: obj({ guild: num, turn: num }),
  HouseholdsUpdated: yearOnly,
  IncomeTaxCollected: obj({ year: num, households: num, companies: num }),
  LicenseBought: obj({ base_good: num, nation: num, expires_turn: num, price: num }),
  LicenseExpired: obj({ base_good: num, nation: num, turn: num }),
  LoanRepaid: obj({ nation_node: num, turn: num }),
  LoanTaken: obj({ nation_node: num, amount: num, balance: num, weekly_payment: num }),
  Lobbied: obj({ nation: num, policy: num, base_good: num, direction: num, amount: num }),
  LostWarehouseCleared: obj({ facility: num, turn: num }),
  MarginCalled: obj({ nation_node: num, shortfall: num, sale_turn: num }),
  MarketBurst: obj({ kind: assetMarketKind, node: num, premium: num, turn: num }),
  MarketHeated: obj({ kind: assetMarketKind, node: num, premium: num, turn: num }),
  MerchantBankrupt: obj({ merchant: num, turn: num }),
  MerchantVehicleBought: obj({ agent: num, vehicle: num }),
  MintingPolicyChanged: obj({ continent: num, step: num }),
  NobleContractBroken: obj({ vassal: num, lord: num, turn: num }),
  NpcLoanDefaulted: obj({ facility: num, merchant: num, nation_node: num, turn: num }),
  PeopleMigrated: obj({ from: num, to: num, people: num }),
  PlayerArrived: obj({ node: num, turn: num }),
  PolicyReviewed: obj({
    nation: num,
    year: num,
    toll_step: num,
    license_price_step: num,
    license_added: num,
    license_removed: num,
    influence: num,
  }),
  RoadBuilt: edgeTurn,
  RoadLost: edgeTurn,
  ShortCalled: obj({ nation: num, shortfall: num, cover_turn: num }),
  ShortsCovered: obj({ nation: num, cost: num, lent: num, turn: num }),
  StateProjectFinished: obj({ kind: num, node: num, turn: num }),
  StatisticsPublished: yearOnly,
  SubsidyEnacted: obj({ nation: num, good: num, turn: num }),
  SupplyContractBroken: obj({ seller: num, buyer: num }),
  SupplyContractSigned: obj({ seller: num, buyer: num, quantity: num, unit_price: num }),
  TripEncounter: obj({ destination: num, loss: num, loss_value: num, refund: num, turn: num }),
  TripStarted: obj({ destination: num, arrival_turn: num, paid: tripQuote, turn: num }),
  WorldEventStarted: obj({ definition: num, node: num, turn: num }),
  CommandApplied: obj({ command: opt(playerCommandSchema), turn: num }),
  CommandRejected: obj({ command: opt(playerCommandSchema), reason: str, turn: num }),
  Other: obj({ type_name: str }),
  /** A scripted scenario event (or a delivered scenario contract, `event` = contract id). */
  ScenarioDialog: obj({ event: str, text: str, coins: num, continent: num, turn: num }),
  ObjectiveDone: obj({ objective: str, turn: num }),
  ScenarioEnded: obj({ net_profit: num, objectives_done: num, turn: num }),
});
export type SimEventRecord = Infer<typeof simEventSchema>;

export const turnReportSchema = obj({ turn: num, events: arr(simEventSchema) });
export type TurnReport = Infer<typeof turnReportSchema>;

const holding = obj({ venture: bool, asset: num, count: num, price: num });
export const viewSchemas = {
  purse: obj({
    node: num,
    local_continent: num,
    coins: arr(num),
    travel_destination: num,
    arrival_turn: num,
    deposits: arr(num),
    loans: arr(obj({ nation: num, balance: num, weekly_payment: num, due: num, overdue_weeks: num })),
    bonds: arr(tup(num, num)),
  }),
  cargo: obj({
    cart: arr(tup(num, num)),
    warehouses: arr(tup(num, num, num)),
    ship_owned: bool,
    ship_docked_at: num,
  }),
  contracts: arr(
    obj({
      seller: num,
      buyer: num,
      from_node: num,
      to_node: num,
      good: num,
      quantity: num,
      unit_price: num,
      end_turn: num,
      prepaid: num,
    }),
  ),
  shares: obj({ shares: arr(holding), ventures: arr(holding), shorts: arr(tup(holding, num)) }),
  licences: arr(obj({ base_good: num, nation: num, expires_turn: num })),
  statement: obj({
    turn: num,
    published_year: num,
    nation_total: arr(num),
    reserve_ratio: arr(num),
    loan_rate: arr(num),
    continent_total: arr(num),
    price_index: arr(num),
    inflation: arr(num),
    records: arr(tup(num, arr(num))),
  }),
  /** Price is raw Fx (whole coins << 16); offer is what traders pay per unit on a sale, in coins. */
  market: obj({ quotes: arr(obj({ good: num, price: num, stock: num, offer: num })) }),
  /** Levels: 0 continent, 1 nation, 2 region, 3 city, 4 village. Terrain: 0 road, 1 mountain, 2 river, 3 sea. */
  map: obj({
    nodes: arr(obj({ id: num, key: str, name: str, level: num, parent: num, continent: num })),
    routes: arr(obj({ from: num, to: num, turns: num, terrain: num })),
  }),
  goods: arr(obj({ id: num, key: str, base_price: num })),
  /** The scenario results statement (null in free mode); coins of the start continent. */
  results: opt(
    obj({
      revenue: num,
      costs: obj({ purchases: num, travel: num, protection: num, losses: num, other: num }),
      net_profit: num,
      start_coins: num,
      end_coins: num,
      weeks: num,
    }),
  ),
  objectives: arr(obj({ id: str, text: str, done: bool, done_week: num })),
  scenario_contracts: arr(
    obj({
      id: str,
      good: num,
      quantity: num,
      node: num,
      reward: num,
      deadline_week: num,
      text: str,
      accepted: bool,
      delivered: bool,
      expired: bool,
    }),
  ),
} as const;

export type ViewName = keyof typeof viewSchemas;
export type Views = { [K in ViewName]: Infer<(typeof viewSchemas)[K]> };

/** `{result: "Ok"}` or `{result: "Error", message}` as the wasm `preview` returns it. */
export type CommandResult = { result: 'Ok' } | { result: 'Error'; message: string };
