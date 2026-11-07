// Runs tools/devseed/New-LocalSecrets.ps1 the way a developer does (Windows PowerShell 5.1) against temp targets.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPublicKey, createPrivateKey } from 'node:crypto';
import { readFileSync, readdirSync, writeFileSync, existsSync, statSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runScript, makeTempDir } from '../../fork/tests/support/powershell.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, '..', '..', '..');
const SCRIPT = join(REPO_ROOT, 'tools', 'devseed', 'New-LocalSecrets.ps1');
const VECTORS = join(REPO_ROOT, 'packages', 'vectors');
const FILE_NAME = 'appsettings.Development.local.json';
const EXIT_OK = 0;
const EXIT_REFUSED = 1;
const EXIT_USAGE = 2;
const MIN_KEY_BYTES = 32;
const MIN_PASSWORD_LENGTH = 16;
const KEY_ID_PATTERN = /^[a-z0-9]{1,8}$/;
const API_SERVICE_NAME = 'starter-api';
const ATTACHMENTS_AUDIENCE = 'starter-attachments';
const API_AUDIENCE = 'starter-api';
const GATEWAY_AUDIENCE = 'integration-gateway';
const OPS_SERVICE_NAME = 'starter-ops-console';
const WEB_ORIGIN = 'http://localhost:4280';
const P256 = 'prime256v1';
const WARNING = /WARNING:/;
/** The PKCS#8 PEM header, built from parts so the secret scan does not read this file as holding a key. */
const PEM_DASHES = '-'.repeat(5);
const PKCS8_PEM_HEADER = new RegExp(`^${PEM_DASHES}BEGIN ${'PRIVATE'} KEY${PEM_DASHES}\\n`);

/** A temp layout under `base`: the API, service, gateway and Ops Console files (each in its own folder) and the store. */
function layout(base) {
  const folders = { api: 'api', service: 'service', gateway: 'gateway', ops: 'ops' };
  const paths = { store: join(base, 'store') };
  for (const [name, folder] of Object.entries(folders)) {
    mkdirSync(join(base, folder), { recursive: true });
    paths[name] = join(base, folder, FILE_NAME);
  }
  return paths;
}

function run(paths, extra = []) {
  const args = ['-Path', paths.api, '-ServicePath', paths.service, '-GatewayPath', paths.gateway, '-OpsPath', paths.ops, '-StorageRoot', paths.store];
  return runScript(SCRIPT, [...args, ...extra], { cwd: REPO_ROOT });
}

function read(target) {
  return JSON.parse(readFileSync(target, 'utf8'));
}

function withTemp(body) {
  const temp = makeTempDir();
  try {
    body(layout(temp.path), temp.path);
  } finally {
    temp.cleanup();
  }
}

function filesUnder(folder) {
  return readdirSync(folder).flatMap((name) => {
    const path = join(folder, name);
    return statSync(path).isDirectory() ? filesUnder(path) : [path];
  });
}

function assertKey(value, label) {
  assert.equal(typeof value, 'string', `${label} is a string`);
  assert.ok(Buffer.from(value, 'base64').length >= MIN_KEY_BYTES, `${label} decodes to at least ${MIN_KEY_BYTES} bytes`);
}

function assertRing(ring, label) {
  assert.match(ring.ActiveKeyId, KEY_ID_PATTERN, `${label} key id`);
  assert.equal(ring.Keys.length, 1, `${label} has one key`);
  assert.equal(ring.Keys[0].KeyId, ring.ActiveKeyId, `${label} active key is listed`);
  assertKey(ring.Keys[0].MaterialBase64, `${label} material`);
}

function spkiDer(key) {
  return createPublicKey(key).export({ type: 'spki', format: 'der' });
}

/** Every generated secret value of the API file, for comparisons. */
function secretsOf(doc) {
  const security = doc.Codelab.Security;
  return [
    doc.Identity.BreakGlass.Password,
    doc.Identity.DevUsers[0].Password,
    doc.Identity.Jwt.SigningKey,
    security.PublicIds.Keys[0].MaterialBase64,
    security.RequestSigning.Keys[0].MaterialBase64,
    security.Tickets.Keys[0].MaterialBase64,
    security.UploadTickets.Keys[0].MaterialBase64,
    security.Uploads.Keys[0].MaterialBase64,
    security.ServiceAssertions.Client.PrivateKeyPem,
    doc.Observability.UserIdHash.Key,
  ];
}

/** The key materials of a ring. */
function materialsOf(ring) {
  return (ring?.Keys ?? []).map((key) => key.MaterialBase64);
}

/** Asserts the service holds no key of the API's own ticket ring (SSO pending/exchange and activation tickets). */
function assertIsolated(api, service) {
  const apiOwn = new Set(materialsOf(api.Codelab.Security.Tickets));
  for (const material of [...materialsOf(service.Codelab.Security.Tickets), ...materialsOf(api.Codelab.Security.UploadTickets)]) {
    assert.ok(!apiOwn.has(material), "the service never gets a key of the API's own ticket ring");
  }
  assert.notEqual(service.Codelab.Security.Tickets.ActiveKeyId, api.Codelab.Security.Tickets.ActiveKeyId, 'a different ring');
}

/** Asserts the two files form one working pair: shared rings, the trusted client key and the service settings. */
function assertPair(api, service, store) {
  const apiSecurity = api.Codelab.Security;
  const serviceSecurity = service.Codelab.Security;
  assert.deepEqual(serviceSecurity.Tickets, apiSecurity.UploadTickets, 'one upload-ticket ring: the API seals, the service opens');
  assertIsolated(api, service);
  assert.deepEqual(serviceSecurity.Uploads, apiSecurity.Uploads, 'one receipt ring');
  const client = apiSecurity.ServiceAssertions.Client;
  const server = serviceSecurity.ServiceAssertions.Server;
  assert.equal(client.ServiceName, API_SERVICE_NAME);
  assert.equal(client.Audience, ATTACHMENTS_AUDIENCE);
  assert.match(client.KeyId, KEY_ID_PATTERN);
  assert.equal(server.Audience, ATTACHMENTS_AUDIENCE);
  assert.equal(server.TrustedServices.length, 1);
  const trusted = server.TrustedServices[0];
  assert.equal(trusted.ServiceName, API_SERVICE_NAME);
  assert.equal(trusted.KeyId, client.KeyId);
  assert.match(client.PrivateKeyPem, PKCS8_PEM_HEADER, 'PKCS#8 PEM');
  const privateKey = createPrivateKey(client.PrivateKeyPem);
  assert.equal(privateKey.asymmetricKeyDetails.namedCurve, P256, 'the client key is ES256 (P-256)');
  assert.deepEqual(spkiDer(trusted.PublicKeyPem), spkiDer(privateKey), "the trusted public key is the client key's public half");
  if (store) assert.equal(service.Attachments.Storage.RootPath, store);
  assert.deepEqual(service.Attachments.Cors.AllowedOrigins, [WEB_ORIGIN]);
}

/** The Ops Console API's private key PEM. */
function opsPrivateKey(ops) {
  return ops.Codelab.Security.ServiceAssertions.Client.PrivateKeyPem;
}

/** Asserts `server` trusts exactly `callerName` with the public half of `privateKeyPem` under `keyId`. */
function assertTrusts(server, { audience, callerName, keyId, privateKeyPem }) {
  assert.equal(server.Audience, audience);
  assert.equal(server.TrustedServices.length, 1);
  const [trusted] = server.TrustedServices;
  assert.equal(trusted.ServiceName, callerName);
  assert.equal(trusted.KeyId, keyId);
  assert.deepEqual(spkiDer(trusted.PublicKeyPem), spkiDer(createPrivateKey(privateKeyPem)), 'the public half of the caller key');
}

/** Every service's S2S wiring of a fresh run: API -> attachments and gateway, Ops Console -> API. */
function assertServiceTrust(paths) {
  const api = read(paths.api);
  const apiClient = api.Codelab.Security.ServiceAssertions.Client;
  assertTrusts(read(paths.gateway).Codelab.Security.ServiceAssertions.Server, {
    audience: GATEWAY_AUDIENCE, callerName: API_SERVICE_NAME, keyId: apiClient.KeyId, privateKeyPem: apiClient.PrivateKeyPem,
  });
  const opsClient = read(paths.ops).Codelab.Security.ServiceAssertions.Client;
  assert.equal(opsClient.ServiceName, OPS_SERVICE_NAME);
  assert.equal(opsClient.Audience, API_AUDIENCE);
  assert.match(opsClient.KeyId, KEY_ID_PATTERN);
  assert.equal(createPrivateKey(opsClient.PrivateKeyPem).asymmetricKeyDetails.namedCurve, P256, 'the Ops key is ES256 (P-256)');
  assertTrusts(api.Codelab.Security.ServiceAssertions.Server, {
    audience: API_AUDIENCE, callerName: OPS_SERVICE_NAME, keyId: opsClient.KeyId, privateKeyPem: opsClient.PrivateKeyPem,
  });
  assert.equal(api.Codelab.Ops.TrustedCallerKeyId, opsClient.KeyId, 'the Ops surface trusts that key id');
  assert.notEqual(opsClient.PrivateKeyPem, apiClient.PrivateKeyPem, 'each caller has its own key pair');
}

test('writes both files in the contract shape with strong values and prints the break-glass user name only', () => {
  withTemp((paths) => {
    const result = run(paths);
    assert.equal(result.code, EXIT_OK, result.output);
    assert.doesNotMatch(result.output, WARNING, 'a fresh pair is consistent');
    const doc = read(paths.api);
    assert.equal(typeof doc.Identity.BreakGlass.UserName, 'string');
    assert.ok(doc.Identity.BreakGlass.Password.length >= MIN_PASSWORD_LENGTH);
    assert.equal(typeof doc.Identity.DevUsers[0].UserName, 'string');
    assert.ok(doc.Identity.DevUsers[0].Password.length >= MIN_PASSWORD_LENGTH);
    assertKey(doc.Identity.Jwt.SigningKey, 'session signing key');
    assertRing(doc.Codelab.Security.PublicIds, 'PublicIds');
    assertRing(doc.Codelab.Security.RequestSigning, 'RequestSigning');
    assertRing(doc.Codelab.Security.Tickets, 'Tickets');
    assertRing(doc.Codelab.Security.UploadTickets, 'UploadTickets');
    assert.equal(doc.Codelab.Security.UploadTickets.Enabled, true, 'upload tickets are enabled locally');
    assertRing(doc.Codelab.Security.Uploads, 'Uploads');
    assert.equal(doc.Codelab.Security.Tickets.Enabled, true, 'sealed tickets are enabled locally');
    assert.equal(doc.Codelab.Security.Uploads.ReceiptsEnabled, true, 'upload receipts are enabled locally');
    assertKey(doc.Observability.UserIdHash.Key, 'user hash key');
    assertPair(doc, read(paths.service), paths.store);
    assert.ok(existsSync(paths.store), 'the attachment store is created');
    assert.ok(result.output.includes(doc.Identity.BreakGlass.UserName), 'prints the break-glass user name');
    for (const secret of secretsOf(doc)) assert.ok(!result.output.includes(secret), 'never prints a secret');
    assert.equal(new Set(secretsOf(doc)).size, secretsOf(doc).length, 'every value is distinct');
  });
});

test('two runs produce different values', () => {
  withTemp((_, base) => {
    const first = layout(join(base, 'first'));
    const second = layout(join(base, 'second'));
    assert.equal(run(first).code, EXIT_OK);
    assert.equal(run(second).code, EXIT_OK);
    const a = [...secretsOf(read(first.api)), opsPrivateKey(read(first.ops))];
    const b = [...secretsOf(read(second.api)), opsPrivateKey(read(second.ops))];
    a.forEach((value, index) => assert.notEqual(value, b[index], `value ${index} differs between runs`));
  });
});

test('patches an existing API file with only the missing keys and derives the service file from it', () => {
  withTemp((paths, base) => {
    const keptStore = join(base, 'kept-store');
    assert.equal(run(paths).code, EXIT_OK);
    const before = read(paths.api);
    delete before.Codelab.Security.Uploads;
    delete before.Codelab.Security.ServiceAssertions;
    before.Custom = { kept: true };
    writeFileSync(paths.api, JSON.stringify(before, null, 2));
    writeFileSync(paths.service, JSON.stringify({ Attachments: { Storage: { RootPath: keptStore } } }));
    writeFileSync(paths.gateway, '{}', 'utf8');

    const result = run(paths);

    assert.equal(result.code, EXIT_OK, result.output);
    assert.match(result.output, /Added to the API local secrets .*Codelab:Security:Uploads, Codelab:Security:ServiceAssertions/);
    const after = read(paths.api);
    assert.deepEqual(after.Identity, before.Identity, 'passwords and the session key are untouched');
    assert.deepEqual(after.Codelab.Security.PublicIds, before.Codelab.Security.PublicIds);
    assert.deepEqual(after.Codelab.Security.RequestSigning, before.Codelab.Security.RequestSigning);
    assert.deepEqual(after.Codelab.Security.Tickets, before.Codelab.Security.Tickets, 'the existing ticket ring is kept');
    assert.deepEqual(after.Observability, before.Observability);
    assert.deepEqual(after.Custom, { kept: true }, 'unknown settings survive');
    const service = read(paths.service);
    assert.equal(service.Attachments.Storage.RootPath, keptStore, 'an existing service value is untouched');
    assert.ok(!existsSync(keptStore), "a store the developer configured is theirs to create");
    assertPair(after, service, null);
    assert.doesNotMatch(result.output, WARNING);
    for (const secret of secretsOf(after)) assert.ok(!result.output.includes(secret), 'never prints a secret');
  });
});

test('a complete pair is left byte-identical', () => {
  withTemp((paths) => {
    assert.equal(run(paths).code, EXIT_OK);
    const api = readFileSync(paths.api, 'utf8');
    const service = readFileSync(paths.service, 'utf8');
    const again = run(paths);
    assert.equal(again.code, EXIT_OK, again.output);
    assert.match(again.output, /API local secrets are complete/);
    assert.match(again.output, /Attachment service local secrets are complete/);
    assert.equal(readFileSync(paths.api, 'utf8'), api);
    assert.equal(readFileSync(paths.service, 'utf8'), service);
  });
});

test('reports, but never repairs, a pair that disagrees; -Force regenerates both files', () => {
  withTemp((paths) => {
    assert.equal(run(paths).code, EXIT_OK);
    const service = read(paths.service);
    service.Codelab.Security.Tickets.Keys[0].MaterialBase64 = Buffer.alloc(MIN_KEY_BYTES, 1).toString('base64');
    writeFileSync(paths.service, JSON.stringify(service));
    const original = read(paths.api);

    const warned = run(paths);
    assert.equal(warned.code, EXIT_OK, warned.output);
    assert.match(warned.output, /Codelab:Security:UploadTickets \(API file\) differs from Codelab:Security:Tickets \(service file\)/);
    assert.deepEqual(read(paths.service), service, 'the service file is untouched');

    const forced = run(paths, ['-Force']);
    assert.equal(forced.code, EXIT_OK, forced.output);
    assert.doesNotMatch(forced.output, WARNING);
    const regenerated = read(paths.api);
    assert.notEqual(regenerated.Identity.BreakGlass.Password, original.Identity.BreakGlass.Password, 'every value is new');
    assertPair(regenerated, read(paths.service), paths.store);
  });
});

test("an older pair whose service holds the API's own ticket ring is reported, never repaired; -Force isolates it", () => {
  withTemp((paths) => {
    assert.equal(run(paths).code, EXIT_OK);
    const api = read(paths.api);
    delete api.Codelab.Security.UploadTickets;
    writeFileSync(paths.api, JSON.stringify(api, null, 2));
    const service = read(paths.service);
    service.Codelab.Security.Tickets = api.Codelab.Security.Tickets;
    writeFileSync(paths.service, JSON.stringify(service));

    const warned = run(paths);
    assert.equal(warned.code, EXIT_OK, warned.output);
    assert.match(warned.output, /Added to the API local secrets .*Codelab:Security:UploadTickets/);
    assert.match(warned.output, /holds a key of the API's own ticket ring/);
    assert.deepEqual(read(paths.service), service, 'the service file is untouched');
    assert.deepEqual(read(paths.api).Codelab.Security.Tickets, api.Codelab.Security.Tickets, "the API's own ring is kept");
    for (const material of materialsOf(api.Codelab.Security.Tickets)) assert.ok(!warned.output.includes(material), 'never prints a key');

    const forced = run(paths, ['-Force']);
    assert.equal(forced.code, EXIT_OK, forced.output);
    assert.doesNotMatch(forced.output, WARNING);
    assertPair(read(paths.api), read(paths.service), paths.store);
  });
});

test("reports an API file whose upload-ticket ring reuses a key of the API's own ticket ring", () => {
  withTemp((paths) => {
    assert.equal(run(paths).code, EXIT_OK);
    const api = read(paths.api);
    api.Codelab.Security.UploadTickets.Keys[0].MaterialBase64 = api.Codelab.Security.Tickets.Keys[0].MaterialBase64;
    writeFileSync(paths.api, JSON.stringify(api, null, 2));
    const service = read(paths.service);
    service.Codelab.Security.Tickets = api.Codelab.Security.UploadTickets;
    writeFileSync(paths.service, JSON.stringify(service));

    const warned = run(paths);
    assert.equal(warned.code, EXIT_OK, warned.output);
    assert.match(warned.output, /holds a key of the API's own ticket ring/);
  });
});

test('refuses to patch a file that is not a JSON object and leaves it untouched', () => {
  withTemp((paths) => {
    const original = 'not json';
    writeFileSync(paths.api, original);
    const refused = run(paths);
    assert.equal(refused.code, EXIT_REFUSED, refused.output);
    assert.equal(readFileSync(paths.api, 'utf8'), original, 'the file is untouched');
    assert.ok(!existsSync(paths.service), 'nothing else is written');
    const forced = run(paths, ['-Force']);
    assert.equal(forced.code, EXIT_OK, forced.output);
    assert.ok(read(paths.api).Identity, 'the file was regenerated');
  });
});

test('writes the API file alone when the service folder does not exist', () => {
  withTemp((paths) => {
    const missing = { ...paths, service: join(dirname(paths.service), 'absent', FILE_NAME) };
    const result = run(missing);
    assert.equal(result.code, EXIT_OK, result.output);
    assert.match(result.output, /Attachment service folder/);
    assert.ok(read(paths.api).Codelab.Security.ServiceAssertions.Client.PrivateKeyPem);
    assert.ok(!existsSync(paths.store), 'no store without a service');
  });
});

test('never writes a value that appears in the committed fixtures', () => {
  withTemp((paths) => {
    assert.equal(run(paths).code, EXIT_OK);
    assert.ok(existsSync(VECTORS), 'the vectors folder exists');
    const fixtures = filesUnder(VECTORS).map((path) => readFileSync(path, 'utf8')).join('\n');
    const publicKey = read(paths.service).Codelab.Security.ServiceAssertions.Server.TrustedServices[0].PublicKeyPem;
    for (const secret of [...secretsOf(read(paths.api)), publicKey]) assert.ok(!fixtures.includes(secret), 'value is not a fixture value');
  });
});

test('writes the gateway and Ops Console files: each caller has its own ES256 pair and every server trusts it', () => {
  withTemp((paths) => {
    const result = run(paths);
    assert.equal(result.code, EXIT_OK, result.output);
    assert.doesNotMatch(result.output, WARNING);
    assertServiceTrust(paths);
    assert.ok(!result.output.includes(opsPrivateKey(read(paths.ops))), 'never prints a key');
    const again = run(paths);
    assert.match(again.output, /integration gateway local secrets are complete/);
    assert.match(again.output, /Ops Console API local secrets are complete/);
  });
});

test('-SkipService leaves out removed services and the API trust for them', () => {
  withTemp((paths) => {
    const result = run(paths, ['-SkipService', 'attachments,sms,ops']);
    assert.equal(result.code, EXIT_OK, result.output);
    assert.ok(!existsSync(paths.service) && !existsSync(paths.ops) && !existsSync(paths.store), 'no file for a removed service');
    const api = read(paths.api);
    assert.equal(api.Codelab.Security.ServiceAssertions.Server, undefined, 'no Ops trust');
    assert.equal(api.Codelab.Ops, undefined, 'no Ops caller key id');
    assert.ok(existsSync(paths.gateway), 'the gateway still trusts the API');
    const noGateway = layout(join(dirname(dirname(paths.api)), 'no-gateway'));
    assert.equal(run(noGateway, ['-SkipService', 'gateway']).code, EXIT_OK);
    assert.ok(!existsSync(noGateway.gateway));
  });
});

test('an unknown -SkipService id is a usage error and writes nothing', () => {
  withTemp((paths) => {
    const result = run(paths, ['-SkipService', 'attachments,payments']);
    assert.equal(result.code, EXIT_USAGE, result.output);
    assert.match(result.output, /payments/);
    for (const file of [paths.api, paths.service, paths.gateway, paths.ops]) assert.ok(!existsSync(file));
  });
});

test('reports, but never repairs, an API file that trusts another Ops Console key', () => {
  withTemp((paths) => {
    assert.equal(run(paths).code, EXIT_OK);
    const api = readFileSync(paths.api, 'utf8');
    writeFileSync(paths.ops, '{}');
    const warned = run(paths);
    assert.equal(warned.code, EXIT_OK, warned.output);
    assert.match(warned.output, /does not trust the Ops Console API's client key/);
    assert.match(warned.output, /TrustedCallerKeyId/);
    assert.equal(readFileSync(paths.api, 'utf8'), api, 'the API file is untouched');
    const forced = run(paths, ['-Force']);
    assert.doesNotMatch(forced.output, WARNING);
    assertServiceTrust(paths);
  });
});
