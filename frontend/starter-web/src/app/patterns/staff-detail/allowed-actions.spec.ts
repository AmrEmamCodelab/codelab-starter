import { describe, expect, it } from 'vitest';
import { ActionTone, decisionButtons, findAllowedAction, isActionTone, type AllowedAction } from './allowed-actions';
import { STAFF_DETAIL_TRANSLATION_KEYS } from './staff-detail-translation-keys';

const APPROVE: AllowedAction = { code: 'approve', labelKey: 'cases.actions.approve', requiresReason: false, tone: ActionTone.Primary };
const REJECT: AllowedAction = { code: 'reject', labelKey: 'cases.actions.reject', requiresReason: true, tone: ActionTone.Destructive };
const RETURN: AllowedAction = { code: 'return', labelKey: 'cases.actions.return', requiresReason: true, tone: ActionTone.Neutral };

describe('decisionButtons', () => {
  it('orders side steps, refusals, then the expected step, with their variants', () => {
    const buttons = decisionButtons([APPROVE, REJECT, RETURN]);
    expect(buttons.map((button) => button.action.code)).toEqual(['return', 'reject', 'approve']);
    expect(buttons.map((button) => [button.variant, button.destructive])).toEqual([
      ['secondary-outline', false],
      ['secondary-outline', true],
      ['primary', false],
    ]);
  });

  it('keeps the server order within a tone and drops duplicate codes', () => {
    const second: AllowedAction = { ...RETURN, code: 'ask-info', labelKey: 'cases.actions.ask-info' };
    const buttons = decisionButtons([RETURN, second, { ...RETURN, labelKey: 'dup' }]);
    expect(buttons.map((button) => button.action.labelKey)).toEqual(['cases.actions.return', 'cases.actions.ask-info']);
  });

  it('shows an action with an unknown tone as a neutral side step', () => {
    const odd = { ...APPROVE, code: 'odd', tone: 'loud' } as unknown as AllowedAction;
    const [button] = decisionButtons([odd]);
    expect(button.variant).toBe('secondary-outline');
    expect(button.destructive).toBe(false);
  });

  it('is empty when the server allows nothing', () => {
    expect(decisionButtons([])).toEqual([]);
  });
});

describe('isActionTone', () => {
  it('accepts the three tones only', () => {
    expect(Object.values(ActionTone).every(isActionTone)).toBe(true);
    expect(isActionTone('loud')).toBe(false);
    expect(isActionTone(undefined)).toBe(false);
  });
});

describe('findAllowedAction', () => {
  it('finds by code, or null when it is not allowed', () => {
    expect(findAllowedAction([APPROVE, REJECT], 'reject')).toBe(REJECT);
    expect(findAllowedAction([APPROVE], 'reject')).toBeNull();
  });
});

describe('StaffDetailTranslationKeys', () => {
  it('has unique keys under patterns.staff-detail.*', () => {
    expect(new Set(STAFF_DETAIL_TRANSLATION_KEYS).size).toBe(STAFF_DETAIL_TRANSLATION_KEYS.length);
    for (const key of STAFF_DETAIL_TRANSLATION_KEYS) {
      expect(key).toMatch(/^patterns\.staff-detail\.[a-z-]+$/);
    }
  });
});
