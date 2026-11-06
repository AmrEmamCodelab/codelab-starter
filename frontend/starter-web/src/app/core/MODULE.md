# Web core (`src/app/core`)

## Purpose

The plumbing every screen of the starter web app relies on. Core holds services, stores, guards, interceptors and
constants; it renders nothing. Features, layouts and shared code import from core; core imports none of them.

| Area | What it gives the app | Files |
| --- | --- | --- |
| Config | `config.json` loaded and validated before bootstrap (`loadRuntimeConfig`), `AppConfigService` (API root, origins, signing prefix, default language, `permissionRefreshSeconds`) | `config/` |
| API | `ApiService`, the ONLY `HttpClient` user: joins `ApiPaths` onto the API root and adds the bearer token. Thin clients per area: `AuthClient`, `MeClient`, `FeaturesClient`, `DashboardClient`, `AccessClient`, `TranslationsClient`. `api/generated/openapi.ts` and `permissions/permission-keys.generated.ts` are written by `npm run api:gen` from DocsGen's OpenAPI and permission catalog (Phase 5 adds the lookups, audit, settings, jobs, notifications and portal schemas and keys) | `api/api.service.ts`, `api/clients/` |
| HTTP | Response-phase interceptors (`sessionExpiredInterceptor`, `forbiddenInterceptor`, `errorDialogInterceptor`), `ErrorDialogStore`, the `SKIP_ERROR_DIALOG` / `SKIP_SESSION_EXPIRY` contexts, `provideStarterHttp()`, the static JSON loader | `http/` |
| Security | `provideStarterSecurity(config)`: `@codelab/ng-security` request signing and response verification with the prefix and API roots from `config.json` | `security/` |
| Auth | `AuthService` (basic, Active Directory and SSO sign-in, proactive refresh, logout incl. SSO end-session, expiry), `TokenStore` (remembers the `SignInMode`), `SignInMode`, the login-rejection mapper (`loginRejectionFrom`, `ssoErrorRejection`, `IdentityErrorCodes`), the SSO callback fragment contract (`sso.ts`), `authGuard`, `permissionGuard()` | `auth/` |
| Permissions | `holdsPermissions(has, required, mode)`, `PermissionMatchMode`, `PermissionRequirement` (one key or a list; an empty list never matches; `shared` re-exports them for `*hasPermission`), `PermissionStore` (`has`, `held`, `permissions`, `loaded`; all signals, change-only writes) and `PermissionSyncService` (`GET api/me/permissions`, plus the live triggers: focus, visibility, `NavigationEnd`, interval) | `permissions/` |
| Features | `FeatureStore` (`GET api/features`, flags only) and `FeatureFlagNames` | `features/` |
| i18n | `LanguageService` (Arabic default, `<html lang dir>`, remembered choice), `TranslationService`, the `t` pipe, `ProtectedTranslationsService`, `CoreTranslationKeys` | `i18n/` |
| Routing | `RoutePaths`, `RouteQueryParams`, `RouteDataKeys`, `SessionEndReasons`, `loginTree` / `unauthorizedTree` / `safeReturnUrl` | `routing/` |
| Session | `AppStorage` + `StorageKeys` (typed browser storage), `APP_CLOCK`, `SessionTimeoutService`, `BrowserLocation` (top-level navigation, fragment read and clear), `initializeCore` (start-up sequence) | `session/` |

## Use

```ts
// A feature service calls the API through a client, never HttpClient:
private readonly dashboard = inject(DashboardClient);
load() { return this.dashboard.get(); }

// Routes (app.routes.ts) gate on permission keys from the generated catalogue:
{ path: RoutePaths.Dashboard, canActivate: [permissionGuard()],
  data: { [RouteDataKeys.Permission]: PermissionKeys.DashboardView }, loadComponent: ... }
// A mount whose pages each carry their own key may open to any of several keys (a list matches any one):
{ path: RoutePaths.Admin, canActivate: [permissionGuard(ADMIN_AREA_PERMISSIONS)], loadChildren: ... }

// Templates translate with the t pipe:
// {{ 'dashboard.title' | t }}   {{ 'greeting' | t: { name: user.displayName } }}

// The login page (each throws the HttpErrorResponse on refusal; read it with loginRejectionFrom(error)):
await inject(AuthService).login(userName, password);             // POST api/auth/login, SignInMode.Basic
await inject(AuthService).loginWithDirectory(userName, password); // POST api/auth/ad/login, SignInMode.ActiveDirectory
await inject(AuthService).startSsoSignIn(returnUrl);              // top-level navigation to api/auth/sso/login?binding=
const proof = inject(AuthService).takeSsoBindingProof();          // the callback page: this tab's binding secret (once)
await inject(AuthService).completeSsoSignIn(ticket, proof);       // POST api/auth/sso/exchange {ticket, bindingProof}
const target = safeReturnUrl(route.snapshot.queryParamMap.get(RouteQueryParams.ReturnUrl));

// The enabled sign-in methods (GET api/auth/modes) and account activation (POST api/auth/activate):
inject(AuthClient).modes();  inject(AuthClient).activate({ ticket, password });
```

- Buttons and menus read `PermissionStore.has(key)` (or `*hasPermission`); routes use `permissionGuard`. The store is a
  signal, so a grant or revoke picked up by the live sync repaints the shell nav and every `*hasPermission` without a
  relogin. The server still authorizes every call; the store only hides what would be refused.
- `AuthService.logout()` picks the endpoint from `TokenStore.signInMode()`: an SSO session calls `api/auth/sso/logout`
  and, when it answers `{ endSessionUrl }`, the whole page navigates there (`BrowserLocation.assign`); a 204, an error,
  or a basic/AD session goes to the login page.
- The layout renders the error dialog from `ErrorDialogStore.current()`, `messageKey()` and `referenceCode()` and
  calls `dismiss()`. A caller that shows its own error passes `context: quietContext()` (or `selfHandledContext()`
  for calls outside a session).
- Language switch: `LanguageService.set('en')`; read `current()` and `dir()`.
- Specs provide `provideTestAppConfig()` (`config/app-config.testing.ts`) and `{ provide: BROWSER_STORAGE,
  useValue: new MemoryStorage() }`.

## Extend

- **New endpoint call:** add the path to `ApiPaths` (relative, under `API_PREFIX`), a method on the area's client
  (or a new `<area>.client.ts`), and a case in `api/clients/clients.spec.ts`. When the generated client types exist
  (`core/api/generated`, from `npm run api:gen`), type the client with them instead of `api.models.ts`.
- **New storage key:** add it to `StorageKeys`; read and write it only through `AppStorage`. A session key is written by
  `TokenStore.save()` before `AccessTokenExpiresAt` and removed by `clear()` before it (see Gotchas).
- **New Identity error code the sign-in pages render:** add it to `IdentityErrorCodes` (mirroring the backend's
  `IdentityErrorCodes`), to the translated list in `login-rejection.ts`, and `errors.<CODE>` to both bundles.
- **New sign-in method:** a `SignInMode` member (same value as the backend's), an `AuthClient` call, an `AuthService`
  method that calls `beginSession(response, mode)`, and the logout branch when it needs its own end-of-session call.
- **New route:** add the path to `RoutePaths`; navigate with `router.navigate([RoutePaths.X])` or a `UrlTree`.
- **New translation key used by core:** add it to `CoreTranslationKeys` and ask fe-features to add it to
  `src/assets/i18n/ar.json` and `en.json`.
- **New feature flag:** add it to `FeatureFlagNames` with the JSON name the API's `GET api/features` returns.
- Every new service, guard, interceptor or helper with logic gets a `*.spec.ts` beside it (Vitest, jsdom).

## Rules

- `HttpClient` appears only in `api/api.service.ts`; `fetch` only in `http/static-json.loader.ts` (content rule
  `base.fe-http-via-api-service`).
- Interceptors are response-phase only. `ApiService` owns the URL and the Authorization header; signing belongs to
  the ng-security interceptors, which stay last in `provideStarterHttp()` (`sessionSignatureInterceptor` wraps
  `signatureInterceptor` and skips requests marked `UNSIGNED_REQUEST`).
- An anonymous call (`ApiOptions.anonymous`) and the signing-key issuance (`AuthClient.signingKey()`) go out unsigned
  (`UNSIGNED_REQUEST`): the API checks proofs only on authenticated requests and answers those calls unsigned, so a
  signed one would have its unsigned 200 refused (`response_signature_missing`).
- A 403 whose signature could not be verified (`ResponseSignatureError` with status 403) counts as a 403 in
  `forbiddenInterceptor` (`isForbidden`): it can only take access away from the view.
- No literals used as keys: API paths, routes, query parameters, route data keys, storage keys, flags and
  translation keys come from the constants in this module.
- The SSO start and the identity provider's end-session page are top-level navigations, never XHR: they set cookies on
  the API origin and redirect. `BrowserLocation.assign` follows only absolute http(s) URLs.
- A one-time ticket in a URL fragment (SSO callback, activation link) is read raw with `BrowserLocation.fragment()`,
  cleared at once with `clearFragment()` (`history.replaceState`), and never logged, stored or put in a signal.
- Background permission syncs are quiet (`quietContext`): a failure keeps the previous keys. A 401 still ends the session
  (a deactivated user is signed out on the next sync).
- Guards fail closed: no permission named means the 403 page. A failed permission sync is not a 403: the navigation
  is cancelled and the error dialog shows `errors.<CODE>` with the reference code (`PermissionSyncService.lastFailure()`);
  navigating again retries.
- Core never imports features, layouts, patterns or shared, and contains no UI.
- `AuthService` is the only writer of `TokenStore`; `ApiService` and `TokenStore` never inject `AuthService`
  (circular dependency).

## Gotchas

- `config.json` is read in `main.ts` before Angular bootstraps, because `provideCodelabSecurity` needs the prefix
  while the providers are built. A malformed file stops the app with an `AppConfigInvalidError` in the console.
- The prefix in `config.json` must equal the API's `Codelab:Security:Protocol:Prefix`; a mismatch is a uniform 401.
- WebCrypto needs a secure context: plain HTTP works only on `localhost`.
- `ProtectedTranslationsService` is off unless the API returns `protectedTranslations: true` from `api/features`;
  the P2 API serves no `api/i18n` yet.
- Specs run without Vitest globals, so `src/test-setup.ts` resets `TestBed` after each test. Use
  `TestBed.tick()` to flush effects.
- A 401 on login, refresh or logout is an answer, not an expired session: those calls set `SKIP_SESSION_EXPIRY`.
- A failed refresh (refused, unverifiable answer, or refused signing keys) is reported to Angular's `ErrorHandler` and
  expires the session (`/login?reason=expired&returnUrl=...`); it never leaves a page that only looks signed in.
  `core/auth/signed-session.spec.ts` runs sign-in, refresh and a signed call over the real HTTP stack with signing on.
- Tabs share one session and the refresh token is single-use (re-sending a rotated one revokes the session). A refresh
  runs under the `CROSS_TAB_LOCK` (`navigator.locks`) and first calls `TokenStore.adoptStored()`; `TokenStore` adopts
  another tab's rotation on the `storage` event of `AccessTokenExpiresAt`, which `save()` writes last and `clear()`
  removes last. Keep that order when adding a session key.
- SSO logout reaches the identity provider only through a top-level GET to `endSessionUrl`, which carries
  `id_token_hint` and `post_logout_redirect_uri`; the provider redirects back only for a GET with the hint, so never
  fetch it or open it in a frame. The id_token stays on the server; the browser holds only the URL for one navigation.
- SSO sign-in is bound to the tab that started it (login-CSRF defence, `auth/sso-binding.ts`): the start makes a
  random secret kept in sessionStorage (`StorageKeys.SsoBindingSecret`) and sends only its base64url SHA-256 as
  `binding`; the exchange sends the secret as `bindingProof`. The API refuses a start without a binding (400) and a
  ticket without the matching proof, and the callback refuses at once, without calling the API, when this tab holds
  no secret (a copied ticket link).
- The SSO return URL travels twice: as `?returnUrl=` to `api/auth/sso/login` and in `StorageKeys.SsoReturnUrl`
  (read once by `takeSsoReturnUrl()`); the callback prefers a `#returnUrl=` the API echoes, and every source passes
  `safeReturnUrl()` again.
- The live permission sync starts in `initializeCore` and follows `TokenStore.session()`; it skips a session whose keys
  were never loaded (the guards own the first sync) and throttles focus, visibility and navigation to one request per
  `PERMISSION_SYNC_THROTTLE_MS`. `permissionRefreshSeconds: 0` turns only the interval off.
- `RoutePaths.SsoCallback` (`auth/sso/callback`) and `RoutePaths.Activate` (`activate`) are public routes listed in
  `src/app/public-routes.ts`.
- `PermissionSyncService` injects `TokenStore` (never `AuthService`, which injects it) and reads `AppConfigService` and
  `Router` lazily, so specs that only need `refresh()` provide nothing extra.
- Config (P4): `AppConfigService` also exposes `branding` (optional, validated by @codelab/ng-dga `parseDgaBranding`; a bad field is `AppConfigInvalidError` with `branding.<field>`) `showcase` (default false; a development build forces it on through `DEVELOPMENT_BUILD_OVERRIDES` in `config/runtime-config.loader.ts`, a production build never does) and `portalContactForm` (parser default false; the shipped `public/config.json` sets it to true from P5). `provideAppBranding(config)` in `config/branding.providers.ts` applies it; `app.config.ts` spreads it.
