import assert from 'node:assert/strict';
import path from 'node:path';
import { describe, it } from 'node:test';
import { API_ASSEMBLY, ExitCode, LOCAL_SECRETS_FILE, PRIVATE_DOTNET, STATE_FILE, Urls } from '../lib/constants.mjs';
import { main, start, stop } from '../lib/run.mjs';

const ROOT = 'R:\\repo';
const STATE_DIR = 'T:\\state';
const STATE = path.join(STATE_DIR, STATE_FILE);
const SECRETS = path.join(ROOT, LOCAL_SECRETS_FILE);
const MANIFEST = path.join(ROOT, 'frontend/starter-web/package.json');
const NODE_MODULES = path.join(ROOT, 'node_modules');

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
    exists: (file) => disk.has(file) || file === PRIVATE_DOTNET || (builds && file.endsWith(API_ASSEMBLY)),
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
    spawnDetached: (command, args) => {
      const pid = (nextPid += 1);
      alive.set(pid, command.toLowerCase().endsWith('cmd.exe') ? 'cmd.exe' : 'dotnet.exe');
      calls.push(['spawn', command, ...args]);
      return { pid, exited: () => exitsEarly };
    },
    httpStatus: async (url) => statuses[url]?.(time) ?? null,
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
  [`${Urls.web}/`]: upAfterStart,
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

describe('stop', () => {
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
