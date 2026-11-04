import { describe, expect, it } from 'vitest';
import {
  createSecurityProtocol,
  InvalidProtocolPrefixError,
  isValidProtocolPrefix,
  SCHEME_REVISION,
  TEST_PROTOCOL_PREFIX,
} from './security-protocol.ts';

describe('createSecurityProtocol', () => {
  it('derives every name from the prefix, mirroring the server contract', () => {
    const protocol = createSecurityProtocol(TEST_PROTOCOL_PREFIX);

    expect(protocol).toEqual({
      prefix: 'CODELABTEST',
      canonVersion: 'CODELABTEST-CANON-v1',
      signatureVersion: 'CODELABTEST-SIG-v1',
      requestProfile: 'CODELABTEST-SIG-v1/REQ',
      responseProfile: 'CODELABTEST-SIG-v1/RESP',
      kdfProfile: 'CODELABTEST-SIG-v1/KDF',
      publicIdSpec: 'CODELABTEST-PUBLICID-v1',
      signatureHeader: 'X-CODELABTEST-Signature',
      responseSignatureHeader: 'X-CODELABTEST-Response-Signature',
    });
  });

  it('follows the prefix: a fork with another prefix gets other names, not a mix', () => {
    const protocol = createSecurityProtocol('ALTPFX');

    for (const value of Object.values(protocol)) expect(value).toContain('ALTPFX');
    expect(JSON.stringify(protocol)).not.toContain(TEST_PROTOCOL_PREFIX);
  });

  it('ends every scheme name with the shared revision', () => {
    const protocol = createSecurityProtocol(TEST_PROTOCOL_PREFIX);

    for (const name of [protocol.canonVersion, protocol.signatureVersion, protocol.publicIdSpec]) {
      expect(name.endsWith(`-${SCHEME_REVISION}`)).toBe(true);
    }
  });

  it('returns a frozen object, so no caller can rename a header at runtime', () => {
    expect(Object.isFrozen(createSecurityProtocol(TEST_PROTOCOL_PREFIX))).toBe(true);
  });

  it.each(['', 'A', 'codelab', 'Codelab', '1ABC', 'AB-CD', 'AB_CD', 'AB CD', 'A'.repeat(33), 'ÄBC'])(
    'refuses the prefix %j',
    (prefix) => {
      expect(isValidProtocolPrefix(prefix)).toBe(false);
      expect(() => createSecurityProtocol(prefix)).toThrow(InvalidProtocolPrefixError);
    },
  );

  it.each(['AB', 'CODELABTEST', 'ALTPFX', 'P2025', 'A'.repeat(32)])('accepts the prefix %j', (prefix) => {
    expect(isValidProtocolPrefix(prefix)).toBe(true);
    expect(createSecurityProtocol(prefix).prefix).toBe(prefix);
  });

  it('refuses a non-string prefix', () => {
    expect(isValidProtocolPrefix(undefined)).toBe(false);
    expect(() => createSecurityProtocol(42 as unknown as string)).toThrow(InvalidProtocolPrefixError);
  });
});
