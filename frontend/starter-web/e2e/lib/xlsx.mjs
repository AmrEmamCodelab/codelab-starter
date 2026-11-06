// Builds the smallest valid .xlsx the user import accepts (pure: rows in, Buffer out): one worksheet of inline strings
// in a stored (uncompressed) zip, so the flow needs no spreadsheet package. The backend reader takes inline strings
// when the workbook has no shared strings part (Users/Infrastructure/Import/XlsxPackage.cs).
import { crc32 } from 'node:zlib';

const ZipSignatures = Object.freeze({ LocalFile: 0x04034b50, CentralDirectory: 0x02014b50, EndOfCentralDirectory: 0x06054b50 });
const ZIP_VERSION = 20;
const STORED = 0;
const UTF8_NAMES_FLAG = 0x0800;
/** 1980-01-01 00:00 in MS-DOS date/time: fixed, so the same rows give the same bytes. */
const DOS_DATE = 0x21;
const DOS_TIME = 0;
const LOCAL_HEADER_SIZE = 30;
const CENTRAL_HEADER_SIZE = 46;
const END_RECORD_SIZE = 22;
const COLUMN_LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
const XML_ESCAPES = Object.freeze({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' });
const XML_SPECIAL = /[&<>"']/g;

const XML_DECLARATION = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>';
const MAIN_NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
const REL_NS = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships';
const PKG_REL_NS = 'http://schemas.openxmlformats.org/package/2006/relationships';
const OFFICE_DOCUMENT_REL = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument';
const WORKSHEET_REL = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet';

/** Escapes text for an XML element or attribute. */
export function escapeXml(text) {
  return String(text).replace(XML_SPECIAL, (character) => XML_ESCAPES[character]);
}

/** The A1 reference of a 0-based column and row (columns A..Z: the import template has 8). */
export function cellReference(column, row) {
  if (column < 0 || column >= COLUMN_LETTERS.length) throw new RangeError(`Column ${column} is outside A..Z.`);
  return `${COLUMN_LETTERS[column]}${row + 1}`;
}

/** The worksheet XML of the rows (every cell an inline string). */
export function sheetXml(rows) {
  const body = rows
    .map((cells, rowIndex) => {
      const cellXml = cells.map((value, column) => `<c r="${cellReference(column, rowIndex)}" t="inlineStr"><is><t>${escapeXml(value)}</t></is></c>`).join('');
      return `<row r="${rowIndex + 1}">${cellXml}</row>`;
    })
    .join('');
  return `${XML_DECLARATION}<worksheet xmlns="${MAIN_NS}"><sheetData>${body}</sheetData></worksheet>`;
}

/** The parts of the package, in zip order: [{ name, text }]. */
export function workbookParts(rows) {
  return [
    {
      name: '[Content_Types].xml',
      text: `${XML_DECLARATION}<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>`,
    },
    { name: '_rels/.rels', text: `${XML_DECLARATION}<Relationships xmlns="${PKG_REL_NS}"><Relationship Id="rId1" Type="${OFFICE_DOCUMENT_REL}" Target="xl/workbook.xml"/></Relationships>` },
    { name: 'xl/workbook.xml', text: `${XML_DECLARATION}<workbook xmlns="${MAIN_NS}" xmlns:r="${REL_NS}"><sheets><sheet name="Users" sheetId="1" r:id="rId1"/></sheets></workbook>` },
    { name: 'xl/_rels/workbook.xml.rels', text: `${XML_DECLARATION}<Relationships xmlns="${PKG_REL_NS}"><Relationship Id="rId1" Type="${WORKSHEET_REL}" Target="worksheets/sheet1.xml"/></Relationships>` },
    { name: 'xl/worksheets/sheet1.xml', text: sheetXml(rows) },
  ];
}

function localHeader(name, data, crc) {
  const header = Buffer.alloc(LOCAL_HEADER_SIZE);
  header.writeUInt32LE(ZipSignatures.LocalFile, 0);
  header.writeUInt16LE(ZIP_VERSION, 4);
  header.writeUInt16LE(UTF8_NAMES_FLAG, 6);
  header.writeUInt16LE(STORED, 8);
  header.writeUInt16LE(DOS_TIME, 10);
  header.writeUInt16LE(DOS_DATE, 12);
  header.writeUInt32LE(crc, 14);
  header.writeUInt32LE(data.length, 18);
  header.writeUInt32LE(data.length, 22);
  header.writeUInt16LE(name.length, 26);
  header.writeUInt16LE(0, 28);
  return header;
}

function centralHeader(name, data, crc, offset) {
  const header = Buffer.alloc(CENTRAL_HEADER_SIZE);
  header.writeUInt32LE(ZipSignatures.CentralDirectory, 0);
  header.writeUInt16LE(ZIP_VERSION, 4);
  header.writeUInt16LE(ZIP_VERSION, 6);
  header.writeUInt16LE(UTF8_NAMES_FLAG, 8);
  header.writeUInt16LE(STORED, 10);
  header.writeUInt16LE(DOS_TIME, 12);
  header.writeUInt16LE(DOS_DATE, 14);
  header.writeUInt32LE(crc, 16);
  header.writeUInt32LE(data.length, 20);
  header.writeUInt32LE(data.length, 24);
  header.writeUInt16LE(name.length, 28);
  header.writeUInt32LE(offset, 42);
  return header;
}

/** A stored zip of the entries: [{ name, data (Buffer) }]. */
export function storedZip(entries) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const entry of entries) {
    const name = Buffer.from(entry.name, 'utf8');
    const crc = crc32(entry.data);
    const local = Buffer.concat([localHeader(name, entry.data, crc), name, entry.data]);
    centrals.push(Buffer.concat([centralHeader(name, entry.data, crc, offset), name]));
    locals.push(local);
    offset += local.length;
  }
  const central = Buffer.concat(centrals);
  const end = Buffer.alloc(END_RECORD_SIZE);
  end.writeUInt32LE(ZipSignatures.EndOfCentralDirectory, 0);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(central.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, central, end]);
}

/** The .xlsx bytes of the rows (the first row is the header row). */
export function buildXlsx(rows) {
  return storedZip(workbookParts(rows).map((part) => ({ name: part.name, data: Buffer.from(part.text, 'utf8') })));
}
