// @vitest-environment node
import { describe, expect, it } from 'vitest';
import {
  activationPassword,
  CONTACT_REFERENCE_PATTERN,
  contactEmail,
  contactReferenceFor,
  faqMarkers,
  findContactReferences,
  importedUser,
  importRows,
  nextSettingValue,
  runToken,
} from '../lib/flow-checks.mjs';
import { IMPORT_HEADERS } from '../lib/flow-constants.mjs';

const STAMP = '2026-11-06T10-20-30-123Z';

describe('contact reference numbers', () => {
  it('finds CU-YYYY-NNN with at least three digits on word edges', () => {
    expect(findContactReferences('Ref CU-2026-007 and CU-2025-1234, not XCU-2026-001 or CU-2026-01')).toEqual([
      { value: 'CU-2026-007', year: 2026 },
      { value: 'CU-2025-1234', year: 2025 },
    ]);
    expect(CONTACT_REFERENCE_PATTERN.test('رقم المرجع CU-2026-042')).toBe(true);
  });

  it('accepts only the given year', () => {
    expect(contactReferenceFor('CU-2025-001 then CU-2026-002', 2026)).toBe('CU-2026-002');
    expect(contactReferenceFor('CU-2025-001', 2026)).toBeNull();
  });
});

describe('run data', () => {
  it('derives a lower-case token from the run stamp', () => {
    expect(runToken(STAMP)).toBe('6t102030123z');
    expect(runToken(STAMP)).toMatch(/^[a-z0-9]+$/);
  });

  it('builds a unique imported user and its import rows in template order', () => {
    const user = importedUser(STAMP);
    expect(user).toEqual({ userName: 'e2e-6t102030123z', displayName: 'E2E user 6t102030123z', email: 'e2e-6t102030123z@example.test' });
    expect(importRows(user)).toEqual([[...IMPORT_HEADERS], [user.userName, user.displayName, user.email, 'internal', 'password']]);
  });

  it('builds a password with every character class, a contact address and two distinct FAQ markers', () => {
    const chosenPassword = activationPassword(STAMP);
    expect(chosenPassword).toMatch(/[A-Z]/);
    expect(chosenPassword).toMatch(/[a-z]/);
    expect(chosenPassword).toMatch(/\d/);
    expect(chosenPassword).toMatch(/[^A-Za-z0-9]/);
    expect(contactEmail(STAMP)).toBe('contact-6t102030123z@example.test');
    const markers = faqMarkers(STAMP);
    expect(markers.ar).not.toBe(markers.en);
    expect(markers.ar).toContain(runToken(STAMP));
  });
});

describe('nextSettingValue', () => {
  it('adds the delta to the shown whole number and refuses non-numbers', () => {
    expect(nextSettingValue(' 20 ')).toBe('21');
    expect(nextSettingValue('abc')).toBeNull();
  });
});
