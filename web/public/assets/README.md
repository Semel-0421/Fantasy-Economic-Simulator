# 정적 자산 폴더와 후속 UI 파일명 계약

아래는 앞으로 생성·검수한 파일을 둘 **예약 경로**다. 현재 PNG/OGG는 없으며 기존 UI가 이 파일을 로드한다고 주장하지 않는다. 이번 커밋은 문서와 폴더만 추가한다. 후속 UI 작업은 아래 철자·대소문자·확장자를 그대로 사용한다.

```
assets/
  map/terrain/    # 지도 바탕과 경로 질감 5개
  map/towns/      # WorldLevel 5단계 기호
  map/caravans/   # 육상·수상 상단 2개
  map/markers/    # 플레이어 표식 1개
  goods/          # 상품 그림 31개
  ui/             # 공용 테두리 1개
  audio/music/   # 반복 음악 2개
  audio/sfx/     # 짧은 효과음 4개
```

배포 URL은 문서 기준 상대 경로 `assets/<아래 경로>`로 만든다. 예: `new URL('assets/goods/wheat.png', document.baseURI)`. 앞에 `/`를 붙여 하위 폴더 배포 경로를 잃지 않는다. PNG는 개별 파일이며 별도 아틀라스·메타데이터·애니메이션 시트는 요구하지 않는다. UI frame의 9분할 테두리는 사방 4픽셀이다. 상품 등급과 수량은 이미지에 넣지 않고 텍스트로 남긴다.

## UI가 후속 작업에서 사용할 정확한 파일명

| id | assets/ 아래 경로 | 크기 또는 길이 | 형식 |
| --- | --- | --- | --- |
| `terrain-plains` | `map/terrain/plains.png` | 28x32 | PNG RGBA |
| `terrain-forest` | `map/terrain/forest.png` | 28x32 | PNG RGBA |
| `terrain-hills` | `map/terrain/hills.png` | 28x32 | PNG RGBA |
| `terrain-mountain` | `map/terrain/mountain.png` | 28x32 | PNG RGBA |
| `terrain-water` | `map/terrain/water.png` | 28x32 | PNG RGBA |
| `terrain-road-overlay` | `map/terrain/road_overlay.png` | 28x32 | PNG RGBA |
| `town-village` | `map/towns/village.png` | 32x32 | PNG RGBA |
| `town-city` | `map/towns/city.png` | 32x32 | PNG RGBA |
| `town-region` | `map/towns/region.png` | 32x32 | PNG RGBA |
| `town-nation` | `map/towns/nation.png` | 32x32 | PNG RGBA |
| `town-continent` | `map/towns/continent.png` | 32x32 | PNG RGBA |
| `caravan-cart` | `map/caravans/cart.png` | 32x32 | PNG RGBA |
| `caravan-boat` | `map/caravans/boat.png` | 32x32 | PNG RGBA |
| `player-marker` | `map/markers/player.png` | 16x16 | PNG RGBA |
| `ui-frame` | `ui/frame.png` | 32x32 | PNG RGBA |
| `good-wheat` | `goods/wheat.png` | 24x24 | PNG RGBA |
| `good-barley` | `goods/barley.png` | 24x24 | PNG RGBA |
| `good-iron_ore` | `goods/iron_ore.png` | 24x24 | PNG RGBA |
| `good-copper_ore` | `goods/copper_ore.png` | 24x24 | PNG RGBA |
| `good-wool` | `goods/wool.png` | 24x24 | PNG RGBA |
| `good-linen` | `goods/linen.png` | 24x24 | PNG RGBA |
| `good-iron_tools` | `goods/iron_tools.png` | 24x24 | PNG RGBA |
| `good-bread` | `goods/bread.png` | 24x24 | PNG RGBA |
| `good-flour` | `goods/flour.png` | 24x24 | PNG RGBA |
| `good-malt` | `goods/malt.png` | 24x24 | PNG RGBA |
| `good-iron_bar` | `goods/iron_bar.png` | 24x24 | PNG RGBA |
| `good-copper_bar` | `goods/copper_bar.png` | 24x24 | PNG RGBA |
| `good-yarn` | `goods/yarn.png` | 24x24 | PNG RGBA |
| `good-clothes` | `goods/clothes.png` | 24x24 | PNG RGBA |
| `good-ale` | `goods/ale.png` | 24x24 | PNG RGBA |
| `good-cookware` | `goods/cookware.png` | 24x24 | PNG RGBA |
| `good-cart` | `goods/cart.png` | 24x24 | PNG RGBA |
| `good-wagon` | `goods/wagon.png` | 24x24 | PNG RGBA |
| `good-porridge` | `goods/porridge.png` | 24x24 | PNG RGBA |
| `good-beans` | `goods/beans.png` | 24x24 | PNG RGBA |
| `good-vegetables` | `goods/vegetables.png` | 24x24 | PNG RGBA |
| `good-cheese` | `goods/cheese.png` | 24x24 | PNG RGBA |
| `good-salt_fish` | `goods/salt_fish.png` | 24x24 | PNG RGBA |
| `good-spice` | `goods/spice.png` | 24x24 | PNG RGBA |
| `good-silk` | `goods/silk.png` | 24x24 | PNG RGBA |
| `good-fur` | `goods/fur.png` | 24x24 | PNG RGBA |
| `good-amber` | `goods/amber.png` | 24x24 | PNG RGBA |
| `good-pickled_vegetables` | `goods/pickled_vegetables.png` | 24x24 | PNG RGBA |
| `good-dried_beans` | `goods/dried_beans.png` | 24x24 | PNG RGBA |
| `good-trade-crate` | `goods/trade_crate.png` | 24x24 | PNG RGBA |
| `good-regional-specialty` | `goods/regional_specialty.png` | 24x24 | PNG RGBA |
| `music-menu` | `audio/music/menu_loop.ogg` | 60 s | Ogg Vorbis, 48 kHz |
| `music-game` | `audio/music/game_loop.ogg` | 96 s | Ogg Vorbis, 48 kHz |
| `sfx-coin` | `audio/sfx/coin.ogg` | 0.30 s | Ogg Vorbis, 48 kHz |
| `sfx-button` | `audio/sfx/button.ogg` | 0.08 s | Ogg Vorbis, 48 kHz |
| `sfx-event` | `audio/sfx/event_alert.ogg` | 0.80 s | Ogg Vorbis, 48 kHz |
| `sfx-results` | `audio/sfx/results_fanfare.ogg` | 3.0 s | Ogg Vorbis, 48 kHz |

## 상품 키 연결 규칙

이름 있는 기본 상품 29개는 각각 `goods/<기본 키>.png`와 정확히 대응한다. 예: `iron_ore` → `goods/iron_ore.png`. 전체 키는 [그림 목록](../../../Docs/assets/art-list.ko.md)에 있다.

전체 규모 합성 데이터의 기본 키는 `g0_0`~`g19_49`이다. 접두어 0~19와 접미어 0~49를 조합한 1,000개에 다음 순서로 대응한다.

| 기본 키 | 사용할 파일 |
| --- | --- |
| `g1_0` | `goods/wheat.png` |
| `g6_0` | `goods/iron_tools.png` |
| `g19_0` | `goods/regional_specialty.png` |
| 위 예외를 제외한 모든 합성 상품 | `goods/trade_crate.png` |

곡물·도구의 그림을 재사용할 뿐 상품 키·이름을 바꾸지 않는다. `@low`, `@mid`, `@high`, `@best` 등급 접미어가 있으면 기본 키로 그림을 찾고 등급 텍스트는 유지한다. 두 CSV에 있는 1,029개 기본 키를 모두 처리한다. 현재 두 데이터에 없는 새 키는 향후 UI에서 누락을 발견할 수 있게 하고, 추가 시 이 문서와 그림 목록·원장을 함께 갱신한다.

## 제작 자료와 적용 전 검사

[한국어 그림 목록](../../../Docs/assets/art-list.ko.md), [한국어 소리 목록](../../../Docs/assets/sound-list.ko.md), [자산 원장](../../../Docs/assets/asset-log.md)을 사용한다. 생성 후 규격과 음량·루프 경계를 검수하고 실제 도구·날짜·증거를 기록한다. `.gitkeep`은 폴더 유지용이며 자산 파일이나 로딩 대상이 아니다. WAV 원본과 도구 스크린샷은 public 폴더에 넣지 않는다. 파일이 실제 준비되고 UI에 연결된 뒤 이미지 표시·오디오 재생·용량을 별도로 검증해야 한다.

## ?? ?? ?? (Plan 7C)

?? 5?? 28?32 RGBA ??? ?? ????? ??? ?? ????. `road_overlay.png`? ?? ??? ?? ????. ?? ???? ground/road/river/sea ??? ???? ???. ??????????? ?? ??? ???? ?? ??? ?? ??? ??? ???.
