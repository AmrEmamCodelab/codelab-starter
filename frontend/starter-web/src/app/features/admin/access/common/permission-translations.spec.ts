import { describe, expect, it } from 'vitest';
import ar from '../../../../../assets/i18n/ar.json';
import en from '../../../../../assets/i18n/en.json';
import { flattenTranslations } from '../../../../core/i18n/translation.service';
import { PermissionKeys } from '../../../../core/permissions/permission-keys.generated';
import { KEY_SEGMENT_SEPARATOR } from '../../../../patterns';
import { permissionGroupLabelKey, permissionLabelKey } from './access-labels';

const KEYS: readonly string[] = Object.values(PermissionKeys);

/** Every tree group a key sits under: each of its dotted prefixes (`a`, `a.b` for `a.b.c`). */
function groupsOf(key: string): string[] {
  const segments = key.split(KEY_SEGMENT_SEPARATOR);
  return segments.slice(1).map((_segment, index) => segments.slice(0, index + 1).join(KEY_SEGMENT_SEPARATOR));
}

const GROUPS = [...new Set(KEYS.flatMap(groupsOf))];

describe('permission translations', () => {
  it('derives every group prefix of a dotted key', () => {
    expect(groupsOf('records.oversight.view')).toEqual(['records', 'records.oversight']);
  });

  it.each([['ar', ar], ['en', en]])('labels every generated permission key in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    expect(KEYS.filter((key) => !flat[permissionLabelKey(key)])).toEqual([]);
  });

  it.each([['ar', ar], ['en', en]])('labels every permission group in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    expect(GROUPS.filter((group) => !flat[permissionGroupLabelKey(group)])).toEqual([]);
  });
});
