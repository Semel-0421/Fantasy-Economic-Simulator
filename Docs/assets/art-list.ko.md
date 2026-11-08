# 심사 빌드 그림 제작 목록

도구: SpriteCook. 아래는 **제작 예정 45개 PNG**의 규격이며 생성·검수·UI 연결은 아직 하지 않았다. 경로는 `web/public/assets/` 기준이다. 영어 프롬프트는 그대로 붙여 넣는다.

## 공통 제작 규격

픽셀 격자가 보이는 2D 지도 기호를 쓴다. PNG RGBA, 원본 픽셀 크기 유지, 투명 스프라이트는 사방 2픽셀 여백. 텍스트·등급·수량·가격·선택 효과를 이미지에 넣지 않는다. 정수 배율·최근접 보간을 쓰는 후속 UI 작업을 전제로 한다. 이 작업에서는 UI를 바꾸지 않는다. 색만으로 상품·플레이어를 구별하지 않고 외곽 형태도 다르게 만든다.

공용 12색 팔레트(투명도 별도): `#2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F`.

흙·양피지·어두운 윤곽선을 기본으로 하고, 물에는 청록·파랑, 바위에는 회색, 플레이어에는 금색을 쓴다. 프롬프트만으로 출력 규격을 보장할 수 없으므로 생성 후 크기·팔레트·투명도·타일 경계를 편집해 맞춘다. 테두리는 4픽셀 9분할용이며 가운데는 투명하게 비운다. 도로·산악·강·바다 이미지는 기존 경로 유형의 표현일 뿐 새 지형 규칙이 아니다. 국가·대륙은 도시가 아닌 시장 범위 기호다. 상단은 정지 이미지 두 개로 충분하며 움직임은 후속 화면에서 위치 이동으로 표현한다.

## 상품 전체 대응

`Assets/StreamingAssets/Data/goods.csv`는 이름 있는 기본 상품 29개, `engine/fixtures/fullscale/goods.csv`는 전체 규모 합성 기본 상품 1,000개를 담고 있으며 둘 다 배포된다. 이름 있는 29개는 같은 키의 PNG를 쓴다. 합성 상품은 아래 예외 외에 모두 `goods/trade_crate.png`를 공유한다. 고유한 이름·번호·등급은 기존 텍스트로 구별한다. 합성 키에 존재하지 않는 상품 정체를 새로 붙이지 않는다.

| 기본 키 | PNG |
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
| `g0_0` … `g19_49`의 나머지 키 | `goods/trade_crate.png` |

각 접두어 `g0`~`g19`에는 접미어 `0`~`49`가 있다. 곡물·도구 예외는 시나리오에서 설명한 모양의 재사용이며 키나 이름을 바꾸지 않는다. 모든 `@low`, `@mid`, `@high`, `@best` 등급은 기본 키의 그림을 공유하며 등급은 텍스트로 남긴다. 1,029개 기본 상품에 누락 없이 대응한다. 키가 바뀌면 이 표와 README를 함께 갱신한다.

## 개별 자산과 붙여 넣을 프롬프트

<a id="terrain-ground"></a>

### terrain-ground: 지도 바탕

파일: `map/terrain/ground.png` · 32x32 px · PNG RGBA · 불투명 반복 타일 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art quiet parchment-colored ground tile with sparse low-contrast grass flecks for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Opaque background; all four edges must tile seamlessly. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="terrain-road"></a>

### terrain-road: 도로 경로 질감

파일: `map/terrain/road.png` · 32x32 px · PNG RGBA · 불투명 반복 타일 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art packed-earth road texture tile without buildings for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Opaque background; all four edges must tile seamlessly. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="terrain-mountain"></a>

### terrain-mountain: 산악 경로 질감

파일: `map/terrain/mountain.png` · 32x32 px · PNG RGBA · 불투명 반복 타일 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art gray rocky mountain-pass texture tile without snow or buildings for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Opaque background; all four edges must tile seamlessly. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="terrain-river"></a>

### terrain-river: 강 경로 질감

파일: `map/terrain/river.png` · 32x32 px · PNG RGBA · 불투명 반복 타일 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art muted blue flowing river-water texture tile without banks or boats for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Opaque background; all four edges must tile seamlessly. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="terrain-sea"></a>

### terrain-sea: 바다 경로 질감

파일: `map/terrain/sea.png` · 32x32 px · PNG RGBA · 불투명 반복 타일 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art deep teal open-water texture tile with sparse gentle waves, no coast or boats for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Opaque background; all four edges must tile seamlessly. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="town-village"></a>

### town-village: 마을 단계 기호

파일: `map/towns/village.png` · 32x32 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art small settlement symbol with two thatched huts for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="town-city"></a>

### town-city: 도시 단계 기호

파일: `map/towns/city.png` · 32x32 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art walled town symbol with a gate and three roofs for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="town-region"></a>

### town-region: 지역 단계 기호

파일: `map/towns/region.png` · 32x32 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art regional market-hub symbol with three canvas stalls around a square for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="town-nation"></a>

### town-nation: 국가 단계 기호

파일: `map/towns/nation.png` · 32x32 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art national market-scope emblem, an unmarked shield and one small tower for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="town-continent"></a>

### town-continent: 대륙 단계 기호

파일: `map/towns/continent.png` · 32x32 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art continental market-scope emblem, a simple compass rose inside a circle for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="caravan-cart"></a>

### caravan-cart: 육상 상단

파일: `map/caravans/cart.png` · 32x32 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art east-facing loaded wooden merchant cart with two wheels and canvas cargo, no rider portrait for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="caravan-boat"></a>

### caravan-boat: 수상 상단

파일: `map/caravans/boat.png` · 32x32 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art east-facing small wooden merchant cargo boat with one plain furled sail, no crew portraits for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="player-marker"></a>

### player-marker: 플레이어 표식

파일: `map/markers/player.png` · 16x16 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 16x16 pixel-art gold diamond-shaped player position marker with a dark center and strong dark outline, visibly different from a cart or selection ring for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 16x16 pixels; no mockup or sprite sheet.
```

<a id="ui-frame"></a>

### ui-frame: 공용 패널 테두리

파일: `ui/frame.png` · 32x32 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 32x32 pixel-art plain parchment panel frame for nine-slice rendering, four-pixel borders on every side and an empty transparent center; no ornaments crossing slice boundaries for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 32x32 pixels; no mockup or sprite sheet.
```

<a id="good-wheat"></a>

### good-wheat: 밀

파일: `goods/wheat.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art a small bundle of golden wheat ears for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-barley"></a>

### good-barley: 보리

파일: `goods/barley.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art a short bundle of barley ears with long awns for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-iron_ore"></a>

### good-iron_ore: 철광석

파일: `goods/iron_ore.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art two rough dark-gray iron ore rocks for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-copper_ore"></a>

### good-copper_ore: 구리 광석

파일: `goods/copper_ore.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one rough stone with muted copper-orange seams for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-wool"></a>

### good-wool: 양모

파일: `goods/wool.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art a compact tuft of cream sheep wool for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-linen"></a>

### good-linen: 아마포

파일: `goods/linen.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one folded cream linen cloth with a blue edge for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-iron_tools"></a>

### good-iron_tools: 철제 도구

파일: `goods/iron_tools.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art a simple iron hammer crossed with small tongs for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-bread"></a>

### good-bread: 빵

파일: `goods/bread.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one round brown bread loaf with three cuts for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-flour"></a>

### good-flour: 밀가루

파일: `goods/flour.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art an open small flour sack with a white heap for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-malt"></a>

### good-malt: 맥아

파일: `goods/malt.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art a small scoop of tan sprouted malt grains for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-iron_bar"></a>

### good-iron_bar: 철괴

파일: `goods/iron_bar.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art two stacked dark-gray iron ingots for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-copper_bar"></a>

### good-copper_bar: 구리괴

파일: `goods/copper_bar.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art two stacked muted orange copper ingots for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-yarn"></a>

### good-yarn: 실

파일: `goods/yarn.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one cream yarn ball with a trailing thread for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-clothes"></a>

### good-clothes: 옷

파일: `goods/clothes.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one folded plain green tunic for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-ale"></a>

### good-ale: 에일

파일: `goods/ale.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one wooden ale tankard with cream foam for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-cookware"></a>

### good-cookware: 조리 도구

파일: `goods/cookware.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one dark iron cooking pot with two small handles for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-cart"></a>

### good-cart: 손수레

파일: `goods/cart.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one small two-wheeled wooden handcart viewed as a goods icon for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-wagon"></a>

### good-wagon: 짐마차

파일: `goods/wagon.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one compact four-wheeled covered wagon viewed as a goods icon for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-porridge"></a>

### good-porridge: 죽

파일: `goods/porridge.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one wooden bowl of pale porridge for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-beans"></a>

### good-beans: 콩

파일: `goods/beans.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art a small scoop holding several distinct brown beans for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-vegetables"></a>

### good-vegetables: 채소

파일: `goods/vegetables.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art a small basket with carrots and leafy greens for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-cheese"></a>

### good-cheese: 치즈

파일: `goods/cheese.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one golden cheese wedge with two small holes for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-salt_fish"></a>

### good-salt_fish: 소금에 절인 생선

파일: `goods/salt_fish.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one salted fish laid across a small salt sack for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-spice"></a>

### good-spice: 향신료

파일: `goods/spice.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one open pouch of muted red spice with a tiny scoop for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-silk"></a>

### good-silk: 비단

파일: `goods/silk.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one neatly folded teal silk cloth with a simple gold edge for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-fur"></a>

### good-fur: 모피

파일: `goods/fur.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one folded brown fur pelt with a cream edge for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-amber"></a>

### good-amber: 호박

파일: `goods/amber.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art three small golden amber stones for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-pickled_vegetables"></a>

### good-pickled_vegetables: 절임 채소

파일: `goods/pickled_vegetables.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one plain glass jar containing green pickled vegetables for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-dried_beans"></a>

### good-dried_beans: 말린 콩

파일: `goods/dried_beans.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one tied small sack with dry brown beans at its mouth for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-trade-crate"></a>

### good-trade-crate: 합성 상품 공용 상자

파일: `goods/trade_crate.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one neutral closed wooden trade crate with a plain rope tie, no implied contents for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

<a id="good-regional-specialty"></a>

### good-regional-specialty: 시나리오 지역 특산품

파일: `goods/regional_specialty.png` · 24x24 px · PNG RGBA · 투명 배경 · 위의 공용 12색 팔레트.

```text
Create one original 24x24 pixel-art one sealed teal trade parcel with a simple gold star-shaped seal, contents hidden, no implied magical effect for a medieval fantasy trading strategy game. Top-down map-symbol view, crisp hand-placed pixel clusters, one-pixel dark outline, flat two-tone shading, light from upper left. Use only this palette: #2B2118 #F3E9D2 #E4D4AE #B07716 #6B5135 #A67C52 #3F5F43 #8AA374 #2F5D62 #3D7EA6 #7D7D7D #8C2F1F. Transparent background; leave two pixels of clear padding. Single static asset, no text, letters, numbers, logos, gradients, antialiasing or reference to existing franchises. Export one PNG at exactly 24x24 pixels; no mockup or sprite sheet.
```

## 완료 확인

1:1 및 2배 크기에서 형태가 읽히는지 확인한다. 타일은 3×3으로 이어 경계를 확인한다. 투명 여백과 9분할 테두리를 검사하고, 화면에 붙인 뒤 주변 텍스트가 가리지 않는지 확인한다. 실제 생성 날짜·프롬프트·수정 과정·화면 증거를 [자산 원장](asset-log.md)에 기록한다. 썸네일과 플레이 영상은 후속 출시 작업에서 실제 게임 화면으로 만들며 별도 생성 그림을 추가하지 않는다.
