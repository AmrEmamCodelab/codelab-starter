import assert from 'node:assert/strict';
import path from 'node:path';
import { describe, it } from 'node:test';
import { DEFAULT_CONTENT_TYPE, contentTypeFor, parseServerArguments, resolveStaticFile } from '../lib/static.mjs';

const ROOT = path.resolve('W:/web/browser');
const files = new Set([path.join(ROOT, 'index.html'), path.join(ROOT, 'config.json'), path.join(ROOT, 'main-ABC.js')]);
const isFile = (file) => files.has(file);

describe('slot web server', () => {
  it('serves an existing file and falls back to index.html for an app route', () => {
    assert.equal(resolveStaticFile(ROOT, '/config.json', isFile), path.join(ROOT, 'config.json'));
    assert.equal(resolveStaticFile(ROOT, '/main-ABC.js?v=1', isFile), path.join(ROOT, 'main-ABC.js'));
    assert.equal(resolveStaticFile(ROOT, '/', isFile), path.join(ROOT, 'index.html'));
    assert.equal(resolveStaticFile(ROOT, '/staff/service-requests', isFile), path.join(ROOT, 'index.html'));
  });

  it('answers 404 for a missing asset and never reads outside the folder', () => {
    assert.equal(resolveStaticFile(ROOT, '/favicon.ico', isFile), null);
    assert.equal(resolveStaticFile(ROOT, '/../../secrets.json', isFile), null);
    assert.equal(resolveStaticFile(ROOT, '/%2e%2e/%2e%2e/appsettings.json', isFile), null);
    assert.equal(resolveStaticFile(ROOT, '/%E0%A4%A', isFile), null, 'a malformed escape is refused');
  });

  it('picks the content type by extension', () => {
    assert.match(contentTypeFor('a/index.html'), /^text\/html/);
    assert.match(contentTypeFor('a/main.JS'), /^text\/javascript/);
    assert.equal(contentTypeFor('a/font.woff2'), 'font/woff2');
    assert.equal(contentTypeFor('a/blob.bin'), DEFAULT_CONTENT_TYPE);
  });

  it('needs a folder and a positive port', () => {
    assert.deepEqual(parseServerArguments(['--dir', 'D', '--port', '4380']), { dir: 'D', port: 4380 });
    assert.equal(parseServerArguments(['--dir', 'D']), null);
    assert.equal(parseServerArguments(['--port', '4380']), null);
    assert.equal(parseServerArguments(['--dir', 'D', '--port', 'x']), null);
  });
});
