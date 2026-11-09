# Judged-build art generation list

Tool: SpriteCook. These are **45 required PNG deliverables plus three optional hex tiles (road overlay, desert, snow; the seeded map generator places desert and snow)**, not generated or integrated assets. Paths are relative to `web/public/assets/`. Each English prompt is ready to paste.

## Shared production specification

Use readable 2D map symbols, PNG RGBA at native pixel size, and two pixels of clear padding for transparent sprites. Do not bake text, quality grades, quantities, prices or selection state into images. Future integration should use integer scaling and nearest-neighbor sampling; no UI changes are made here. Distinguish shapes as well as colors.

Shared 12-color palette (alpha separate): `#2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F`.

Use earth/parchment and dark outlines; water uses teal/blue, stone gray, and the player gold. Prompts do not guarantee valid exports: correct size, palette, alpha and tile seams after generation. The frame uses four-pixel nine-slice borders and an empty transparent center. Hex terrain is decorative; roads, rivers, mountain passes and sea lanes retain the engine route rules. Nation and Continent are scope emblems, not invented settlements. Two static caravan sprites suffice; future movement changes position rather than requiring animation frames.

## Complete goods coverage

Both data sets ship: 29 named base goods in `Assets/StreamingAssets/Data/goods.csv` and 1,000 synthetic base goods in `engine/fixtures/fullscale/goods.csv`. Named goods use the same-key PNG. All synthetic goods share `goods/trade_crate.png` except the three scenario-specific visual overrides below. Plan 7C supplies UI display names; engine keys, grade text and this art mapping stay unchanged.

| Base key | PNG |
| --- | --- |
| `wheat` | `goods/wheat.png` |
| `barley` | `goods/barley.png` |
| `iron_ore` | `goods/iron_ore.png` |
| `copper_ore` | `goods/copper_ore.png` |
| `wool` | `goods/wool.png` |
| `linen` | `goods/linen.png` |
| `iron_tools` | `goods/iron_tools.png` |
| `bread` | `goods/bread.png` |
| `flour` | `goods/flour.png` |
| `malt` | `goods/malt.png` |
| `iron_bar` | `goods/iron_bar.png` |
| `copper_bar` | `goods/copper_bar.png` |
| `yarn` | `goods/yarn.png` |
| `clothes` | `goods/clothes.png` |
| `ale` | `goods/ale.png` |
| `cookware` | `goods/cookware.png` |
| `cart` | `goods/cart.png` |
| `wagon` | `goods/wagon.png` |
| `porridge` | `goods/porridge.png` |
| `beans` | `goods/beans.png` |
| `vegetables` | `goods/vegetables.png` |
| `cheese` | `goods/cheese.png` |
| `salt_fish` | `goods/salt_fish.png` |
| `spice` | `goods/spice.png` |
| `silk` | `goods/silk.png` |
| `fur` | `goods/fur.png` |
| `amber` | `goods/amber.png` |
| `pickled_vegetables` | `goods/pickled_vegetables.png` |
| `dried_beans` | `goods/dried_beans.png` |
| `g1_0` | `goods/wheat.png` |
| `g6_0` | `goods/iron_tools.png` |
| `g19_0` | `goods/regional_specialty.png` |
| All remaining keys from `g0_0` through `g19_49` | `goods/trade_crate.png` |

Each prefix `g0` through `g19` has suffixes `0` through `49`. Grain/tools overrides reuse the scenario-described visual motif; they do not rename a key or assert a new item identity. All `@low`, `@mid`, `@high`, `@best` grades reuse their base-key image and retain grade text. This covers all 1,029 base goods. Update this table and the README together if data keys change.

## Deliverables and ready-to-paste prompts

<a id="terrain-plains"></a>

### terrain-plains: Plains hex tile

File: `map/terrain/plains.png` ? 28x32 px ? PNG RGBA ? pointy-top hex, transparent outside.

```text
Create one original 28x32 pixel-art pointy-top hex tile showing quiet parchment grassland for a medieval fantasy trading map. Hex vertices: (14,0), (28,8), (28,24), (14,32), (0,24), (0,8); pixels outside are transparent. Crisp pixel clusters, flat two-tone shading, light from upper left. Use only #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Single static asset, no text, numbers, logos, antialiasing, reference art or franchise motifs. Export exactly 28x32 PNG RGBA, no mockup or sheet. Terrain is decorative; travel rules are unchanged.
```

<a id="terrain-forest"></a>

### terrain-forest: Forest hex tile

File: `map/terrain/forest.png` ? 28x32 px ? PNG RGBA ? pointy-top hex, transparent outside.

```text
Create one original 28x32 pixel-art pointy-top hex tile showing deep green woodland for a medieval fantasy trading map. Hex vertices: (14,0), (28,8), (28,24), (14,32), (0,24), (0,8); pixels outside are transparent. Crisp pixel clusters, flat two-tone shading, light from upper left. Use only #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Single static asset, no text, numbers, logos, antialiasing, reference art or franchise motifs. Export exactly 28x32 PNG RGBA, no mockup or sheet. Terrain is decorative; travel rules are unchanged.
```

<a id="terrain-hills"></a>

### terrain-hills: Hills hex tile

File: `map/terrain/hills.png` ? 28x32 px ? PNG RGBA ? pointy-top hex, transparent outside.

```text
Create one original 28x32 pixel-art pointy-top hex tile showing low rolling ochre hills for a medieval fantasy trading map. Hex vertices: (14,0), (28,8), (28,24), (14,32), (0,24), (0,8); pixels outside are transparent. Crisp pixel clusters, flat two-tone shading, light from upper left. Use only #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Single static asset, no text, numbers, logos, antialiasing, reference art or franchise motifs. Export exactly 28x32 PNG RGBA, no mockup or sheet. Terrain is decorative; travel rules are unchanged.
```

<a id="terrain-mountain"></a>

### terrain-mountain: Mountain hex tile

File: `map/terrain/mountain.png` ? 28x32 px ? PNG RGBA ? pointy-top hex, transparent outside.

```text
Create one original 28x32 pixel-art pointy-top hex tile showing grey rocky mountains for a medieval fantasy trading map. Hex vertices: (14,0), (28,8), (28,24), (14,32), (0,24), (0,8); pixels outside are transparent. Crisp pixel clusters, flat two-tone shading, light from upper left. Use only #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Single static asset, no text, numbers, logos, antialiasing, reference art or franchise motifs. Export exactly 28x32 PNG RGBA, no mockup or sheet. Terrain is decorative; travel rules are unchanged.
```

<a id="terrain-water"></a>

### terrain-water: Water hex tile

File: `map/terrain/water.png` ? 28x32 px ? PNG RGBA ? pointy-top hex, transparent outside.

```text
Create one original 28x32 pixel-art pointy-top hex tile showing deep teal open water for a medieval fantasy trading map. Hex vertices: (14,0), (28,8), (28,24), (14,32), (0,24), (0,8); pixels outside are transparent. Crisp pixel clusters, flat two-tone shading, light from upper left. Use only #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Single static asset, no text, numbers, logos, antialiasing, reference art or franchise motifs. Export exactly 28x32 PNG RGBA, no mockup or sheet. Terrain is decorative; travel rules are unchanged.
```

<a id="terrain-desert"></a>

### terrain-desert: Desert hex tile

File: `map/terrain/desert.png` ? 28x32 px ? PNG RGBA ? pointy-top hex, transparent outside.

```text
Create one original 28x32 pixel-art pointy-top hex tile showing dry sand dunes and sparse scrub for a medieval fantasy trading map. Hex vertices: (14,0), (28,8), (28,24), (14,32), (0,24), (0,8); pixels outside are transparent. Crisp pixel clusters, flat two-tone shading, light from upper left. Use only #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Single static asset, no text, numbers, logos, antialiasing, reference art or franchise motifs. Export exactly 28x32 PNG RGBA, no mockup or sheet. Terrain is decorative; travel rules are unchanged.
```

<a id="terrain-snow"></a>

### terrain-snow: Snow hex tile

File: `map/terrain/snow.png` ? 28x32 px ? PNG RGBA ? pointy-top hex, transparent outside.

```text
Create one original 28x32 pixel-art pointy-top hex tile showing snowfield and ice with faint drifts for a medieval fantasy trading map. Hex vertices: (14,0), (28,8), (28,24), (14,32), (0,24), (0,8); pixels outside are transparent. Crisp pixel clusters, flat two-tone shading, light from upper left. Use only #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Single static asset, no text, numbers, logos, antialiasing, reference art or franchise motifs. Export exactly 28x32 PNG RGBA, no mockup or sheet. Terrain is decorative; travel rules are unchanged.
```

<a id="terrain-road-overlay"></a>

### terrain-road-overlay: Road-overlay hex tile

File: `map/terrain/road_overlay.png` ? 28x32 px ? PNG RGBA ? pointy-top hex, transparent outside. ?? ?? (optional).

```text
Create one original 28x32 pixel-art pointy-top hex tile showing a packed-earth road overlay, otherwise transparent for a medieval fantasy trading map. Hex vertices: (14,0), (28,8), (28,24), (14,32), (0,24), (0,8); pixels outside are transparent. Crisp pixel clusters, flat two-tone shading, light from upper left. Use only #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Single static asset, no text, numbers, logos, antialiasing, reference art or franchise motifs. Export exactly 28x32 PNG RGBA, no mockup or sheet. Terrain is decorative; travel rules are unchanged.
```

<a id="town-village"></a>

### town-village: Village level symbol

File: `map/towns/village.png` · 32x32 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 32x32 pixel-art small settlement symbol with two thatched huts for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="town-city"></a>

### town-city: City level symbol

File: `map/towns/city.png` · 32x32 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 32x32 pixel-art walled town symbol with a gate and three roofs for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="town-region"></a>

### town-region: Region level symbol

File: `map/towns/region.png` · 32x32 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 32x32 pixel-art regional market-hub symbol with three canvas stalls around a square for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="town-nation"></a>

### town-nation: Nation level symbol

File: `map/towns/nation.png` · 32x32 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 32x32 pixel-art national market-scope emblem, an unmarked shield and one small tower for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="town-continent"></a>

### town-continent: Continent level symbol

File: `map/towns/continent.png` · 32x32 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 32x32 pixel-art continental market-scope emblem, a simple compass rose inside a circle for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="caravan-cart"></a>

### caravan-cart: Land caravan

File: `map/caravans/cart.png` · 32x32 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 32x32 pixel-art east-facing loaded wooden merchant cart with two wheels and canvas cargo, no rider portrait for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="caravan-boat"></a>

### caravan-boat: Water caravan

File: `map/caravans/boat.png` · 32x32 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 32x32 pixel-art east-facing small wooden merchant cargo boat with one plain furled sail, no crew portraits for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="player-marker"></a>

### player-marker: Player marker

File: `map/markers/player.png` · 16x16 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 16x16 pixel-art gold diamond-shaped player position marker with a dark center and strong dark outline, visibly different from a cart or selection ring for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 16x16 pixels; no mockup or sprite sheet.
```

<a id="ui-frame"></a>

### ui-frame: Shared panel frame

File: `ui/frame.png` · 32x32 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 32x32 pixel-art plain parchment panel frame for nine-slice rendering, four-pixel borders on every side and an empty transparent center; no ornaments crossing slice boundaries for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="good-wheat"></a>

### good-wheat: Wheat

File: `goods/wheat.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art a small bundle of golden wheat ears for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-barley"></a>

### good-barley: Barley

File: `goods/barley.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art a short bundle of barley ears with long awns for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-iron_ore"></a>

### good-iron_ore: Iron Ore

File: `goods/iron_ore.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art two rough dark-gray iron ore rocks for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-copper_ore"></a>

### good-copper_ore: Copper Ore

File: `goods/copper_ore.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one rough stone with muted copper-orange seams for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-wool"></a>

### good-wool: Wool

File: `goods/wool.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art a compact tuft of cream sheep wool for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-linen"></a>

### good-linen: Linen

File: `goods/linen.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one folded cream linen cloth with a blue edge for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-iron_tools"></a>

### good-iron_tools: Iron Tools

File: `goods/iron_tools.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art a simple iron hammer crossed with small tongs for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-bread"></a>

### good-bread: Bread

File: `goods/bread.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one round brown bread loaf with three cuts for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-flour"></a>

### good-flour: Flour

File: `goods/flour.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art an open small flour sack with a white heap for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-malt"></a>

### good-malt: Malt

File: `goods/malt.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art a small scoop of tan sprouted malt grains for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-iron_bar"></a>

### good-iron_bar: Iron Bar

File: `goods/iron_bar.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art two stacked dark-gray iron ingots for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-copper_bar"></a>

### good-copper_bar: Copper Bar

File: `goods/copper_bar.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art two stacked muted orange copper ingots for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-yarn"></a>

### good-yarn: Yarn

File: `goods/yarn.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one cream yarn ball with a trailing thread for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-clothes"></a>

### good-clothes: Clothes

File: `goods/clothes.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one folded plain green tunic for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-ale"></a>

### good-ale: Ale

File: `goods/ale.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one wooden ale tankard with cream foam for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-cookware"></a>

### good-cookware: Cookware

File: `goods/cookware.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one dark iron cooking pot with two small handles for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-cart"></a>

### good-cart: Cart

File: `goods/cart.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one small two-wheeled wooden handcart viewed as a goods icon for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-wagon"></a>

### good-wagon: Wagon

File: `goods/wagon.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one compact four-wheeled covered wagon viewed as a goods icon for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-porridge"></a>

### good-porridge: Porridge

File: `goods/porridge.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one wooden bowl of pale porridge for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-beans"></a>

### good-beans: Beans

File: `goods/beans.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art a small scoop holding several distinct brown beans for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-vegetables"></a>

### good-vegetables: Vegetables

File: `goods/vegetables.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art a small basket with carrots and leafy greens for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-cheese"></a>

### good-cheese: Cheese

File: `goods/cheese.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one golden cheese wedge with two small holes for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-salt_fish"></a>

### good-salt_fish: Salt Fish

File: `goods/salt_fish.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one salted fish laid across a small salt sack for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-spice"></a>

### good-spice: Spice

File: `goods/spice.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one open pouch of muted red spice with a tiny scoop for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-silk"></a>

### good-silk: Silk

File: `goods/silk.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one neatly folded teal silk cloth with a simple gold edge for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-fur"></a>

### good-fur: Fur

File: `goods/fur.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one folded brown fur pelt with a cream edge for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-amber"></a>

### good-amber: Amber

File: `goods/amber.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art three small golden amber stones for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-pickled_vegetables"></a>

### good-pickled_vegetables: Pickled Vegetables

File: `goods/pickled_vegetables.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one plain glass jar containing green pickled vegetables for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-dried_beans"></a>

### good-dried_beans: Dried Beans

File: `goods/dried_beans.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one tied small sack with dry brown beans at its mouth for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-trade-crate"></a>

### good-trade-crate: Synthetic goods fallback

File: `goods/trade_crate.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one neutral closed wooden trade crate with a plain rope tie, no implied contents for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-regional-specialty"></a>

### good-regional-specialty: Scenario regional specialty

File: `goods/regional_specialty.png` · 24x24 px · PNG RGBA · transparent · shared 12-color palette above.

```text
Create one original 24x24 pixel-art one sealed teal trade parcel with a simple gold star-shaped seal, contents hidden, no implied magical effect for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

## Acceptance

Inspect silhouettes at 1x and 2x; repeat tiles in a 3x3 grid to check seams. Check alpha padding and nine-slice borders; future integration must preserve nearby text. Record actual generation date, final prompt, edits and screenshots in [the asset log](asset-log.md). Thumbnail and gameplay video are later release captures of the real game, not additional generated scenes.
