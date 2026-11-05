// Loopback relay for the Phase 3 Playwright driver (see ../playwright.md). The Playwright MCP code sandbox has no
// file system, so this process hands it two things over http://127.0.0.1:5399 only:
//   GET /        the two local dev passwords (breakglass, dev.viewer) read from the gitignored local settings file
//   GET /driver  the current text of p3-exit-scenarios.js, which the MCP call evaluates
// Nothing is written anywhere. It stops by itself after RELAY_LIFETIME_MS or when the process is stopped.
//
// node docs/evidence/P3/driver/secret-relay.mjs backend/src/Starter.Api/appsettings.Development.local.json
import { readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const RELAY_HOST = '127.0.0.1';
const RELAY_PORT = 5399;
const RELAY_LIFETIME_MS = 3 * 60 * 60 * 1000;
const DRIVER_PATH = '/driver';
const driverFile = join(dirname(fileURLToPath(import.meta.url)), 'p3-exit-scenarios.js');

const settingsFile = process.argv[2];
if (!settingsFile) {
  console.error('usage: node secret-relay.mjs <appsettings.Development.local.json>');
  process.exit(2);
}
const settings = JSON.parse(readFileSync(settingsFile, 'utf8'));
const secrets = JSON.stringify({
  breakglass: settings.Identity.BreakGlass.Password,
  viewer: settings.Identity.DevUsers[0].Password,
});

const server = createServer((request, response) => {
  if (request.url === DRIVER_PATH) {
    response.setHeader('content-type', 'text/javascript; charset=utf-8');
    response.end(readFileSync(driverFile, 'utf8'));
    return;
  }
  response.setHeader('content-type', 'application/json');
  response.end(secrets);
});
server.listen(RELAY_PORT, RELAY_HOST);
setTimeout(() => server.close(() => process.exit(0)), RELAY_LIFETIME_MS).unref();
