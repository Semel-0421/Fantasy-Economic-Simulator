# Judged-build sound generation list

Plan only: two loops with ElevenLabs Music and four cues with ElevenLabs Sound Effects. Nothing has been generated or wired into playback. Paths are relative to `web/public/assets/`; paste the English prompts unchanged.

## Shared production specification

Ship Ogg Vorbis at 48 kHz: stereo music at target160 kbps, effects at target96 kbps with channels below. Preserve 48 kHz/24-bit PCM WAV masters outside Git and the public directory when large. Loudness values are project production targets, not ElevenLabs or contest requirements. Use integrated LUFS-I for music and sample peak dBFS for short cues; every export must stay at or below -1 dBTP. Prompt output is not a length/loudness guarantee; edit and measure offline before encoding.

## Deliverables and ready-to-paste prompts

<a id="music-menu"></a>

### music-menu: Menu music loop

Tool: ElevenLabs Music. File: `audio/music/menu_loop.ogg`. Length: 60 s. Format: Ogg Vorbis, 48 kHz, stereo, 160 kbps. Loudness target: -20 LUFS-I; <= -1 dBTP.

```text
Original instrumental menu music for a thoughtful medieval fantasy trading game. 80 BPM, 4/4, exactly 20 bars / 60 seconds. Warm lute, soft wooden flute and restrained plucked bass. Calm curiosity and a welcoming market morning; sparse arrangement, no vocals, lyrics, choir, cinematic impact, famous melody or artist imitation. Loop-ready: the final bar must lead naturally into the first bar at the same tempo and harmony; no intro, outro or fade. Leave headroom for interface sounds.
```

<a id="music-game"></a>

### music-game: In-game music loop

Tool: ElevenLabs Music. File: `audio/music/game_loop.ogg`. Length: 96 s. Format: Ogg Vorbis, 48 kHz, stereo, 160 kbps. Loudness target: -22 LUFS-I; <= -1 dBTP.

```text
Original unobtrusive instrumental loop for a medieval fantasy economic strategy game played for thirty minutes. 80 BPM, 4/4, exactly 32 bars / 96 seconds. Gentle lute ostinato, quiet recorder phrases and warm low strings, with space between phrases. Focused planning, steady trade and mild optimism; no battle tension, percussion hits, vocals, lyrics, artist imitation or recognizable melody. Loop-ready ending returns smoothly to the first bar with no intro, outro or fade; keep dynamics even and leave room for interface sounds.
```

<a id="sfx-coin"></a>

### sfx-coin: Coin confirmation

Tool: ElevenLabs Sound Effects. File: `audio/sfx/coin.ogg`. Length: 0.30 s. Format: Ogg Vorbis, 48 kHz, mono, 96 kbps. Loudness target: peak -10 dBFS; <= -1 dBTP.

```text
One original short sound effect: a single small metal coin lands softly on a wooden merchant counter with a clear gentle clink and a very short natural decay. Friendly purchase or payment confirmation, about 0.30 seconds. No pile of coins, speech, music, room ambience, harsh treble or reverb tail. Clean isolated foreground sound.
```

<a id="sfx-button"></a>

### sfx-button: Button confirmation

Tool: ElevenLabs Sound Effects. File: `audio/sfx/button.ogg`. Length: 0.08 s. Format: Ogg Vorbis, 48 kHz, mono, 96 kbps. Loudness target: peak -14 dBFS; <= -1 dBTP.

```text
One original soft dry wooden interface tick, about 0.08 seconds, for confirming a button in a calm strategy game. A small muted tap with a clear but gentle attack. No speech, music, ambience, metallic ringing, double click or long decay. Clean isolated sound, comfortable when repeated.
```

<a id="sfx-event"></a>

### sfx-event: Event alert

Tool: ElevenLabs Sound Effects. File: `audio/sfx/event_alert.ogg`. Length: 0.80 s. Format: Ogg Vorbis, 48 kHz, mono, 96 kbps. Loudness target: peak -8 dBFS; <= -1 dBTP.

```text
One original gentle two-note notification chime for new market news in a medieval fantasy trading strategy game, about 0.80 seconds. Soft wood-and-bell timbre, clear rising interval, calm attention rather than alarm. No voice, words, background music, ambience, startling attack or long reverb. Clean isolated sound distinct from a single coin clink.
```

<a id="sfx-results"></a>

### sfx-results: Results fanfare

Tool: ElevenLabs Sound Effects. File: `audio/sfx/results_fanfare.ogg`. Length: 3.0 s. Format: Ogg Vorbis, 48 kHz, stereo, 96 kbps. Loudness target: -18 LUFS-I; <= -1 dBTP.

```text
One original short instrumental result-screen fanfare for completing a medieval fantasy trading scenario, about 3 seconds. Three warm plucked-string chords and a soft recorder flourish, gently conclusive and suitable for either a modest or strong result. No triumphal brass blast, drums, vocals, words, famous tune, artist imitation or ambience. Clean start and fully decaying tail, not a loop.
```

## Finishing, acceptance and scope

Trim music on exact bar boundaries; match waveform and ambience at the seam. Audition the encoded OGG for ten repetitions in the actual player, checking clicks, silence and level jumps. Trim leading silence on effects, preserve natural decays, and apply short fades to cut tails. Check headphones and small speakers: coin, button and alert must be distinct and comfortable when repeated. Future playback needs user-gesture start, volume/mute and overlap limits; this task adds no playback code.

Retain text feedback so no sound is the only signal. The results cue is a neutral conclusion, suitable even when profit is low. No separate win/lose songs, voices, ambience or extra themes. Record generation UI, duration/waveform/loudness evidence and edits in [the asset log](asset-log.md).
