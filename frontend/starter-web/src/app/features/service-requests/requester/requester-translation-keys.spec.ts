import { describe, expect, it } from 'vitest';
import ar from '../../../../assets/i18n/ar.json';
import en from '../../../../assets/i18n/en.json';
import { flattenTranslations } from '../../../core/i18n/translation.service';
import { ServiceRequestActions } from '../data';
import { SERVICE_REQUEST_STATUS_KEYS, requesterStatusKey, serviceRequestActionLabelKey } from './requester-status';
import { SERVICE_REQUESTS_REQUESTER_TRANSLATION_KEYS } from './requester-translation-keys';

const BUNDLES = { ar: flattenTranslations(ar), en: flattenTranslations(en) };

/** The keys the requester screens render: their own texts, the requester wording of every status, every action label. */
const RENDERED_KEYS: readonly string[] = [
  ...SERVICE_REQUESTS_REQUESTER_TRANSLATION_KEYS,
  ...Object.values(SERVICE_REQUEST_STATUS_KEYS).map(requesterStatusKey),
  ...Object.values(ServiceRequestActions).map(serviceRequestActionLabelKey),
];

describe('requester translation keys', () => {
  it('keeps every key under serviceRequests.* (remove-module strips that prefix)', () => {
    for (const key of RENDERED_KEYS) expect(key.startsWith('serviceRequests.')).toBe(true);
  });

  it.each(Object.entries(BUNDLES))('has every key in %s', (_language, bundle) => {
    const missing = RENDERED_KEYS.filter((key) => !(key in bundle));
    expect(missing).toEqual([]);
  });
});
