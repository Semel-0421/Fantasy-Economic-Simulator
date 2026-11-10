// PNG chunks, scanline filters and Adam7: https://www.w3.org/TR/png/
import { inflateSync } from 'node:zlib';
import { crc32 } from './checksums.mjs';

const fail = (message) => { throw new Error(message); };
const paeth = (a, b, c) => {
  const p = a + b - c, da = Math.abs(p - a), db = Math.abs(p - b), dc = Math.abs(p - c);
  return da <= db && da <= dc ? a : db <= dc ? b : c;
};
function acceptOtherChunk(state, type, length) {
  if (state.dataStarted) state.dataEnded = true;
  if (type === 'IEND') { if (length) fail('Invalid PNG IEND'); state.ended = true; return; }
  checkAncillaryChunk(type);
}

function acceptChunk(state, type, chunk, length) {
  if (!state.header && type !== 'IHDR') fail('PNG must begin with IHDR');
  if (type === 'IHDR') {
    if (state.header || length !== 13) fail('Invalid PNG IHDR');
    state.header = chunk;
  } else if (type === 'IDAT') {
    if (state.dataEnded) fail('PNG IDAT chunks must be consecutive');
    state.dataStarted = true; state.compressed.push(chunk);
  } else acceptOtherChunk(state, type, length);
}

function readChunks(bytes) {
  let offset = 8;
  const state = { header: undefined, ended: false, dataStarted: false, dataEnded: false, compressed: [] };
  while (offset < bytes.length) {
    if (offset + 12 > bytes.length) fail('Truncated PNG chunk');
    const length = bytes.readUInt32BE(offset), type = bytes.toString('ascii', offset + 4, offset + 8), end = offset + 12 + length;
    if (end > bytes.length) fail('Truncated PNG chunk');
    if (crc32(bytes.subarray(offset + 4, end - 4)) !== bytes.readUInt32BE(end - 4)) fail(`PNG CRC mismatch (${type})`);
    acceptChunk(state, type, bytes.subarray(offset + 8, end - 4), length);
    offset = end;
    if (state.ended) break;
  }
  if (!state.header || !state.ended || !state.compressed.length || offset !== bytes.length) fail('Incomplete PNG or trailing bytes');
  return { header: state.header, compressed: state.compressed };
}

function checkAncillaryChunk(type) {
  if (type === 'acTL') fail('Static PNG required');
  if (type !== 'PLTE' && /^[A-Z]/.test(type)) fail(`Unsupported PNG critical chunk ${type}`);
}

function readHeader(header, size) {
  const width = header.readUInt32BE(0), height = header.readUInt32BE(4), depth = header[8], colour = header[9], interlace = header[12];
  if (`${width}x${height}` !== size) fail(`PNG size ${width}x${height}, expected ${size}`);
  if (colour !== 6 || ![8,16].includes(depth)) fail('PNG must be RGBA (colour type 6, 8 or 16 bits)');
  if (header[10] || header[11] || interlace > 1) fail('Unsupported PNG encoding');
  return { width, height, depth, interlace };
}

function unfilterRow(raw, position, previous, pixelBytes) {
  const filter = raw[position++], decoded = Buffer.alloc(previous.length);
  if (filter > 4) fail('Invalid PNG scanline filter');
  for (let i = 0; i < decoded.length; i++) {
    const left = i >= pixelBytes ? decoded[i - pixelBytes] : 0, up = previous[i], upperLeft = i >= pixelBytes ? previous[i - pixelBytes] : 0;
    const predictor = [0, left, up, Math.floor((left + up) / 2), paeth(left, up, upperLeft)][filter];
    decoded[i] = raw[position++] + predictor & 255;
  }
  return { decoded, position };
}

function passLayout({ width, height, interlace }, pixelBytes) {
  const passes = interlace ? [[0,0,8,8],[4,0,8,8],[0,4,4,8],[2,0,4,4],[0,2,2,4],[1,0,2,2],[0,1,1,2]] : [[0,0,1,1]];
  const dimensions = passes.map(([x,y,dx,dy]) => [Math.max(0, Math.ceil((width-x)/dx)), Math.max(0, Math.ceil((height-y)/dy))]);
  const expected = dimensions.reduce((sum, [w,h]) => sum + (w && h ? (w * pixelBytes + 1) * h : 0), 0);
  return { passes, dimensions, expected };
}

function decodeAlpha(raw, { width, height, depth }, { passes, dimensions }, pixelBytes) {
  const sampleBytes = depth / 8;
  const alpha = new Uint16Array(width * height);
  let position = 0;
  passes.forEach(([x,y,dx,dy], pass) => {
    const [w,h] = dimensions[pass];
    if (!w || !h) return;
    let previous = Buffer.alloc(w * pixelBytes);
    for (let row = 0; row < h; row++) {
      const unfiltered = unfilterRow(raw, position, previous, pixelBytes), decoded = unfiltered.decoded;
      position = unfiltered.position;
      for (let col = 0; col < w; col++) alpha[(y + row * dy) * width + x + col * dx] = sampleBytes === 1 ? decoded[col * pixelBytes + 3] : decoded.readUInt16BE(col * pixelBytes + 6);
      previous = decoded;
    }
  });
  return alpha;
}

function hasOpaqueNeighbour(alpha, width, height, x, y, opaque) {
  let adjacent = false;
  for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) if (x+dx >= 0 && x+dx < width && y+dy >= 0 && y+dy < height && alpha[(y+dy)*width+x+dx] === opaque) adjacent = true;
  return adjacent;
}

function checkAlpha(alpha, { width, height, depth }, hex) {
  const opaque = depth === 8 ? 255 : 65535;
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const value = alpha[y * width + x];
    if (hex && value && Math.abs(x + 0.5 - 14) > Math.min(14, (16 - Math.abs(y + 0.5 - 16)) * 1.75)) fail(`PNG pixel outside hex at ${x},${y}`);
    if (!value || value === opaque) continue;
    if (!hasOpaqueNeighbour(alpha, width, height, x, y, opaque)) fail(`PNG semi-transparent halo exceeds 1 px at ${x},${y}`);
  }
}

export function checkPng(bytes, size, hex = false) {
  if (!bytes.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10]))) fail('Invalid PNG signature');
  const { header, compressed } = readChunks(bytes);
  const info = readHeader(header, size), pixelBytes = 4 * (info.depth / 8), layout = passLayout(info, pixelBytes);
  const raw = inflateSync(Buffer.concat(compressed), { maxOutputLength: layout.expected });
  if (raw.length !== layout.expected) fail('Invalid PNG scanline length');
  checkAlpha(decodeAlpha(raw, info, layout, pixelBytes), info, hex);
  return { width: info.width, height: info.height, depth: info.depth };
}
