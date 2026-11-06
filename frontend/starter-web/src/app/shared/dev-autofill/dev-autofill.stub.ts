// The PRODUCTION stand-in for dev-autofill.impl.ts (angular.json production fileReplacements). Same exports and
// the same template API, so every screen compiles unchanged, but it renders nothing, registers nothing and holds
// no DevAutofill text. Never import this file directly, and never put the marker or a data set here.
import { ChangeDetectionStrategy, Component, Directive, type EnvironmentProviders, input, makeEnvironmentProviders, output } from '@angular/core';
import type { FormGroup } from '@angular/forms';
import type { DevAutofillSet, DevAutofillSummary } from './dev-autofill.model';

/** Registers nothing: production builds carry no data sets. */
export function provideDevAutofill(..._sets: DevAutofillSet[]): EnvironmentProviders {
  return makeEnvironmentProviders([]);
}

/** Renders nothing. */
@Component({
  selector: 'dga-dev-autofill',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevAutofillComponent {
  readonly formKey = input.required<string>();
  readonly form = input.required<FormGroup>();
  readonly autofilled = output<DevAutofillSummary>();
}

/** Does nothing. */
@Directive({ selector: '[devAutofill]' })
export class DevAutofillDirective {
  readonly devAutofill = input.required<string>();
}
