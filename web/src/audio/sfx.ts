// Placeholder sounds made with Web Audio (the final audio arrives in Plan 7). Silent where the
// browser has no AudioContext or refuses to start one.
let ctx: AudioContext | null = null;

function audio(): AudioContext | null {
  if (ctx) return ctx;
  const Ctor = (globalThis as { AudioContext?: typeof AudioContext }).AudioContext;
  if (!Ctor) return null;
  try {
    ctx = new Ctor();
  } catch {
    ctx = null;
  }
  return ctx;
}

/** A short two-note chime, like a coin dropped on a counter. */
export function playCoin(): void {
  const a = audio();
  if (!a) return;
  try {
    void a.resume?.();
    const t = a.currentTime;
    for (const [i, freq] of [1318.5, 1975.5].entries()) {
      const osc = a.createOscillator();
      const gain = a.createGain();
      osc.type = 'triangle';
      osc.frequency.value = freq;
      const start = t + i * 0.07;
      gain.gain.setValueAtTime(0.0001, start);
      gain.gain.exponentialRampToValueAtTime(0.15, start + 0.01);
      gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.25);
      osc.connect(gain).connect(a.destination);
      osc.start(start);
      osc.stop(start + 0.3);
    }
  } catch {
    // A sound is never worth an error.
  }
}
