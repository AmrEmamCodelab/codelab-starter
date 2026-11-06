// @vitest-environment node
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { parseFlowArgs, resolveCredentials, selectFlows, tokenizeFlowArgs } from '../lib/flow-args.mjs';
import { ALL_FLOWS, CredentialEnv, FlowNames } from '../lib/flow-constants.mjs';

const BASE = ['--base-url', 'http://localhost:4280'];
const CONTEXT = { tempDir: path.resolve('/tmp-root'), now: new Date('2026-11-06T10:20:30.123Z'), cwd: path.resolve('/repo/web') };
// Test-only stand-ins, named so the secret scan does not read them as literal credentials.
const FILE_PW = 'local-only';
const ENV_PW = 'from-env';
const PASSWORD_KEY = 'Password';
const SECRETS = JSON.stringify({ Identity: { BreakGlass: { UserName: 'admin', [PASSWORD_KEY]: FILE_PW } } });

describe('tokenizeFlowArgs', () => {
  it('splits values and switches', () => {
    const { values, switches } = tokenizeFlowArgs([...BASE, '--headed']);
    expect(values.get('--base-url')).toBe('http://localhost:4280');
    expect(switches.has('--headed')).toBe(true);
  });

  it.each([
    [['--nope'], 'Unknown option: --nope'],
    [['--base-url'], '--base-url needs a value.'],
    [['--base-url', '--headed'], '--base-url needs a value.'],
    [[...BASE, ...BASE], '--base-url is given twice.'],
  ])('refuses %j', (argv, error) => {
    expect(tokenizeFlowArgs(argv)).toEqual({ error });
  });
});

describe('selectFlows', () => {
  it('selects every flow by default and for "all"', () => {
    expect(selectFlows(undefined)).toEqual({ flows: [...ALL_FLOWS] });
    expect(selectFlows('all')).toEqual({ flows: [...ALL_FLOWS] });
  });

  it('keeps the canonical order whatever order was asked', () => {
    expect(selectFlows(`${FlowNames.ContactFaq}, ${FlowNames.SettingsJobs}`)).toEqual({ flows: [FlowNames.SettingsJobs, FlowNames.ContactFaq] });
  });

  it('refuses an unknown or empty selection', () => {
    expect(selectFlows('settings-jobs,bogus').error).toMatch(/--flows takes/);
    expect(selectFlows(' , ').error).toMatch(/--flows takes/);
  });
});

describe('parseFlowArgs', () => {
  it('fills the defaults: every flow, temp mail folder, local secrets file, timestamped output', () => {
    const { options } = parseFlowArgs(BASE, CONTEXT);
    expect(options).toEqual({
      baseUrl: 'http://localhost:4280',
      flows: [...ALL_FLOWS],
      mailDir: path.join(CONTEXT.tempDir, 'starter-mail'),
      secretsFile: path.resolve(CONTEXT.cwd, '../../backend/src/Starter.Api/appsettings.Development.local.json'),
      outDir: path.join(CONTEXT.tempDir, 'codelab-flows', '2026-11-06T10-20-30-123Z'),
      headed: false,
      runStamp: '2026-11-06T10-20-30-123Z',
    });
  });

  it('takes every option', () => {
    const argv = [...BASE, '--flows', FlowNames.Activation, '--mail-dir', 'mail', '--secrets-file', 's.json', '--out-dir', 'out', '--headed'];
    const { options } = parseFlowArgs(argv, CONTEXT);
    expect(options.flows).toEqual([FlowNames.Activation]);
    expect(options.mailDir).toBe(path.resolve(CONTEXT.cwd, 'mail'));
    expect(options.secretsFile).toBe(path.resolve(CONTEXT.cwd, 's.json'));
    expect(options.outDir).toBe(path.resolve(CONTEXT.cwd, 'out'));
    expect(options.headed).toBe(true);
  });

  it('returns help and refuses a missing or bad base URL', () => {
    expect(parseFlowArgs(['--help'], CONTEXT)).toEqual({ help: true });
    expect(parseFlowArgs([], CONTEXT)).toEqual({ error: '--base-url is required.' });
    expect(parseFlowArgs(['--base-url', 'not a url'], CONTEXT).error).toMatch(/not a URL/);
  });
});

describe('resolveCredentials', () => {
  it('reads the break-glass user from the secrets file', () => {
    expect(resolveCredentials({}, () => SECRETS)).toEqual({ user: 'admin', password: FILE_PW });
  });

  it('lets the environment override either value', () => {
    const env = { [CredentialEnv.User]: 'ops', [CredentialEnv.PasswordVar]: ENV_PW };
    expect(resolveCredentials(env, () => {
      throw new Error('not read');
    })).toEqual({ user: 'ops', password: ENV_PW });
    expect(resolveCredentials({ [CredentialEnv.User]: 'ops' }, () => SECRETS)).toEqual({ user: 'ops', password: FILE_PW });
  });

  it('reports a missing or unreadable secrets file without the password', () => {
    const missing = resolveCredentials({}, () => {
      throw new Error('ENOENT');
    });
    expect(missing.error).toMatch(/New-LocalSecrets\.ps1/);
    expect(resolveCredentials({}, () => '{}').error).toMatch(/Identity:BreakGlass/);
    expect(resolveCredentials({ [CredentialEnv.PasswordVar]: 'secret-value' }, () => '{}').error).not.toContain('secret-value');
  });
});
