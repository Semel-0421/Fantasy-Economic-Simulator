import { inflateSync, brotliDecompressSync } from 'node:zlib';

/** Only cmap is needed; WOFF2 glyph transformations do not change that table. */
export function fontCodepoints(bytes) {
  const buffer = Buffer.from(bytes);
  let cmap;
  if (buffer.toString('ascii', 0, 4) === 'wOFF') {
    for (let i = 0; i < buffer.readUInt16BE(12); i++) {
      const at = 44 + i * 20;
      if (buffer.toString('ascii', at, at + 4) !== 'cmap') continue;
      const data = buffer.subarray(buffer.readUInt32BE(at + 4), buffer.readUInt32BE(at + 4) + buffer.readUInt32BE(at + 8));
      cmap = data.length === buffer.readUInt32BE(at + 12) ? data : inflateSync(data);
    }
  } else if (buffer.toString('ascii', 0, 4) === 'wOF2') {
    let cursor = 48, offset = 0, cmapRange;
    const base128 = () => {
      let value = 0;
      for (let i = 0; i < 5; i++) {
        const byte = buffer[cursor++];
        if (byte === undefined || value > 0x1ffffff) throw new Error('Invalid WOFF2 directory');
        value = value * 128 + (byte & 127);
        if (!(byte & 128)) return value;
      }
      throw new Error('Invalid WOFF2 integer');
    };
    for (let i = 0; i < buffer.readUInt16BE(12); i++) {
      const flags = buffer[cursor++], tagIndex = flags & 63, version = flags >> 6;
      const tag = tagIndex === 63 ? buffer.toString('ascii', cursor, cursor += 4) : tagIndex === 0 ? 'cmap' : '';
      const original = base128();
      const transformed = tagIndex === 10 || tagIndex === 11 ? version !== 3 : version !== 0;
      const length = transformed ? base128() : original;
      if (tag === 'cmap') {
        if (transformed) throw new Error('Unexpected transformed cmap');
        cmapRange = [offset, offset + length];
      }
      offset += length;
    }
    if (cmapRange) cmap = brotliDecompressSync(buffer.subarray(cursor, cursor + buffer.readUInt32BE(20))).subarray(...cmapRange);
  } else {
    throw new Error('Expected WOFF or WOFF2 font');
  }
  if (!cmap) throw new Error('Font has no cmap');
  const points = new Set();
  for (let table = 0; table < cmap.readUInt16BE(2); table++) {
    const start = cmap.readUInt32BE(4 + table * 8 + 4), format = cmap.readUInt16BE(start);
    if (format === 12) {
      for (let group = 0; group < cmap.readUInt32BE(start + 12); group++) {
        const at = start + 16 + group * 12;
        for (let code = cmap.readUInt32BE(at); code <= cmap.readUInt32BE(at + 4); code++) {
          if (cmap.readUInt32BE(at + 8) + code - cmap.readUInt32BE(at) !== 0) points.add(code);
        }
      }
    } else if (format === 4) {
      const count = cmap.readUInt16BE(start + 6) / 2, end = start + 14, begins = end + count * 2 + 2, deltas = begins + count * 2, ranges = deltas + count * 2;
      for (let segment = 0; segment < count; segment++) {
        const rangeAt = ranges + segment * 2, range = cmap.readUInt16BE(rangeAt), delta = cmap.readInt16BE(deltas + segment * 2);
        for (let code = cmap.readUInt16BE(begins + segment * 2); code <= cmap.readUInt16BE(end + segment * 2) && code < 0xffff; code++) {
          const glyph = range ? cmap.readUInt16BE(rangeAt + range + 2 * (code - cmap.readUInt16BE(begins + segment * 2))) : (code + delta) & 0xffff;
          if (glyph && (!range || ((glyph + delta) & 0xffff))) points.add(code);
        }
      }
    }
  }
  if (!points.size) throw new Error('No supported Unicode cmap');
  return points;
}

export function missingGlyphs(text, points) {
  return [...new Set([...text.replace(/\{\w+\}/g, '')].filter(char => !/[\n\r\t]/.test(char) && !points.has(char.codePointAt(0))))];
}
