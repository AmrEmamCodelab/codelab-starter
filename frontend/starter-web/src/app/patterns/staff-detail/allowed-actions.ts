import type { DgaButtonVariant } from '@codelab/ng-dga';

/** How a decision button looks. The server picks the tone with the action; the pattern never infers it from the code. */
export const ActionTone = {
  /** The expected next step (approve, accept): the one solid button. */
  Primary: 'primary',
  /** A side step (return for changes, request information). */
  Neutral: 'neutral',
  /** A step that ends or refuses the record (reject, cancel). */
  Destructive: 'destructive',
} as const;

export type ActionTone = (typeof ActionTone)[keyof typeof ActionTone];

/**
 * One decision the current user may take on the record now, as the server computed it from the workflow state AND
 * the user's permissions. The screen passes the server's list as it is: the pattern shows only these actions, so a
 * user never sees a button the API would refuse.
 *
 * - `code` is the action's stable code (the screen's constant), echoed back in `decide`.
 * - `labelKey` is the translation key of the button.
 * - `requiresReason` tells the screen to open the decision dialog before it calls the API.
 */
export interface AllowedAction {
  readonly code: string;
  readonly labelKey: string;
  readonly requiresReason: boolean;
  readonly tone: ActionTone;
}

/** A decision button as the panel renders it. */
export interface DecisionButton {
  readonly action: AllowedAction;
  readonly variant: DgaButtonVariant;
  readonly destructive: boolean;
}

/** Panel order by tone: side steps first, refusals next, the expected step last (at the inline end). */
const TONE_ORDER: Readonly<Record<ActionTone, number>> = {
  [ActionTone.Neutral]: 1,
  [ActionTone.Destructive]: 2,
  [ActionTone.Primary]: 3,
};

const TONE_VARIANT: Readonly<Record<ActionTone, DgaButtonVariant>> = {
  [ActionTone.Primary]: 'primary',
  [ActionTone.Neutral]: 'secondary-outline',
  [ActionTone.Destructive]: 'secondary-outline',
};

/** True when `tone` is one of the {@link ActionTone} values (server data is checked before use). */
export function isActionTone(tone: unknown): tone is ActionTone {
  return typeof tone === 'string' && tone in TONE_ORDER;
}

/**
 * The panel's buttons for the server's allowed actions: one per code (the first wins), in tone order (stable within a
 * tone), each with its button variant. An action with an unknown tone is shown as a neutral side step.
 */
export function decisionButtons(actions: readonly AllowedAction[]): DecisionButton[] {
  const seen = new Set<string>();
  const unique = actions.filter((action) => !seen.has(action.code) && seen.add(action.code));
  return unique
    .map((action) => ({ action, tone: isActionTone(action.tone) ? action.tone : ActionTone.Neutral }))
    .sort((left, right) => TONE_ORDER[left.tone] - TONE_ORDER[right.tone])
    .map(({ action, tone }) => ({ action, variant: TONE_VARIANT[tone], destructive: tone === ActionTone.Destructive }));
}

/** The allowed action with `code`, or null when the server does not allow it (any more). */
export function findAllowedAction(actions: readonly AllowedAction[], code: string): AllowedAction | null {
  return actions.find((action) => action.code === code) ?? null;
}
