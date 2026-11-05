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
} as const;
