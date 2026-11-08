// Scaffold page: starts the simulation Worker and plays turns, to prove the whole path works.
import { SimBridge } from './sim/bridge';

const $ = (id: string): HTMLElement => {
  const el = document.getElementById(id);
  if (!el) throw new Error(`Missing #${id}`);
  return el;
};

async function main(): Promise<void> {
  const bridge = await SimBridge.start(1);
  const next = $('next') as HTMLButtonElement;
  const refresh = async (events: number | null): Promise<void> => {
    $('turn').textContent = String(await bridge.turn());
    $('hash').textContent = await bridge.worldHash();
    $('events').textContent = events === null ? '-' : String(events);
  };
  await refresh(null);
  $('status').textContent = 'Ready.';
  next.disabled = false;
  next.addEventListener('click', () => {
    next.disabled = true;
    bridge
      .next()
      .then((report) => refresh(report.events.length))
      .catch((e: unknown) => {
        $('status').textContent = `Error: ${e instanceof Error ? e.message : String(e)}`;
      })
      .finally(() => {
        next.disabled = false;
      });
  });
}

main().catch((e: unknown) => {
  $('status').textContent = `Could not start: ${e instanceof Error ? e.message : String(e)}`;
});
