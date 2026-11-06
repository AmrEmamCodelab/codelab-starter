import assert from 'node:assert/strict';
import path from 'node:path';
import { describe, it } from 'node:test';
import { ATTACHMENTS_HEALTH_PATH, DEVELOPMENT, Hosts, PRIVATE_DOTNET, PRIVATE_NODE_DIR, SEQ_SERVER_URL_ENV, Urls } from '../lib/constants.mjs';
import {
  UsageError,
  apiAssemblyPath,
  apiEnvironment,
  attachmentsDecision,
  attachmentsEnvironment,
  breakGlassUser,
  parseArguments,
  parseState,
  pollUntilReady,
  portsToCheck,
  prependPath,
  resolveDotnet,
  resolveNodeDir,
  usesDefaultLocalDb,
  secretsNeedSeeding,
  seqDecision,
  seqUrl,
  serializeState,
  startPlan,
  taskMatches,
  webDecision,
  webEnvironment,
} from '../lib/plan.mjs';

describe('arguments', () => {
  it('parses the switches and the root', () => {
    assert.deepEqual(parseArguments(['--stop', '--no-web', '--no-seq', '--no-attachments', '--root', 'x'], 'R'), {
      stop: true, noWeb: true, noSeq: true, noAttachments: true, help: false, root: path.resolve('x'),
    });
    assert.equal(parseArguments([], 'R').root, 'R');
    assert.equal(parseArguments([], 'R').noAttachments, false, 'the Attachment service starts by default');
  });

  it('refuses unknown arguments', () => {
    assert.throws(() => parseArguments(['--start'], 'R'), UsageError);
    assert.throws(() => parseArguments(['--root'], 'R'), UsageError);
  });
});

describe('toolchain and environment', () => {
  it('resolves dotnet and node from the overrides, then the private toolchain', () => {
    assert.equal(resolveDotnet({ CODELAB_DOTNET: 'D' }, () => false), 'D');
    assert.equal(resolveDotnet({}, () => true), PRIVATE_DOTNET);
    assert.equal(resolveDotnet({}, () => false), 'dotnet');
    assert.equal(resolveNodeDir({ CODELAB_NODE_DIR: 'N' }, () => true), 'N');
    assert.equal(resolveNodeDir({}, () => true), PRIVATE_NODE_DIR);
    assert.equal(resolveNodeDir({}, () => false), null);
  });

  it('puts the private node first on PATH, keeping the PATH key casing', () => {
    assert.equal(prependPath('A', 'N'), `N${path.delimiter}A`);
    assert.equal(prependPath(undefined, 'N'), 'N');
    assert.equal(prependPath('A', null), 'A');
    assert.equal(webEnvironment({ Path: 'A' }, 'N').Path, `N${path.delimiter}A`);
  });

  it('runs the API in Development and passes the Seq URL only when one is used', () => {
    assert.equal(apiEnvironment({}, null).ASPNETCORE_ENVIRONMENT, DEVELOPMENT);
    assert.equal(apiEnvironment({}, null)[SEQ_SERVER_URL_ENV], undefined);
    assert.equal(apiEnvironment({}, 'http://seq')[SEQ_SERVER_URL_ENV], 'http://seq');
    assert.equal(seqUrl({}), Urls.seqDefault);
    assert.equal(seqUrl({ CODELAB_SEQ_URL: 'http://other:1' }), 'http://other:1');
  });
});

describe('API assembly', () => {
  it('picks the newest target framework folder that holds the assembly', () => {
    const exists = (file) => !file.includes('net11.0');
    assert.equal(apiAssemblyPath(['net9.0', 'net10.0', 'net11.0'], 'out', 'A.dll', exists), path.join('out', 'net10.0', 'A.dll'));
    assert.equal(apiAssemblyPath([], 'out', 'A.dll', () => true), null);
    assert.equal(apiEnvironment({}, null).ASPNETCORE_URLS, Urls.api);
  });
});

describe('decisions', () => {
  const manifest = { scripts: { start: 'ng serve' } };

  it('starts the web app only with a start script and installed dependencies', () => {
    assert.deepEqual(webDecision({ noWeb: false, manifest, hasNodeModules: true, startScript: 'start' }), { start: true });
    assert.match(webDecision({ noWeb: true, manifest, hasNodeModules: true, startScript: 'start' }).reason, /NoWeb/);
    assert.match(webDecision({ noWeb: false, manifest: null, hasNodeModules: true, startScript: 'start' }).reason, /does not exist/);
    assert.match(webDecision({ noWeb: false, manifest: { scripts: {} }, hasNodeModules: true, startScript: 'start' }).reason, /no "start"/);
    assert.match(webDecision({ noWeb: false, manifest, hasNodeModules: false, startScript: 'start' }).reason, /npm install/);
  });

  it('uses a reachable Seq, starts an installed one, else skips with a warning', () => {
    assert.equal(seqDecision({ noSeq: false, url: 'u', reachable: true, serviceInstalled: false }).action, 'use');
    assert.equal(seqDecision({ noSeq: false, url: 'u', reachable: false, serviceInstalled: true }).action, 'start-service');
    assert.match(seqDecision({ noSeq: false, url: 'u', reachable: false, serviceInstalled: false }).reason, /neither reachable/);
    assert.equal(seqDecision({ noSeq: true, url: 'u', reachable: true, serviceInstalled: true }).action, 'skip');
  });
});

describe('state, tasklist and secrets', () => {
  it('round-trips the state and drops malformed entries', () => {
    const processes = [{ name: 'api', pid: 10, image: 'dotnet.exe' }];
    assert.deepEqual(parseState(serializeState(processes, 'T')), { startedAt: 'T', processes });
    assert.deepEqual(parseState('{"processes":[{"pid":-1,"image":"x"},{"pid":3}]}').processes, []);
    assert.equal(parseState('not json'), null);
    assert.equal(parseState('{}'), null);
  });

  it('matches a PID only while it still runs the recorded image', () => {
    assert.ok(taskMatches('"dotnet.exe","1234","Console","1","50,000 K"', 'dotnet.exe'));
    assert.ok(!taskMatches('"notepad.exe","1234","Console","1","5 K"', 'dotnet.exe'));
    assert.ok(!taskMatches('INFO: No tasks are running which match the specified criteria.', 'dotnet.exe'));
  });

  it('reads the break-glass user name from the secrets file', () => {
    assert.equal(breakGlassUser('{"Identity":{"BreakGlass":{"UserName":"breakglass","Password":"x"}}}'), 'breakglass');
    assert.equal(breakGlassUser('{}'), null);
    assert.equal(breakGlassUser('nope'), null);
  });
});

describe('health polling', () => {
  function clock() {
    let time = 0;
    return { now: () => time, wait: async (ms) => { time += ms; } };
  }

  it('is ready as soon as the probe answers', async () => {
    let calls = 0;
    const result = await pollUntilReady({ ...clock(), probe: async () => (calls += 1) >= 3, exited: () => false, timeoutMs: 100, intervalMs: 10 });
    assert.equal(result, 'ready');
    assert.equal(calls, 3);
  });

  it('stops when the process exits or the deadline passes', async () => {
    assert.equal(await pollUntilReady({ ...clock(), probe: async () => false, exited: () => true, timeoutMs: 100, intervalMs: 10 }), 'exited');
    assert.equal(await pollUntilReady({ ...clock(), probe: async () => false, exited: () => false, timeoutMs: 100, intervalMs: 10 }), 'timeout');
  });
});

describe('usesDefaultLocalDb', () => {
  it('is true without an override or with one that names the default instance, false for another server', () => {
    assert.equal(usesDefaultLocalDb({}), true);
    assert.equal(usesDefaultLocalDb({ ConnectionStrings__Starter: 'Server=(localdb)\MSSQLLocalDB;Database=X' }), true);
    assert.equal(usesDefaultLocalDb({ ConnectionStrings__Starter: 'Server=(localdb)\P2Proof;Database=X' }), false);
  });
});

describe('the Attachment service', () => {
  it('runs on :5185 in Development and is health-checked on the readiness path', () => {
    assert.equal(Urls.attachments, 'http://localhost:5185');
    assert.equal(ATTACHMENTS_HEALTH_PATH, '/health/ready');
    assert.equal(`${Hosts.attachments.url}${Hosts.attachments.healthPath}`, 'http://localhost:5185/health/ready');
    assert.equal(Hosts.attachments.projectDir, 'services/attachment-service/src/Codelab.Attachments.Api');
    const env = attachmentsEnvironment({ KEEP: '1' }, 'http://seq');
    assert.equal(env.ASPNETCORE_ENVIRONMENT, DEVELOPMENT);
    assert.equal(env.ASPNETCORE_URLS, Urls.attachments);
    assert.equal(env[SEQ_SERVER_URL_ENV], 'http://seq');
    assert.equal(env.KEEP, '1');
    assert.equal(attachmentsEnvironment({}, null)[SEQ_SERVER_URL_ENV], undefined);
  });

  it('starts unless -NoAttachments is given or the project is absent', () => {
    assert.deepEqual(attachmentsDecision({ noAttachments: false, projectExists: true }), { start: true });
    assert.match(attachmentsDecision({ noAttachments: true, projectExists: true }).reason, /NoAttachments/);
    assert.match(attachmentsDecision({ noAttachments: false, projectExists: false }).reason, /does not exist/);
  });

  it('starts after the API and before the web app, and checks only the planned ports', () => {
    assert.deepEqual(startPlan({ attachments: true, web: true }), ['api', 'attachments', 'web']);
    assert.deepEqual(startPlan({ attachments: false, web: true }), ['api', 'web']);
    assert.deepEqual(startPlan({ attachments: true, web: false }), ['api', 'attachments']);
    assert.deepEqual(startPlan({ attachments: false, web: false }), ['api']);
    assert.deepEqual(portsToCheck(['api', 'attachments', 'web']), [Urls.api, Urls.attachments, Urls.web]);
    assert.deepEqual(portsToCheck(['api']), [Urls.api]);
  });
});

describe('secretsNeedSeeding', () => {
  const complete = JSON.stringify({
    Codelab: {
      Security: {
        Tickets: { ActiveKeyId: 'a' },
        UploadTickets: { ActiveKeyId: 'c' },
        Uploads: { ActiveKeyId: 'b' },
        ServiceAssertions: { Client: { PrivateKeyPem: 'p' } },
      },
    },
  });
  const beforeUploadTicketRing = JSON.stringify({
    Codelab: { Security: { Tickets: { ActiveKeyId: 'a' }, Uploads: { ActiveKeyId: 'b' }, ServiceAssertions: { Client: { PrivateKeyPem: 'p' } } } },
  });
  const beforeAttachments = JSON.stringify({ Codelab: { Security: { Tickets: { ActiveKeyId: 'a' } } } });

  it('seeds when the API file is missing', () => {
    assert.equal(secretsNeedSeeding({ apiSecretsText: null, serviceSecretsExists: true, withAttachments: false }), true);
  });

  it('never touches an existing API file when the Attachment service does not start', () => {
    assert.equal(secretsNeedSeeding({ apiSecretsText: beforeAttachments, serviceSecretsExists: false, withAttachments: false }), false);
  });

  it('seeds when the service file is missing or the API file lacks an attachment key', () => {
    assert.equal(secretsNeedSeeding({ apiSecretsText: complete, serviceSecretsExists: false, withAttachments: true }), true);
    assert.equal(secretsNeedSeeding({ apiSecretsText: beforeAttachments, serviceSecretsExists: true, withAttachments: true }), true);
    assert.equal(secretsNeedSeeding({ apiSecretsText: 'not json', serviceSecretsExists: true, withAttachments: true }), true);
    assert.equal(secretsNeedSeeding({ apiSecretsText: complete, serviceSecretsExists: true, withAttachments: true }), false);
  });

  it('seeds when the API file predates the dedicated upload-ticket ring', () => {
    assert.equal(secretsNeedSeeding({ apiSecretsText: beforeUploadTicketRing, serviceSecretsExists: true, withAttachments: true }), true);
  });
});
