#!/usr/bin/env node
// The web app of a run-local slot (--slot n): serves a frozen development build on http://localhost:<port> with the
// Angular-route fallback of lib/static.mjs. run-local starts it detached and stops it with -Stop -Slot n.
//
// Usage: node tools/run-local/static-server.mjs --dir <browser build folder> --port <port>
// Exit codes: 2 usage.
import fs from 'node:fs';
import http from 'node:http';
import { HttpMethod, HttpStatus, LOOPBACK_HOSTS, NO_STORE_HEADERS, READ_METHODS, contentTypeFor, parseServerArguments, resolveStaticFile } from './lib/static.mjs';

const USAGE_EXIT = 2;
const isFile = (file) => fs.existsSync(file) && fs.statSync(file).isFile();

function handle(dir, request, response) {
  if (!READ_METHODS.includes(request.method)) {
    response.writeHead(HttpStatus.methodNotAllowed).end();
    return;
  }
  const file = resolveStaticFile(dir, request.url, isFile);
  if (!file) {
    response.writeHead(HttpStatus.notFound).end();
    return;
  }
  response.writeHead(HttpStatus.ok, { ...NO_STORE_HEADERS, 'Content-Type': contentTypeFor(file) });
  if (request.method === HttpMethod.head) {
    response.end();
    return;
  }
  fs.createReadStream(file).pipe(response);
}

const options = parseServerArguments(process.argv.slice(2));
if (!options) {
  console.error('Usage: node tools/run-local/static-server.mjs --dir <folder> --port <port>');
  process.exit(USAGE_EXIT);
}
for (const host of LOOPBACK_HOSTS) {
  const server = http.createServer((request, response) => handle(options.dir, request, response));
  server.on('error', (error) => console.error(`static-server: ${host}:${options.port} ${error.message}`));
  server.listen(options.port, host, () => console.log(`Serving ${options.dir} on http://${host.includes(':') ? `[${host}]` : host}:${options.port}`));
}
