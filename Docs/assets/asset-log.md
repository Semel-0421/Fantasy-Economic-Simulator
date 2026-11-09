# 자산 원장과 AI 사용 공개 규칙

이 원장은 심사 빌드용 **제작 예정 자산 51개**의 예약 경로를 기록한다. 아직 이미지·음악·효과음을 생성하거나 사용 증거를 확보하지 않았다. `예정`은 도구 사용 실적이 아니며 날짜의 `—`도 생성일을 뜻하지 않는다. 생성 후 한 행씩 실제 도구·모델/버전(확인 가능한 경우)·최종 프롬프트·수정 과정·날짜·증거 경로로 갱신한다. 같은 자산을 다시 만들면 notes에 이전 버전과 최종 채택본을 구분한다.

## 공개 규칙과 도구별 수상 증거

근거: [승인 스펙 8절](../superpowers/specs/2026-11-04-rust-web-port-design.md#8-assets-and-ai-use-user-ai-only-as-a-production-tool), [프로젝트 대회 참고문서](../GDD/references/CHROMA_AWARDS_S2.md), [공식 규칙](https://chromaawards.com/terms), [Puzzle & Strategy 안내](https://chromaawards.com/prizes/game-puzzle-strategy). 공식 두 페이지는 2026-11-09에 확인했으며 제출 직전에 다시 확인한다.

- 제출 작품에는 최소 한 가지 AI 도구 사용이 필요하다. 실제 사용한 도구와 범위를 정확하고 완전하게 공개하고, 계획·생성·수작업 수정·최종 사용을 구분한다. 직접 기여자의 저작·기여도 표시도 남긴다.
- 도구별 수상을 노리려면 해당 Tool Tag를 붙이고, 실제 작품에서 그 도구를 의미 있게 사용하며, 짧은 설명·스크린샷·추정 사용 비중 등 증거를 제출한다. 이 목록을 작성하거나 도구를 태그하는 것만으로 수상 자격을 확보했다고 주장하지 않는다.
- 최종 생성 화면에는 도구명과 프롬프트/작업 이력이 보이게 한다. 채택본을 게임에서 사용한 화면도 남긴다. 추정 비중을 쓴다면 분모와 계산 방법(예: 최종 채택된 그림 45개 중 SpriteCook 생성 후 편집한 개수)을 함께 적고, 작업하지 않은 비중을 만들어 쓰지 않는다.
- SpriteCook은 픽셀 그림, ElevenLabs Music/Sound Effects는 음악·효과음 제작에 사용할 예정이다. 실제 생성·편집·사용이 확인된 것만 최종 공개한다. 이 문서와 프롬프트 초안은 Codex(GPT-6.1-Sol)가 작성했으며 그것은 SpriteCook/ElevenLabs 생성 실적이 아니다. 최종 제출 공개에는 코드·문구에 실제 사용한 Claude/Codex 등도 별도로 정확하게 기록한다.
- 원본·참고 자료·음원·인물·로고의 사용 권리를 확보하고 무단 제3자 IP를 쓰지 않는다. 생성 결과를 검사하고, 수작업 편집도 숨기지 않는다. 비밀키나 계정 정보가 보이는 증거는 남기지 않는다.
- AI는 제작 도구로만 쓴다. 배포 게임은 완성된 로컬 PNG/OGG만 읽으며 게임 중 AI API나 외부 생성 호출을 하지 않는다. Meshy/3D 자산은 이 2D 빌드에 쓰지 않는다.

원본과 도구별 증거는 생성 후 정리한다. 큰 원본·5 MB 초과 파일은 Git에 넣지 않는다. 증거 스크린샷도 아직 존재하지 않는 예약 경로이며 실제 확보 후 경로와 날짜를 확인한다. 영어 게임 텍스트와 플레이 영상·썸네일은 후속 화면/출시 작업에서 실제 빌드로 검수한다.

## 자산별 기록

| id | file path | tool | prompt or process | date | screenshot path | notes |
| --- | --- | --- | --- | --- | --- | --- |
| `terrain-plains` | `web/public/assets/map/terrain/plains.png` | SpriteCook (??) | [terrain-plains](art-list.md#terrain-plains)? 28?32 RGBA ?? ?? ???? | ? | `Docs/assets/evidence/terrain-plains/generation.png` | ?? ??; ?? ??? |
| `terrain-forest` | `web/public/assets/map/terrain/forest.png` | SpriteCook (??) | [terrain-forest](art-list.md#terrain-forest)? 28?32 RGBA ?? ?? ???? | ? | `Docs/assets/evidence/terrain-forest/generation.png` | ?? ??; ?? ??? |
| `terrain-hills` | `web/public/assets/map/terrain/hills.png` | SpriteCook (??) | [terrain-hills](art-list.md#terrain-hills)? 28?32 RGBA ?? ?? ???? | ? | `Docs/assets/evidence/terrain-hills/generation.png` | ?? ??; ?? ??? |
| `terrain-mountain` | `web/public/assets/map/terrain/mountain.png` | SpriteCook (??) | [terrain-mountain](art-list.md#terrain-mountain)? 28?32 RGBA ?? ?? ???? | ? | `Docs/assets/evidence/terrain-mountain/generation.png` | ?? ??; ?? ??? |
| `terrain-water` | `web/public/assets/map/terrain/water.png` | SpriteCook (??) | [terrain-water](art-list.md#terrain-water)? 28?32 RGBA ?? ?? ???? | ? | `Docs/assets/evidence/terrain-water/generation.png` | ?? ??; ?? ??? |
| `terrain-road-overlay` | `web/public/assets/map/terrain/road_overlay.png` | SpriteCook (??) | [terrain-road-overlay](art-list.md#terrain-road-overlay)? 28?32 RGBA ?? ?? ???? | ? | `Docs/assets/evidence/terrain-road-overlay/generation.png` | ?? ??; ?? ???; ?? ?? |
| `town-village` | `web/public/assets/map/towns/village.png` | SpriteCook (예정) | [town-village](art-list.md#town-village)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/town-village/generation.png` | 제작 예정; 증거 미확보 |
| `town-city` | `web/public/assets/map/towns/city.png` | SpriteCook (예정) | [town-city](art-list.md#town-city)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/town-city/generation.png` | 제작 예정; 증거 미확보 |
| `town-region` | `web/public/assets/map/towns/region.png` | SpriteCook (예정) | [town-region](art-list.md#town-region)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/town-region/generation.png` | 제작 예정; 증거 미확보 |
| `town-nation` | `web/public/assets/map/towns/nation.png` | SpriteCook (예정) | [town-nation](art-list.md#town-nation)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/town-nation/generation.png` | 제작 예정; 증거 미확보 |
| `town-continent` | `web/public/assets/map/towns/continent.png` | SpriteCook (예정) | [town-continent](art-list.md#town-continent)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/town-continent/generation.png` | 제작 예정; 증거 미확보 |
| `caravan-cart` | `web/public/assets/map/caravans/cart.png` | SpriteCook (예정) | [caravan-cart](art-list.md#caravan-cart)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/caravan-cart/generation.png` | 제작 예정; 증거 미확보 |
| `caravan-boat` | `web/public/assets/map/caravans/boat.png` | SpriteCook (예정) | [caravan-boat](art-list.md#caravan-boat)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/caravan-boat/generation.png` | 제작 예정; 증거 미확보 |
| `player-marker` | `web/public/assets/map/markers/player.png` | SpriteCook (예정) | [player-marker](art-list.md#player-marker)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/player-marker/generation.png` | 제작 예정; 증거 미확보 |
| `ui-frame` | `web/public/assets/ui/frame.png` | SpriteCook (예정) | [ui-frame](art-list.md#ui-frame)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/ui-frame/generation.png` | 제작 예정; 증거 미확보 |
| `good-wheat` | `web/public/assets/goods/wheat.png` | SpriteCook (예정) | [good-wheat](art-list.md#good-wheat)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-wheat/generation.png` | 제작 예정; 증거 미확보 |
| `good-barley` | `web/public/assets/goods/barley.png` | SpriteCook (예정) | [good-barley](art-list.md#good-barley)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-barley/generation.png` | 제작 예정; 증거 미확보 |
| `good-iron_ore` | `web/public/assets/goods/iron_ore.png` | SpriteCook (예정) | [good-iron_ore](art-list.md#good-iron_ore)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-iron_ore/generation.png` | 제작 예정; 증거 미확보 |
| `good-copper_ore` | `web/public/assets/goods/copper_ore.png` | SpriteCook (예정) | [good-copper_ore](art-list.md#good-copper_ore)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-copper_ore/generation.png` | 제작 예정; 증거 미확보 |
| `good-wool` | `web/public/assets/goods/wool.png` | SpriteCook (예정) | [good-wool](art-list.md#good-wool)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-wool/generation.png` | 제작 예정; 증거 미확보 |
| `good-linen` | `web/public/assets/goods/linen.png` | SpriteCook (예정) | [good-linen](art-list.md#good-linen)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-linen/generation.png` | 제작 예정; 증거 미확보 |
| `good-iron_tools` | `web/public/assets/goods/iron_tools.png` | SpriteCook (예정) | [good-iron_tools](art-list.md#good-iron_tools)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-iron_tools/generation.png` | 제작 예정; 증거 미확보 |
| `good-bread` | `web/public/assets/goods/bread.png` | SpriteCook (예정) | [good-bread](art-list.md#good-bread)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-bread/generation.png` | 제작 예정; 증거 미확보 |
| `good-flour` | `web/public/assets/goods/flour.png` | SpriteCook (예정) | [good-flour](art-list.md#good-flour)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-flour/generation.png` | 제작 예정; 증거 미확보 |
| `good-malt` | `web/public/assets/goods/malt.png` | SpriteCook (예정) | [good-malt](art-list.md#good-malt)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-malt/generation.png` | 제작 예정; 증거 미확보 |
| `good-iron_bar` | `web/public/assets/goods/iron_bar.png` | SpriteCook (예정) | [good-iron_bar](art-list.md#good-iron_bar)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-iron_bar/generation.png` | 제작 예정; 증거 미확보 |
| `good-copper_bar` | `web/public/assets/goods/copper_bar.png` | SpriteCook (예정) | [good-copper_bar](art-list.md#good-copper_bar)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-copper_bar/generation.png` | 제작 예정; 증거 미확보 |
| `good-yarn` | `web/public/assets/goods/yarn.png` | SpriteCook (예정) | [good-yarn](art-list.md#good-yarn)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-yarn/generation.png` | 제작 예정; 증거 미확보 |
| `good-clothes` | `web/public/assets/goods/clothes.png` | SpriteCook (예정) | [good-clothes](art-list.md#good-clothes)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-clothes/generation.png` | 제작 예정; 증거 미확보 |
| `good-ale` | `web/public/assets/goods/ale.png` | SpriteCook (예정) | [good-ale](art-list.md#good-ale)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-ale/generation.png` | 제작 예정; 증거 미확보 |
| `good-cookware` | `web/public/assets/goods/cookware.png` | SpriteCook (예정) | [good-cookware](art-list.md#good-cookware)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-cookware/generation.png` | 제작 예정; 증거 미확보 |
| `good-cart` | `web/public/assets/goods/cart.png` | SpriteCook (예정) | [good-cart](art-list.md#good-cart)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-cart/generation.png` | 제작 예정; 증거 미확보 |
| `good-wagon` | `web/public/assets/goods/wagon.png` | SpriteCook (예정) | [good-wagon](art-list.md#good-wagon)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-wagon/generation.png` | 제작 예정; 증거 미확보 |
| `good-porridge` | `web/public/assets/goods/porridge.png` | SpriteCook (예정) | [good-porridge](art-list.md#good-porridge)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-porridge/generation.png` | 제작 예정; 증거 미확보 |
| `good-beans` | `web/public/assets/goods/beans.png` | SpriteCook (예정) | [good-beans](art-list.md#good-beans)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-beans/generation.png` | 제작 예정; 증거 미확보 |
| `good-vegetables` | `web/public/assets/goods/vegetables.png` | SpriteCook (예정) | [good-vegetables](art-list.md#good-vegetables)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-vegetables/generation.png` | 제작 예정; 증거 미확보 |
| `good-cheese` | `web/public/assets/goods/cheese.png` | SpriteCook (예정) | [good-cheese](art-list.md#good-cheese)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-cheese/generation.png` | 제작 예정; 증거 미확보 |
| `good-salt_fish` | `web/public/assets/goods/salt_fish.png` | SpriteCook (예정) | [good-salt_fish](art-list.md#good-salt_fish)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-salt_fish/generation.png` | 제작 예정; 증거 미확보 |
| `good-spice` | `web/public/assets/goods/spice.png` | SpriteCook (예정) | [good-spice](art-list.md#good-spice)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-spice/generation.png` | 제작 예정; 증거 미확보 |
| `good-silk` | `web/public/assets/goods/silk.png` | SpriteCook (예정) | [good-silk](art-list.md#good-silk)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-silk/generation.png` | 제작 예정; 증거 미확보 |
| `good-fur` | `web/public/assets/goods/fur.png` | SpriteCook (예정) | [good-fur](art-list.md#good-fur)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-fur/generation.png` | 제작 예정; 증거 미확보 |
| `good-amber` | `web/public/assets/goods/amber.png` | SpriteCook (예정) | [good-amber](art-list.md#good-amber)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-amber/generation.png` | 제작 예정; 증거 미확보 |
| `good-pickled_vegetables` | `web/public/assets/goods/pickled_vegetables.png` | SpriteCook (예정) | [good-pickled_vegetables](art-list.md#good-pickled_vegetables)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-pickled_vegetables/generation.png` | 제작 예정; 증거 미확보 |
| `good-dried_beans` | `web/public/assets/goods/dried_beans.png` | SpriteCook (예정) | [good-dried_beans](art-list.md#good-dried_beans)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-dried_beans/generation.png` | 제작 예정; 증거 미확보 |
| `good-trade-crate` | `web/public/assets/goods/trade_crate.png` | SpriteCook (예정) | [good-trade-crate](art-list.md#good-trade-crate)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-trade-crate/generation.png` | 제작 예정; 증거 미확보 |
| `good-regional-specialty` | `web/public/assets/goods/regional_specialty.png` | SpriteCook (예정) | [good-regional-specialty](art-list.md#good-regional-specialty)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/good-regional-specialty/generation.png` | 제작 예정; 증거 미확보 |
| `music-menu` | `web/public/assets/audio/music/menu_loop.ogg` | ElevenLabs Music (예정) | [music-menu](sound-list.md#music-menu)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/music-menu/generation.png` | 제작 예정; 증거 미확보 |
| `music-game` | `web/public/assets/audio/music/game_loop.ogg` | ElevenLabs Music (예정) | [music-game](sound-list.md#music-game)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/music-game/generation.png` | 제작 예정; 증거 미확보 |
| `sfx-coin` | `web/public/assets/audio/sfx/coin.ogg` | ElevenLabs Sound Effects (예정) | [sfx-coin](sound-list.md#sfx-coin)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/sfx-coin/generation.png` | 제작 예정; 증거 미확보 |
| `sfx-button` | `web/public/assets/audio/sfx/button.ogg` | ElevenLabs Sound Effects (예정) | [sfx-button](sound-list.md#sfx-button)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/sfx-button/generation.png` | 제작 예정; 증거 미확보 |
| `sfx-event` | `web/public/assets/audio/sfx/event_alert.ogg` | ElevenLabs Sound Effects (예정) | [sfx-event](sound-list.md#sfx-event)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/sfx-event/generation.png` | 제작 예정; 증거 미확보 |
| `sfx-results` | `web/public/assets/audio/sfx/results_fanfare.ogg` | ElevenLabs Sound Effects (예정) | [sfx-results](sound-list.md#sfx-results)의 프롬프트; 생성 후 실제 편집 과정 추가 | — | `Docs/assets/evidence/sfx-results/generation.png` | 제작 예정; 증거 미확보 |
