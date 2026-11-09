import { audioContext, sounds } from '../assets/loader';

export type Sound = 'coin' | 'button' | 'event' | 'results';
let volume = 70;
let muted = false;
let musicOn = true;
let gesture = false;
let scene: 'menu' | 'game' = 'menu';
let master: GainNode | null = null;
let loop: AudioBufferSourceNode | null = null;
let loopId = '';

function output(): GainNode | null {
  const ctx = audioContext();
  if (!ctx) return null;
  if (!master) { master = ctx.createGain(); master.connect(ctx.destination); }
  master.gain.value = muted ? 0 : volume / 100;
  return master;
}

export function setVolume(value: number): void {
  volume = Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 70;
  if (master) master.gain.value = muted ? 0 : volume / 100;
  updateMusic();
}
export function toggleMute(): boolean {
  muted = !muted;
  setVolume(volume);
  updateMusic();
  return muted;
}
export const isMuted = (): boolean => muted;
export function setMusic(on: boolean): void { musicOn = on; updateMusic(); }
export function musicScene(next: 'menu' | 'game'): void { scene = next; updateMusic(); }

function updateMusic(): void {
  const id = `music-${scene}`;
  if (loop && (id !== loopId || !musicOn || muted || volume === 0)) { loop.stop(); loop = null; }
  if (loop || !gesture || !musicOn || muted || volume === 0 || !sounds.has(id)) return;
  try {
    const ctx = audioContext(), target = output();
    if (!ctx || !target) return;
    loop = ctx.createBufferSource();
    loop.buffer = sounds.get(id)!;
    loop.loop = true;
    loop.connect(target);
    loop.start();
    loopId = id;
  } catch { loop = null; }
}

/** Install once; no sound starts until a real player gesture. */
export function installSound(): void {
  const unlock = (): void => {
    gesture = true;
    void audioContext()?.resume().then(updateMusic).catch(() => {});
    document.removeEventListener('pointerdown', unlock);
    document.removeEventListener('keydown', unlock);
  };
  document.addEventListener('pointerdown', unlock);
  document.addEventListener('keydown', unlock);
  document.addEventListener('click', (event) => {
    if ((event.target as Element | null)?.closest('button')) play('button');
  });
}

/** Missing OGGs use a quiet synth cue, and missing music stays silent. */
export function play(id: Sound): void {
  if (volume === 0 || muted) return;
  try {
    const ctx = audioContext(), target = output();
    if (!ctx || !target) return;
    void ctx.resume().catch(() => {});
    const buffer = sounds.get(`sfx-${id}`);
    if (buffer) {
      const source = ctx.createBufferSource();
      source.buffer = buffer;
      source.connect(target);
      source.start();
      return;
    }
    const notes = id === 'coin' ? [1318.5, 1975.5] : id === 'results' ? [659, 831, 988] : id === 'event' ? [660, 880] : [440];
    for (const [i, freq] of notes.entries()) {
      const osc = ctx.createOscillator(), gain = ctx.createGain();
      const start = ctx.currentTime + i * 0.07;
      osc.type = 'triangle';
      osc.frequency.value = freq;
      gain.gain.setValueAtTime(0.0001, start);
      gain.gain.exponentialRampToValueAtTime(0.15, start + 0.01);
      gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.25);
      osc.connect(gain).connect(target);
      osc.start(start);
      osc.stop(start + 0.3);
    }
  } catch { /* Sound must never interrupt play. */ }
}
