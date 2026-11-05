// The session lifecycle over the REAL HTTP stack with request signing and response verification on: ApiService, the
// app interceptors and both @codelab/ng-security interceptors, answered the way the API answers. The API checks proofs
// only on authenticated requests and signs only the answers to verified ones, so an anonymous call (login, refresh)
// and the signing-key issuance are answered unsigned. Regression: the refresh used to go out signed, its unsigned 200
// was refused (response_signature_missing) and the session was dropped silently about a minute before expiry.
import { HttpTestingController, provideHttpClientTesting, type TestRequest } from '@angular/common/http/testing';
import { ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import {
  SECURITY_PROTOCOL,
  SIGNED_REQUEST_NONCE,
  SessionKeyStore,
  buildResponseSigningInputBytes,
  computeMac,
  formatResponseSignatureHeader,
  hashBody,
  type KeyRecordStore,
  type SigningSessionBlock,
  type SigningSessionMarker,
} from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiPaths } from '../api/clients/api-paths';
import type { SessionResponse } from '../api/clients/api.models';
import { AUTHORIZATION_HEADER, ApiService } from '../api/api.service';
import { TEST_API_BASE_URL, TEST_APP_CONFIG, provideTestAppConfig } from '../config/app-config.testing';
import { provideStarterHttp } from '../http/http.providers';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { provideStarterSecurity } from '../security/security.providers';
import { BROWSER_STORAGE } from '../session/app-storage';
import { MemoryStorage } from '../session/memory-storage';
import { AuthService } from './auth.service';
import { CROSS_TAB_LOCK, noCrossTabLock } from './cross-tab-lock';
import { TokenStore } from './token.store';

const KEY_BYTES = 32;
const TOKEN_LIFETIME_MS = 600_000;
const JSON_CONTENT_TYPE = 'application/json; charset=utf-8';
const PERMISSIONS = { permissions: ['dashboard.view'] };

class MemoryMarker implements SigningSessionMarker {
  private set_ = false;
  isSet(): boolean {
    return this.set_;
  }
  set(): void {
    this.set_ = true;
  }
  clear(): void {
    this.set_ = false;
  }
}

class MemoryRecords implements KeyRecordStore {
  private record: unknown = null;
  isAvailable(): boolean {
    return true;
  }
  write(record: unknown): Promise<void> {
    this.record = record;
    return Promise.resolve();
  }
  read(): Promise<unknown> {
    return Promise.resolve(this.record);
  }
  erase(): Promise<void> {
    this.record = null;
    return Promise.resolve();
  }
}

/** One server-issued session key: the block the API hands out and the key the API signs answers with. */
interface IssuedKey {
  readonly block: SigningSessionBlock;
  readonly signer: Promise<CryptoKey>;
}

function issueKey(): IssuedKey {
  const bytes = crypto.getRandomValues(new Uint8Array(KEY_BYTES));
  return {
    block: {
      alg: 'HMAC-SHA256',
      sessionSigningKey: btoa(String.fromCharCode(...bytes)),
      audience: 'Starter.Api',
      verifyResponses: true,
      serverTimeUnix: Math.floor(Date.now() / 1000),
    },
    signer: crypto.subtle.importKey('raw', bytes, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']),
  };
}

function session(generation: number): SessionResponse {
  return {
    accessToken: `a${generation}`,
    refreshToken: `r${generation}`,
    accessTokenExpiresAt: new Date(Date.now() + TOKEN_LIFETIME_MS).toISOString(),
    idleTimeoutMinutes: 15,
    user: { id: 'u1', displayName: 'User' },
  };
}

const url = (path: string): string => `${TEST_API_BASE_URL}/${path}`;

describe('a signed session over the real HTTP stack', () => {
  let http: HttpTestingController;

  /** The next request to `path`, once the asynchronous signer has sent it. */
  function next(path: string): Promise<TestRequest> {
    return vi.waitFor(() => http.expectOne(url(path)));
  }

  function signatureOf(request: TestRequest): string | null {
    return request.request.headers.get(TestBed.inject(SECURITY_PROTOCOL).signatureHeader);
  }

  /** Answers a signed request the way the API does: the MAC over (nonce, status, content type, body hash). */
  async function answerSigned(request: TestRequest, key: IssuedKey, body: unknown): Promise<void> {
    const nonce = request.request.context.get(SIGNED_REQUEST_NONCE);
    expect(nonce).not.toBeNull();
    // Copied into an ArrayBuffer of this realm, which the testing backend's `instanceof ArrayBuffer` check accepts.
    const encoded = new TextEncoder().encode(JSON.stringify(body));
    const bytes = new Uint8Array(new ArrayBuffer(encoded.length));
    bytes.set(encoded);
    const protocol = TestBed.inject(SECURITY_PROTOCOL);
    const canonical = buildResponseSigningInputBytes(protocol, {
      requestNonce: nonce!,
      status: 200,
      contentType: JSON_CONTENT_TYPE,
      bodyHash: await hashBody(bytes),
    });
    const mac = await computeMac(await key.signer, canonical);
    request.flush(bytes.buffer, {
      headers: { 'Content-Type': JSON_CONTENT_TYPE, [protocol.responseSignatureHeader]: formatResponseSignatureHeader(mac) },
    });
  }

  /** Answers an anonymous or key-issuance request the way the API does: unsigned. */
  function answerUnsigned(request: TestRequest, body: object): void {
    expect(signatureOf(request)).toBeNull();
    request.flush(body, { headers: { 'Content-Type': JSON_CONTENT_TYPE } });
  }

  async function signIn(key: IssuedKey): Promise<void> {
    const login = TestBed.inject(AuthService).login('user', 'secret');
    answerUnsigned(await next(ApiPaths.AuthLogin), session(1));
    answerUnsigned(await next(ApiPaths.SessionSigningKey), key.block);
    await login;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTestAppConfig(),
        provideStarterHttp(),
        provideHttpClientTesting(),
        ...provideStarterSecurity(TEST_APP_CONFIG),
        {
          provide: SessionKeyStore,
          useFactory: () => new SessionKeyStore({ marker: new MemoryMarker(), records: new MemoryRecords(), clock: () => Date.now() }),
        },
        { provide: BROWSER_STORAGE, useValue: new MemoryStorage() },
        { provide: CROSS_TAB_LOCK, useValue: noCrossTabLock },
        { provide: PermissionSyncService, useValue: { refresh: vi.fn().mockResolvedValue(undefined), clear: vi.fn() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('signs an authenticated call and delivers its verified answer', async () => {
    const key = issueKey();
    await signIn(key);
    const call = firstValueFrom(TestBed.inject(ApiService).get(ApiPaths.MePermissions));
    const request = await next(ApiPaths.MePermissions);
    expect(signatureOf(request)).not.toBeNull();
    await answerSigned(request, key, PERMISSIONS);
    await expect(call).resolves.toEqual(PERMISSIONS);
  });

  it('refreshes unsigned, adopts the reissued key, and keeps signing with it', async () => {
    const first = issueKey();
    await signIn(first);
    const auth = TestBed.inject(AuthService);
    const report = vi.spyOn(TestBed.inject(ErrorHandler), 'handleError');

    const refreshed = auth.refresh();
    const refresh = await next(ApiPaths.AuthRefresh);
    expect(refresh.request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
    answerUnsigned(refresh, session(2));
    const second = issueKey();
    const reissue = await next(ApiPaths.SessionSigningKey);
    expect(reissue.request.headers.get(AUTHORIZATION_HEADER)).toContain('a2');
    answerUnsigned(reissue, second.block);

    await expect(refreshed).resolves.toBe(true);
    expect(report).not.toHaveBeenCalled();
    expect(TestBed.inject(TokenStore).accessToken()).toBe('a2');

    const call = firstValueFrom(TestBed.inject(ApiService).get(ApiPaths.MePermissions));
    await answerSigned(await next(ApiPaths.MePermissions), second, PERMISSIONS);
    await expect(call).resolves.toEqual(PERMISSIONS);
  });
});
