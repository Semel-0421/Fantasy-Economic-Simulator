// Ogg framing / Vorbis mapping: https://xiph.org/ogg/doc/framing.html and https://xiph.org/vorbis/doc/Vorbis_I_spec.html
import { oggCrc } from './checksums.mjs';
const fail = (message) => { throw new Error(message); };

export function checkOgg(bytes, expectedDuration) {
  let offset = 0, stream = null, duration = 0, streams = 0;
  while (offset < bytes.length) {
    if (offset + 27 > bytes.length) fail('Truncated Ogg header');
    if (bytes.toString('ascii', offset, offset+4) !== 'OggS' || bytes[offset+4] !== 0) fail('Invalid Ogg container');
    const flags = bytes[offset+5], count = bytes[offset+26], headerEnd = offset + 27 + count;
    if (headerEnd > bytes.length) fail('Truncated Ogg segment table');
    const laces = bytes.subarray(offset+27, headerEnd), length = [...laces].reduce((a,b) => a+b, 0), end = headerEnd + length;
    if (end > bytes.length) fail('Truncated Ogg page');
    const page = Buffer.from(bytes.subarray(offset, end)), checksum = page.readUInt32LE(22); page.fill(0,22,26);
    if (oggCrc(page) !== checksum) fail('Ogg CRC mismatch');
    const serial = bytes.readUInt32LE(offset+14), sequence = bytes.readUInt32LE(offset+18), granule = bytes.readBigInt64LE(offset+6);
    if (flags & ~7) fail('Invalid Ogg header flags');
    if (flags & 2) {
      if (stream || sequence !== 0 || flags & 1) fail('Invalid Ogg stream beginning');
      stream = { serial, sequence: 0, packets: 0, parts: [], last: 0n, rate: 0, audio: 0 }; streams++;
    }
    if (!stream || serial !== stream.serial || sequence !== stream.sequence++) fail('Invalid Ogg page sequence');
    if (Boolean(flags & 1) !== Boolean(stream.parts.length)) fail('Invalid Ogg continued packet');
    let position = headerEnd;
    for (const lace of laces) {
      stream.parts.push(bytes.subarray(position, position+lace)); position += lace;
      if (lace === 255) continue;
      const packet = Buffer.concat(stream.parts); stream.parts = [];
      if (stream.packets < 3) {
        if (packet[0] !== [1,3,5][stream.packets] || packet.toString('ascii',1,7) !== 'vorbis') fail('Ogg must contain Vorbis headers');
        if (stream.packets === 0) {
          if (packet.length !== 30 || packet.readUInt32LE(7) !== 0 || packet[11] === 0 || packet[29] !== 1) fail('Invalid Vorbis identification header');
          const small = packet[28] & 15, large = packet[28] >> 4;
          if (small < 6 || large > 13 || small > large) fail('Invalid Vorbis block sizes');
          stream.rate = packet.readUInt32LE(12);
          if (stream.rate !== 48000) fail(`Vorbis sample rate ${stream.rate}, expected 48000 Hz`);
        } else if (stream.packets === 1) {
          if (packet.length < 16) fail('Truncated Vorbis comment header');
          let cursor = 11 + packet.readUInt32LE(7);
          if (cursor+4 > packet.length) fail('Truncated Vorbis comment');
          const comments = packet.readUInt32LE(cursor); cursor += 4;
          for (let i = 0; i < comments; i++) {
            if (cursor+4 > packet.length) fail('Truncated Vorbis comment');
            cursor += 4 + packet.readUInt32LE(cursor);
            if (cursor > packet.length) fail('Truncated Vorbis comment');
          }
          if (packet[cursor] !== 1) fail('Invalid Vorbis comment framing bit');
        } else if (packet.length < 8) fail('Truncated Vorbis setup header');
      } else {
        if (!packet.length || packet[0] & 1) fail('Invalid Vorbis audio packet');
        stream.audio++;
      }
      stream.packets++;
    }
    if (flags & 2 && stream.packets !== 1) fail('Vorbis identification must be alone on the first page');
    if (granule >= 0) {
      if (granule < stream.last) fail('Ogg granule position went backwards');
      stream.last = granule;
    }
    if (flags & 4) {
      if (stream.parts.length || stream.packets < 4 || !stream.audio || granule <= 0 || granule > BigInt(Number.MAX_SAFE_INTEGER)) fail('Incomplete Ogg stream');
      duration += Number(granule) / stream.rate; stream = null;
    }
    offset = end;
  }
  if (!streams || stream) fail('Incomplete Ogg stream (missing end page)');
  if (Math.abs(duration - expectedDuration) > expectedDuration * 0.1 + 1e-9) fail(`Ogg duration ${duration.toFixed(3)} s, expected ${expectedDuration} s ±10%`);
  return { duration, sampleRate: 48000 };
}
