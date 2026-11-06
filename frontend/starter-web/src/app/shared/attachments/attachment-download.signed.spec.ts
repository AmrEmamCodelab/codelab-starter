// The attachment download over the REAL HTTP stack with request signing and response verification on: the adapter,
// AttachmentsClient, ApiService.getBlob, the app interceptors and both @codelab/ng-security interceptors, answered the
// way the API answers the content endpoint (`[UnsignedResponse]`: the signature covers nonce, status and content type
// with the body field `UNSIGNED-BODY`). Regression for the phase 6 browser download blocker: the download must be a
// signed fetch whose Blob the app saves itself (an object URL), never a link or a navigation to the API, and it must
// not depend on Content-Disposition (the API no longer sends it; download managers intercept responses that carry it).
import {
  HttpTestingController,
  provideHttpClientTesting,
  type TestRequest,
} from "@angular/common/http/testing";
import { TestBed } from "@angular/core/testing";
import { provideRouter } from "@angular/router";
import {
  SECURITY_PROTOCOL,
  SIGNED_REQUEST_NONCE,
  SessionKeyStore,
  UNSIGNED_BODY,
  buildResponseSigningInputBytes,
  computeMac,
  formatResponseSignatureHeader,
  type KeyRecordStore,
  type SigningSessionBlock,
  type SigningSessionMarker,
} from "@codelab/ng-security";
import { firstValueFrom } from "rxjs";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AUTHORIZATION_HEADER } from "../../core/api/api.service";
import { CROSS_TAB_LOCK, noCrossTabLock } from "../../core/auth/cross-tab-lock";
import { SignInMode } from "../../core/auth/sign-in-mode";
import { TokenStore } from "../../core/auth/token.store";
import {
  TEST_API_BASE_URL,
  TEST_APP_CONFIG,
  provideTestAppConfig,
} from "../../core/config/app-config.testing";
import { provideStarterHttp } from "../../core/http/http.providers";
import { PermissionSyncService } from "../../core/permissions/permission-sync.service";
import { provideStarterSecurity } from "../../core/security/security.providers";
import { BROWSER_STORAGE } from "../../core/session/app-storage";
import { MemoryStorage } from "../../core/session/memory-storage";
import { AttachmentErrorCodes } from "./attachment-error-codes";
import type { AttachmentError } from "./attachment-error";
import { AttachmentsApiPaths } from "./attachments-api";
import { TEST_ATTACHMENT, TEST_ATTACHMENT_ID } from "./attachments.testing";
import { FileSaver } from "./file-saver";
import { RecordAttachmentsAdapter } from "./record-attachments.adapter";

const KEY_BYTES = 32;
const ACCESS_TOKEN = "access-1";
const TOKEN_LIFETIME_MS = 600_000;
const PDF_CONTENT_TYPE = "application/pdf";
const PROBLEM_CONTENT_TYPE = "application/problem+json; charset=utf-8";
const OK = 200;
const NOT_FOUND = 404;
const FILE_TEXT = "%PDF-1.7 the whole file";

class MemoryMarker implements SigningSessionMarker {
  private marked = false;
  isSet(): boolean {
    return this.marked;
  }
  set(): void {
    this.marked = true;
  }
  clear(): void {
    this.marked = false;
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

/** The bytes as an ArrayBuffer of this realm (the testing backend checks `instanceof ArrayBuffer`). */
function bufferOf(text: string): ArrayBuffer {
  const encoded = new TextEncoder().encode(text);
  const bytes = new Uint8Array(new ArrayBuffer(encoded.length));
  bytes.set(encoded);
  return bytes.buffer;
}

describe("an attachment download over the signed HTTP stack", () => {
  let http: HttpTestingController;
  let signer: Promise<CryptoKey>;
  const contentUrl = `${TEST_API_BASE_URL}/${AttachmentsApiPaths.content(TEST_ATTACHMENT_ID)}`;

  /** The content request, once the asynchronous signer has sent it. */
  function contentRequest(): Promise<TestRequest> {
    return vi.waitFor(() => http.expectOne(contentUrl));
  }

  /** Answers the way the API answers a `[UnsignedResponse]` endpoint: the MAC binds nonce, status and content type. */
  async function answer(
    request: TestRequest,
    status: number,
    contentType: string,
    body: string,
  ): Promise<void> {
    const protocol = TestBed.inject(SECURITY_PROTOCOL);
    const nonce = request.request.context.get(SIGNED_REQUEST_NONCE);
    expect(nonce).not.toBeNull();
    const canonical = buildResponseSigningInputBytes(protocol, {
      requestNonce: nonce!,
      status,
      contentType,
      bodyHash: UNSIGNED_BODY,
    });
    const headers = {
      "Content-Type": contentType,
      [protocol.responseSignatureHeader]: formatResponseSignatureHeader(
        await computeMac(await signer, canonical),
      ),
    };
    if (status === OK) {
      request.flush(bufferOf(body), { headers });
    } else {
      request.flush(bufferOf(body), { headers, status, statusText: "Refused" });
    }
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTestAppConfig(),
        provideStarterHttp(),
        provideHttpClientTesting(),
        ...provideStarterSecurity(TEST_APP_CONFIG),
        {
          provide: SessionKeyStore,
          useFactory: () =>
            new SessionKeyStore({
              marker: new MemoryMarker(),
              records: new MemoryRecords(),
              clock: () => Date.now(),
            }),
        },
        { provide: BROWSER_STORAGE, useValue: new MemoryStorage() },
        { provide: CROSS_TAB_LOCK, useValue: noCrossTabLock },
        {
          provide: PermissionSyncService,
          useValue: {
            refresh: vi.fn().mockResolvedValue(undefined),
            clear: vi.fn(),
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);

    const keyBytes = crypto.getRandomValues(new Uint8Array(KEY_BYTES));
    signer = crypto.subtle.importKey(
      "raw",
      keyBytes,
      { name: "HMAC", hash: "SHA-256" },
      false,
      ["sign"],
    );
    const block: SigningSessionBlock = {
      alg: "HMAC-SHA256",
      sessionSigningKey: btoa(String.fromCharCode(...keyBytes)),
      audience: "Starter.Api",
      verifyResponses: true,
      serverTimeUnix: Math.floor(Date.now() / 1000),
    };
    TestBed.inject(TokenStore).save(
      {
        accessToken: ACCESS_TOKEN,
        refreshToken: "refresh-1",
        accessTokenExpiresAt: new Date(
          Date.now() + TOKEN_LIFETIME_MS,
        ).toISOString(),
        idleTimeoutMinutes: 15,
        user: { id: "u1", displayName: "User" },
      },
      SignInMode.Basic,
    );
    await TestBed.inject(SessionKeyStore).adopt(block);
  });

  afterEach(() => http.verify());

  it("sends a signed GET with the bearer token and saves the verified Blob under the list name", async () => {
    const save = vi
      .spyOn(TestBed.inject(FileSaver), "save")
      .mockImplementation(() => undefined);
    const done = firstValueFrom(
      TestBed.inject(RecordAttachmentsAdapter).download(TEST_ATTACHMENT),
      { defaultValue: undefined },
    );

    const request = await contentRequest();
    expect(request.request.method).toBe("GET");
    expect(request.request.headers.get(AUTHORIZATION_HEADER)).toBe(
      `Bearer ${ACCESS_TOKEN}`,
    );
    expect(
      request.request.headers.get(
        TestBed.inject(SECURITY_PROTOCOL).signatureHeader,
      ),
    ).toMatch(/\S/);
    // ng-security reads the exact bytes (arraybuffer) and hands the caller the Blob it asked for.
    expect(request.request.responseType).toBe("arraybuffer");
    await answer(request, OK, PDF_CONTENT_TYPE, FILE_TEXT);
    await done;

    expect(save).toHaveBeenCalledTimes(1);
    const [blob, fileName] = save.mock.calls[0]!;
    expect(fileName).toBe(TEST_ATTACHMENT.fileName);
    expect(blob.type).toBe(PDF_CONTENT_TYPE);
    expect(await blob.text()).toBe(FILE_TEXT);
  });

  it("surfaces a signed refusal as an AttachmentError and saves nothing", async () => {
    const save = vi.spyOn(TestBed.inject(FileSaver), "save");
    const failed = firstValueFrom(
      TestBed.inject(RecordAttachmentsAdapter).download(TEST_ATTACHMENT),
    ).catch((error: unknown) => error);

    const request = await contentRequest();
    await answer(
      request,
      NOT_FOUND,
      PROBLEM_CONTENT_TYPE,
      JSON.stringify({
        code: AttachmentErrorCodes.NotFound,
        referenceCode: "API01-REF09",
      }),
    );

    await expect(failed).resolves.toMatchObject<Partial<AttachmentError>>({
      code: AttachmentErrorCodes.NotFound,
      status: NOT_FOUND,
    });
    expect(save).not.toHaveBeenCalled();
  });
});
