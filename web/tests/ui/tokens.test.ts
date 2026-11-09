import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const src = resolve('src');
const files = (dir: string): string[] => readdirSync(dir, { withFileTypes: true }).flatMap((entry) =>
  entry.isDirectory() ? files(resolve(dir, entry.name)) : [resolve(dir, entry.name)]);

describe('design tokens', () => {
  it('keeps raw colours in the token and Pixi colour modules', () => {
    for (const file of files(src).filter((file) => /\.ts$|[/\\]ui[/\\][^/\\]+\.css$/.test(file))) {
      if (/[/\\](tokens\.css|colours\.ts)$/.test(file)) continue;
      expect(readFileSync(file, 'utf8'), file).not.toMatch(/#[\da-f]{3,8}\b/i);
    }
  });

  it('meets WCAG contrast for semantic pairs', () => {
    const css = readFileSync(resolve(src, 'ui/tokens.css'), 'utf8');
    const tokens = new Map([...css.matchAll(/(--[\w-]+):\s*([^;]+);/g)].map((match) => [match[1]!, match[2]!.trim()]));
    const colour = (key: string): string => {
      const value = tokens.get(key)!;
      return value.startsWith('var(') ? colour(value.slice(4, -1)) : value;
    };
    const luminance = (key: string): number => {
      const channels = colour(key).slice(1).match(/../g)!.map((hex) => {
        const v = parseInt(hex, 16) / 255;
        return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
      });
      return channels[0]! * 0.2126 + channels[1]! * 0.7152 + channels[2]! * 0.0722;
    };
    const pairs: [string, string, number][] = [
      ['--text-on-card', '--surface-card', 4.5],
      ['--text-on-card', '--surface-card-muted', 4.5],
      ['--text-on-frame', '--surface-frame', 4.5],
      ['--accent-coin', '--surface-frame', 4.5],
      ['--text-on-card', '--accent-coin', 4.5],
      ['--danger', '--surface-card', 4.5],
      ['--text-on-primary', '--action-primary', 3],
      ['--focus', '--surface-card', 3],
      ['--focus-on-frame', '--surface-frame', 3],
      ['--border-card', '--surface-card', 3],
    ];
    for (const [text, background, minimum] of pairs) {
      const a = luminance(text), b = luminance(background);
      expect((Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05), `${text} on ${background}`).toBeGreaterThanOrEqual(minimum);
    }
  });
});
