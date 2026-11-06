// Pure parts of the Service Request flow (flows/service-requests.mjs): the requester credentials, the run variants,
// the record number, the request titles and the timeline and terminal checks. Unit-tested in
// tests/service-request-checks.test.mjs; nothing here touches the browser.
import { runToken } from './flow-checks.mjs';
import {
  DEV_USERS_PATH,
  SR_LANGUAGES,
  SR_NUMBER_MIN_DIGITS,
  SR_NUMBER_PREFIX,
  SrData,
  SrViewports,
} from './service-request-constants.mjs';

/**
 * The requester: the first Development user (Identity:DevUsers:0) of the local secrets JSON text. Returns
 * { user, password } or { error } (the error never contains the password).
 */
export function requesterCredentials(secretsText) {
  let first;
  try {
    first = DEV_USERS_PATH.reduce((node, key) => node?.[key], JSON.parse(secretsText))?.[0];
  } catch (error) {
    return { error: `The local secrets file is not JSON (${error.message}).` };
  }
  if (!first?.UserName || !first?.Password) return { error: `${DEV_USERS_PATH.join(':')}:0 has no UserName/Password: run tools/devseed/New-LocalSecrets.ps1.` };
  return { user: first.UserName, password: first.Password };
}

/** The runs of the flow, in order: every language at the desktop width, then every language at the phone width. */
export function flowVariants(languages = SR_LANGUAGES) {
  return [SrViewports.Desktop, SrViewports.Mobile].flatMap((viewport) =>
    languages.map((language) => ({ language, viewport, label: `${language}-${viewport.name}` })),
  );
}

/** The record numbers SR-YYYY-NNN of the given year in a text, in order of appearance. */
export function recordNumbersIn(text, year) {
  const pattern = new RegExp(`\\b${SR_NUMBER_PREFIX}-${year}-\\d{${SR_NUMBER_MIN_DIGITS},}\\b`, 'g');
  return String(text ?? '').match(pattern) ?? [];
}

/** The one record number the text shows (trimmed), or null when it is not exactly an SR-YYYY-NNN of that year. */
export function recordNumberOf(text, year) {
  const trimmed = String(text ?? '').trim();
  const found = recordNumbersIn(trimmed, year);
  return found.length === 1 && found[0] === trimmed ? trimmed : null;
}

/** The title of one request of one run variant: unique per run, variant and request (first, second). */
export function requestTitle(runStamp, variantLabel, ordinal) {
  return `${SrData.TitlePrefix}${runToken(runStamp)} ${variantLabel} #${ordinal}`;
}

/**
 * Whether the timeline's actions (newest or oldest first, as rendered) contain the expected actions as an ordered
 * subsequence in either direction. Returns { ok, missing } where missing lists the expected actions not matched.
 */
export function timelineCovers(renderedActions, expectedActions) {
  const attempt = (actions) => {
    let next = 0;
    for (const action of actions) {
      if (next < expectedActions.length && action === expectedActions[next]) next += 1;
    }
    return expectedActions.slice(next);
  };
  const forward = attempt(renderedActions);
  if (forward.length === 0) return { ok: true, missing: [] };
  const backward = attempt([...renderedActions].reverse());
  if (backward.length === 0) return { ok: true, missing: [] };
  return { ok: false, missing: forward.length <= backward.length ? forward : backward };
}

/**
 * The actor lines of a timeline: every entry must name an actor, and a request worked by a requester and a reviewer
 * shows at least `minimumActors` different ones. Returns { ok, distinct, blank }.
 */
export function actorSummary(actorLines, minimumActors) {
  const lines = actorLines.map((line) => String(line ?? '').trim());
  const blank = lines.filter((line) => line.length === 0).length;
  const distinct = [...new Set(lines.filter((line) => line.length > 0))];
  return { ok: lines.length > 0 && blank === 0 && distinct.length >= minimumActors, distinct, blank };
}
