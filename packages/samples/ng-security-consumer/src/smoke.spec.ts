/*
 * Package integration test of @codelab/ng-security as a consumer installs it: from the packed tarball in feed/npm,
 * never from the workspace source. The published build must frame and digest exactly like the C# side, so it is
 * checked against the checked-in golden vectors (packages/vectors).
 */
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { describe, expect, it } from 'vitest';
import * as angularEntry from '@codelab/ng-security';
import {
  TEST_PROTOCOL_PREFIX,
  buildCanonicalBytes,
  buildRequestSigningInputBytes,
  createSecurityProtocol,
  hashBody,
  isValidProtocolPrefix,
} from '@codelab/ng-security/core';

// tools/build.ps1 step 13 runs a copy of this project outside the repo when it tests a fresh prerelease; it then
// points CODELAB_VECTORS_DIR at packages/vectors.
const VECTORS_DIR_VARIABLE = 'CODELAB_VECTORS_DIR';
const VECTORS_FOLDER = process.env[VECTORS_DIR_VARIABLE]
  ? pathToFileURL(`${process.env[VECTORS_DIR_VARIABLE]}/`)
  : new URL('../../../vectors/', import.meta.url);
const SECOND_PREFIX = 'ALTPFX';
const FRAME_KIND = 'frame';
const SIGN_KIND = 'sign';
const UTF8 = 'utf8';

interface VectorFile {
  readonly prefix: string;
  readonly cases: readonly Record<string, unknown>[];
}

function loadVectors(area: string): VectorFile {
  return JSON.parse(readFileSync(new URL(`${area}.vectors.json`, VECTORS_FOLDER), 'utf8')) as VectorFile;
}

function base64Of(bytes: Uint8Array): string {
  return Buffer.from(bytes).toString('base64');
}

function field(vectorCase: Record<string, unknown>, name: string): string {
  const value = vectorCase[name];
  if (typeof value !== 'string') throw new TypeError(`vector field ${name} is not a string`);
  return value;
}

describe('@codelab/ng-security installed from the feed', () => {
  it('resolves from the packed tarball, not from the workspace source', () => {
    const require = createRequire(import.meta.url);
    const manifestPath = require.resolve('@codelab/ng-security/package.json');
    const manifest = JSON.parse(readFileSync(manifestPath, 'utf8')) as { version: string };

    expect(manifestPath).toContain('node_modules');
    expect(manifest.version).toMatch(/^\d+\.\d+\.\d+(-ci\.\d+)?$/);
  });

  it('exposes the Angular entry point and the framework-free core', () => {
    expect(typeof angularEntry.provideCodelabSecurity).toBe('function');
    expect(typeof angularEntry.signatureInterceptor).toBe('function');
    expect(typeof angularEntry.responseSignatureInterceptor).toBe('function');
    expect(isValidProtocolPrefix(SECOND_PREFIX)).toBe(true);
  });

  it('derives every protocol name from the configured prefix', () => {
    const protocol = createSecurityProtocol(SECOND_PREFIX);

    expect(protocol.canonVersion).toBe(`${SECOND_PREFIX}-CANON-v1`);
    expect(protocol.signatureVersion).toBe(`${SECOND_PREFIX}-SIG-v1`);
    expect(protocol.signatureHeader).toBe(`X-${SECOND_PREFIX}-Signature`);
    expect(protocol.responseSignatureHeader).toBe(`X-${SECOND_PREFIX}-Response-Signature`);
  });

  it('frames every canonical vector byte for byte', () => {
    const file = loadVectors('canonical');
    const protocol = createSecurityProtocol(file.prefix);
    const cases = file.cases.filter((vectorCase) => vectorCase['kind'] === FRAME_KIND);

    expect(file.prefix).toBe(TEST_PROTOCOL_PREFIX);
    expect(cases.length).toBeGreaterThan(0);
    for (const vectorCase of cases) {
      const fields = vectorCase['fields'] as string[];
      const bytes = buildCanonicalBytes(protocol, field(vectorCase, 'profile'), fields);
      expect(base64Of(bytes), field(vectorCase, 'name')).toBe(field(vectorCase, 'expectedCanonicalBase64'));
    }
  });

  it('builds the request signing input and body hash of every UTF-8 signing vector', async () => {
    const file = loadVectors('request-signature');
    const protocol = createSecurityProtocol(file.prefix);
    const cases = file.cases.filter((vectorCase) => vectorCase['kind'] === SIGN_KIND
      && vectorCase['bodyUnsigned'] === false
      && (vectorCase['body'] as { encoding: string }).encoding === UTF8);

    expect(cases.length).toBeGreaterThan(0);
    for (const vectorCase of cases) {
      const body = new TextEncoder().encode((vectorCase['body'] as { text: string }).text);
      const bodyHash = await hashBody(body);
      const canonical = buildRequestSigningInputBytes(protocol, {
        audience: field(vectorCase, 'audience'),
        method: field(vectorCase, 'method'),
        path: field(vectorCase, 'path'),
        query: field(vectorCase, 'query'),
        bodyHash,
        timestamp: field(vectorCase, 'timestamp'),
        nonce: field(vectorCase, 'nonce'),
      });
      expect(bodyHash, field(vectorCase, 'name')).toBe(field(vectorCase, 'expectedBodyHash'));
      expect(base64Of(canonical), field(vectorCase, 'name')).toBe(field(vectorCase, 'expectedCanonicalBase64'));
    }
  });
});
