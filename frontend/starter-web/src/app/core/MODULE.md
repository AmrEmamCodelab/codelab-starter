# Web core (`src/app/core`)

## Purpose

The plumbing every screen of the starter web app relies on. Core holds services, stores, guards, interceptors and
constants; it renders nothing. Features, layouts and shared code import from core; core imports none of them.

| Area | What it gives the app | Files |
| --- | --- | --- |
| Config | `config.json` loaded and validated before bootstrap (`loadRuntimeConfig`), `AppConfigService` (API root, origins, signing prefix, default language) | `config/` |
| API | `ApiService`, the ONLY `HttpClient` user: joins `ApiPaths` onto the API root and adds the bearer token. Thin clients per area: `AuthClient`, `MeClient`, `FeaturesClient`, `DashboardClient`, `AccessClient`, `TranslationsClient` | `api/api.service.ts`, `api/clients/` |
| HTTP | Response-phase interceptors (`sessionExpiredInterceptor`, `forbiddenInterceptor`, `errorDialogInterceptor`), `ErrorDialogStore`, the `SKIP_ERROR_DIALOG` / `SKIP_SESSION_EXPIRY` contexts, `provideStarterHttp()`, the static JSON loader | `http/` |
| Security | `provideStarterSecurity(config)`: `@codelab/ng-security` request signing and response verification with the prefix and API roots from `config.json` | `security/` |
| Auth | `AuthService` (login, proactive refresh, logout, expiry), `TokenStore`, `authGuard`, `permissionGuard()` | `auth/` |
| Permissions | `PermissionStore` (`has`, `permissions`, `loaded`) and `PermissionSyncService` (`GET api/me/permissions`) | `permissions/` |
| Features | `FeatureStore` (`GET api/features`, flags only) and `FeatureFlagNames` | `features/` |
| i18n | `LanguageService` (Arabic default, `<html lang dir>`, remembered choice), `TranslationService`, the `t` pipe, `ProtectedTranslationsService`, `CoreTranslationKeys` | `i18n/` |
| Routing | `RoutePaths`, `RouteQueryParams`, `RouteDataKeys`, `SessionEndReasons`, `loginTree` / `unauthorizedTree` / `safeReturnUrl` | `routing/` |
| Session | `AppStorage` + `StorageKeys` (typed browser storage), `APP_CLOCK`, `SessionTimeoutService`, `initializeCore` (start-up sequence) | `session/` |

## Use

```ts
// A feature service calls the API through a client, never HttpClient:
private readonly dashboard = inject(DashboardClient);
load() { return this.dashboard.get(); }

// Routes (app.routes.ts) gate on permission keys from the generated catalogue:
{ path: RoutePaths.Admin, canActivate: [authGuard, permissionGuard()],
  data: { [RouteDataKeys.Permission]: PermissionKeys.AccessAdminView }, loadComponent: ... }

// Templates translate with the t pipe:
// {{ 'dashboard.title' | t }}   {{ 'greeting' | t: { name: user.displayName } }}

// The login page:
await inject(AuthService).login(userName, password); // throws the HttpErrorResponse on refusal
const target = safeReturnUrl(route.snapshot.queryParamMap.get(RouteQueryParams.ReturnUrl));
```

- Buttons and menus read `PermissionStore.has(key)`; routes use `permissionGuard`. The server still authorizes
  every call; the store only hides what would be refused.
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
- **New storage key:** add it to `StorageKeys`; read and write it only through `AppStorage`.
- **New route:** add the path to `RoutePaths`; navigate with `router.navigate([RoutePaths.X])` or a `UrlTree`.
- **New translation key used by core:** add it to `CoreTranslationKeys` and ask fe-features to add it to
  `src/assets/i18n/ar.json` and `en.json`.
- **New feature flag:** add it to `FeatureFlagNames` with the JSON name the API's `GET api/features` returns.
- Every new service, guard, interceptor or helper with logic gets a `*.spec.ts` beside it (Vitest, jsdom).

## Rules

- `HttpClient` appears only in `api/api.service.ts`; `fetch` only in `http/static-json.loader.ts` (content rule
  `base.fe-http-via-api-service`).
- Interceptors are response-phase only. `ApiService` owns the URL and the Authorization header; signing belongs to
  the ng-security interceptors, which stay last in `provideStarterHttp()`.
- No literals used as keys: API paths, routes, query parameters, route data keys, storage keys, flags and
  translation keys come from the constants in this module.
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
- Tabs share one session and the refresh token is single-use (re-sending a rotated one revokes the session). A refresh
  runs under the `CROSS_TAB_LOCK` (`navigator.locks`) and first calls `TokenStore.adoptStored()`; `TokenStore` adopts
  another tab's rotation on the `storage` event of `AccessTokenExpiresAt`, which `save()` writes last and `clear()`
  removes last. Keep that order when adding a session key.
