/** The route prefix every API endpoint shares (the backend's `ApiRoutes.Prefix`). */
export const API_PREFIX = 'api';

/**
 * Every API path the core clients call, relative to the API root. Paths carry no leading slash (the content
 * rule refuses absolute API path literals); `ApiService` joins them onto `AppConfig.apiBaseUrl`.
 */
export const ApiPaths = {
  AuthLogin: `${API_PREFIX}/auth/login`,
  AuthRefresh: `${API_PREFIX}/auth/refresh`,
  AuthLogout: `${API_PREFIX}/auth/logout`,
  /** GET, anonymous: which sign-in methods the deployment enables (`Auth:Modes`). */
  AuthModes: `${API_PREFIX}/auth/modes`,
  /** POST, anonymous: Active Directory sign-in; answers like {@link ApiPaths.AuthLogin}. */
  AuthAdLogin: `${API_PREFIX}/auth/ad/login`,
  /** A top-level NAVIGATION target, never an XHR: the API redirects the browser to the identity provider. */
  AuthSsoLogin: `${API_PREFIX}/auth/sso/login`,
  /** POST, anonymous: swaps the one-time callback ticket for a session. */
  AuthSsoExchange: `${API_PREFIX}/auth/sso/exchange`,
  /** POST, authenticated: ends an SSO session and answers the identity provider's end-session URL. */
  AuthSsoLogout: `${API_PREFIX}/auth/sso/logout`,
  /** POST, anonymous: sets the first password from an activation ticket. */
  AuthActivate: `${API_PREFIX}/auth/activate`,
  MePermissions: `${API_PREFIX}/me/permissions`,
  Features: `${API_PREFIX}/features`,
  Dashboard: `${API_PREFIX}/dashboard`,
  AccessAdmin: `${API_PREFIX}/access/admin`,
  SessionSigningKey: `${API_PREFIX}/security/session-signing-key`,
  ProtectedTranslations: `${API_PREFIX}/i18n`,
} as const;

export type ApiPath = (typeof ApiPaths)[keyof typeof ApiPaths];

/** Query parameter names the core clients send. */
export const ApiQueryParams = {
  Language: 'lang',
  /** The app-relative URL the SSO sign-in returns to (`GET api/auth/sso/login?returnUrl=`). */
  ReturnUrl: 'returnUrl',
  /** The base64url SHA-256 of the tab's SSO binding secret (`GET api/auth/sso/login?binding=`). */
  Binding: 'binding',
} as const;
