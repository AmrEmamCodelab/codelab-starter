import { DgaConfirmDialogVariant, type DgaButtonVariant } from '@codelab/ng-dga';
import type { TranslationParams } from '../../core/i18n/translation.service';
import { RecordsTranslationKeys } from './records-translation-keys';
import { ReasonRequirement, WORKFLOW_MAX_REASON_LENGTH, type RecordAllowedAction } from './records.models';

/** How a decision button looks. The record type picks it per action (`toneFor`); the panel never infers it. */
export enum DecisionTone {
  /** A side step (return for changes): an outline button. The default. */
  Neutral = 1,
  /** The expected next step (approve, start review): the one solid button. */
  Primary = 2,
  /** A step that ends or refuses the record (reject, cancel): a destructive button and a danger dialog. */
  Destructive = 3,
}

/** Maps an action key to its tone. */
export type DecisionToneResolver = (actionKey: string) => DecisionTone;

/** The default resolver: every action is a neutral side step. */
export const NEUTRAL_DECISION_TONE: DecisionToneResolver = () => DecisionTone.Neutral;

/** What the panel emits: the chosen action, and the trimmed reason (null when none was asked for or given). */
export interface RecordDecision {
  readonly actionKey: string;
  readonly reason: string | null;
}

/** A decision button as the panel renders it. */
export interface DecisionButton {
  readonly action: RecordAllowedAction;
  readonly tone: DecisionTone;
  readonly variant: DgaButtonVariant;
  readonly destructive: boolean;
}

/** Panel order by tone: side steps first, refusals next, the expected step last (at the inline end). */
const TONE_ORDER: Readonly<Record<DecisionTone, number>> = {
  [DecisionTone.Neutral]: 1,
  [DecisionTone.Destructive]: 2,
  [DecisionTone.Primary]: 3,
};

const TONE_VARIANT: Readonly<Record<DecisionTone, DgaButtonVariant>> = {
  [DecisionTone.Neutral]: 'secondary-outline',
  [DecisionTone.Destructive]: 'secondary-outline',
  [DecisionTone.Primary]: 'primary',
};

function knownTone(tone: DecisionTone): DecisionTone {
  return tone in TONE_ORDER ? tone : DecisionTone.Neutral;
}

/**
 * The panel's buttons for the server's allowed actions: one per action key (the first wins), in tone order (stable
 * within a tone). An unknown tone is shown as a neutral side step. Nothing is added: an empty list gives no button.
 */
export function decisionButtons(actions: readonly RecordAllowedAction[], toneFor: DecisionToneResolver): readonly DecisionButton[] {
  const seen = new Set<string>();
  return actions
    .filter((action) => !seen.has(action.actionKey) && seen.add(action.actionKey))
    .map((action) => ({ action, tone: knownTone(toneFor(action.actionKey)) }))
    .sort((left, right) => TONE_ORDER[left.tone] - TONE_ORDER[right.tone])
    .map(({ action, tone }) => ({
      action,
      tone,
      variant: TONE_VARIANT[tone],
      destructive: tone === DecisionTone.Destructive,
    }));
}

/** The confirm-dialog variant of a tone: destructive decisions ask in a danger dialog (focus starts on Cancel). */
export function dialogVariantFor(tone: DecisionTone): DgaConfirmDialogVariant {
  return tone === DecisionTone.Destructive ? DgaConfirmDialogVariant.Danger : DgaConfirmDialogVariant.Default;
}

/** True when the action asks for a reason in a dialog before it is sent (Optional or Required). */
export function asksForReason(action: RecordAllowedAction): boolean {
  return action.reason === ReasonRequirement.Optional || action.reason === ReasonRequirement.Required;
}

/** Why a reason cannot be sent. Integers, like every enum the starter owns. */
export enum DecisionReasonProblem {
  Required = 1,
  TooLong = 2,
}

/** The reason as it is sent: trimmed, and null when blank. */
export function reasonToSend(text: string | null | undefined): string | null {
  const trimmed = (text ?? '').trim();
  return trimmed.length > 0 ? trimmed : null;
}

/**
 * What is wrong with `text` for an action with `requirement`, or null when it may be sent: a blank reason is refused
 * only when the reason is Required; any reason longer than `maxLength` (after trimming) is refused.
 */
export function decisionReasonProblem(
  requirement: ReasonRequirement,
  text: string | null | undefined,
  maxLength: number = WORKFLOW_MAX_REASON_LENGTH,
): DecisionReasonProblem | null {
  const reason = reasonToSend(text);
  if (reason === null) {
    return requirement === ReasonRequirement.Required ? DecisionReasonProblem.Required : null;
  }
  return reason.length > maxLength ? DecisionReasonProblem.TooLong : null;
}

/** A message to show: a translation key and its parameters. */
export interface DecisionMessage {
  readonly key: string;
  readonly params?: TranslationParams;
}

/** The field message of a reason problem. */
export function decisionReasonMessage(problem: DecisionReasonProblem, maxLength: number): DecisionMessage {
  return problem === DecisionReasonProblem.Required
    ? { key: RecordsTranslationKeys.Decision.ReasonRequired }
    : { key: RecordsTranslationKeys.Decision.ReasonTooLong, params: { max: maxLength } };
}

/** The hint above the reason field: required or optional. */
export function reasonHintKey(requirement: ReasonRequirement): string {
  return requirement === ReasonRequirement.Required
    ? RecordsTranslationKeys.Decision.ReasonRequiredHint
    : RecordsTranslationKeys.Decision.ReasonOptionalHint;
}
