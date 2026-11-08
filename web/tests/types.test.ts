import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { validate, variantNames } from '../src/sim/schema';
import {
  playerCommandSchema,
  simEventSchema,
  turnReportSchema,
  viewSchemas,
  type PlayerCommand,
  type SimEventRecord,
} from '../src/sim/types';

const fixture = (name: string): unknown =>
  JSON.parse(readFileSync(new URL(`./fixtures/${name}.json`, import.meta.url), 'utf8'));

const nameOf = (v: unknown): string =>
  typeof v === 'string' ? v : (Object.keys(v as object)[0] as string);

describe('engine JSON fixtures fit the TypeScript types', () => {
  it('parses one sample of every PlayerCommand variant', () => {
    const commands = fixture('commands') as PlayerCommand[];
    for (const [i, c] of commands.entries()) {
      expect(validate(playerCommandSchema, c), `commands[${i}]`).toBeNull();
    }
    expect(commands.map(nameOf).sort()).toEqual(variantNames(playerCommandSchema).sort());
  });

  it('parses one sample of every SimEventRecord variant', () => {
    const events = fixture('events') as SimEventRecord[];
    for (const [i, e] of events.entries()) {
      expect(validate(simEventSchema, e), `events[${i}]`).toBeNull();
    }
    expect([...new Set(events.map(nameOf))].sort()).toEqual(variantNames(simEventSchema).sort());
  });

  it('parses a turn report and every view', () => {
    expect(validate(turnReportSchema, fixture('turn_report'))).toBeNull();
    for (const [name, schema] of Object.entries(viewSchemas)) {
      expect(validate(schema, fixture(name)), name).toBeNull();
    }
  });

  it('rejects shapes that do not fit', () => {
    expect(validate(playerCommandSchema, { BuyPlot: {} })).not.toBeNull();
    expect(validate(playerCommandSchema, { TakeLoan: { amount: '5' } })).not.toBeNull();
    expect(validate(playerCommandSchema, { TakeLoan: { amount: 5, extra: 1 } })).not.toBeNull();
    expect(validate(simEventSchema, { NoSuchEvent: {} })).not.toBeNull();
  });
});
