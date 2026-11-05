import { DOCUMENT, Injectable, inject } from '@angular/core';

/** The schemes a top-level navigation may leave the app for (the API's SSO start, the IdP's end-session page). */
const NAVIGABLE_PROTOCOLS: ReadonlySet<string> = new Set(['https:', 'http:']);

/** The fragment's leading `#`. */
const FRAGMENT_MARK = '#';

/**
 * The browser address bar, behind one seam so specs never navigate the test runner.
 *
 * - {@link assign} is a top-level navigation that leaves the app (the SSO start and the identity provider's
 *   end-session page): those flows set cookies and redirect, which an XHR cannot do.
 * - {@link fragment} and {@link clearFragment} read and scrub a one-time ticket the server put in the URL
 *   fragment. The fragment never reaches a server log; clearing it (with `history.replaceState`) keeps it off
 *   screen and out of Back and the history list.
 */
@Injectable({ providedIn: 'root' })
export class BrowserLocation {
  private readonly window = inject(DOCUMENT).defaultView;

  /**
   * Navigates the whole page to an absolute http(s) URL.
   *
   * @returns false (and stays) for anything else, so a malformed or `javascript:` URL is never followed.
   */
  assign(url: string): boolean {
    if (!this.window || !isNavigableUrl(url)) {
      return false;
    }
    this.window.location.assign(url);
    return true;
  }

  /** The raw fragment without its `#`, still percent-encoded; empty when there is none. */
  fragment(): string {
    const hash = this.window?.location.hash ?? '';
    return hash.startsWith(FRAGMENT_MARK) ? hash.slice(FRAGMENT_MARK.length) : hash;
  }

  /** Removes the fragment from the address bar and the current history entry, keeping the router's state. */
  clearFragment(): void {
    const location = this.window?.location;
    if (!location || location.hash === '') {
      return;
    }
    this.window?.history.replaceState(this.window.history.state, '', `${location.pathname}${location.search}`);
  }
}

/** True for an absolute http(s) URL. */
export function isNavigableUrl(candidate: string): boolean {
  try {
    return NAVIGABLE_PROTOCOLS.has(new URL(candidate).protocol);
  } catch {
    return false;
  }
}

/**
 * Parses a raw `name=value&name=value` fragment. Values are percent-decoded one by one (a `+` stays a `+`, unlike
 * `URLSearchParams`, so a base64 ticket survives); a malformed escape drops that pair rather than throwing.
 */
export function parseFragmentParams(fragment: string): ReadonlyMap<string, string> {
  const params = new Map<string, string>();
  for (const pair of fragment.split('&')) {
    const separator = pair.indexOf('=');
    if (separator <= 0) {
      continue;
    }
    const name = safeDecode(pair.slice(0, separator));
    const value = safeDecode(pair.slice(separator + 1));
    if (name !== null && value !== null && !params.has(name)) {
      params.set(name, value);
    }
  }
  return params;
}

function safeDecode(text: string): string | null {
  try {
    return decodeURIComponent(text);
  } catch {
    return null;
  }
}
