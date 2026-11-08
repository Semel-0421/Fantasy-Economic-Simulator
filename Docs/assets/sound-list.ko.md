# 심사 빌드 소리 제작 목록

ElevenLabs Music으로 반복 음악 2개, ElevenLabs Sound Effects로 짧은 효과음 4개만 준비한다. 아래는 제작 계획이며 실제 생성·재생 연결은 아직 하지 않았다. 경로는 `web/public/assets/` 기준이고 프롬프트는 영어 그대로 붙여 넣는다.

## 공통 제작 규격

배포 파일은 모두 Ogg Vorbis, 48 kHz이며 음악은 스테레오 160 kbps, 효과음은 아래 채널 수로 96 kbps를 목표로 한다. 원본은 48 kHz/24-bit PCM WAV로 보관하되 큰 원본을 Git이나 배포 폴더에 넣지 않는다. 음량 값은 프로젝트의 제작 목표이며 ElevenLabs나 대회가 요구하는 수치가 아니다. 긴 음악은 통합 LUFS-I, 짧은 효과음은 샘플 피크 dBFS로 맞추고 모든 출력의 실제 피크는 -1 dBTP 이하여야 한다. 생성 프롬프트가 정확한 길이나 음량을 보장하지 않으므로 오프라인 편집·측정 후 인코딩한다.

## 개별 자산과 붙여 넣을 프롬프트

<a id="music-menu"></a>

### music-menu: 메뉴 음악 반복 구간

도구: ElevenLabs Music. 파일: `audio/music/menu_loop.ogg`. 길이: 60 s. 형식: Ogg Vorbis, 48 kHz, 스테레오, 160 kbps. 음량 목표: -20 LUFS-I; <= -1 dBTP.

```text
Original instrumental menu music for a thoughtful medieval fantasy trading game. 80 BPM, 4/4, exactly 20 bars / 60 seconds. Warm lute, soft wooden flute and restrained plucked bass. Calm curiosity and a welcoming market morning; sparse arrangement, no vocals, lyrics, choir, cinematic impact, famous melody or artist imitation. Loop-ready: the final bar must lead naturally into the first bar at the same tempo and harmony; no intro, outro or fade. Leave headroom for interface sounds.
```

<a id="music-game"></a>

### music-game: 플레이 중 음악 반복 구간

도구: ElevenLabs Music. 파일: `audio/music/game_loop.ogg`. 길이: 96 s. 형식: Ogg Vorbis, 48 kHz, 스테레오, 160 kbps. 음량 목표: -22 LUFS-I; <= -1 dBTP.

```text
Original unobtrusive instrumental loop for a medieval fantasy economic strategy game played for thirty minutes. 80 BPM, 4/4, exactly 32 bars / 96 seconds. Gentle lute ostinato, quiet recorder phrases and warm low strings, with space between phrases. Focused planning, steady trade and mild optimism; no battle tension, percussion hits, vocals, lyrics, artist imitation or recognizable melody. Loop-ready ending returns smoothly to the first bar with no intro, outro or fade; keep dynamics even and leave room for interface sounds.
```

<a id="sfx-coin"></a>

### sfx-coin: 동전 확인 효과음

도구: ElevenLabs Sound Effects. 파일: `audio/sfx/coin.ogg`. 길이: 0.30 s. 형식: Ogg Vorbis, 48 kHz, 모노, 96 kbps. 음량 목표: peak -10 dBFS; <= -1 dBTP.

```text
One original short sound effect: a single small metal coin lands softly on a wooden merchant counter with a clear gentle clink and a very short natural decay. Friendly purchase or payment confirmation, about 0.30 seconds. No pile of coins, speech, music, room ambience, harsh treble or reverb tail. Clean isolated foreground sound.
```

<a id="sfx-button"></a>

### sfx-button: 버튼 확인 효과음

도구: ElevenLabs Sound Effects. 파일: `audio/sfx/button.ogg`. 길이: 0.08 s. 형식: Ogg Vorbis, 48 kHz, 모노, 96 kbps. 음량 목표: peak -14 dBFS; <= -1 dBTP.

```text
One original soft dry wooden interface tick, about 0.08 seconds, for confirming a button in a calm strategy game. A small muted tap with a clear but gentle attack. No speech, music, ambience, metallic ringing, double click or long decay. Clean isolated sound, comfortable when repeated.
```

<a id="sfx-event"></a>

### sfx-event: 사건 알림 효과음

도구: ElevenLabs Sound Effects. 파일: `audio/sfx/event_alert.ogg`. 길이: 0.80 s. 형식: Ogg Vorbis, 48 kHz, 모노, 96 kbps. 음량 목표: peak -8 dBFS; <= -1 dBTP.

```text
One original gentle two-note notification chime for new market news in a medieval fantasy trading strategy game, about 0.80 seconds. Soft wood-and-bell timbre, clear rising interval, calm attention rather than alarm. No voice, words, background music, ambience, startling attack or long reverb. Clean isolated sound distinct from a single coin clink.
```

<a id="sfx-results"></a>

### sfx-results: 결과 화면 짧은 팡파르

도구: ElevenLabs Sound Effects. 파일: `audio/sfx/results_fanfare.ogg`. 길이: 3.0 s. 형식: Ogg Vorbis, 48 kHz, 스테레오, 96 kbps. 음량 목표: -18 LUFS-I; <= -1 dBTP.

```text
One original short instrumental result-screen fanfare for completing a medieval fantasy trading scenario, about 3 seconds. Three warm plucked-string chords and a soft recorder flourish, gently conclusive and suitable for either a modest or strong result. No triumphal brass blast, drums, vocals, words, famous tune, artist imitation or ambience. Clean start and fully decaying tail, not a loop.
```

## 마무리·검수·사용 범위

음악은 지정된 길이의 박자 경계로 자른 뒤 시작과 끝의 파형·잔향을 맞춘다. 인코딩한 OGG를 실제 플레이어에서 10회 반복해 클릭·무음 틈·튀는 음량이 없는지 듣는다. 효과음은 앞 무음을 자르고 끝이 자연스럽게 감쇠하게 하며 잘라 낸 끝에는 짧은 페이드를 쓴다. 헤드폰과 작은 스피커에서 코인·버튼·사건 알림이 구별되고 반복해도 날카롭지 않은지 확인한다. 최종 화면에서는 사용자가 오디오를 시작할 수 있게 하고 음량·음소거·중첩 제한을 적용해야 하지만 이번 작업에는 재생 코드가 없다.

효과음을 유일한 정보 전달 수단으로 쓰지 않고 텍스트 알림을 유지한다. 결과 팡파르는 실패에도 조롱처럼 들리지 않는 중립적 마침표로 쓴다. 별도의 승리·패배 곡, 음성, 환경음, 추가 테마는 심사 빌드 목록에 넣지 않는다. 생성 화면과 파형·길이·음량 측정 화면, 실제 편집 과정을 [자산 원장](asset-log.md)에 남긴다.
