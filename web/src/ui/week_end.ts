// The end of a week after the report is shown: the growth presentation, the scenario's dialogs,
// then (after the last week) the results screen, which opens even if an earlier step failed.
export interface WeekEnd {
  /** The scenario's last week has been played. */
  ended: boolean;
  present(): Promise<void>;
  dialogs(): Promise<void>;
  results(): Promise<void>;
  onError(e: unknown): void;
}

export async function finishWeek(w: WeekEnd): Promise<void> {
  try {
    await w.present();
    await w.dialogs();
  } catch (e) {
    w.onError(e);
  }
  if (w.ended) await w.results();
}
