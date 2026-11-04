# @codelab/ng-security

## Purpose

The browser half of the Codelab security core. It is the TypeScript twin of `Codelab.Security` and agrees with it byte
for byte through the golden vectors in `packages/vectors`, not through shared code.

| Area | What it gives an app | Files |
| --- | --- | --- |
| Protocol names | `{Prefix}-CANON-v1`, `{Prefix}-SIG-v1` (`/REQ`, `/RESP`, `/KDF`), `{Prefix}-PUBLICID-v1`, `X-{Prefix}-Signature`, `X-{Prefix}-Response-Signature`, all derived from one prefix | `src/protocol/` |
| Canonical framing | `{utf8Length}:{value}\n` framing, method allowlist, path and query normalisation | `src/canonical/` |
| Request signing | HMAC-SHA256 proof over audience, method, path, query, body hash, timestamp and a fresh nonce; `signatureInterceptor` with one re-sign on `signature_stale` | `src/signing/`, `src/angular/signature.interceptor.ts` |
| Response verification | verifies the server's MAC over the exact received bytes, status and content type; `responseSignatureInterceptor` | `src/verification/`, `src/angular/response-signature.interceptor.ts` |
| Session keys | `SessionKeyStore`: imports the login `signing` block into two non-extractable `CryptoKey` handles, persists them in IndexedDB, measures clock skew once | `src/session/` |
| Wire ids | `WireId<D>` branded opaque identifiers; `tryAsId` / `asId` / `toIdSet` are the only narrowing doors | `src/wire-id/` |
| Errors | `parseApiError` for the one problem+json envelope (`code`, `correlationId`, `referenceCode`, field errors), `apiErrorMessageKey`, `referenceCodeFrom` | `src/errors/` |

What a signature proves: integrity, anti-replay and anti-CSRF. The server derives the same key it verifies with, so a
valid MAC proves only that the sender held the session key. It does not authenticate the caller, and nothing built on
this package may claim that it does.

## Use

```ts
import {
  createApiRootClassifier,
  provideCodelabSecurity,
  responseSignatureInterceptor,
  signatureInterceptor,
} from '@codelab/ng-security';

export const appConfig: ApplicationConfig = {
  providers: [
    provideCodelabSecurity({
      prefix: runtimeConfig.securityPrefix, // the same prefix the API's security options carry
      classifyServiceUrl: createApiRootClassifier({
        roots: () => [runtimeConfig.apiUrl],
        pageOrigin: () => location.origin,
      }),
    }),
    // The two security interceptors go LAST, in this order.
    provideHttpClient(withInterceptors([authInterceptor, signatureInterceptor, responseSignatureInterceptor])),
  ],
};
```

- At login, pass the response's `signing` block to `inject(SessionKeyStore).adopt(block)`; at logout call `clear()`.
  When the server does not sign (no `signing` block), do nothing: both interceptors pass requests straight through.
- Errors: `parseApiError(httpErrorResponse, clientTraceId?)`, then translate with `apiErrorMessageKey(error)`. Branch
  on `serverCode`, render from `code`.
- Ids: declare the app's domains once with `defineIdDomains([...])`, type them as `WireId<'record'>`, and narrow
  route parameters with `tryAsId('record', value)`.
- Node tools (the vector runner) import the framework-free half from `@codelab/ng-security/core`.

Scripts (Node from `.nvmrc`, run from the repo root or this folder):

| Script | What it does |
| --- | --- |
| `npm run build -w @codelab/ng-security` | `tsc` to `dist/` (ES2022 modules and `.d.ts`); the source version stays `0.0.0`, the pack step stamps it |
| `npm test -w @codelab/ng-security` | type-checks sources and specs, then `vitest run` |

## Extend

- New logic gets a `*.spec.ts` beside it in the same change. Specs run in Node (WebCrypto, `HttpRequest` and the
  Angular injector all work there); browser storage is reached through the `SigningSessionMarker` and
  `KeyRecordStore` ports, so specs use the in-memory doubles in `src/testing/`.
- A new protocol name is derived in `createSecurityProtocol` from the prefix, never written as a literal, and the C#
  side gets the same derivation in the same change.
- Golden-vector specs live in `src/vectors/` (owned by the vectors work, read from `packages/vectors`). They are the
  only cross-language check; an in-process spec on either side agrees with itself.
- Export every public symbol from `src/core.ts` (framework-free) or `src/index.ts` (Angular); a spec fails on a module
  that neither exports.

## Rules

- Do not "improve" the canonical code: no sorting, trimming, case folding or URL rewriting. Each breaks agreement with
  the server, and the symptom is an intermittent 401, not a failing test.
- Fail closed. A signing fault throws `RequestNotSignedError` or `SessionSigningKeyMissingError` and sends nothing; an
  unverified response throws `ResponseSignatureError` and is never delivered as data. None of these is an
  `HttpErrorResponse`, so an auth interceptor cannot turn them into a logout.
- No key material in the repo. Specs generate keys at runtime; the vector fixture keys are starter-only and test-only.
- Time comes from the injected clock (`SECURITY_CLOCK`), never from `Date.now()` inside logic.
- `as WireId<...>` is allowed only in `src/wire-id/wire-id.ts` (the `wire-id-cast` lint in `@codelab/ng-tools`).
- Platform-owner review for any change (`packages/` in CODEOWNERS); exact dependency pins (`.npmrc save-exact`).

## Gotchas

- The prefix must match the API's exactly (2 to 32 upper-case ASCII letters and digits). A mismatch is a uniform 401;
  `provideCodelabSecurity` throws at bootstrap on a malformed one but cannot know the server's value.
- The signed path is the SERVICE-RELATIVE path (`/items`, not `/api/items`). The server excludes `PathBase`; only a
  real browser against a real deployment proves the classifier roots are right.
- WebCrypto needs a secure context: any plain-HTTP host other than `localhost` cannot sign, and `adopt` refuses login.
- `responseSignatureInterceptor` must be last: it re-issues signed requests as `arraybuffer` and decodes after
  verifying, so progress events lose `partialText`.
- The clock offset is in memory only; after a reload the server's freshness window absorbs the drift and a stale
  proof gets exactly one re-sign.
