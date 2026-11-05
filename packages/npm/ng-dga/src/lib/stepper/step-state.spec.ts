import { describe, expect, it } from 'vitest';
import { clampActiveStep, type DgaStep, dgaStepState, isDgaStepSelectable } from './step-state';

const PLAIN: DgaStep = { label: 'Step' };

describe('clampActiveStep', () => {
  it('keeps an index inside the steps', () => {
    expect(clampActiveStep(2, 4)).toBe(2);
  });

  it('clamps below the first step and past the last step', () => {
    expect(clampActiveStep(-3, 4)).toBe(0);
    expect(clampActiveStep(9, 4)).toBe(4);
  });

  it('treats NaN as the first step and truncates fractions', () => {
    expect(clampActiveStep(Number.NaN, 4)).toBe(0);
    expect(clampActiveStep(1.7, 4)).toBe(1);
  });
});

describe('dgaStepState', () => {
  it('linear: marks earlier steps completed, the active one current and later ones upcoming', () => {
    expect([0, 1, 2].map((index) => dgaStepState({ index, active: 1, step: PLAIN, linear: true }))).toEqual([
      'completed',
      'current',
      'upcoming',
    ]);
  });

  it('linear: marks every step completed when the active index is past the last step', () => {
    expect([0, 1].map((index) => dgaStepState({ index, active: 2, step: PLAIN, linear: true }))).toEqual(['completed', 'completed']);
  });

  it('non-linear: completion comes only from the step flag, wherever the step sits', () => {
    expect(dgaStepState({ index: 0, active: 1, step: PLAIN, linear: false })).toBe('upcoming');
    expect(dgaStepState({ index: 2, active: 1, step: { label: 'x', completed: true }, linear: false })).toBe('completed');
  });

  it('linear: a later step the user already finished stays completed after going back', () => {
    expect(dgaStepState({ index: 2, active: 0, step: { label: 'x', completed: true }, linear: true })).toBe('completed');
  });

  it('invalid wins over completed, but the active step is always current', () => {
    const invalid: DgaStep = { label: 'x', invalid: true, completed: true };
    expect(dgaStepState({ index: 0, active: 1, step: invalid, linear: true })).toBe('invalid');
    expect(dgaStepState({ index: 1, active: 1, step: invalid, linear: true })).toBe('current');
  });
});

describe('isDgaStepSelectable', () => {
  it('never selects the current or a disabled step', () => {
    expect(isDgaStepSelectable({ index: 1, active: 1, step: PLAIN, linear: false })).toBe(false);
    expect(isDgaStepSelectable({ index: 0, active: 1, step: { label: 'x', disabled: true }, linear: false })).toBe(false);
  });

  it('linear: allows going back, and forward only to a completed step', () => {
    expect(isDgaStepSelectable({ index: 0, active: 1, step: PLAIN, linear: true })).toBe(true);
    expect(isDgaStepSelectable({ index: 2, active: 1, step: PLAIN, linear: true })).toBe(false);
    expect(isDgaStepSelectable({ index: 2, active: 1, step: { label: 'x', completed: true }, linear: true })).toBe(true);
  });

  it('non-linear: allows any other enabled step', () => {
    expect(isDgaStepSelectable({ index: 3, active: 0, step: PLAIN, linear: false })).toBe(true);
  });
});
