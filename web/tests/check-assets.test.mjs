import { deflateSync } from 'node:zlib';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { checkPng } from '../scripts/lib/png.mjs';
import { checkOgg } from '../scripts/lib/ogg.mjs';
import { checkAssets } from '../scripts/lib/asset-check.mjs';

function crc(bytes, ogg = false) {
  let value = ogg ? 0 : 0xffffffff;
  for (const byte of bytes) {
    value ^= ogg ? byte << 24 : byte;
    for (let bit = 0; bit < 8; bit++) value = ogg ? (value << 1) ^ (value & 0x80000000 ? 0x04c11db7 : 0) : (value >>> 1) ^ (value & 1 ? 0xedb88320 : 0);
  }
  return (ogg ? value : value ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const body = Buffer.concat([Buffer.from(type), data]), out = Buffer.alloc(body.length + 8);
  out.writeUInt32BE(data.length); body.copy(out, 4); out.writeUInt32BE(crc(body), out.length - 4); return out;
}
function png(width = 24, type = 6, alpha = (x, y) => x >= 2 && x < width - 2 && y >= 2 && y < width - 2 ? 255 : 0, height = width) {
  const header = Buffer.alloc(13); header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = type;
  const pixels = Buffer.alloc(height * (width * 4 + 1));
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const index = y * (width * 4 + 1) + 1 + x * 4;
    pixels[index] = 63; pixels[index + 1] = 95; pixels[index + 2] = 67; pixels[index + 3] = alpha(x, y);
  }
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk('IHDR', header), chunk('IDAT', deflateSync(pixels)), chunk('IEND', Buffer.alloc(0))]);
}
function page(packet, sequence, flags, granule) {
  const laces = [...Array(Math.floor(packet.length / 255)).fill(255), packet.length % 255];
  const out = Buffer.alloc(27 + laces.length + packet.length); out.write('OggS'); out[5] = flags; out.writeBigInt64LE(BigInt(granule), 6);
  out.writeUInt32LE(7, 14); out.writeUInt32LE(sequence, 18); out[26] = laces.length; Buffer.from(laces).copy(out, 27); packet.copy(out, 27 + laces.length);
  out.writeUInt32LE(crc(out, true), 22); return out;
}
function ogg(rate = 48000, duration = 0.08) {
  const id = Buffer.alloc(30); id[0] = 1; id.write('vorbis', 1); id[11] = 1; id.writeUInt32LE(rate, 12); id[28] = 0xb8; id[29] = 1;
  const comment = Buffer.alloc(16); comment[0] = 3; comment.write('vorbis', 1); comment[15] = 1;
  return Buffer.concat([page(id, 0, 2, 0), page(comment, 1, 0, 0), page(Buffer.from([5,118,111,114,98,105,115,1]), 2, 0, 0), page(Buffer.from([0,0]), 3, 4, Math.round(rate * duration))]);
}
function variantPng(depth, interlace, filter) {
  const width = 24, height = 24, bpp = depth / 8 * 4, header = Buffer.alloc(13), rows = [];
  header.writeUInt32BE(width); header.writeUInt32BE(height, 4); header[8] = depth; header[9] = 6; header[12] = interlace;
  const passes = interlace ? [[0,0,8,8],[4,0,8,8],[0,4,4,8],[2,0,4,4],[0,2,2,4],[1,0,2,2],[0,1,1,2]] : [[0,0,1,1]];
  for (const [x,y,dx,dy] of passes) {
    const w = Math.ceil((width-x)/dx), h = Math.ceil((height-y)/dy);
    let previous = Buffer.alloc(w*bpp);
    for (let row = 0; row < h; row++) {
      const decoded = Buffer.alloc(w*bpp, 255), encoded = Buffer.alloc(w*bpp+1); encoded[0] = filter;
      for (let i = 0; i < decoded.length; i++) {
        const a = i >= bpp ? decoded[i-bpp] : 0, b = previous[i], c = i >= bpp ? previous[i-bpp] : 0, p = a+b-c;
        const choices = [a,b,c], distances = choices.map((value) => Math.abs(p-value)), paeth = choices[distances.indexOf(Math.min(...distances))];
        encoded[i+1] = decoded[i] - [0,a,b,Math.floor((a+b)/2),paeth][filter] & 255;
      }
      rows.push(encoded); previous = decoded;
    }
  }
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',header),chunk('IDAT',deflateSync(Buffer.concat(rows))),chunk('IEND',Buffer.alloc(0))]);
}

describe('delivered asset validation', () => {
  it('accepts both RGBA bit depths, Adam7 and all five legal PNG filters', () => {
    for (const depth of [8,16]) for (const interlace of [0,1]) for (const filter of [0,1,2,3,4]) expect(() => checkPng(variantPng(depth,interlace,filter),'24x24')).not.toThrow();
  });
  it('accepts exact RGBA PNGs and a one-pixel alpha edge; rejects size, colour, corruption and deep halos', () => {
    expect(() => checkPng(png(), '24x24')).not.toThrow();
    expect(() => checkPng(png(24, 6, (x, y) => x >= 3 && x < 21 && y >= 3 && y < 21 ? 255 : x >= 2 && x < 22 && y >= 2 && y < 22 ? 128 : 0), '24x24')).not.toThrow();
    expect(() => checkPng(png(23), '24x24')).toThrow(/size/i);
    expect(() => checkPng(png(24, 2), '24x24')).toThrow(/RGBA/i);
    expect(() => checkPng(png(24, 6, (x, y) => x >= 3 && x < 21 && y >= 3 && y < 21 ? 255 : x >= 1 && x < 23 && y >= 1 && y < 23 ? 128 : 0), '24x24')).toThrow(/halo/i);
    const corrupt = png(); corrupt[corrupt.length - 1] ^= 1;
    expect(() => checkPng(corrupt, '24x24')).toThrow(/CRC/i);
    expect(() => checkPng(png(28, 6, () => 255, 32), '28x32', true)).toThrow(/outside hex/i);
  });
  it('checks Vorbis identification, 48kHz, duration, CRC and complete framing', () => {
    expect(checkOgg(ogg(), 0.08).duration).toBeCloseTo(0.08);
    expect(() => checkOgg(ogg(44100), 0.08)).toThrow(/48000/);
    expect(() => checkOgg(ogg(48000, 0.2), 0.08)).toThrow(/duration/i);
    expect(() => checkOgg(ogg().subarray(0, -1), 0.08)).toThrow(/truncated/i);
    const corrupt = ogg(); corrupt[corrupt.length - 1] ^= 1;
    expect(() => checkOgg(corrupt, 0.08)).toThrow(/CRC/i);
  });
  it('allowlists paths, reports every error, bounds file size and prints stable sha256 evidence', () => {
    const root = mkdtempSync(join(tmpdir(), 'fes-assets-'));
    try {
      mkdirSync(join(root, 'goods')); writeFileSync(join(root, 'goods/wheat.png'), png());
      writeFileSync(join(root, 'README.md'), 'Contract'); writeFileSync(join(root, 'present.json'), '[]');
      const valid = checkAssets(root); expect(valid.errors).toEqual([]); expect(valid.files[0].sha256).toMatch(/^[a-f0-9]{64}$/);
      writeFileSync(join(root, 'unknown.png'), png()); writeFileSync(join(root, 'goods/barley.png'), Buffer.alloc(200001));
      const bad = checkAssets(root); expect(bad.errors).toHaveLength(2); expect(bad.errors.join('\n')).toMatch(/unknown/i); expect(bad.errors.join('\n')).toMatch(/200/);
    } finally { expect(root.startsWith(join(tmpdir(), 'fes-assets-'))).toBe(true); rmSync(root, { recursive: true, force: true }); }
  });
});
