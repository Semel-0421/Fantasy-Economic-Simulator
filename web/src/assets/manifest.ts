// Exact production contract: public/assets/README.md.
export const MANIFEST: Record<string, { path: string; size: string }> = {
  "terrain-ground": { path: "map/terrain/ground.png", size: "32x32" },
  "terrain-road": { path: "map/terrain/road.png", size: "32x32" },
  "terrain-mountain": { path: "map/terrain/mountain.png", size: "32x32" },
  "terrain-river": { path: "map/terrain/river.png", size: "32x32" },
  "terrain-sea": { path: "map/terrain/sea.png", size: "32x32" },
  "town-village": { path: "map/towns/village.png", size: "32x32" },
  "town-city": { path: "map/towns/city.png", size: "32x32" },
  "town-region": { path: "map/towns/region.png", size: "32x32" },
  "town-nation": { path: "map/towns/nation.png", size: "32x32" },
  "town-continent": { path: "map/towns/continent.png", size: "32x32" },
  "caravan-cart": { path: "map/caravans/cart.png", size: "32x32" },
  "caravan-boat": { path: "map/caravans/boat.png", size: "32x32" },
  "player-marker": { path: "map/markers/player.png", size: "16x16" },
  "ui-frame": { path: "ui/frame.png", size: "32x32" },
  "good-wheat": { path: "goods/wheat.png", size: "24x24" },
  "good-barley": { path: "goods/barley.png", size: "24x24" },
  "good-iron_ore": { path: "goods/iron_ore.png", size: "24x24" },
  "good-copper_ore": { path: "goods/copper_ore.png", size: "24x24" },
  "good-wool": { path: "goods/wool.png", size: "24x24" },
  "good-linen": { path: "goods/linen.png", size: "24x24" },
  "good-iron_tools": { path: "goods/iron_tools.png", size: "24x24" },
  "good-bread": { path: "goods/bread.png", size: "24x24" },
  "good-flour": { path: "goods/flour.png", size: "24x24" },
  "good-malt": { path: "goods/malt.png", size: "24x24" },
  "good-iron_bar": { path: "goods/iron_bar.png", size: "24x24" },
  "good-copper_bar": { path: "goods/copper_bar.png", size: "24x24" },
  "good-yarn": { path: "goods/yarn.png", size: "24x24" },
  "good-clothes": { path: "goods/clothes.png", size: "24x24" },
  "good-ale": { path: "goods/ale.png", size: "24x24" },
  "good-cookware": { path: "goods/cookware.png", size: "24x24" },
  "good-cart": { path: "goods/cart.png", size: "24x24" },
  "good-wagon": { path: "goods/wagon.png", size: "24x24" },
  "good-porridge": { path: "goods/porridge.png", size: "24x24" },
  "good-beans": { path: "goods/beans.png", size: "24x24" },
  "good-vegetables": { path: "goods/vegetables.png", size: "24x24" },
  "good-cheese": { path: "goods/cheese.png", size: "24x24" },
  "good-salt_fish": { path: "goods/salt_fish.png", size: "24x24" },
  "good-spice": { path: "goods/spice.png", size: "24x24" },
  "good-silk": { path: "goods/silk.png", size: "24x24" },
  "good-fur": { path: "goods/fur.png", size: "24x24" },
  "good-amber": { path: "goods/amber.png", size: "24x24" },
  "good-pickled_vegetables": { path: "goods/pickled_vegetables.png", size: "24x24" },
  "good-dried_beans": { path: "goods/dried_beans.png", size: "24x24" },
  "good-trade-crate": { path: "goods/trade_crate.png", size: "24x24" },
  "good-regional-specialty": { path: "goods/regional_specialty.png", size: "24x24" },
  "music-menu": { path: "audio/music/menu_loop.ogg", size: "60 s" },
  "music-game": { path: "audio/music/game_loop.ogg", size: "96 s" },
  "sfx-coin": { path: "audio/sfx/coin.ogg", size: "0.30 s" },
  "sfx-button": { path: "audio/sfx/button.ogg", size: "0.08 s" },
  "sfx-event": { path: "audio/sfx/event_alert.ogg", size: "0.80 s" },
  "sfx-results": { path: "audio/sfx/results_fanfare.ogg", size: "3.0 s" },
};

/** Grades keep their own names in the UI, but share their base art. */
export function goodAsset(key: string): string {
  const base = key.replace(/@(low|mid|high|best)$/, '');
  const special: Record<string, string> = { g1_0: 'good-wheat', g6_0: 'good-iron_tools', g19_0: 'good-regional-specialty' };
  if (special[base]) return special[base];
  if (/^g(?:[0-9]|1[0-9])_(?:[0-9]|[1-4][0-9])$/.test(base)) return 'good-trade-crate';
  return MANIFEST[`good-${base}`] ? `good-${base}` : 'good-trade-crate';
}
