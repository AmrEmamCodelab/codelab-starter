// @vitest-environment node
import { crc32 } from 'node:zlib';
import { describe, expect, it } from 'vitest';
import { buildXlsx, cellReference, escapeXml, sheetXml, storedZip, workbookParts } from '../lib/xlsx.mjs';

/** Reads a stored zip back: [{ name, data }] from the central directory (the reader the backend uses is .NET's). */
function readStoredZip(buffer) {
  const endOffset = buffer.length - 22;
  expect(buffer.readUInt32LE(endOffset)).toBe(0x06054b50);
  const count = buffer.readUInt16LE(endOffset + 10);
  let offset = buffer.readUInt32LE(endOffset + 16);
  const entries = [];
  for (let index = 0; index < count; index += 1) {
    expect(buffer.readUInt32LE(offset)).toBe(0x02014b50);
    const crc = buffer.readUInt32LE(offset + 16);
    const size = buffer.readUInt32LE(offset + 20);
    const nameLength = buffer.readUInt16LE(offset + 28);
    const localOffset = buffer.readUInt32LE(offset + 42);
    const name = buffer.subarray(offset + 46, offset + 46 + nameLength).toString('utf8');
    expect(buffer.readUInt32LE(localOffset)).toBe(0x04034b50);
    const dataStart = localOffset + 30 + buffer.readUInt16LE(localOffset + 26);
    const data = buffer.subarray(dataStart, dataStart + size);
    expect(crc32(data)).toBe(crc);
    entries.push({ name, data });
    offset += 46 + nameLength;
  }
  return entries;
}

describe('escapeXml and cellReference', () => {
  it('escapes the five XML specials', () => {
    expect(escapeXml(`a&b<c>"d'`)).toBe('a&amp;b&lt;c&gt;&quot;d&apos;');
  });

  it('maps 0-based column and row to A1 and refuses columns past Z', () => {
    expect(cellReference(0, 0)).toBe('A1');
    expect(cellReference(7, 1)).toBe('H2');
    expect(() => cellReference(26, 0)).toThrow(RangeError);
  });
});

describe('sheetXml', () => {
  it('writes every cell as an escaped inline string with its reference', () => {
    const xml = sheetXml([['UserName', 'Email'], ['e2e-1', 'a&b@x.test']]);
    expect(xml).toContain('<row r="1"><c r="A1" t="inlineStr"><is><t>UserName</t></is></c><c r="B1" t="inlineStr"><is><t>Email</t></is></c></row>');
    expect(xml).toContain('<c r="B2" t="inlineStr"><is><t>a&amp;b@x.test</t></is></c>');
  });
});

describe('storedZip and buildXlsx', () => {
  it('round-trips entries with valid CRCs and offsets', () => {
    const entries = [{ name: 'a.txt', data: Buffer.from('hello') }, { name: 'dir/b.xml', data: Buffer.from('<x/>') }];
    expect(readStoredZip(storedZip(entries)).map((entry) => [entry.name, entry.data.toString()])).toEqual([['a.txt', 'hello'], ['dir/b.xml', '<x/>']]);
  });

  it('packs the OPC parts the import reader resolves (rels -> workbook -> first sheet), no shared strings', () => {
    const rows = [['UserName'], ['e2e-1']];
    const names = readStoredZip(buildXlsx(rows)).map((entry) => entry.name);
    expect(names).toEqual(workbookParts(rows).map((part) => part.name));
    expect(names).toContain('xl/worksheets/sheet1.xml');
    expect(names.some((name) => name.includes('sharedStrings'))).toBe(false);
  });

  it('is deterministic for the same rows', () => {
    expect(buildXlsx([['a']]).equals(buildXlsx([['a']]))).toBe(true);
  });
});
