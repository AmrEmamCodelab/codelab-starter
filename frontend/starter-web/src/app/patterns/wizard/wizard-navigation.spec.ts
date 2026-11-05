import { describe, expect, it } from 'vitest';
import { clampWizardStep, firstInvalidStep, guardNext, guardStepChange, guardSubmit } from './wizard-navigation';

describe('firstInvalidStep', () => {
  it('finds the first invalid step, optionally inside a range', () => {
    expect(firstInvalidStep([true, false, false])).toBe(1);
    expect(firstInvalidStep([true, false, false], { from: 2, to: 3 })).toBe(2);
    expect(firstInvalidStep([true, false, true], { from: 2, to: 3 })).toBeNull();
  });

  it('is null when every step is valid and clamps the range to the steps', () => {
    expect(firstInvalidStep([true, true])).toBeNull();
    expect(firstInvalidStep([false], { from: -4, to: 99 })).toBe(0);
  });
});

describe('guardStepChange', () => {
  const validity = [true, false, true, true];

  it('always allows going back or staying, never validating', () => {
    expect(guardStepChange({ validity, current: 2 }, 0, true)).toEqual({ allowed: true, blockedAt: null });
    expect(guardStepChange({ validity, current: 1 }, 1, true).allowed).toBe(true);
  });

  it('linear: blocks a forward jump at the first invalid step on the way', () => {
    expect(guardStepChange({ validity, current: 0 }, 3, true)).toEqual({ allowed: false, blockedAt: 1 });
    expect(guardStepChange({ validity, current: 2 }, 3, true).allowed).toBe(true);
  });

  it('non-linear: allows any forward jump', () => {
    expect(guardStepChange({ validity, current: 0 }, 3, false).allowed).toBe(true);
  });

  it('refuses targets outside the wizard without blaming a step', () => {
    expect(guardStepChange({ validity, current: 0 }, 4, false)).toEqual({ allowed: false, blockedAt: null });
    expect(guardStepChange({ validity, current: 0 }, -1, false).allowed).toBe(false);
    expect(guardStepChange({ validity, current: 0 }, 1.5, false).allowed).toBe(false);
  });
});

describe('guardNext', () => {
  it('validates the current step only', () => {
    expect(guardNext({ validity: [false, true], current: 0 })).toEqual({ allowed: false, blockedAt: 0 });
    expect(guardNext({ validity: [true, false, true], current: 0 }).allowed).toBe(true);
  });

  it('refuses Next on the last step', () => {
    expect(guardNext({ validity: [true, true], current: 1 })).toEqual({ allowed: false, blockedAt: null });
  });
});

describe('guardSubmit', () => {
  it('needs every step valid and names the first invalid one', () => {
    expect(guardSubmit([true, true]).allowed).toBe(true);
    expect(guardSubmit([true, false, false])).toEqual({ allowed: false, blockedAt: 1 });
  });
});

describe('clampWizardStep', () => {
  it('clamps to the steps and treats NaN or an empty wizard as the first step', () => {
    expect(clampWizardStep({ step: 5, count: 3 })).toBe(2);
    expect(clampWizardStep({ step: -1, count: 3 })).toBe(0);
    expect(clampWizardStep({ step: Number.NaN, count: 3 })).toBe(0);
    expect(clampWizardStep({ step: 1, count: 0 })).toBe(0);
    expect(clampWizardStep({ step: 1.9, count: 3 })).toBe(1);
  });
});
