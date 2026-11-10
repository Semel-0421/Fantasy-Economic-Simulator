import { inflateSync, brotliDecompressSync } from 'node:zlib';

function woffCmap(buffer) {
  let cmap;
  for (let i = 0; i < buffer.readUInt16BE(12); i++) {
    const at = 44 + i * 20;
    if (buffer.toString('ascii', at, at + 4) !== 'cmap') continue;
    const data = buffer.subarray(buffer.readUInt32BE(at + 4), buffer.readUInt32BE(at + 4) + buffer.readUInt32BE(at + 8));
    cmap = data.length === buffer.readUInt32BE(at + 12) ? data : inflateSync(data);
  }
  return cmap;
}

function woff2Directory(buffer) {
  let cursor = 48;
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
  const entry = () => {
    const flags = buffer[cursor++], tagIndex = flags & 63, version = flags >> 6;
    const tag = tagIndex === 63 ? buffer.toString('ascii', cursor, cursor += 4) : tagIndex === 0 ? 'cmap' : '';
    const original = base128();
    const transformed = tagIndex === 10 || tagIndex === 11 ? version !== 3 : version !== 0;
    const length = transformed ? base128() : original;
    return { tag, transformed, length };
  };
  let offset = 0, cmapRange;
  for (let i = 0; i < buffer.readUInt16BE(12); i++) {
    const { tag, transformed, length } = entry();
    if (tag === 'cmap') {
      if (transformed) throw new Error('Unexpected transformed cmap');
      cmapRange = [offset, offset + length];
    }
    offset += length;
  }
  return { cmapRange, dataStart: cursor };
}

function woff2Cmap(buffer) {
  const { cmapRange, dataStart } = woff2Directory(buffer);
  if (!cmapRange) return undefined;
  return brotliDecompressSync(buffer.subarray(dataStart, dataStart + buffer.readUInt32BE(20))).subarray(...cmapRange);
}

function readCmap(buffer) {
  const signature = buffer.toString('ascii', 0, 4);
  if (signature === 'wOFF') return woffCmap(buffer);
  if (signature === 'wOF2') return woff2Cmap(buffer);
  throw new Error('Expected WOFF or WOFF2 font');
}

function addFormat12(cmap, start, points) {
  for (let group = 0; group < cmap.readUInt32BE(start + 12); group++) {
    const at = start + 16 + group * 12;
    for (let code = cmap.readUInt32BE(at); code <= cmap.readUInt32BE(at + 4); code++) {
      if (cmap.readUInt32BE(at + 8) + code - cmap.readUInt32BE(at) !== 0) points.add(code);
    }
  }
}

function addFormat4Segment(cmap, layout, segment, points) {
  const { end, begins, deltas, ranges } = layout;
  const rangeAt = ranges + segment * 2, range = cmap.readUInt16BE(rangeAt), delta = cmap.readInt16BE(deltas + segment * 2);
  for (let code = cmap.readUInt16BE(begins + segment * 2); code <= cmap.readUInt16BE(end + segment * 2) && code < 0xffff; code++) {
    const glyph = range ? cmap.readUInt16BE(rangeAt + range + 2 * (code - cmap.readUInt16BE(begins + segment * 2))) : (code + delta) & 0xffff;
    if (glyph && (!range || ((glyph + delta) & 0xffff))) points.add(code);
  }
}

function addFormat4(cmap, start, points) {
  const count = cmap.readUInt16BE(start + 6) / 2, end = start + 14, begins = end + count * 2 + 2, deltas = begins + count * 2, ranges = deltas + count * 2;
  for (let segment = 0; segment < count; segment++) addFormat4Segment(cmap, { end, begins, deltas, ranges }, segment, points);
}

/** Only cmap is needed; WOFF2 glyph transformations do not change that table. */
export function fontCodepoints(bytes) {
  const cmap = readCmap(Buffer.from(bytes));
  if (!cmap) throw new Error('Font has no cmap');
  const points = new Set();
  for (let table = 0; table < cmap.readUInt16BE(2); table++) {
    const start = cmap.readUInt32BE(4 + table * 8 + 4), format = cmap.readUInt16BE(start);
    if (format === 12) addFormat12(cmap, start, points);
    else if (format === 4) addFormat4(cmap, start, points);
  }
  if (!points.size) throw new Error('No supported Unicode cmap');
  return points;
}

export function missingGlyphs(text, points) {
  return [...new Set([...text.replace(/\{\w+\}/g, '')].filter(char => !/[\n\r\t]/.test(char) && !points.has(char.codePointAt(0))))];
}
