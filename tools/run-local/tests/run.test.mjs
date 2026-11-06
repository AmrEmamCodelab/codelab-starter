import assert from 'node:assert/strict';
import path from 'node:path';
import { describe, it } from 'node:test';
import {
  API_ASSEMBLY,
  ATTACHMENTS_ASSEMBLY,
  ATTACHMENTS_PROJECT_FILE,
  ATTACHMENTS_SECRETS_FILE,
  ExitCode,
  LOCAL_SECRETS_FILE,
  PRIVATE_DOTNET,
  STATE_FILE,
  Urls,
} from '../lib/constants.mjs';
import { main, start, stop } from '../lib/run.mjs';

const ROOT = 'R:\\repo';
const STATE_DIR = 'T:\\state';
const STATE = path.join(STATE_DIR, STATE_FILE);
const SECRETS = path.join(ROOT, LOCAL_SECRETS_FILE);
const MANIFEST = path.join(ROOT, 'frontend/starter-web/package.json');
const NODE_MODULES = path.join(ROOT, 'node_modules');
const ATTACHMENTS_PROJECT = path.join(ROOT, ATTACHMENTS_PROJECT_FILE);
const SERVICE_SECRETS = path.join(ROOT, ATTACHMENTS_SECRETS_FILE);

/** A fake system: files in a map, commands answered by `commands`, URLs by `statuses`, a manual clock. */
function fakeSystem({ files = {}, statuses = {}, alive = new Map(), commands = {}, exitsEarly = false, builds = true } = {}) {
  const disk = new Map(Object.entries(files));
  const calls = [];
  let time = 0;
  let nextPid = 100;
  const system = {
    env: {},
    stateDir: STATE_DIR,
    lines: [],
    warnings: [],
    calls,
    disk,
    exists: (file) => disk.has(file) || file === PRIVATE_DOTNET || (builds && (file.endsWith(API_ASSEMBLY) || file.endsWith(ATTACHMENTS_ASSEMBLY))),
    listDirs: () => ['net9.0', 'net10.0'],
    readText: (file) => disk.get(file),
    writeText: (file, text) => disk.set(file, text),
    removeStateFile: (file) => disk.delete(file),
    runSync: (command, args) => {
      calls.push([command, ...args]);
      if (command === 'tasklist.exe') {
        const pid = Number(args[1].split(' ').pop());
        return alive.has(pid) ? { code: 0, output: `"${alive.get(pid)}","${pid}","Console","1","1 K"` } : { code: 0, output: 'INFO: none' };
      }
      if (command === 'taskkill.exe') {
        alive.delete(Number(args[1]));
        return { code: 0, output: '' };
      }
      const answer = commands[command];
      return answer ?? { code: 0, output: '' };
    },
    spawned: [],
    spawnDetached: (command, args, options) => {
      const pid = (nextPid += 1);
      alive.set(pid, command.toLowerCase().endsWith('cmd.exe') ? 'cmd.exe' : 'dotnet.exe');
      calls.push(['spawn', command, ...args]);
      system.spawned.push({ command, args, options });
      const early = typeof exitsEarly === 'function' ? exitsEarly(args) : exitsEarly;
      return { pid, exited: () => early };
    },
    probed: [],
    httpStatus: async (url) => {
      system.probed.push(url);
      return statuses[url]?.(time) ?? null;
    },
    now: () => time,
    wait: async (ms) => { time += ms; },
    log: (line) => system.lines.push(line),
    warn: (line) => system.warnings.push(line),
  };
  return system;
}

const upAfterStart = (time) => (time > 0 ? 200 : null);
const healthy = {
  [`${Urls.api}/health/ready`]: upAfterStart,
  [`${Urls.attachments}/health/ready`]: upAfterStart,
  [`${Urls.web}/`]: upAfterStart,
};
/** Secrets that already hold the attachment keys, the service file next to them and the service project. */
const COMPLETE_SECRETS = JSON.stringify({
  Identity: { BreakGlass: { UserName: 'breakglass' } },
  Codelab: {
    Security: {
      Tickets: { ActiveKeyId: 'a' },
      UploadTickets: { ActiveKeyId: 'c' },
      Uploads: { ActiveKeyId: 'b' },
      ServiceAssertions: { Client: { PrivateKeyPem: 'p' } },
    },
  },
});
const WITH_ATTACHMENTS = {
  [SECRETS]: COMPLETE_SECRETS,
  [SERVICE_SECRETS]: '{}',
  [ATTACHMENTS_PROJECT]: '<Project />',
  [MANIFEST]: '{"scripts":{"start":"ng serve"}}',
  [NODE_MODULES]: '',
};

describe('start', () => {
  it('starts the API and the web app, records both PIDs and prints the URLs and the break-glass user', async () => {
    const system = fakeSystem({
      files: { [SECRETS]: '{"Identity":{"BreakGlass":{"UserName":"breakglass"}}}', [MANIFEST]: '{"scripts":{"start":"ng serve"}}', [NODE_MODULES]: '' },
      statuses: healthy,
      commands: { 'sc.exe': { code: 1, output: 'not installed' } },
    });

    const lines = await start(system, { root: ROOT, noWeb: false, noSeq: false });

    const state = JSON.parse(system.disk.get(STATE));
    assert.deepEqual(state.processes.map((entry) => entry.name), ['api', 'web']);
    assert.ok(lines.some((line) => line.includes(Urls.api)) && lines.some((line) => line.includes(Urls.web)));
    assert.ok(lines.some((line) => line.includes('breakglass')));
    assert.ok(system.warnings.some((line) => /Seq is neither reachable/.test(line)));
    assert.ok(system.calls.some((call) => call[0] === 'sqllocaldb' && call[1] === 'start'));
    assert.ok(!system.calls.some((call) => call[0] === 'powershell.exe'), 'existing secrets are never rewritten');
  });

  it('leaves LocalDB alone when the connection string is overridden to another server', async () => {
    const system = fakeSystem({
      files: { [SECRETS]: '{"Identity":{"BreakGlass":{"UserName":"breakglass"}}}', [MANIFEST]: '{"scripts":{"start":"ng serve"}}', [NODE_MODULES]: '' },
      statuses: healthy,
      commands: { 'sc.exe': { code: 1, output: 'not installed' } },
    });
    system.env = { ConnectionStrings__Starter: 'Server=(localdb)\P2Proof;Database=StarterThrowaway' };

    await start(system, { root: ROOT, noWeb: false, noSeq: false });

    assert.ok(!system.calls.some((call) => call[0] === 'sqllocaldb'), 'never starts the default instance');
    assert.ok(system.lines.some((line) => line.includes('is not started')));
  });

  it('writes missing secrets once, skips a web app without dependencies and uses a reachable Seq', async () => {
    const system = fakeSystem({
      files: { [MANIFEST]: '{"scripts":{"start":"ng serve"}}' },
      statuses: { ...healthy, [Urls.seqDefault]: () => 200 },
    });

    const lines = await start(system, { root: ROOT, noWeb: false, noSeq: false });

    assert.ok(system.calls.some((call) => call[0] === 'powershell.exe' && call.at(-1).endsWith('New-LocalSecrets.ps1')));
    assert.ok(system.warnings.some((line) => /npm install/.test(line)));
    assert.deepEqual(JSON.parse(system.disk.get(STATE)).processes.map((entry) => entry.name), ['api']);
    assert.ok(lines.some((line) => line.includes(`Seq      ${Urls.seqDefault}`)));
  });

  it('refuses while a recorded process still runs, and when the API port is taken', async () => {
    const running = fakeSystem({ files: { [STATE]: '{"processes":[{"name":"api","pid":7,"image":"dotnet.exe"}]}' }, alive: new Map([[7, 'dotnet.exe']]) });
    await assert.rejects(start(running, { root: ROOT, noWeb: true, noSeq: true }), /already running/);

    const busy = fakeSystem({ statuses: { [Urls.api]: () => 404 } });
    await assert.rejects(start(busy, { root: ROOT, noWeb: true, noSeq: true }), /already answers/);
    assert.ok(!busy.calls.some((call) => call[0] === 'spawn'));
  });

  it('stops what it started when the API never becomes healthy', async () => {
    const system = fakeSystem({ files: { [SECRETS]: '{}' }, exitsEarly: true });

    assert.equal(await main({ root: ROOT, noWeb: true, noSeq: true, stop: false }, system), ExitCode.failed);
    assert.ok(system.warnings.some((line) => /exited before it was healthy/.test(line)));
    assert.ok(system.calls.some((call) => call[0] === 'taskkill.exe'));
    assert.ok(!system.disk.has(STATE));
  });
});

describe('API build', () => {
  it('builds the API first and runs the built assembly from the project folder', async () => {
    const system = fakeSystem({ files: { [SECRETS]: '{}' }, statuses: healthy });

    await start(system, { root: ROOT, noWeb: true, noSeq: true });

    const build = system.calls.findIndex((call) => call[1] === 'build');
    const spawnAt = system.calls.findIndex((call) => call[0] === 'spawn');
    assert.ok(build >= 0 && build < spawnAt);
    assert.match(system.calls[spawnAt].at(-1), /net10\.0[\\/]Starter\.Api\.dll$/);
  });

  it('refuses with the build output when the build fails, and starts nothing', async () => {
    const system = fakeSystem({ files: { [SECRETS]: '{}' }, commands: { [PRIVATE_DOTNET]: { code: 1, output: 'error CS1002' } } });

    await assert.rejects(start(system, { root: ROOT, noWeb: true, noSeq: true }), /build failed:\nerror CS1002/);
    assert.ok(!system.calls.some((call) => call[0] === 'spawn'));
  });
});

describe('the Attachment service', () => {
  it('builds and starts it after the API and before the web app, in Development on :5185, and health-checks it', async () => {
    const system = fakeSystem({ files: WITH_ATTACHMENTS, statuses: healthy });

    const lines = await start(system, { root: ROOT, noWeb: false, noSeq: true, noAttachments: false });

    assert.deepEqual(JSON.parse(system.disk.get(STATE)).processes.map((entry) => entry.name), ['api', 'attachments', 'web']);
    const steps = system.calls
      .filter((call) => call[1] === 'build' || call[0] === 'spawn')
      .map((call) => (call[1] === 'build' ? `build ${call[2]}` : `spawn ${call[1] === 'cmd.exe' ? 'web' : path.basename(call.at(-1))}`));
    assert.deepEqual(steps, [
      'build backend/src/Starter.Api/Starter.Api.csproj',
      'spawn Starter.Api.dll',
      `build ${ATTACHMENTS_PROJECT_FILE}`,
      'spawn Codelab.Attachments.Api.dll',
      'spawn web',
    ]);
    const service = system.spawned[1];
    assert.equal(service.command, PRIVATE_DOTNET, 'the private SDK runs the service');
    assert.equal(service.options.env.ASPNETCORE_URLS, Urls.attachments);
    assert.equal(service.options.env.ASPNETCORE_ENVIRONMENT, 'Development');
    assert.equal(service.options.cwd, path.join(ROOT, 'services/attachment-service/src/Codelab.Attachments.Api'));
    const apiReady = `${Urls.api}/health/ready`;
    const serviceReady = `${Urls.attachments}/health/ready`;
    const webRoot = `${Urls.web}/`;
    assert.ok(system.probed.indexOf(serviceReady) > system.probed.indexOf(apiReady), 'the API is checked first');
    assert.ok(system.probed.lastIndexOf(webRoot) > system.probed.lastIndexOf(serviceReady), 'the web app is checked last');
    assert.ok(lines.some((line) => line.includes(serviceReady)));
    assert.ok(!system.calls.some((call) => call[0] === 'powershell.exe'), 'complete secrets are not reseeded');
  });

  it('is skipped with -NoAttachments: no build, no port check, no secrets patch', async () => {
    const files = { ...WITH_ATTACHMENTS, [SECRETS]: '{"Identity":{"BreakGlass":{"UserName":"breakglass"}}}' };
    delete files[SERVICE_SECRETS];
    const system = fakeSystem({ files, statuses: { ...healthy, [Urls.attachments]: () => 200 } });

    const lines = await start(system, { root: ROOT, noWeb: true, noSeq: true, noAttachments: true });

    assert.deepEqual(JSON.parse(system.disk.get(STATE)).processes.map((entry) => entry.name), ['api']);
    assert.ok(!system.calls.some((call) => call[2] === ATTACHMENTS_PROJECT_FILE));
    assert.ok(!system.probed.includes(Urls.attachments), 'its port is not checked');
    assert.ok(!system.calls.some((call) => call[0] === 'powershell.exe'), 'the existing API secrets are left alone');
    assert.ok(system.warnings.some((line) => /NoAttachments/.test(line)));
    assert.ok(lines.some((line) => /Attachment service not started/.test(line)));
  });

  it('is skipped with a warning when the project is absent', async () => {
    const files = { ...WITH_ATTACHMENTS };
    delete files[ATTACHMENTS_PROJECT];
    const system = fakeSystem({ files, statuses: healthy });

    await start(system, { root: ROOT, noWeb: true, noSeq: true });

    assert.deepEqual(JSON.parse(system.disk.get(STATE)).processes.map((entry) => entry.name), ['api']);
    assert.ok(system.warnings.some((line) => /project does not exist/.test(line)));
  });

  it('patches the secrets when the API file predates the service', async () => {
    const files = { ...WITH_ATTACHMENTS, [SECRETS]: '{"Codelab":{"Security":{"Tickets":{"ActiveKeyId":"a"}}}}' };
    const system = fakeSystem({ files, statuses: healthy });

    await start(system, { root: ROOT, noWeb: true, noSeq: true });

    const seed = system.calls.find((call) => call[0] === 'powershell.exe');
    assert.ok(seed && seed.at(-1).endsWith('New-LocalSecrets.ps1'), 'the seeding script runs');
    assert.ok(!seed.includes('-Force'), 'never with -Force: existing values are kept');
  });

  it('refuses when :5185 is taken', async () => {
    const system = fakeSystem({ files: WITH_ATTACHMENTS, statuses: { [Urls.attachments]: () => 404 } });

    await assert.rejects(start(system, { root: ROOT, noWeb: true, noSeq: true }), /already answers on http:\/\/localhost:5185/);
    assert.ok(!system.calls.some((call) => call[0] === 'spawn'));
  });

  it('stops the API and itself when it never becomes healthy', async () => {
    const apiOnly = { [`${Urls.api}/health/ready`]: upAfterStart };
    const system = fakeSystem({ files: WITH_ATTACHMENTS, statuses: apiOnly, exitsEarly: (args) => args.at(-1).endsWith(ATTACHMENTS_ASSEMBLY) });

    assert.equal(await main({ root: ROOT, noWeb: true, noSeq: true, noAttachments: false, stop: false }, system), ExitCode.failed);
    assert.ok(system.warnings.some((line) => /The Attachment service exited before it was healthy/.test(line)));
    assert.equal(system.calls.filter((call) => call[0] === 'taskkill.exe').length, 2);
    assert.ok(!system.disk.has(STATE));
  });
});

describe('stop', () => {
  it('stops the API, the Attachment service and the web app it recorded, and nothing else', () => {
    const alive = new Map([[1, 'dotnet.exe'], [2, 'dotnet.exe'], [3, 'cmd.exe'], [4, 'dotnet.exe']]);
    const system = fakeSystem({
      files: { [STATE]: '{"processes":[{"name":"api","pid":1,"image":"dotnet.exe"},{"name":"attachments","pid":2,"image":"dotnet.exe"},{"name":"web","pid":3,"image":"cmd.exe"}]}' },
      alive,
    });

    const lines = stop(system);

    assert.deepEqual(system.calls.filter((call) => call[0] === 'taskkill.exe').map((call) => call[2]), ['1', '2', '3']);
    assert.ok(alive.has(4), 'an unrecorded dotnet process is never killed');
    assert.ok(lines.includes('Stopped attachments (PID 2).'));
  });

  it('kills only recorded PIDs that still run the recorded image, then forgets them', () => {
    const alive = new Map([[1, 'dotnet.exe'], [2, 'notepad.exe']]);
    const system = fakeSystem({
      files: { [STATE]: '{"processes":[{"name":"api","pid":1,"image":"dotnet.exe"},{"name":"web","pid":2,"image":"cmd.exe"},{"name":"x","pid":3,"image":"cmd.exe"}]}' },
      alive,
    });

    const lines = stop(system);

    const kills = system.calls.filter((call) => call[0] === 'taskkill.exe');
    assert.deepEqual(kills, [['taskkill.exe', '/PID', '1', '/T', '/F']]);
    assert.ok(alive.has(2), 'a reused PID is never killed');
    assert.equal(lines.length, 3);
    assert.ok(!system.disk.has(STATE));
  });

  it('has nothing to stop without a state file', () => {
    assert.match(stop(fakeSystem())[0], /Nothing to stop/);
  });
});
