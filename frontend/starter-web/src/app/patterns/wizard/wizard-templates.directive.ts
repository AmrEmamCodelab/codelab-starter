import { Directive, TemplateRef, inject, input } from '@angular/core';
import type { FieldErrorMap } from '../editor-dialog/field-errors';

/** What a step template receives: `let-stepId` and `let-errors="errors"` (that step's server field errors). */
export interface WizardStepContext {
  readonly $implicit: string;
  readonly errors: FieldErrorMap;
}

/**
 * The body of one wizard step: `<ng-template wizardStep="applicant" let-errors="errors">...</ng-template>`. The id
 * matches a `WizardStepDef.id`. Only the current step is rendered, so keep the entered values in the screen's form.
 */
@Directive({ selector: 'ng-template[wizardStep]' })
export class WizardStepDirective {
  readonly id = input.required<string>({ alias: 'wizardStep' });
  readonly template = inject<TemplateRef<WizardStepContext>>(TemplateRef);

  static ngTemplateContextGuard(_directive: WizardStepDirective, context: unknown): context is WizardStepContext {
    return true;
  }
}

/**
 * The review step: `<ng-template wizardReview>...</ng-template>`. When present, the wizard appends a last step (id
 * `review`, title `reviewTitleKey`) that shows a read-only summary before Submit.
 */
@Directive({ selector: 'ng-template[wizardReview]' })
export class WizardReviewDirective {
  readonly template = inject<TemplateRef<WizardStepContext>>(TemplateRef);

  static ngTemplateContextGuard(_directive: WizardReviewDirective, context: unknown): context is WizardStepContext {
    return true;
  }
}
