import { FormControl, FormGroup, Validators } from '@angular/forms';
import { describe, expect, it, vi } from 'vitest';
import {
  applyDevAutofill,
  devAutofillSetProblems,
  devAutofillSetsFor,
  hasDevAutofillLabelSegment,
  resolveDevAutofillValue,
} from './dev-autofill.fill';
import type { DevAutofillSet } from './dev-autofill.model';

const FORM_KEY = 'test-record-form';
const LABEL_KEY = 'testRecords.devAutofill.valid';
const MIN_TITLE = 5;

function form(): FormGroup {
  return new FormGroup({
    title: new FormControl('', [Validators.required, Validators.minLength(MIN_TITLE)]),
    code: new FormControl({ value: 'locked', disabled: true }),
    address: new FormGroup({ city: new FormControl(''), street: new FormControl('') }),
  });
}

function set(values: Record<string, unknown>, overrides: Partial<DevAutofillSet> = {}): DevAutofillSet {
  return { formKey: FORM_KEY, labelKey: LABEL_KEY, values, ...overrides };
}

describe('devAutofillSetsFor', () => {
  it('keeps the sets of one form in registration order', () => {
    const first = set({ title: 'a' });
    const other = set({ title: 'b' }, { formKey: 'other-form' });
    const second = set({ title: 'c' }, { labelKey: 'testRecords.devAutofill.edge' });
    expect(devAutofillSetsFor([first, other, second], FORM_KEY)).toEqual([first, second]);
  });

  it('is empty for a form without sets', () => {
    expect(devAutofillSetsFor([set({ title: 'a' })], 'unknown-form')).toEqual([]);
  });
});

describe('resolveDevAutofillValue', () => {
  it('calls a factory once and returns its result', () => {
    const factory = vi.fn(() => '2026-11-07');
    expect(resolveDevAutofillValue(factory)).toBe('2026-11-07');
    expect(factory).toHaveBeenCalledTimes(1);
  });

  it('returns any other value as it is', () => {
    const value = { city: 'x' };
    expect(resolveDevAutofillValue(value)).toBe(value);
    expect(resolveDevAutofillValue(null)).toBeNull();
  });
});

describe('hasDevAutofillLabelSegment', () => {
  it('needs the whole devAutofill segment', () => {
    expect(hasDevAutofillLabelSegment(LABEL_KEY)).toBe(true);
    expect(hasDevAutofillLabelSegment('devAutofill.valid')).toBe(true);
    expect(hasDevAutofillLabelSegment('testRecords.devAutofillValid')).toBe(false);
    expect(hasDevAutofillLabelSegment('testRecords.wizard.valid')).toBe(false);
  });
});

describe('devAutofillSetProblems', () => {
  it('accepts well-formed sets', () => {
    expect(devAutofillSetProblems([set({ title: 'a' }), set({ title: 'b' }, { formKey: 'other-form' })])).toEqual([]);
  });

  it('names every problem', () => {
    const problems = devAutofillSetProblems([
      set({}, { formKey: ' ', labelKey: 'testRecords.valid' }),
      set({ title: 'a' }),
      set({ title: 'b' }),
    ]);
    expect(problems).toHaveLength(4);
    expect(problems[0]).toContain('form key is empty');
    expect(problems[1]).toContain("'devAutofill' segment");
    expect(problems[2]).toContain('no values');
    expect(problems[3]).toContain('registered twice');
  });
});

describe('applyDevAutofill', () => {
  it('writes the values, marks them dirty and touched, and reports them', () => {
    const target = form();
    const summary = applyDevAutofill(target, set({ title: 'A valid title', 'address.city': 'Sample City' }));

    expect(target.get('title')?.value).toBe('A valid title');
    expect(target.get('address.city')?.value).toBe('Sample City');
    expect(target.get('title')?.dirty).toBe(true);
    expect(target.get('title')?.touched).toBe(true);
    expect(target.get('address.street')?.dirty).toBe(false);
    expect(summary).toEqual({
      formKey: FORM_KEY,
      labelKey: LABEL_KEY,
      filled: ['title', 'address.city'],
      skipped: [],
      unknown: [],
      invalid: [],
    });
  });

  it('patches a group with a partial object', () => {
    const target = form();
    applyDevAutofill(target, set({ address: { street: 'Main Street' } }));
    expect(target.get('address')?.value).toEqual({ city: '', street: 'Main Street' });
  });

  it('calls value factories at fill time', () => {
    const target = form();
    applyDevAutofill(target, set({ title: () => 'Computed title' }));
    expect(target.get('title')?.value).toBe('Computed title');
  });

  it('leaves disabled controls alone and reports stale paths without throwing', () => {
    const target = form();
    const summary = applyDevAutofill(target, set({ code: 'changed', removedField: 'x' }));
    expect(target.get('code')?.value).toBe('locked');
    expect(summary.skipped).toEqual(['code']);
    expect(summary.unknown).toEqual(['removedField']);
    expect(summary.filled).toEqual([]);
  });

  it('reports filled controls that are still invalid', () => {
    const summary = applyDevAutofill(form(), set({ title: 'abc' }));
    expect(summary.invalid).toEqual(['title']);
  });
});
