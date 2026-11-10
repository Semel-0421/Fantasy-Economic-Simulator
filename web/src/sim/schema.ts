// A tiny schema language: one description gives both the TypeScript type (`Infer`) and a runtime
// check (`validate`), so the types in types.ts are tested against the JSON the engine writes.
export type Schema =
  | { t: 'num' }
  | { t: 'bool' }
  | { t: 'str' }
  | { t: 'arr'; of: Schema }
  | { t: 'tup'; items: readonly Schema[] }
  | { t: 'obj'; fields: Record<string, Schema> }
  | { t: 'opt'; of: Schema }
  | { t: 'lit'; values: readonly string[] }
  | { t: 'tag'; cases: Record<string, Schema | null> };

export const num = { t: 'num' } as const;
export const bool = { t: 'bool' } as const;
export const str = { t: 'str' } as const;
export const arr = <const S extends Schema>(of: S) => ({ t: 'arr', of }) as const;
export const tup = <const S extends readonly Schema[]>(...items: S) => ({ t: 'tup', items }) as const;
export const obj = <const F extends Record<string, Schema>>(fields: F) => ({ t: 'obj', fields }) as const;
export const opt = <const S extends Schema>(of: S) => ({ t: 'opt', of }) as const;
export const lit = <const V extends readonly string[]>(...values: V) => ({ t: 'lit', values }) as const;
/** A serde externally tagged enum: a unit variant is its name, the others `{Name: body}`. */
export const tag = <const C extends Record<string, Schema | null>>(cases: C) => ({ t: 'tag', cases }) as const;

type Simplify<T> = { [K in keyof T]: T[K] } & unknown;

export type Infer<S> = S extends { t: 'num' }
  ? number
  : S extends { t: 'bool' }
    ? boolean
    : S extends { t: 'str' }
      ? string
      : S extends { t: 'arr'; of: infer O }
        ? Infer<O>[]
        : S extends { t: 'tup'; items: infer I }
          ? { -readonly [K in keyof I]: Infer<I[K]> }
          : S extends { t: 'obj'; fields: infer F }
            ? Simplify<{ -readonly [K in keyof F]: Infer<F[K]> }>
            : S extends { t: 'opt'; of: infer O }
              ? Infer<O> | null
              : S extends { t: 'lit'; values: readonly (infer V)[] }
                ? V
                : S extends { t: 'tag'; cases: infer C }
                  ? {
                      [K in keyof C]: C[K] extends null ? K : { [P in K]: Infer<C[K]> };
                    }[keyof C]
                  : never;

/** Returns a description of the first mismatch, or null when `value` fits the schema. */
export function validate(schema: Schema, value: unknown, path = '$'): string | null {
  const bad = (what: string): string => `${path}: expected ${what}, got ${JSON.stringify(value)}`;
  switch (schema.t) {
    case 'num':
      return typeof value === 'number' && Number.isFinite(value) ? null : bad('a number');
    case 'bool':
      return typeof value === 'boolean' ? null : bad('a boolean');
    case 'str':
      return typeof value === 'string' ? null : bad('a string');
    case 'opt':
      return value === null ? null : validate(schema.of, value, path);
    case 'lit':
      return typeof value === 'string' && schema.values.includes(value) ? null : bad(schema.values.join('|'));
    case 'arr':
      return validateArray(schema.of, value, path, bad);
    case 'tup':
      return validateTuple(schema.items, value, path, bad);
    case 'obj':
      return validateObject(schema.fields, value, path, bad);
    case 'tag':
      return validateTag(schema.cases, value, path, bad);
  }
}

function validateArray(of: Schema, value: unknown, path: string, bad: (what: string) => string): string | null {
  if (!Array.isArray(value)) return bad('an array');
  for (let i = 0; i < value.length; i++) {
    const e = validate(of, value[i], `${path}[${i}]`);
    if (e) return e;
  }
  return null;
}

function validateTuple(items: readonly unknown[], value: unknown, path: string, bad: (what: string) => string): string | null {
  if (!Array.isArray(value) || value.length !== items.length) {
    return bad(`a ${items.length}-tuple`);
  }
  for (let i = 0; i < items.length; i++) {
    const e = validate(items[i] as Schema, value[i], `${path}[${i}]`);
    if (e) return e;
  }
  return null;
}

function validateObject(
  fields: Record<string, Schema>,
  value: unknown,
  path: string,
  bad: (what: string) => string,
): string | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return bad('an object');
  const record = value as Record<string, unknown>;
  for (const key of Object.keys(record)) {
    if (!(key in fields)) return `${path}: unexpected field '${key}'`;
  }
  for (const [key, sub] of Object.entries(fields)) {
    if (!(key in record)) return `${path}: missing field '${key}'`;
    const e = validate(sub, record[key], `${path}.${key}`);
    if (e) return e;
  }
  return null;
}

function validateTag(
  cases: Record<string, Schema | null>,
  value: unknown,
  path: string,
  bad: (what: string) => string,
): string | null {
  if (typeof value === 'string') {
    return value in cases && cases[value] === null ? null : bad('a unit variant');
  }
  if (typeof value !== 'object' || value === null) return bad('a variant');
  const keys = Object.keys(value);
  const name = keys[0];
  if (keys.length !== 1 || name === undefined) return bad('a single-key variant');
  const body = cases[name];
  if (!body) return `${path}: unknown variant '${name}'`;
  return validate(body, (value as Record<string, unknown>)[name], `${path}.${name}`);
}

/** Every variant name of a tag schema. */
export function variantNames(schema: { cases: Record<string, unknown> }): string[] {
  return Object.keys(schema.cases);
}
