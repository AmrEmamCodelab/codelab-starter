import { describe, expect, it } from 'vitest';
import { DgaStepperOrientation } from './step-state';
import { nextFocusableStep, rovingStepIndex } from './stepper-navigation';

const LTR = { orientation: DgaStepperOrientation.Horizontal, rightToLeft: false };
const RTL = { orientation: DgaStepperOrientation.Horizontal, rightToLeft: true };
const VERTICAL = { orientation: DgaStepperOrientation.Vertical, rightToLeft: false };
const FOCUSABLE = [true, true, false, true];

describe('nextFocusableStep', () => {
  it('moves forward and backward along the visual order, skipping steps that cannot take focus', () => {
    expect(nextFocusableStep(FOCUSABLE, 1, 'ArrowRight', LTR)).toBe(3);
    expect(nextFocusableStep(FOCUSABLE, 3, 'ArrowLeft', LTR)).toBe(1);
  });

  it('mirrors left and right in RTL', () => {
    expect(nextFocusableStep(FOCUSABLE, 1, 'ArrowLeft', RTL)).toBe(3);
    expect(nextFocusableStep(FOCUSABLE, 1, 'ArrowRight', RTL)).toBe(0);
  });

  it('uses Up and Down for a vertical trail, whatever the direction', () => {
    expect(nextFocusableStep(FOCUSABLE, 0, 'ArrowDown', VERTICAL)).toBe(1);
    expect(nextFocusableStep(FOCUSABLE, 1, 'ArrowUp', { ...VERTICAL, rightToLeft: true })).toBe(0);
    expect(nextFocusableStep(FOCUSABLE, 1, 'ArrowRight', VERTICAL)).toBeNull();
  });

  it('jumps to the first and last focusable step with Home and End', () => {
    expect(nextFocusableStep([false, true, true, false], 2, 'Home', LTR)).toBe(1);
    expect(nextFocusableStep([false, true, true, false], 1, 'End', LTR)).toBe(2);
  });

  it('stops at the ends and ignores other keys', () => {
    expect(nextFocusableStep(FOCUSABLE, 3, 'ArrowRight', LTR)).toBeNull();
    expect(nextFocusableStep(FOCUSABLE, 0, 'ArrowLeft', LTR)).toBeNull();
    expect(nextFocusableStep(FOCUSABLE, 0, 'Enter', LTR)).toBeNull();
  });
});

describe('rovingStepIndex', () => {
  it('is the active step when it can take focus, else the first focusable step, else null', () => {
    expect(rovingStepIndex(FOCUSABLE, 1)).toBe(1);
    expect(rovingStepIndex(FOCUSABLE, 2)).toBe(0);
    expect(rovingStepIndex([false, false], 0)).toBeNull();
  });
});
