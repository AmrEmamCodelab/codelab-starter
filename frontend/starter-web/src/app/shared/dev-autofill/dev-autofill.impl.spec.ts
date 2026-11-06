import { Component, type EnvironmentProviders, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { beforeEach, describe, expect, it } from 'vitest';
import { TranslationService } from '../../core/i18n/translation.service';
import { DevAutofillTestIds, DevAutofillTranslationKeys } from './dev-autofill-keys';
import { DEV_AUTOFILL_MARKER, DevAutofillComponent, DevAutofillDirective, provideDevAutofill } from './dev-autofill.impl';
import { DEV_AUTOFILL_SETS, type DevAutofillSet, type DevAutofillSummary } from './dev-autofill.model';

const FORM_KEY = 'test-record-form';
const VALID: DevAutofillSet = {
  formKey: FORM_KEY,
  labelKey: 'testRecords.devAutofill.valid',
  values: { title: 'A valid title', description: 'Filled by the dev tool' },
};
const EDGE: DevAutofillSet = { formKey: FORM_KEY, labelKey: 'testRecords.devAutofill.edge', values: { title: 'Edge' } };
const OTHER: DevAutofillSet = { formKey: 'other-form', labelKey: 'otherRecords.devAutofill.valid', values: { name: 'x' } };

function recordForm(): FormGroup {
  return new FormGroup({ title: new FormControl(''), description: new FormControl('') });
}

@Component({
  imports: [DevAutofillComponent],
  template: `<dga-dev-autofill [formKey]="formKey()" [form]="form" (autofilled)="summaries.push($event)" />`,
})
class ComponentHost {
  readonly formKey = signal(FORM_KEY);
  readonly form = recordForm();
  readonly summaries: DevAutofillSummary[] = [];
}

@Component({
  imports: [ReactiveFormsModule, DevAutofillDirective],
  template: `<form [formGroup]="form" [devAutofill]="formKey"><input formControlName="title" /></form>`,
})
class DirectiveHost {
  readonly formKey = FORM_KEY;
  readonly form = recordForm();
}

@Component({
  imports: [DevAutofillDirective],
  template: `<section devAutofill="test-record-form"></section>`,
})
class DirectiveWithoutFormHost {}

function configure(...providers: EnvironmentProviders[]): void {
  TestBed.configureTestingModule({
    providers: [...providers, { provide: TranslationService, useValue: { translate: (key: string) => key } }],
  });
}

function buttons(host: HTMLElement): HTMLButtonElement[] {
  return [...host.querySelectorAll(`[data-testid="${DevAutofillTestIds.Fill}"] button`)] as HTMLButtonElement[];
}

function group(host: HTMLElement): HTMLElement | null {
  return host.querySelector(`[data-testid="${DevAutofillTestIds.Group}"]`);
}

describe('provideDevAutofill (real)', () => {
  it('registers every set, and the calls add up', () => {
    configure(provideDevAutofill(VALID, EDGE), provideDevAutofill(OTHER));
    expect(TestBed.inject(DEV_AUTOFILL_SETS)).toEqual([VALID, EDGE, OTHER]);
  });

  it('refuses a malformed set', () => {
    expect(() => provideDevAutofill({ ...VALID, labelKey: 'testRecords.valid' })).toThrow(/devAutofill' segment/);
  });
});

describe('DevAutofillComponent (real)', () => {
  let fixture: ComponentFixture<ComponentHost>;
  let host: HTMLElement;

  function create(...providers: EnvironmentProviders[]): void {
    configure(...providers);
    fixture = TestBed.createComponent(ComponentHost);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  it('shows one translated button per set of its form, in a labelled group carrying the marker', () => {
    create(provideDevAutofill(VALID, OTHER, EDGE));
    expect(buttons(host).map((button) => button.textContent?.trim())).toEqual([VALID.labelKey, EDGE.labelKey]);
    expect(group(host)?.getAttribute('role')).toBe('group');
    expect(group(host)?.getAttribute('aria-label')).toBe(DevAutofillTranslationKeys.Title);
    expect(group(host)?.getAttribute('data-dev-autofill')).toBe(DEV_AUTOFILL_MARKER);
  });

  it('fills the form and emits the summary when a button is pressed', () => {
    create(provideDevAutofill(VALID, EDGE));
    buttons(host)[0]?.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.form.value).toEqual(VALID.values);
    expect(fixture.componentInstance.form.dirty).toBe(true);
    expect(fixture.componentInstance.summaries).toHaveLength(1);
    expect(fixture.componentInstance.summaries[0]?.filled).toEqual(['title', 'description']);
  });

  it('renders nothing when its form has no set', () => {
    create(provideDevAutofill(OTHER));
    expect(group(host)).toBeNull();
    expect(host.textContent?.trim()).toBe('');
  });

  it('renders nothing when no set is registered at all', () => {
    create();
    expect(group(host)).toBeNull();
  });

  it('follows a change of form key', () => {
    create(provideDevAutofill(VALID, OTHER));
    fixture.componentInstance.formKey.set(OTHER.formKey);
    fixture.detectChanges();
    expect(buttons(host).map((button) => button.textContent?.trim())).toEqual([OTHER.labelKey]);
  });
});

describe('DevAutofillDirective (real)', () => {
  it('renders the buttons after the form and fills the form group of its element', () => {
    configure(provideDevAutofill(VALID));
    const fixture = TestBed.createComponent(DirectiveHost);
    const host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    const formElement = host.querySelector('form');
    expect(formElement?.nextElementSibling?.tagName.toLowerCase()).toBe('dga-dev-autofill');
    buttons(host)[0]?.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.form.value).toEqual(VALID.values);
    expect((host.querySelector('input') as HTMLInputElement).value).toBe('A valid title');
  });

  it('renders nothing when the form has no set', () => {
    configure(provideDevAutofill(OTHER));
    const fixture = TestBed.createComponent(DirectiveHost);
    fixture.detectChanges();
    expect(group(fixture.nativeElement as HTMLElement)).toBeNull();
  });

  it('refuses an element without [formGroup]', () => {
    configure(provideDevAutofill(VALID));
    const fixture = TestBed.createComponent(DirectiveWithoutFormHost);
    expect(() => fixture.detectChanges()).toThrow(/needs \[formGroup\]/);
  });
});

beforeEach(() => {
  TestBed.resetTestingModule();
});
