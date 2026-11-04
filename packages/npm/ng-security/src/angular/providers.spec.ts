import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Injector } from '@angular/core';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { InvalidProtocolPrefixError, TEST_PROTOCOL_PREFIX } from '../protocol/security-protocol.ts';
import { SessionKeyStore } from '../session/session-keys.store.ts';
import { provideCodelabSecurity } from './providers.ts';
import { SECURITY_CLOCK, SECURITY_PROTOCOL, SESSION_VERIFICATION_KEY_SOURCE, SIGNED_SERVICE_URL_CLASSIFIER } from './tokens.ts';

const classify = () => null;

describe('provideCodelabSecurity', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('binds the protocol, the classifier, the store and the verification key source', async () => {
    const injector = Injector.create({ providers: provideCodelabSecurity({ prefix: TEST_PROTOCOL_PREFIX, classifyServiceUrl: classify }) });

    expect(injector.get(SECURITY_PROTOCOL).signatureHeader).toBe('X-CODELABTEST-Signature');
    expect(injector.get(SIGNED_SERVICE_URL_CLASSIFIER)).toBe(classify);
    expect(injector.get(SessionKeyStore)).toBeInstanceOf(SessionKeyStore);
    expect(injector.get(SessionKeyStore)).toBe(injector.get(SessionKeyStore));
    expect(await injector.get(SESSION_VERIFICATION_KEY_SOURCE).responseVerificationKey()).toBeNull();
  });

  it('stamps proofs from the injected clock', () => {
    const injector = Injector.create({
      providers: [
        ...provideCodelabSecurity({ prefix: TEST_PROTOCOL_PREFIX, classifyServiceUrl: classify }),
        { provide: SECURITY_CLOCK, useValue: () => 1_900_000_000_999 },
      ],
    });

    expect(injector.get(SessionKeyStore).nowUnixSeconds()).toBe(1_900_000_000);
  });

  it('defaults the clock to the system time', () => {
    const injector = Injector.create({ providers: provideCodelabSecurity({ prefix: TEST_PROTOCOL_PREFIX, classifyServiceUrl: classify }) });
    const before = Math.floor(Date.now() / 1000);

    expect(injector.get(SessionKeyStore).nowUnixSeconds()).toBeGreaterThanOrEqual(before);
  });

  it('fails at bootstrap on a malformed prefix', () => {
    expect(() => provideCodelabSecurity({ prefix: 'bad prefix', classifyServiceUrl: classify })).toThrow(InvalidProtocolPrefixError);
  });

  it('has no default protocol: a missing provider fails loudly', () => {
    expect(() => Injector.create({ providers: [] }).get(SECURITY_PROTOCOL)).toThrow();
  });
});

describe('the core entry point', () => {
  const source = fileURLToPath(new URL('..', import.meta.url));

  it('imports no Angular module, so Node tools can load it', () => {
    const core = readFileSync(join(source, 'core.ts'), 'utf8');
    const modules = [...core.matchAll(/from '(\.[^']+)'/g)].map((match) => match[1]);

    for (const module of modules) {
      const text = readFileSync(join(source, module), 'utf8');
      expect(text, module).not.toMatch(/from '@angular\//);
    }
  });

  it('is re-exported by the package index', () => {
    expect(readFileSync(join(source, 'index.ts'), 'utf8')).toContain("export * from './core.ts';");
  });

  it('exposes every source module from the index (no orphan module)', () => {
    const index = readFileSync(join(source, 'index.ts'), 'utf8') + readFileSync(join(source, 'core.ts'), 'utf8');
    const folders = ['angular', 'canonical', 'errors', 'protocol', 'session', 'signing', 'verification', 'wire-id'];
    for (const folder of folders) {
      for (const file of readdirSync(join(source, folder)).filter((name) => name.endsWith('.ts') && !name.endsWith('.spec.ts'))) {
        expect(index.includes(`./${folder}/${file}`), `${folder}/${file}`).toBe(true);
      }
    }
  });
});

