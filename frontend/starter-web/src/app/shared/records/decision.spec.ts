import { DgaConfirmDialogVariant } from '@codelab/ng-dga';
import { describe, expect, it } from 'vitest';
import {
  DecisionReasonProblem,
  DecisionTone,
  NEUTRAL_DECISION_TONE,
  asksForReason,
  decisionButtons,
  decisionReasonMessage,
  decisionReasonProblem,
  dialogVariantFor,
  reasonHintKey,
  reasonToSend,
  type DecisionToneResolver,
} from './decision';
import { RecordsTranslationKeys } from './records-translation-keys';
import { ReasonRequirement, WORKFLOW_MAX_REASON_LENGTH } from './records.models';
import { aRequiredReasonAction, anAction, anOptionalReasonAction } from './records.testing';

const TONES: Record<string, DecisionTone> = { approve: DecisionTone.Primary, reject: DecisionTone.Destructive };
const toneFor: DecisionToneResolver = (actionKey) => TONES[actionKey] ?? DecisionTone.Neutral;

describe('decision buttons', () => {
  it('offers nothing when the server allows nothing (a terminal record)', () => {
    expect(decisionButtons([], toneFor)).toEqual([]);
  });

  it('orders side steps, then refusals, then the expected step, one button per action key', () => {
    const buttons = decisionButtons(
      [anAction(), aRequiredReasonAction(), anAction({ actionKey: 'return' }), anAction({ labelKey: 'duplicate' })],
      toneFor,
    );
    expect(buttons.map((button) => button.action.actionKey)).toEqual(['return', 'reject', 'approve']);
    expect(buttons.map((button) => button.variant)).toEqual(['secondary-outline', 'secondary-outline', 'primary']);
    expect(buttons.map((button) => button.destructive)).toEqual([false, true, false]);
    expect(buttons[2]?.action.labelKey).toBe(anAction().labelKey);
  });

  it('shows every action as neutral by default and an unknown tone as neutral', () => {
    expect(decisionButtons([anAction()], NEUTRAL_DECISION_TONE)[0]?.tone).toBe(DecisionTone.Neutral);
    expect(decisionButtons([anAction()], () => 42 as DecisionTone)[0]?.tone).toBe(DecisionTone.Neutral);
  });

  it('asks a destructive decision in a danger dialog', () => {
    expect(dialogVariantFor(DecisionTone.Destructive)).toBe(DgaConfirmDialogVariant.Danger);
    expect(dialogVariantFor(DecisionTone.Primary)).toBe(DgaConfirmDialogVariant.Default);
  });
});

describe('decision reasons', () => {
  it('asks for a reason only when the action takes one', () => {
    expect(asksForReason(anAction())).toBe(false);
    expect(asksForReason(anOptionalReasonAction())).toBe(true);
    expect(asksForReason(aRequiredReasonAction())).toBe(true);
  });

  it.each(['', '   ', null, undefined])('blocks an empty mandatory reason (%j)', (text) => {
    expect(decisionReasonProblem(ReasonRequirement.Required, text)).toBe(DecisionReasonProblem.Required);
  });

  it('lets an optional reason stay blank', () => {
    expect(decisionReasonProblem(ReasonRequirement.Optional, '  ')).toBeNull();
    expect(decisionReasonProblem(ReasonRequirement.None, '')).toBeNull();
  });

  it('blocks a reason over the workflow limit, measured after trimming', () => {
    const atLimit = 'x'.repeat(WORKFLOW_MAX_REASON_LENGTH);
    expect(decisionReasonProblem(ReasonRequirement.Required, `  ${atLimit}  `)).toBeNull();
    expect(decisionReasonProblem(ReasonRequirement.Optional, `${atLimit}x`)).toBe(DecisionReasonProblem.TooLong);
    expect(decisionReasonProblem(ReasonRequirement.Required, 'abcdef', 5)).toBe(DecisionReasonProblem.TooLong);
  });

  it('sends the trimmed reason, or null when blank', () => {
    expect(reasonToSend('  why  ')).toBe('why');
    expect(reasonToSend('  ')).toBeNull();
    expect(reasonToSend(null)).toBeNull();
  });

  it('words each problem and each hint', () => {
    expect(decisionReasonMessage(DecisionReasonProblem.Required, 10)).toEqual({ key: RecordsTranslationKeys.Decision.ReasonRequired });
    expect(decisionReasonMessage(DecisionReasonProblem.TooLong, 10)).toEqual({
      key: RecordsTranslationKeys.Decision.ReasonTooLong,
      params: { max: 10 },
    });
    expect(reasonHintKey(ReasonRequirement.Required)).toBe(RecordsTranslationKeys.Decision.ReasonRequiredHint);
    expect(reasonHintKey(ReasonRequirement.Optional)).toBe(RecordsTranslationKeys.Decision.ReasonOptionalHint);
  });
});
