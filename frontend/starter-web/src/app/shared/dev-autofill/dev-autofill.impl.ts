// The REAL DevAutofill implementation: development builds only. The production configuration of angular.json swaps
// this file for dev-autofill.stub.ts (fileReplacements), and tools/ci/no-dev-autofill.mjs fails the build when
// DEV_AUTOFILL_MARKER, which only this file renders, is found in a production bundle. Keep the exports of the two
// files identical (dev-autofill.stub.spec.ts checks it).
import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  type EnvironmentProviders,
  type OnInit,
  ViewContainerRef,
  computed,
  inject,
  input,
  makeEnvironmentProviders,
  output,
} from '@angular/core';
import { FormGroupDirective, type FormGroup } from '@angular/forms';
import { DgaButtonComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { DevAutofillTestIds, DevAutofillTranslationKeys } from './dev-autofill-keys';
import { applyDevAutofill, devAutofillSetProblems, devAutofillSetsFor } from './dev-autofill.fill';
import { DEV_AUTOFILL_SETS, type DevAutofillSet, type DevAutofillSummary } from './dev-autofill.model';

/**
 * The unique string that proves the real implementation is in a bundle. Rendered as the `data-dev-autofill`
 * attribute of the button group, so it survives tree shaking whenever the component does. Mirrored by
 * `DEV_AUTOFILL_MARKER` in tools/ci/no-dev-autofill.mjs (its test checks the two are equal).
 */
export const DEV_AUTOFILL_MARKER = 'codelab-dev-autofill-impl';

/**
 * Registers named data sets for `<dga-dev-autofill>` / `[devAutofill]`. Call it once per list of sets (the calls
 * add up). Throws when a set is malformed (see `devAutofillSetProblems`), so a broken set fails the dev boot loudly.
 * In a production build the stub registers nothing.
 */
export function provideDevAutofill(...sets: DevAutofillSet[]): EnvironmentProviders {
  const problems = devAutofillSetProblems(sets);
  if (problems.length > 0) throw new Error(`DevAutofill: invalid data set(s):\n${problems.join('\n')}`);
  return makeEnvironmentProviders(sets.map((set) => ({ provide: DEV_AUTOFILL_SETS, useValue: set, multi: true })));
}

/**
 * One button per data set registered for `formKey`; pressing one writes the set into `form` and emits what it did.
 * Renders nothing when no set exists for the form, and nothing at all in a production build (stub).
 */
@Component({
  selector: 'dga-dev-autofill',
  imports: [DgaButtonComponent, TranslatePipe],
  templateUrl: './dev-autofill.component.html',
  styleUrl: './dev-autofill.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevAutofillComponent {
  private readonly allSets = inject(DEV_AUTOFILL_SETS, { optional: true }) ?? [];

  /** The form's key in the registered sets. */
  readonly formKey = input.required<string>();
  /** The form the sets are written into. */
  readonly form = input.required<FormGroup>();
  /** What a press did (filled, skipped, unknown and still-invalid paths). */
  readonly autofilled = output<DevAutofillSummary>();

  protected readonly sets = computed(() => devAutofillSetsFor(this.allSets, this.formKey()));
  protected readonly keys = DevAutofillTranslationKeys;
  protected readonly testIds = DevAutofillTestIds;
  protected readonly marker = DEV_AUTOFILL_MARKER;

  protected fill(set: DevAutofillSet): void {
    this.autofilled.emit(applyDevAutofill(this.form(), set));
  }
}

/**
 * `<form [formGroup]="form" devAutofill="my-form">`: renders `<dga-dev-autofill>` right after the form, bound to the
 * form's own `FormGroup`, without touching the form's template otherwise. A no-op in a production build (stub).
 */
@Directive({ selector: '[devAutofill]' })
export class DevAutofillDirective implements OnInit {
  private readonly formGroup = inject(FormGroupDirective, { self: true, optional: true });
  private readonly container = inject(ViewContainerRef);

  /** The form's key in the registered sets. */
  readonly devAutofill = input.required<string>();

  ngOnInit(): void {
    if (this.formGroup === null) throw new Error('DevAutofill: [devAutofill] needs [formGroup] on the same element.');
    const panel = this.container.createComponent(DevAutofillComponent);
    panel.setInput('formKey', this.devAutofill());
    panel.setInput('form', this.formGroup.form);
  }
}
