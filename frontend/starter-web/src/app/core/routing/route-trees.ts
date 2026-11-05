import type { Router, UrlTree } from '@angular/router';
import { RoutePaths, RouteQueryParams, type SessionEndReason } from './route-paths';

/** Where a signed-out user goes: the login page, remembering where they were heading. */
export function loginTree(router: Router, returnUrl?: string, reason?: SessionEndReason): UrlTree {
  const queryParams: Record<string, string> = {};
  if (returnUrl && !isLoginUrl(returnUrl)) {
    queryParams[RouteQueryParams.ReturnUrl] = returnUrl;
  }
  if (reason) {
    queryParams[RouteQueryParams.Reason] = reason;
  }
  return router.createUrlTree([RoutePaths.Login], { queryParams });
}

/** The 403 page. */
export function unauthorizedTree(router: Router): UrlTree {
  return router.createUrlTree([RoutePaths.Unauthorized]);
}

/**
 * A safe post-login target: only an app-relative URL (`/x`, never `//host` or `https://host`), so a
 * crafted `returnUrl` cannot send the user off-site.
 */
export function safeReturnUrl(candidate: string | null | undefined): string | null {
  if (!candidate || !candidate.startsWith('/') || candidate.startsWith('//') || isLoginUrl(candidate)) {
    return null;
  }
  return candidate;
}

function isLoginUrl(url: string): boolean {
  const path = url.split(/[?#]/)[0] ?? '';
  return path.replace(/^\/+/, '') === RoutePaths.Login;
}
