// Reads a source file as text: UTF-8 (with or without a BOM) or UTF-16 with a BOM.
import { readFileSync } from 'node:fs';

const UTF16_LE_BOM = [0xff, 0xfe];
const UTF16_BE_BOM = [0xfe, 0xff];
const UTF8 = 'utf-8';
const UTF16_LE = 'utf-16le';
const UTF16_BE = 'utf-16be';

export function decodeSource(bytes) {
  return new TextDecoder(encodingOf(bytes)).decode(bytes);
}

export function readSource(path) {
  return decodeSource(readFileSync(path));
}

function encodingOf(bytes) {
  if (startsWith(bytes, UTF16_LE_BOM)) {
    return UTF16_LE;
  }
  return startsWith(bytes, UTF16_BE_BOM) ? UTF16_BE : UTF8;
}

function startsWith(bytes, prefix) {
  return prefix.every((value, index) => bytes[index] === value);
}
