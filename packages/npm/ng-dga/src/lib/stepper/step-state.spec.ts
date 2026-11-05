import { describe, expect, it } from 'vitest';
import { clampActiveStep, dgaStepState } from './step-state';

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
  it('marks earlier steps completed, the active one current and later ones upcoming', () => {
    expect([0, 1, 2].map((index) => dgaStepState(index, 1))).toEqual(['completed', 'current', 'upcoming']);
  });

  it('marks every step completed when the active index is past the last step', () => {
    expect([0, 1].map((index) => dgaStepState(index, 2))).toEqual(['completed', 'completed']);
  });
});
